using MissionPlanner;
using MissionPlanner.Plugin;
using System;
using System.IO.Ports;
using System.IO;
using System.Linq;
using System.Threading;
using System.Drawing;
using System.Windows.Forms;
using System.Collections;
using System.Management;
using RCListener.Config;
using RCListener.Model;
using RCListener.Processing;
using RCListener.Transport;

namespace RCListener
{
    public class RCListener : Plugin
    {
        // ---- serial state ----
        private string connectedPort;
        private SerialPort serialPort;
        private Thread monitorThread;
        private Thread rcThread;
        private bool running;
        private readonly object portLock = new object();

        private DateTime lastDataUtc = DateTime.MinValue;

        private string lastKnownGoodPort = null;
        
        private volatile bool handshakeConfirmed = false;
        private ManualResetEventSlim handshakeEvent = new ManualResetEventSlim(false);
        private DateTime portOpenUtc = DateTime.MinValue;
        private volatile bool scanning = false;
        private volatile bool waitingForDeviceChange = false;
        private volatile bool waitingNoticeShown = false;
        private volatile bool indefiniteHandshakeWait = false;

        // ---- rc state ----
        private DateTime lastRCSend = DateTime.MinValue;

        private readonly RcFrameParser frameParser;
        private readonly ChannelProcessor channelProcessor;
        private readonly GimbalCommandSender gimbalSender;
        private readonly string lastPortCacheFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rc_listener_last_port.txt");
        
        // ---- timing / tuning ----
        private const int noDataTimeoutMs = 1000;
        private const int HandshakeTimeoutMs = 2000;
        private const int SendPeriodMs = 20;

        // =========================
        //        UI / STATUS
        // =========================
        private ToolStripButton rcStatusButton;
        private readonly Color colorConnected = Color.FromArgb(0, 200, 0);
        private readonly Color colorDisconnected = Color.FromArgb(200, 0, 0);
        private readonly int statusIconSize = 20;

        public override string Name => "RadioMaster RC Control";
        public override string Version => "1.6";
        public override string Author => "fortis";

        public RCListener()
        {
            frameParser = new RcFrameParser(Log);
            channelProcessor = new ChannelProcessor();
            gimbalSender = new GimbalCommandSender(Log);
        }

        public override bool Init() => true;

        public override bool Loaded()
        {
            Log("RC Control plugin loaded");
            running = true;
            if (Host != null)
            {
                try { Host.DeviceChanged += OnDeviceChanged; } catch { }
            }
            InitStatusButton();
            LoadLastKnownPort();
            Thread.Sleep(300);

            monitorThread = new Thread(MonitorLoop) { IsBackground = true };
            rcThread = new Thread(RCSendLoop) { IsBackground = true };
            monitorThread.Start();
            rcThread.Start();

            return true;
        }

        private void InitStatusButton()
        {
            try
            {
                rcStatusButton = new ToolStripButton
                {
                    Name = "RCLinkStatus",
                    Text = "RC LINK",
                    TextAlign = ContentAlignment.BottomCenter,
                    TextImageRelation = TextImageRelation.ImageAboveText,
                    DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                    Image = CreateStatusIcon(colorDisconnected),
                    ToolTipText = "RadioMaster link status (click to rescan ports)"
                };

                rcStatusButton.Click += (s, e) => RestartScanQueue();

                var idx = MainV2.instance.MainMenu.Items.IndexOfKey("MenuHelp");
                if (idx < 0) idx = MainV2.instance.MainMenu.Items.Count - 1;
                MainV2.instance.MainMenu.Items.Insert(idx, rcStatusButton);

                Log("[UI] RC LINK indicator added to menu");
            }
            catch (Exception ex)
            {
                Log($"[UI] Failed to init status button: {ex.Message}");
            }
        }

        private Bitmap CreateStatusIcon(Color color)
        {
            var bmp = new Bitmap(statusIconSize, statusIconSize);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var brush = new SolidBrush(color))
                    g.FillEllipse(brush, 2, 2, statusIconSize - 4, statusIconSize - 4);
            }
            return bmp;
        }

        private void UpdateStatusButton(bool connected)
        {
            if (rcStatusButton == null) return;

            try
            {
                var color = connected ? colorConnected : colorDisconnected;
                rcStatusButton.Image?.Dispose();
                rcStatusButton.Image = CreateStatusIcon(color);
            }
            catch (Exception ex)
            {
                Log($"[UI] UpdateStatusButton error: {ex.Message}");
            }
        }

        private void RestartScanQueue()
        {
            try
            {
                if (scanning)
                {
                    Log("[UI] Scan restart requested but scan is already in progress");
                    return;
                }

                Log("[UI] Scan restart requested by user");

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        waitingForDeviceChange = false;
                        waitingNoticeShown = false;
                        DisconnectPort();
                        Thread.Sleep(200);
                        ScanPortsOnce();
                    }
                    catch (Exception ex)
                    {
                        Log($"[UI] RestartScanQueue worker error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"[UI] RestartScanQueue error: {ex.Message}");
            }
        }

        // =========================
        //   PORT / SCAN MANAGEMENT
        // =========================
        private bool CurrentPortHealthy()
        {
            if (serialPort == null || !serialPort.IsOpen)
                return false;

            if (!handshakeConfirmed)
                return false;

            if ((DateTime.UtcNow - lastDataUtc).TotalMilliseconds > noDataTimeoutMs)
                return false;

            try
            {
                _ = serialPort.BytesToRead;
            }
            catch
            {
                return false;
            }

            return true;
        }

        private void ScanPortsOnce()
        {
            if (CurrentPortHealthy())
                return;

            if (waitingForDeviceChange)
            {
                if (!waitingNoticeShown)
                {
                    Log("[SCAN] Waiting for device change event before rescanning ports");
                    waitingNoticeShown = true;
                }
                return;
            }

            if (serialPort != null || connectedPort != null)
            {
                Log("[SCAN] Current port is not healthy, forcing disconnect");
                DisconnectPort();
                Thread.Sleep(200);
            }

            if (scanning) return;
            scanning = true;

            try
            {
                var ports = GetCandidatePorts();

                if (ports.Count == 0)
                {
                    waitingForDeviceChange = true;
                    waitingNoticeShown = false;
                    Log("[SCAN] No ports matched WMI filter; waiting for device change");
                    return;
                }

                waitingForDeviceChange = false;
                waitingNoticeShown = false;

                Log($"[SCAN] Candidate port order: {string.Join(", ", ports)}");

                bool singleCandidate = ports.Count == 1;

                foreach (var port in ports)
                {
                    if (TryOpenCandidatePort(port, singleCandidate))
                        break;
                    Thread.Sleep(100);
                }
            }
            finally
            {
                scanning = false;
            }
        }

        private bool TryOpenCandidatePort(string port, bool waitIndefinitelyForHandshake)
        {
            handshakeEvent.Reset();
            indefiniteHandshakeWait = waitIndefinitelyForHandshake;

            lock (portLock)
            {
                try
                {
                    DisconnectPortInternal();
                    Thread.Sleep(100);

                    serialPort = new SerialPort(port, 115200)
                    {
                        ReadTimeout = 200,
                        WriteTimeout = 200
                    };
                    serialPort.DataReceived += SerialDataReceived;
                    serialPort.Open();

                    connectedPort = port;
                    serialBuffer = "";
                    lastDataUtc = DateTime.UtcNow;
                    portOpenUtc = DateTime.UtcNow;
                    handshakeConfirmed = false;

                    if (waitIndefinitelyForHandshake)
                        Log($"[SCAN] Opened single candidate port {port}, awaiting $RM,... without timeout");
                    else
                        Log($"[SCAN] Opened candidate port {port}, waiting for $RM,...");
                }
                catch (Exception ex)
                {
                    Log($"[SCAN] Failed to open {port}: {ex.Message}");
                    serialPort = null;
                    connectedPort = null;
                    handshakeConfirmed = false;
                    return false;
                }
            }

            if (waitIndefinitelyForHandshake)
                return true;

            // ⏳ ЧЕКАЄМО handshake (поза lock, щоб SerialDataReceived міг спрацювати)
            if (!handshakeEvent.Wait(HandshakeTimeoutMs))
            {
                Log($"[SCAN] No handshake on {port}, closing");
                DisconnectPort();
                return false;
            }

            // ✅ handshake підтверджено
            return true;
        }

        private List<string> GetCandidatePorts()
        {
            var ports = FilterPortsWithWmi(SerialPort.GetPortNames())
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            if (!string.IsNullOrEmpty(lastKnownGoodPort) && ports.Contains(lastKnownGoodPort))
            {
                ports.Remove(lastKnownGoodPort);
                ports.Insert(0, lastKnownGoodPort);
            }

            return ports;
        }

        private IEnumerable<string> FilterPortsWithWmi(IEnumerable<string> ports)
        {
            var candidates = new HashSet<string>(ports ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (candidates.Count == 0)
                return Array.Empty<string>();

            try
            {
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using (var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
                {
                    foreach (var obj in searcher.Get().OfType<ManagementObject>())
                    {
                        var name = obj["Name"]?.ToString() ?? string.Empty;
                        var pnpId = obj["PNPDeviceID"]?.ToString() ?? string.Empty;

                        var match = candidates.FirstOrDefault(port => name.IndexOf(port, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (string.IsNullOrEmpty(match))
                            continue;

                        if (!MeetsWmiCriteria(name, pnpId))
                        {
                            Log($"[SCAN] Skipping {match} via WMI filter ({pnpId})");
                            continue;
                        }

                        allowed.Add(match);
                    }
                }

                if (allowed.Count > 0)
                    return allowed;

                Log("[SCAN] WMI filter returned no matches, stopping scan until device change");
            }
            catch (Exception ex)
            {
                Log($"[SCAN] WMI filter error: {ex.Message}");
            }

            return Array.Empty<string>();
        }

        private bool MeetsWmiCriteria(string name, string pnpId)
        {
            var signature = $"{name} {pnpId}".ToUpperInvariant();

            return signature.Contains("VID_0483&PID_5740");
        }

        private void LoadLastKnownPort()
        {
            try
            {
                if (File.Exists(lastPortCacheFile))
                {
                    var port = File.ReadAllText(lastPortCacheFile).Trim();
                    if (!string.IsNullOrEmpty(port))
                    {
                        lastKnownGoodPort = port;
                        Log($"[CFG] Loaded last known port: {port}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[CFG] Failed to load last port cache: {ex.Message}");
            }
        }

        private void PersistLastKnownPort(string port)
        {
            if (string.IsNullOrEmpty(port))
                return;

            try
            {
                File.WriteAllText(lastPortCacheFile, port);
            }
            catch (Exception ex)
            {
                Log($"[CFG] Failed to persist last port '{port}': {ex.Message}");
            }
        }

        private void DisconnectPortInternal()
        {
            try
            {
                handshakeEvent.Reset();

                if (serialPort != null)
                {
                    try { serialPort.DataReceived -= SerialDataReceived; } catch { }

                    try
                    {
                        if (serialPort.IsOpen)
                            serialPort.Close();
                    }
                    catch { }

                    try { serialPort.Dispose(); } catch { }

                    serialPort = null;
                }

                if (connectedPort != null)
                    Log($"Disconnected from {connectedPort}");
            }
            catch { }

            connectedPort = null;
            handshakeConfirmed = false;
            portOpenUtc = DateTime.MinValue;
            lastDataUtc = DateTime.MinValue;
            scanning = false;
            indefiniteHandshakeWait = false;

            Array.Clear(latestChannels, 0, latestChannels.Length);
            Array.Clear(ema, 0, ema.Length);
            UpdateStatusButton(false);
        }

        private void DisconnectPort()
        {
            lock (portLock)
            {
                DisconnectPortInternal();
                Thread.Sleep(100);
            }
        }

        private bool IsPortStillPresent()
        {
            try
            {
                if (serialPort == null)
                    return false;

                // try a lightweight query to check if port is responsive
                _ = serialPort.BytesToRead;  // will throw if handle invalid

                var names = SerialPort.GetPortNames();
                return connectedPort != null && names.Contains(connectedPort);
            }
            catch
            {
                return false; // any error → port is gone or frozen
            }
        }

        // =========================
        //      SERIAL RX PARSER
        // =========================
        private void SerialDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (serialPort == null || !serialPort.IsOpen)
                return;

            string chunk;
            lock (portLock)
            {
                try
                {
                    chunk = serialPort.ReadExisting();
                }
                catch (Exception ex)
                {
                    Log($"SerialDataReceived read error: {ex.Message}");
                    return;
                }
            }

            foreach (var frame in frameParser.Push(chunk))
            {
                lastDataUtc = DateTime.UtcNow;

                if (!handshakeConfirmed)
                {
                    handshakeConfirmed = true;
                    indefiniteHandshakeWait = false;
                    handshakeEvent.Set();
                    Log($"[SCAN] Confirmed RadioMaster on {connectedPort}, stop scanning");
                    lastKnownGoodPort = connectedPort;
                    PersistLastKnownPort(connectedPort);
                    UpdateStatusButton(true);
                }

                var result = channelProcessor.Process(frame);
                foreach (var action in result.Actions)
                    gimbalSender.Send(action);
            }
        }

        // =========================
        //       RC OVERRIDE LOOP
        // =========================
        private void RCSendLoop()
        {
            try { Thread.CurrentThread.IsBackground = true; } catch { }
            try { Thread.CurrentThread.Priority = ThreadPriority.Highest; } catch { }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            double nextSendMs = 0;
            const double interval = SendPeriodMs;

            int localCount = 0;
            DateTime lastLog = DateTime.UtcNow;

            while (running)
            {
                double now = sw.Elapsed.TotalMilliseconds;
                if (now >= nextSendMs)
                {
                    nextSendMs += interval;

                    try
                    {
                        if (MainV2.comPort.BaseStream?.IsOpen == true)
                        {
                            SendRCOverride();
                            localCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"RC send error: {ex.Message}");
                    }
                }

                //if ((DateTime.UtcNow - lastLog).TotalSeconds >= 1)
                //{
                //    Log($"RC packets per second: {localCount}");
                //    localCount = 0;
                //    lastLog = DateTime.UtcNow;
                //}

                Thread.SpinWait(5000);
            }
        }

        private void SendRCOverride()
        {
            ushort[] ch = channelProcessor.BuildOverrideChannels();

            var pkt = new MAVLink.mavlink_rc_channels_override_t
            {
                chan1_raw = ch[0],
                chan2_raw = ch[1],
                chan3_raw = ch[2],
                chan4_raw = ch[3],
                chan5_raw = ch[4],
                chan6_raw = ch[5],
                chan7_raw = ch[6],
                chan8_raw = ch[7],
                chan9_raw = ch[8],
                chan10_raw = ch[9],
                chan11_raw = ch[10],
                chan12_raw = ch[11],
                chan13_raw = ch[12],
                chan14_raw = ch[13],
                chan15_raw = ch[14],
                chan16_raw = ch[15],
                chan17_raw = ch[16],
                chan18_raw = ch[17],
                target_system = (byte)MainV2.comPort.sysidcurrent,
                target_component = (byte)MainV2.comPort.compidcurrent
            };

            MainV2.comPort.sendPacket(pkt,
                MainV2.comPort.sysidcurrent,
                MainV2.comPort.compidcurrent);
        }

        // =========================
        //     DEVICE CHANGE HOOK
        // =========================
        private void OnDeviceChanged(MainV2.WM_DEVICECHANGE_enum cause)
        {
            if (!running)
                return;

            try
            {
                Log($"[DEV] Device change detected ({cause}), restarting scan");
                waitingForDeviceChange = false;
                waitingNoticeShown = false;

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        DisconnectPort();
                        Thread.Sleep(200);
                        ScanPortsOnce();
                    }
                    catch (Exception ex)
                    {
                        Log($"[DEV] Device change worker error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"[DEV] Device change handler error: {ex.Message}");
            }
        }

        // =========================
        //        MONITOR LOOP
        // =========================
        private void MonitorLoop()
        {
            try { Thread.CurrentThread.IsBackground = true; } catch { }

            int missingCount = 0;

            while (running)
            {
                try
                {
                    if (serialPort == null || !serialPort.IsOpen || connectedPort == null)
                    {
                        ScanPortsOnce();
                        Thread.Sleep(300);
                        continue;
                    }

                    bool portAlive = true;
                    try { _ = serialPort.BytesToRead; }
                    catch { portAlive = false; }

                    bool namePresent = false;
                    try
                    {
                        var names = SerialPort.GetPortNames();
                        namePresent = connectedPort != null && names.Contains(connectedPort);
                    }
                    catch { }

                    if (!portAlive || !namePresent)
                        missingCount++;
                    else
                        missingCount = 0;

                    bool handshakeTimedOut =
                        !handshakeConfirmed &&
                        !indefiniteHandshakeWait &&
                        (DateTime.UtcNow - portOpenUtc).TotalMilliseconds > HandshakeTimeoutMs;

                    bool dataTimedOut =
                        handshakeConfirmed &&
                        (DateTime.UtcNow - lastDataUtc).TotalMilliseconds > noDataTimeoutMs;

                    if (missingCount >= 10 || handshakeTimedOut || dataTimedOut)
                    {
                        Log($"[MON] Port {connectedPort} seems lost (missingCount={missingCount}) → disconnect");
                        missingCount = 0;

                        DisconnectPort();

                        Thread.Sleep(2000);
                        ScanPortsOnce();
                        continue;
                    }

                    Thread.Sleep(200);
                }
                catch (Exception ex)
                {
                    Log($"MonitorLoop error: {ex.Message}");
                    Thread.Sleep(500);
                }
            }
        }

        // =========================
        //        HELPERS
        // =========================
        private static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0) return min;
            if (value.CompareTo(max) > 0) return max;
            return value;
        }

        private void Log(string msg)
        {
            try { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}"); }
            catch { System.Diagnostics.Debug.WriteLine(msg); }
        }

        public override bool Loop()
        {
            loopratehz = 0.5f;
            return true;
        }

        public override bool Exit()
        {
            try
            {
                Log("[EXIT] Stopping RC Control plugin...");
                running = false;
                if (Host != null)
                {
                    try { Host.DeviceChanged -= OnDeviceChanged; } catch { }
                }

                try
                {
                    if (monitorThread != null && monitorThread.IsAlive)
                    {
                        Log("[EXIT] Waiting for monitorThread...");
                        if (!monitorThread.Join(1000))
                            monitorThread.Interrupt();
                    }

                    if (rcThread != null && rcThread.IsAlive)
                    {
                        Log("[EXIT] Waiting for rcThread...");
                        if (!rcThread.Join(1000))
                            rcThread.Interrupt();
                    }
                }
                catch (Exception ex)
                {
                    Log($"[EXIT] Thread join error: {ex.Message}");
                }

                try
                {
                    DisconnectPort();
                    Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    Log($"[EXIT] Port close error: {ex.Message}");
                }

                try { gimbalSender?.Dispose(); }
                catch (Exception ex) { Log($"[EXIT] UDP dispose error: {ex.Message}"); }

                try
                {
                    if (MainV2.instance != null && rcStatusButton != null)
                    {
                        MainV2.instance.BeginInvoke((Action)(() => UpdateStatusButton(false)));
                    }
                }
                catch { }

                Log("[EXIT] RC Control stopped cleanly");
            }
            catch (Exception ex)
            {
                Log($"[EXIT] Unexpected error: {ex.Message}");
            }

            return true;
        }

    }
}
