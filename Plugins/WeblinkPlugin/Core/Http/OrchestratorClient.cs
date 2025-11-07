using System;
using System.Threading.Tasks;
using WeblinkPlugin.Core.Http.Models;

namespace WeblinkPlugin.Core.Http
{
    internal class OrchestratorClient : HttpClientBase
    {
        private readonly string _baseUrl;

        public OrchestratorClient(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl))
                throw new ArgumentNullException(nameof(baseUrl));

            _baseUrl = baseUrl.TrimEnd('/');
        }

        public async Task<OrchestratorStatus> PingAsync()
        {
            var url = string.Format("{0}/api/status/ping", _baseUrl);
            return await GetJsonAsync<OrchestratorStatus>(url);
        }

        public async Task<bool> IsAliveAsync()
        {
            var result = await PingAsync();
            return result != null && string.Equals(result.Status, "connected", StringComparison.OrdinalIgnoreCase);
        }
    }
}
