using MissionPlanner;
using MissionPlanner.Plugin;
using System;
using System.IO.Ports;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections;
using System.Collections.Generic;
using RCListener.Config;
using RCListener.Model;
using RCListener.Processing;
using RCListener.Transport;
using RCListener.Ui;

namespace RCListener
{
    public class RCListener : Plugin
    {
        // ---- serial state ----
        private readonly SerialSession serialSession;
        private readonly PortScanner portScanner;
        private Task monitorTask;
        private Task rcTask;
        private bool running;
        private CancellationTokenSource lifecycleCts;

        private DateTime lastDataUtc = DateTime.MinValue;
        
        private volatile bool handshakeConfirmed = false;
        private TaskCompletionSource<bool> handshakeTcs;
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

        private readonly UiStatusPresenter statusPresenter;

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
            statusPresenter = new UiStatusPresenter(Log, RestartScanQueue);
        }

        public override bool Init() => true;

        public override bool Loaded()
        {
            Log("RC Control plugin loaded");
            running = true;
            lifecycleCts = new CancellationTokenSource();
            if (Host != null)
            {
                try { Host.DeviceChanged += OnDeviceChanged; } catch { }
            }
            statusPresenter.Initialize();
            portScanner.LoadLastKnownPort();
            Task.Delay(300, lifecycleCts.Token).ConfigureAwait(false).GetAwaiter().GetResult();

            monitorTask = Task.Run(() => MonitorLoopAsync(lifecycleCts.Token));
            rcTask = Task.Run(() => RCSendLoopAsync(lifecycleCts.Token));

            return true;
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

                Task.Run(async () =>
                {
                    try
                    {
                        waitingForDeviceChange = false;
                        waitingNoticeShown = false;
                        await DisconnectPortAsync(lifecycleCts?.Token ?? CancellationToken.None);
                        await Task.Delay(200, lifecycleCts?.Token ?? CancellationToken.None);
                        await ScanPortsOnceAsync(lifecycleCts?.Token ?? CancellationToken.None);
                    }
                    catch (OperationCanceledException)
                    {
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

        private async Task ScanPortsOnceAsync(CancellationToken token)
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
                await DisconnectPortAsync(token);
                await Task.Delay(200, token);
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
                    if (await TryOpenCandidatePortAsync(port, singleCandidate, token))
                        break;
                    await Task.Delay(100, token);
                }
            }
            finally
            {
                scanning = false;
            }
        }

        private async Task<bool> TryOpenCandidatePortAsync(string port, bool waitIndefinitelyForHandshake, CancellationToken token)
        {
            indefiniteHandshakeWait = waitIndefinitelyForHandshake;
            handshakeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            DisconnectPortInternal();
            await Task.Delay(100, token);

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
            var completed = await Task.WhenAny(handshakeTcs.Task, Task.Delay(HandshakeTimeoutMs, token));
            var handshakeOk = handshakeTcs.Task.Status == TaskStatus.RanToCompletion && handshakeTcs.Task.Result;
            if (completed != handshakeTcs.Task || !handshakeOk)
            {
                Log($"[SCAN] No handshake on {port}, closing");
                await DisconnectPortAsync(token);
                return false;
            }

            // ✅ handshake підтверджено
            return true;
        }

        private void DisconnectPortInternal()
        {
            try
            {
                handshakeTcs?.TrySetCanceled();

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

            statusPresenter.SetConnected(false);
        }

        private async Task DisconnectPortAsync(CancellationToken token)
        {
            DisconnectPortInternal();
            await Task.Delay(100, token);
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
                    handshakeTcs?.TrySetResult(true);
                    Log($"[SCAN] Confirmed RadioMaster on {serialSession.ConnectedPort}, stop scanning");
                    portScanner.RecordSuccessfulPort(serialSession.ConnectedPort);
                    statusPresenter.SetConnected(true);
                }

                var result = channelProcessor.Process(frame);
                foreach (var action in result.Actions)
                    gimbalSender.Send(action);
            }
        }

        // =========================
        //       RC OVERRIDE LOOP
        // =========================
        private async Task RCSendLoopAsync(CancellationToken token)
        {
            try { Thread.CurrentThread.IsBackground = true; } catch { }
            try { Thread.CurrentThread.Priority = ThreadPriority.Highest; } catch { }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            double nextSendMs = 0;
            const double interval = SendPeriodMs;

            int localCount = 0;
            DateTime lastLog = DateTime.UtcNow;

            while (!token.IsCancellationRequested)
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

                try
                {
                    await Task.Delay(1, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
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

                Task.Run(async () =>
                {
                    try
                    {
                        await DisconnectPortAsync(lifecycleCts?.Token ?? CancellationToken.None);
                        await Task.Delay(200, lifecycleCts?.Token ?? CancellationToken.None);
                        await ScanPortsOnceAsync(lifecycleCts?.Token ?? CancellationToken.None);
                    }
                    catch (OperationCanceledException)
                    {
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
        private async Task MonitorLoopAsync(CancellationToken token)
        {
            try { Thread.CurrentThread.IsBackground = true; } catch { }

            int missingCount = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!serialSession.HasOpenPort)
                    {
                        await ScanPortsOnceAsync(token);
                        await Task.Delay(300, token);
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

                        await DisconnectPortAsync(token);

                        await Task.Delay(2000, token);
                        await ScanPortsOnceAsync(token);
                        continue;
                    }

                    await Task.Delay(200, token);
                }
                catch (Exception ex)
                {
                    Log($"MonitorLoop error: {ex.Message}");
                    try
                    {
                        await Task.Delay(500, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
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
                lifecycleCts?.Cancel();
                if (Host != null)
                {
                    try { Host.DeviceChanged -= OnDeviceChanged; } catch { }
                }

                try
                {
                    if (monitorTask != null)
                    {
                        Log("[EXIT] Waiting for monitor task...");
                        Task.WaitAny(new[] { monitorTask }, 1000);
                    }

                    if (rcTask != null)
                    {
                        Log("[EXIT] Waiting for RC task...");
                        Task.WaitAny(new[] { rcTask }, 1000);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[EXIT] Thread join error: {ex.Message}");
                }

                try
                {
                    DisconnectPortInternal();
                    Task.Delay(100).ConfigureAwait(false).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log($"[EXIT] Port close error: {ex.Message}");
                }

                try { gimbalSender?.Dispose(); }
                catch (Exception ex) { Log($"[EXIT] UDP dispose error: {ex.Message}"); }

                try
                {
                    statusPresenter.SetConnected(false);
                }
                catch { }

                try
                {
                    statusPresenter.Dispose();
                }
                catch (Exception ex)
                {
                    Log($"[EXIT] UI dispose error: {ex.Message}");
                }

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
