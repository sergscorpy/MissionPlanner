using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MissionPlanner;
using RCListener.Logging;
using RCListener.Processing;
using RCListener.Transport;

namespace RCListener.Control
{
    public class RcListenerController : IDisposable
    {
        private readonly ILogger logger;
        private readonly Action<string> log;
        private readonly SerialSession serialSession;
        private readonly PortScanner portScanner;
        private readonly RcFrameParser frameParser;
        private readonly ChannelProcessor channelProcessor;
        private readonly GimbalCommandSender gimbalSender;

        private CancellationTokenSource lifecycleCts;
        private Task monitorTask;
        private Task rcTask;
        private readonly object workQueueSync = new object();
        private Task workQueue = Task.CompletedTask;

        private DateTime lastDataUtc = DateTime.MinValue;

        private volatile bool handshakeConfirmed;
        private TaskCompletionSource<bool> handshakeTcs;
        private volatile bool scanning;
        private volatile bool waitingForDeviceChange;
        private volatile bool waitingNoticeShown;
        private volatile bool indefiniteHandshakeWait;

        private const int NoDataTimeoutMs = 1000;
        private const int HandshakeTimeoutMs = 2000;
        private const int SingleCandidateHandshakeTimeoutMs = 8000;
        private const int SendPeriodMs = 20;

        public RcListenerController(
            ILogger logger,
            SerialSession serialSession,
            PortScanner portScanner,
            RcFrameParser frameParser,
            ChannelProcessor channelProcessor,
            GimbalCommandSender gimbalSender)
        {
            this.logger = logger;
            this.serialSession = serialSession;
            this.portScanner = portScanner;
            this.frameParser = frameParser;
            this.channelProcessor = channelProcessor;
            this.gimbalSender = gimbalSender;
        }

        public event Action<bool> ConnectionChanged;

        public event Action<bool> ScanStateChanged;

        public void Start()
        {
            if (lifecycleCts != null)
                return;

            lifecycleCts = new CancellationTokenSource();
            ResetState();
            ResetWorkQueue();

            portScanner.LoadLastKnownPort();

            Task.Delay(300, lifecycleCts.Token).ConfigureAwait(false).GetAwaiter().GetResult();

            monitorTask = Task.Run(() => MonitorLoopAsync(lifecycleCts.Token), lifecycleCts.Token);
            rcTask = Task.Run(() => RCSendLoopAsync(lifecycleCts.Token), lifecycleCts.Token);
        }

        public Task StopAsync()
        {
            return StopInternalAsync();
        }

        public void HandleDeviceChange()
        {
            EnqueueWork(async token =>
            {
                logger.Log("[DEV] Device change detected, restarting scan");
                waitingForDeviceChange = false;
                waitingNoticeShown = false;

                await DisconnectPortAsync(token);
                await Task.Delay(200, token);
                await ScanPortsOnceAsync(token);
            }, "[DEV] Device change worker error");
        }

        public void RequestManualRescan()
        {
            EnqueueWork(async token =>
            {
                if (scanning)
                {
                    logger.Log("[UI] Scan restart requested but scan is already in progress");
                    return;
                }

                logger.Log("[UI] Scan restart requested by user");

                waitingForDeviceChange = false;
                waitingNoticeShown = false;
                await DisconnectPortAsync(token);
                await Task.Delay(200, token);
                await ScanPortsOnceAsync(token);
            }, "[UI] RestartScanQueue worker error");
        }

        public void Dispose()
        {
            StopInternalAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private void ResetState()
        {
            handshakeConfirmed = false;
            lastDataUtc = DateTime.MinValue;
            waitingForDeviceChange = false;
            waitingNoticeShown = false;
            indefiniteHandshakeWait = false;
            scanning = false;
        }

        private bool CurrentPortHealthy()
        {
            return serialSession.IsHealthy(handshakeConfirmed, lastDataUtc, NoDataTimeoutMs);
        }

        private void ResetWorkQueue()
        {
            lock (workQueueSync)
            {
                workQueue = Task.CompletedTask;
            }
        }

        private async Task ScanPortsOnceAsync(CancellationToken token)
        {
            if (CurrentPortHealthy())
                return;

            if (waitingForDeviceChange)
            {
                if (!waitingNoticeShown)
                {
                    logger.Log("[SCAN] Waiting for device change event before rescanning ports"); 
                    waitingNoticeShown = true;
                }
                return;
            }

            if (serialSession.HasOpenPort)
            {
                logger.Log("[SCAN] Current port is not healthy, forcing disconnect");
                await DisconnectPortAsync(token);
                await Task.Delay(200, token);
            }

            if (scanning)
                return;

            SetScanning(true);

            try
            {
                var ports = portScanner.GetCandidatePorts();

                if (ports.Count == 0)
                {
                    waitingForDeviceChange = true;
                    waitingNoticeShown = false;
                    logger.Log("[SCAN] No ports matched WMI filter; waiting for device change");
                    return;
                }

                waitingForDeviceChange = false;
                waitingNoticeShown = false;

                logger.Log($"[SCAN] Candidate port order: {string.Join(", ", ports)}");

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
                SetScanning(false);
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
                logger.Log($"[SCAN] Opened single candidate port {port}, awaiting $RM,... without timeout");
            else
                logger.Log($"[SCAN] Opened candidate port {port}, waiting for $RM,...");

            if (waitIndefinitelyForHandshake)
                return true;

            var handshakeTimeout = waitIndefinitelyForHandshake ? SingleCandidateHandshakeTimeoutMs : HandshakeTimeoutMs;
            var completed = await Task.WhenAny(handshakeTcs.Task, Task.Delay(handshakeTimeout, token));
            var handshakeOk = handshakeTcs.Task.Status == TaskStatus.RanToCompletion && handshakeTcs.Task.Result;
            if (completed != handshakeTcs.Task || !handshakeOk)
            {
                logger.Log($"[SCAN] No handshake on {port}, closing");
                await DisconnectPortAsync(token);
                return false;
            }

            return true;
        }

        private void SerialDataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
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
                    logger.Log($"[SCAN] Confirmed RadioMaster on {serialSession.ConnectedPort}, stop scanning");
                    portScanner.RecordSuccessfulPort(serialSession.ConnectedPort);
                    NotifyConnectionChanged(true);
                }

                var result = channelProcessor.Process(frame);
                foreach (var action in result.Actions)
                    gimbalSender.Send(action);
            }
        }

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

                    int handshakeTimeout = indefiniteHandshakeWait ? SingleCandidateHandshakeTimeoutMs : HandshakeTimeoutMs;

                    bool handshakeTimedOut =
                        !handshakeConfirmed &&
                        serialSession.PortOpenUtc != DateTime.MinValue &&
                        (DateTime.UtcNow - serialSession.PortOpenUtc).TotalMilliseconds > handshakeTimeout;

                    bool dataTimedOut =
                        handshakeConfirmed &&
                        (DateTime.UtcNow - lastDataUtc).TotalMilliseconds > NoDataTimeoutMs;

                    if (missingCount >= 10 || handshakeTimedOut || dataTimedOut)
                    {
                        logger.Log($"[MON] Port {serialSession.ConnectedPort} seems lost (missingCount={missingCount}) → disconnect");
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
                    logger.Log($"MonitorLoop error: {ex.Message}");
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

        private async Task RCSendLoopAsync(CancellationToken token)
        {
            try { Thread.CurrentThread.IsBackground = true; } catch { }
            try { Thread.CurrentThread.Priority = ThreadPriority.Highest; } catch { }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            double nextSendMs = 0;
            const double interval = SendPeriodMs;

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
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Log($"RC send error: {ex.Message}");
                    }
                }

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

        private void DisconnectPortInternal()
        {
            try
            {
                handshakeTcs?.TrySetCanceled();

                if (serialSession.HasOpenPort)
                {
                    logger.Log($"Disconnected from {serialSession.ConnectedPort}");
                }

                serialSession.Close();
            }
            catch
            {
            }

            handshakeConfirmed = false;
            lastDataUtc = DateTime.MinValue;
            indefiniteHandshakeWait = false;

            NotifyConnectionChanged(false);
        }

        private async Task DisconnectPortAsync(CancellationToken token)
        {
            DisconnectPortInternal();
            await Task.Delay(100, token);
        }

        private void NotifyConnectionChanged(bool connected)
        {
            try
            {
                ConnectionChanged?.Invoke(connected);
            }
            catch
            {
            }
        }

        private void SetScanning(bool isScanning)
        {
            scanning = isScanning;

            try
            {
                ScanStateChanged?.Invoke(isScanning);
            }
            catch
            {
            }
        }

        private void EnqueueWork(Func<CancellationToken, Task> work, string errorContext)
        {
            var cts = lifecycleCts;
            if (cts == null || cts.IsCancellationRequested)
                return;

            lock (workQueueSync)
            {
                workQueue = workQueue.ContinueWith(async previous =>
                {
                    try
                    {
                        await previous.ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    if (cts.IsCancellationRequested)
                        return;

                    try
                    {
                        await work(cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        logger.Log($"{errorContext}: {ex.Message}");
                    }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            }
        }

        private async Task StopInternalAsync()
        {
            var cts = lifecycleCts;
            if (cts == null)
                return;

            cts.Cancel();

            var tasks = new List<Task>();
            if (monitorTask != null) tasks.Add(monitorTask);
            if (rcTask != null) tasks.Add(rcTask);

            try
            {
                await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(1000));
            }
            catch (Exception ex)
            {
                logger.Log($"[EXIT] Thread join error: {ex.Message}");
            }

            try
            {
                Task pending;
                lock (workQueueSync)
                {
                    pending = workQueue;
                }

                await Task.WhenAny(pending, Task.Delay(500));
            }
            catch (Exception ex)
            {
                logger.Log($"[EXIT] Work queue drain error: {ex.Message}");
            }

            try
            {
                DisconnectPortInternal();
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                logger.Log($"[EXIT] Port close error: {ex.Message}");
            }

            try { gimbalSender?.Dispose(); }
            catch (Exception ex) { logger.Log($"[EXIT] UDP dispose error: {ex.Message}"); }

            try { serialSession?.Dispose(); }
            catch (Exception ex) { logger.Log($"[EXIT] Serial dispose error: {ex.Message}"); }

            try
            {
                cts.Dispose();
            }
            catch
            {
            }
            finally
            {
                lifecycleCts = null;
            }
        }
    }
}