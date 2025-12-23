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
using System.Collections.Generic;
using RCListener.Config;
using RCListener.Model;
using RCListener.Processing;
using RCListener.Transport;

namespace RCListener
{
    public class RCListener : Plugin
    {
        // ---- serial state ----
        private readonly SerialSession serialSession;
        private readonly PortScanner portScanner;
        private Thread monitorThread;
        private Thread rcThread;
        private bool running;

        private DateTime lastDataUtc = DateTime.MinValue;
        
        private volatile bool handshakeConfirmed = false;
        private ManualResetEventSlim handshakeEvent = new ManualResetEventSlim(false);
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
            serialSession = new SerialSession(Log);
            portScanner = new PortScanner(Log, lastPortCacheFile);
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
            portScanner.LoadLastKnownPort();
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
            return serialSession.IsHealthy(handshakeConfirmed, lastDataUtc, noDataTimeoutMs);
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

            if (serialSession.HasOpenPort)
            {
                Log("[SCAN] Current port is not healthy, forcing disconnect");
                DisconnectPort();
                Thread.Sleep(200);
            }

            if (scanning) return;
            scanning = true;

            try
            {
                var ports = portScanner.GetCandidatePorts();

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

            DisconnectPortInternal();
            Thread.Sleep(100);

            if (!serialSession.TryOpen(port, SerialDataReceived))
                return false;

            lastDataUtc = DateTime.UtcNow;
            handshakeConfirmed = false;

            if (waitIndefinitelyForHandshake)
                Log($"[SCAN] Opened single candidate port {port}, awaiting $RM,... without timeout");
            else
                Log($"[SCAN] Opened candidate port {port}, waiting for $RM,...");

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

        private void DisconnectPortInternal()
        {
            try
            {
                handshakeEvent.Reset();

                if (serialSession.HasOpenPort)
                {
                    Log($"Disconnected from {serialSession.ConnectedPort}");
                }

                serialSession.Close();
            }
            catch { }

            handshakeConfirmed = false;
            lastDataUtc = DateTime.MinValue;
            scanning = false;
            indefiniteHandshakeWait = false;

            UpdateStatusButton(false);
        }

        private void DisconnectPort()
        {
            DisconnectPortInternal();
            Thread.Sleep(100);
        }

        private bool IsPortStillPresent()
        {
            return serialSession.IsPortStillPresent();
        }

        // =========================
        //      SERIAL RX PARSER
        // =========================
        private void SerialDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (!serialSession.HasOpenPort)
                return;

            var chunk = serialSession.ReadExisting();
            if (string.IsNullOrEmpty(chunk))
                return;

            foreach (var frame in frameParser.Push(chunk))
            {
                lastDataUtc = DateTime.UtcNow;

                if (!handshakeConfirmed)
                {
                    handshakeConfirmed = true;
                    indefiniteHandshakeWait = false;
                    handshakeEvent.Set();
                    Log($"[SCAN] Confirmed RadioMaster on {serialSession.ConnectedPort}, stop scanning");
                    portScanner.RecordSuccessfulPort(serialSession.ConnectedPort);
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
                    if (!serialSession.HasOpenPort)
                    {
                        ScanPortsOnce();
                        Thread.Sleep(300);
                        continue;
                    }

                    var portAlive = serialSession.IsPortStillPresent();

                    if (!portAlive) missingCount++;
                    else
                        missingCount = 0;

                    bool handshakeTimedOut =
                        !handshakeConfirmed &&
                        !indefiniteHandshakeWait &&
                        serialSession.PortOpenUtc != DateTime.MinValue &&
                        (DateTime.UtcNow - serialSession.PortOpenUtc).TotalMilliseconds > HandshakeTimeoutMs;

                    bool dataTimedOut =
                        handshakeConfirmed &&
                        (DateTime.UtcNow - lastDataUtc).TotalMilliseconds > noDataTimeoutMs;

                    if (missingCount >= 10 || handshakeTimedOut || dataTimedOut)
                    {
                        Log($"[MON] Port {serialSession.ConnectedPort} seems lost (missingCount={missingCount}) → disconnect");
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
