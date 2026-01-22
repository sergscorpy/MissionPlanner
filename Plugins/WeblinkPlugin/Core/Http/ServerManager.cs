using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WeblinkPlugin.Core.Http.Storage;

namespace WeblinkPlugin.Core.Http
{
    public class ServerManager : IDisposable
    {
        private readonly OrchestratorClient _orchestratorClient;
        private DeviceClient _deviceClient;

        private readonly Timer _orchestratorTimer;
        private readonly Timer _telemetryTimer;
        private readonly int _orchIntervalMs = 2000;
        private readonly int _telemetryIntervalMs = 500;

        private bool _orchUpdating;
        private bool _telemetryUpdating;

        public event Action<double, double> TelemetryUpdated;

        public ServerManager(string orchestratorUrl)
        {
            _orchestratorClient = new OrchestratorClient(orchestratorUrl);

            _orchestratorTimer = new Timer(async _ => await UpdateOrchestratorAsync(), null, 0, _orchIntervalMs);

            _telemetryTimer = new Timer(async _ => await UpdateTelemetryAsync(), null, Timeout.Infinite, Timeout.Infinite);
        }

        private async Task UpdateOrchestratorAsync()
        {
            if (_orchUpdating)
                return;

            _orchUpdating = true;

            try
            {
                var state = SharedState.Instance;
                var orchestratorStatus = await _orchestratorClient.PingAsync();

                if (orchestratorStatus == null)
                {
                    state.UpdateStatus("Orchestrator unreachable", "Unknown");
                    _deviceClient = null;
                    _telemetryTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    return;
                }

                bool deviceConnected = orchestratorStatus.Status == "connected";
                state.UpdateStatus(
                    deviceConnected ? "Orchestrator connected" : "Orchestrator disconnected",
                    state.DeviceStatus,
                    orchestratorStatus.Device_ip,
                    9999
                );

                if (deviceConnected && !string.IsNullOrEmpty(orchestratorStatus.Device_ip))
                {
                    if (_deviceClient == null ||
                        !_deviceClient.Matches(orchestratorStatus.Device_ip, orchestratorStatus.Device_port))
                    {
                        _deviceClient?.Dispose();
                        _deviceClient = new DeviceClient(orchestratorStatus.Device_ip, orchestratorStatus.Device_port);
                        Debug.WriteLine($"[ServerManager] Connected to device {orchestratorStatus.Device_ip}:{orchestratorStatus.Device_port}");
                    }

                    var deviceStatus = await _deviceClient.PingAsync();
                    if (deviceStatus?.Status == "connected")
                    {
                        state.UpdateStatus(state.ServerStatus, "Device connected");

                        _telemetryTimer.Change(0, _telemetryIntervalMs);
                    }
                    else
                    {
                        state.UpdateStatus(state.ServerStatus, "Device offline");
                        _telemetryTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    }
                }
                else
                {
                    _deviceClient = null;
                    state.UpdateStatus(state.ServerStatus, "No device connected");
                    _telemetryTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServerManager] Orchestrator loop error: {ex.Message}");
            }
            finally
            {
                _orchUpdating = false;
            }
        }

        private async Task UpdateTelemetryAsync()
        {
            if (_telemetryUpdating || _deviceClient == null)
                return;

            _telemetryUpdating = true;

            try
            {
                var telemetry = await _deviceClient.GetTelemetryAsync();
                if (telemetry != null)
                {
                    SharedState.Instance.UpdateTelemetry(telemetry.Lat, telemetry.Lon, telemetry.Alt, telemetry.Sats);
                    TelemetryUpdated?.Invoke(telemetry.Lat, telemetry.Lon);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServerManager] Telemetry loop error: {ex.Message}");
            }
            finally
            {
                _telemetryUpdating = false;
            }
        }

        public async Task<bool> SendDataAsync(object payload)
        {
            try
            {
                var state = SharedState.Instance;
                string targetUrl = !string.IsNullOrEmpty(state.DeviceIp) && state.DevicePort > 0
                    ? $"http://{state.DeviceIp}:{state.DevicePort}/starlink/gps/location"
                    : "http://localhost:8080/starlink/gps/location";

                using (var http = new HttpClient())
                {
                    string json = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    var resp = await http.PostAsync(targetUrl, content);
                    return resp.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServerManager] SendDataAsync error: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            _orchestratorTimer?.Dispose();
            _telemetryTimer?.Dispose();
            _deviceClient?.Dispose();
            _orchestratorClient?.Dispose();
        }
    }
}
