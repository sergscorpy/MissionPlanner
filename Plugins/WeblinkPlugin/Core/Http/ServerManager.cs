using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WeblinkPlugin.Core.Http.Storage;
using WeblinkPlugin.Core.Rc;

namespace WeblinkPlugin.Core.Http
{
    public sealed class ServerManager : IDisposable
    {
        // -------------------------------------------------
        // clients
        // -------------------------------------------------

        private readonly OrchestratorClient _orchestratorClient;
        private volatile DeviceClient _deviceClient;
        private readonly RcListener _rcListener;

        // -------------------------------------------------
        // orchestrator polling
        // -------------------------------------------------

        private readonly Timer _orchestratorTimer;
        private readonly int _orchIntervalMs = 1000;
        private bool _orchUpdating;

        // -------------------------------------------------
        // device polling
        // -------------------------------------------------

        private CancellationTokenSource _pollCts;
        private Task _pollTask;

        // -------------------------------------------------
        // events
        // -------------------------------------------------

        public event Action<double, double> TelemetryUpdated;

        // -------------------------------------------------
        // ctor
        // -------------------------------------------------

        public ServerManager(string orchestratorUrl)
        {
            _orchestratorClient = new OrchestratorClient(orchestratorUrl);
            _rcListener = new RcListener();

            _orchestratorTimer = new Timer(
                _ => UpdateOrchestratorSafe(),
                null,
                0,
                _orchIntervalMs
            );
        }

        // -------------------------------------------------
        // orchestrator loop
        // -------------------------------------------------

        private async void UpdateOrchestratorSafe()
        {
            if (_orchUpdating)
                return;

            _orchUpdating = true;

            try
            {
                await UpdateOrchestratorAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServerManager] Orchestrator error: {ex}");
            }
            finally
            {
                _orchUpdating = false;
            }
        }

        private async Task UpdateOrchestratorAsync()
        {
            var state = SharedState.Instance;

            // ---- ping orchestrator ----
            var status = await _orchestratorClient.PingAsync();

            if (status == null)
            {
                DisconnectDevice("Orchestrator unreachable");
                return;
            }

            bool deviceAdvertised = status.Status == "connected";

            int port = status.Device_port ?? 9999;

            state.UpdateStatus(
                status.Status,
                state.DeviceStatus,
                status.Ip,
                port
            );

            if (!deviceAdvertised || string.IsNullOrEmpty(status.Ip))
            {
                DisconnectDevice("No device connected");
                return;
            }

            // ---- connect / reconnect device ----
            if (_deviceClient == null ||
                !_deviceClient.Matches(status.Ip, port))
            {
                await ConnectDeviceAsync(status.Ip, port);
            }

            // ---- ping device ----
            var devPing = await _deviceClient.PingAsync();

            if (devPing == null || devPing.Status != "connected")
            {
                DisconnectDevice("Device offline");
            }
        }

        // -------------------------------------------------
        // device connect / disconnect
        // -------------------------------------------------

        private async Task<bool> ConnectDeviceAsync(string ip, int port)
        {
            DisconnectPolling();

            _deviceClient?.Dispose();
            _deviceClient = new DeviceClient(ip, port);

            _rcListener.SetDevice(_deviceClient);

            var ping = await _deviceClient.PingAsync();
            if (ping == null || ping.Status != "connected")
            {
                _rcListener.SetDevice(null);
                _deviceClient.Dispose();
                _deviceClient = null;

                SharedState.Instance.UpdateStatus(
                    SharedState.Instance.ServerStatus,
                    "Device offline"
                );

                return false;
            }

            SharedState.Instance.UpdateStatus(
                SharedState.Instance.ServerStatus,
                "Device connected"
            );

            StartPolling();
            return true;
        }

        private void DisconnectDevice(string reason)
        {
            if (_deviceClient == null)
                return;

            DisconnectPolling();

            _rcListener.SetDevice(null);

            _deviceClient.Dispose();
            _deviceClient = null;

            SharedState.Instance.UpdateStatus(
                SharedState.Instance.ServerStatus,
                reason
            );
        }

        // -------------------------------------------------
        // polling loop (telemetry + channel)
        // -------------------------------------------------

        private void StartPolling()
        {
            if (_pollTask != null)
                return;

            _pollCts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollLoopAsync(_pollCts.Token));
        }

        private void DisconnectPolling()
        {
            try
            {
                _pollCts?.Cancel();
            }
            catch { }

            _pollTask = null;
            _pollCts = null;
        }

        private async Task PollLoopAsync(CancellationToken ct)
        {
            var lastChannelTs = DateTime.MinValue;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var device = _deviceClient;
                    if (device != null)
                    {
                        // ---- telemetry ----
                        var telemetry = await device.GetTelemetryAsync();
                        if (telemetry != null)
                        {
                            SharedState.Instance.UpdateTelemetry(
                                telemetry.Lat,
                                telemetry.Lon,
                                telemetry.Alt,
                                telemetry.Sats
                            );

                            TelemetryUpdated?.Invoke(
                                telemetry.Lat,
                                telemetry.Lon
                            );
                        }

                        // ---- channel (1 Hz) ----
                        if ((DateTime.UtcNow - lastChannelTs).TotalSeconds >= 1)
                        {
                            var channel = await device.GetChannelAsync();
                            if (channel != null)
                            {
                                SharedState.Instance.UpdateChanel(channel.Channel);
                                Debug.WriteLine($"[Channel] {channel.Channel}");
                            }

                            lastChannelTs = DateTime.UtcNow;
                        }
                    }

                    await Task.Delay(200, ct);
                }
            }
            catch (TaskCanceledException)
            {
                // normal shutdown
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServerManager] Poll loop error: {ex}");
            }
        }

        // -------------------------------------------------
        // helper http
        // -------------------------------------------------

        public async Task<bool> SendDataAsync(object payload)
        {
            try
            {
                var state = SharedState.Instance;

                string url =
                    !string.IsNullOrEmpty(state.DeviceIp)
                        ? $"http://{state.DeviceIp}:9999/starlink/location/gps"
                        : "http://localhost:8080/starlink/gps/location";

                using (var http = new HttpClient())
                {
                    string json = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(
                        json,
                        System.Text.Encoding.UTF8,
                        "application/json"
                    );

                    var resp = await http.PostAsync(url, content);
                    return resp.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServerManager] SendDataAsync error: {ex}");
                return false;
            }
        }

        // -------------------------------------------------
        // dispose
        // -------------------------------------------------

        public void Dispose()
        {
            DisconnectPolling();

            _orchestratorTimer?.Dispose();
            _deviceClient?.Dispose();
            _rcListener?.Dispose();
            _orchestratorClient?.Dispose();
        }
    }
}
