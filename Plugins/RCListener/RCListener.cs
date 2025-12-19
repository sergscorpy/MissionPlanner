using MissionPlanner;
using MissionPlanner.Plugin;
using System;
using System.IO.Ports;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Collections;
using System.Management;

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

        private string serialBuffer = "";
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
        private readonly ushort[] latestChannels = new ushort[24];
        private readonly double[] ema = new double[18];
        private readonly Dictionary<int, string> lastRangeAction = new Dictionary<int, string>();

        private DateTime lastRCSend = DateTime.MinValue;

        // ---- UDP/gimbal ----
        private UdpClient udpClient;
        private IPEndPoint gimbalEndpoint;

        // ---- timing / tuning ----
        private const int noDataTimeoutMs = 1000;
        private const int HandshakeTimeoutMs = 2000;
        private const int SendPeriodMs = 20;
        private const double EmaAlpha = 0.35;

        // =========================
        //        UI / STATUS
        // =========================
        private ToolStripButton rcStatusButton;
        private readonly Color colorConnected = Color.FromArgb(0, 200, 0);
        private readonly Color colorDisconnected = Color.FromArgb(200, 0, 0);
        private readonly int statusIconSize = 20;

        private readonly string lastPortCacheFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rc_listener_last_port.txt");

        public override string Name => "RadioMaster RC Control";
        public override string Version => "1.6";
        public override string Author => "fortis";

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

            // --- UDP socket initialization ---
            try
            {
                udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, 15001));
                gimbalEndpoint = new IPEndPoint(IPAddress.Parse(RcConfig.UdpIp), RcConfig.UdpPort);
                Log($"UDP socket bound to port 15001 → {RcConfig.UdpIp}:{RcConfig.UdpPort}");
            }
            catch (Exception ex)
            {
                Log($"Failed to init UDP socket: {ex.Message}");
            }

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

            string[] lines;
            lock (portLock)
            {
                try
                {
                    serialBuffer += serialPort.ReadExisting();
                }
                catch (Exception ex)
                {
                    Log($"SerialDataReceived read error: {ex.Message}");
                    return;
                }

                lines = serialBuffer.Split('\n');
                serialBuffer = lines.Last();
            }

            foreach (var raw in lines.Take(lines.Length - 1))
            {
                var line = raw.Trim();
                if (!line.StartsWith("$RM,"))
                    continue;

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

                var payload = line.Substring(4).Split(',');
                int count = Math.Min(24, payload.Length);

                // Parse RC channels
                for (int i = 0; i < Math.Min(18, count); i++)
                {
                    if (ushort.TryParse(payload[i], out ushort v))
                        latestChannels[i] = v == 0 ? (ushort)0 : NormalizePwm(v);
                    else
                        latestChannels[i] = 0;
                }

                // Parse extra channels
                for (int i = 18; i < count && i < 24; i++)
                {
                    if (ushort.TryParse(payload[i], out ushort v))
                        latestChannels[i] = (ushort)Clamp(v, 1000, 2000);
                    else
                        latestChannels[i] = 0;
                }

                for (int i = count; i < 24; i++)
                    latestChannels[i] = 0;

                HandleExtraChannels();
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
            ushort[] ch = new ushort[18];
            for (int i = 0; i < 18; i++)
            {
                ushort raw = latestChannels[i];
                ch[i] = (i <= 3) ? SmoothStick(i, raw) : raw;
            }

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

        private ushort SmoothStick(int idx, ushort raw)
        {
            if (raw == 0) return 0;
            double v = raw;
            if (ema[idx] == 0) ema[idx] = v;
            ema[idx] = EmaAlpha * v + (1 - EmaAlpha) * ema[idx];
            return (ushort)Math.Round(ema[idx]);
        }

        // =========================
        //   EXTRA CHANNEL ACTIONS
        // =========================
        private void HandleExtraChannels()
        {
            for (int ch = 19; ch <= 24; ch++)
            {
                if (!RcConfig.Channels.TryGetValue(ch, out var cfg))
                    continue;

                int val = latestChannels[ch - 1];
                string matched = null;

                foreach (var range in cfg.Ranges)
                {
                    if (val >= range.Min && val <= range.Max)
                    {
                        matched = range.Action;
                        break;
                    }
                }

                if (matched == null)
                {
                    lastRangeAction.Remove(ch);
                    continue;
                }

                if (!lastRangeAction.TryGetValue(ch, out string prev) || prev != matched)
                {
                    lastRangeAction[ch] = matched;
                    SendGimbalCommand(matched);
                }
            }
        }

        // =========================
        //          UDP
        // =========================
        private void SendUdpPacket(byte[] data)
        {
            try { udpClient?.Send(data, data.Length, gimbalEndpoint); }
            catch (Exception ex) { Log($"UDP send error: {ex.Message}"); }
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

                try
                {
                    udpClient?.Dispose();
                }
                catch (Exception ex)
                {
                    Log($"[EXIT] UDP dispose error: {ex.Message}");
                }

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

        // =========================
        //        CONFIG
        // =========================
        public class RangeAction { public int Min, Max; public string Action; }
        public class ChannelConfig { public string Name = ""; public List<RangeAction> Ranges = new List<RangeAction>(); }

        public static class RcConfig
        {
            public static readonly string UdpIp = "192.168.144.25";
            public static readonly int UdpPort = 37260;

            public static readonly Dictionary<int, ChannelConfig> Channels = new Dictionary<int, ChannelConfig>()
            {
                [19] = new ChannelConfig
                {
                    Name = "Down - Center",
                    Ranges =
                    {
                        new RangeAction { Min = 1750, Max = 2000, Action = "down" },
                        new RangeAction { Min = 1251, Max = 1749, Action = "down_45" },
                        new RangeAction { Min = 1000, Max = 1250, Action = "center" }
                    }
                },
                [20] = new ChannelConfig
                {
                    Name = "Zoom",
                    Ranges =
                    {
                        new RangeAction { Min = 1750, Max = 2000, Action = "zoom_in" },
                        new RangeAction { Min = 1250, Max = 1750, Action = "zoom_stop" },
                        new RangeAction { Min = 1000, Max = 1250, Action = "zoom_out" }
                    }
                },
                [21] = new ChannelConfig
                {
                    Name = "Pitch control",
                    Ranges =
                    {
                        new RangeAction { Min = 1700, Max = 1850, Action = "pitch_down_40" },
                        new RangeAction { Min = 1851, Max = 2000, Action = "pitch_down_80" },
                        new RangeAction { Min = 1475, Max = 1525, Action = "pitch_stop" },
                        new RangeAction { Min = 1150, Max = 1450, Action = "pitch_up_40" },
                        new RangeAction { Min = 1000, Max = 1149, Action = "pitch_up_80" }
                    }
                }
            };
        }

        // =========================
        //      PACKETS / CRC
        // =========================
        public static class PacketStore
        {
            public static readonly Dictionary<string, byte[]> Packets = new Dictionary<string, byte[]>()
            {
                ["down"] = new byte[] { 0x55, 0x66, 0x01, 0x01, 0x00, 0x00, 0x00, 0x08, 0x04, },
                ["down_45"] = new byte[] { 0x55, 0x66, 0x01, 0x04, 0x00, 0x00, 0x00, 0x0E, 0x00, 0x00, 0x3E, 0xFE },
                ["center"] = new byte[] { 0x55, 0x66, 0x01, 0x01, 0x00, 0x00, 0x00, 0x08, 0x01 },
                ["zoom_in"] = new byte[] { 0x55, 0x66, 0x01, 0x01, 0x00, 0x00, 0x00, 0x05, 0x01 },
                ["zoom_stop"] = new byte[] { 0x55, 0x66, 0x01, 0x01, 0x00, 0x00, 0x00, 0x05, 0x00 },
                ["zoom_out"] = new byte[] { 0x55, 0x66, 0x01, 0x01, 0x00, 0x00, 0x00, 0x05, 0xFF },
                ["pitch_up_40"] = new byte[] { 0x55, 0x66, 0x01, 0x02, 0x00, 0x00, 0x00, 0x07, 0x00, 0xD8 },
                ["pitch_up_80"] = new byte[] { 0x55, 0x66, 0x01, 0x02, 0x00, 0x00, 0x00, 0x07, 0x00, 0xB0 },
                ["pitch_stop"] = new byte[] { 0x55, 0x66, 0x01, 0x02, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00 },
                ["pitch_down_40"] = new byte[] { 0x55, 0x66, 0x01, 0x02, 0x00, 0x00, 0x00, 0x07, 0x00, 0x28 },
                ["pitch_down_80"] = new byte[] { 0x55, 0x66, 0x01, 0x02, 0x00, 0x00, 0x00, 0x07, 0x00, 0x50 }
            };
        }

        private void SendGimbalCommand(string key)
        {
            if (!PacketStore.Packets.TryGetValue(key, out var baseCmd))
            {
                Log($"Gimbal command '{key}' not found");
                return;
            }

            var signed = AppendCrc16(baseCmd);
            SendUdpPacket(signed);
            //Log($"[GIMBAL CMD] {key} → {BitConverter.ToString(signed)}");
        }

        private static ushort NormalizePwm(ushort val)
        {
            const double inMin = 988.0;
            const double inMax = 2012.0;
            const double outMin = 1000.0;
            const double outMax = 2000.0;

            if (val <= inMin) return (ushort)outMin;
            if (val >= inMax) return (ushort)outMax;

            double scaled = (val - inMin) / (inMax - inMin);
            double result = outMin + scaled * (outMax - outMin);
            return (ushort)Math.Round(result);
        }

        private static byte[] AppendCrc16(byte[] data)
        {
            ushort crc = ComputeCrc16(data);
            var full = new byte[data.Length + 2];
            Buffer.BlockCopy(data, 0, full, 0, data.Length);
            full[full.Length - 2] = (byte)((crc >> 8) & 0xFF);
            full[full.Length - 1] = (byte)(crc & 0xFF);
            return full;
        }

        private static ushort ComputeCrc16(byte[] data)
        {
            ushort crc = 0x0000;
            foreach (byte b in data)
            {
                byte temp = (byte)((crc >> 8) & 0xFF);
                crc = (ushort)((crc << 8) ^ Crc16Table[(b ^ temp) & 0xFF]);
            }
            return (ushort)(((crc >> 8) & 0xFF) | ((crc & 0xFF) << 8));
        }

        private static readonly ushort[] Crc16Table = new ushort[256]
        {
            0x0000,0x1021,0x2042,0x3063,0x4084,0x50A5,0x60C6,0x70E7,
            0x8108,0x9129,0xA14A,0xB16B,0xC18C,0xD1AD,0xE1CE,0xF1EF,
            0x1231,0x0210,0x3273,0x2252,0x52B5,0x4294,0x72F7,0x62D6,
            0x9339,0x8318,0xB37B,0xA35A,0xD3BD,0xC39C,0xF3FF,0xE3DE,
            0x2462,0x3443,0x0420,0x1401,0x64E6,0x74C7,0x44A4,0x5485,
            0xA56A,0xB54B,0x8528,0x9509,0xE5EE,0xF5CF,0xC5AC,0xD58D,
            0x3653,0x2672,0x1611,0x0630,0x76D7,0x66F6,0x5695,0x46B4,
            0xB75B,0xA77A,0x9719,0x8738,0xF7DF,0xE7FE,0xD79D,0xC7BC,
            0x48C4,0x58E5,0x6886,0x78A7,0x0840,0x1861,0x2802,0x3823,
            0xC9CC,0xD9ED,0xE98E,0xF9AF,0x8948,0x9969,0xA90A,0xB92B,
            0x5AF5,0x4AD4,0x7AB7,0x6A96,0x1A71,0x0A50,0x3A33,0x2A12,
            0xDBFD,0xCBDC,0xFBBF,0xEB9E,0x9B79,0x8B58,0xBB3B,0xAB1A,
            0x6CA6,0x7C87,0x4CE4,0x5CC5,0x2C22,0x3C03,0x0C60,0x1C41,
            0xEDAE,0xFD8F,0xCDEC,0xDDCD,0xAD2A,0xBD0B,0x8D68,0x9D49,
            0x7E97,0x6EB6,0x5ED5,0x4EF4,0x3E13,0x2E32,0x1E51,0x0E70,
            0xFF9F,0xEFBE,0xDFDD,0xCFFC,0xBF1B,0xAF3A,0x9F59,0x8F78,
            0x9188,0x81A9,0xB1CA,0xA1EB,0xD10C,0xC12D,0xF14E,0xE16F,
            0x1080,0x00A1,0x30C2,0x20E3,0x5004,0x4025,0x7046,0x6067,
            0x83B9,0x9398,0xA3FB,0xB3DA,0xC33D,0xD31C,0xE37F,0xF35E,
            0x02B1,0x1290,0x22F3,0x32D2,0x4235,0x5214,0x6277,0x7256,
            0xB5EA,0xA5CB,0x95A8,0x8589,0xF56E,0xE54F,0xD52C,0xC50D,
            0x34E2,0x24C3,0x14A0,0x0481,0x7466,0x6447,0x5424,0x4405,
            0xA7DB,0xB7FA,0x8799,0x97B8,0xE75F,0xF77E,0xC79D,0xD7BC,
            0x26D3,0x36F2,0x0691,0x16B0,0x6657,0x7676,0x4615,0x5634,
            0xD94C,0xC96D,0xF90E,0xE92F,0x99C8,0x89E9,0xB98A,0xA9AB,
            0x5844,0x4865,0x7806,0x6827,0x18C0,0x08E1,0x3882,0x28A3,
            0xCB7D,0xDB5C,0xEB3F,0xFB1E,0x8BF9,0x9BD8,0xABBB,0xBB9A,
            0x4A75,0x5A54,0x6A37,0x7A16,0x0AF1,0x1AD0,0x2AB3,0x3A92,
            0xFD2E,0xED0F,0xDD6C,0xCD4D,0xBDAA,0xAD8B,0x9DE8,0x8DC9,
            0x7C26,0x6C07,0x5C64,0x4C45,0x3CA2,0x2C83,0x1CE0,0x0CC1,
            0xEF1F,0xFF3E,0xCF5D,0xDF7C,0xAF9B,0xBFBA,0x8FD9,0x9FF8,
            0x6E17,0x7E36,0x4E55,0x5E74,0x2E93,0x3EB2,0x0ED1,0x1EF0
        };
    }
}
