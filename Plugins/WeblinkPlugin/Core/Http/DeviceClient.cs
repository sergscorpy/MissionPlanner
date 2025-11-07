using System;
using System.Threading.Tasks;
using WeblinkPlugin.Core.Http.Models;

namespace WeblinkPlugin.Core.Http
{
    internal class DeviceClient : HttpClientBase
    {
        public string BaseIp { get; }
        public int BasePort { get; }
        public string BaseUrl { get; }

        public DeviceClient(string ip, int port)
        {
            BaseIp = ip ?? throw new ArgumentNullException(nameof(ip));
            BasePort = port > 0 ? port : throw new ArgumentOutOfRangeException(nameof(port));
            BaseUrl = string.Format("http://{0}:{1}", ip, port);
        }

        public async Task<DeviceStatus> PingAsync()
        {
            var url = string.Format("{0}/api/status/ping", BaseUrl);
            return await GetJsonAsync<DeviceStatus>(url);
        }

        public async Task<TelemetryPacket> GetTelemetryAsync()
        {
            var url = string.Format("{0}/api/telemetry", BaseUrl);
            return await GetJsonAsync<TelemetryPacket>(url);
        }

        public async Task<bool> RestartAsync()
        {
            var url = string.Format("{0}/api/restart", BaseUrl);
            var payload = new { command = "restart" };
            return await PostJsonAsync(url, payload);
        }

        public bool Matches(string ip, int port)
        {
            return string.Equals(BaseIp, ip, StringComparison.OrdinalIgnoreCase) && BasePort == port;
        }
    }
}
