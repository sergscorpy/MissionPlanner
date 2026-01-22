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
            var url = string.Format("{0}/", BaseUrl);
            return await GetJsonAsync<DeviceStatus>(url);
        }

        public async Task<TelemetryPacket> GetTelemetryAsync()
        {
            var url = string.Format("{0}/starlink/gps/location", BaseUrl);
            return await GetJsonAsync<TelemetryPacket>(url);
        }

        public bool Matches(string ip, int port)
        {
            return string.Equals(BaseIp, ip, StringComparison.OrdinalIgnoreCase) && BasePort == port;
        }
    }
}
