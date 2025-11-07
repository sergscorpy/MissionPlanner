using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WeblinkPlugin.Core.Http
{
    public abstract class HttpClientBase : IDisposable
    {
        protected readonly HttpClient Client;
        private bool _disposed;

        protected HttpClientBase()
        {
            Client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
        }

        protected async Task<T> GetJsonAsync<T>(string url) where T : class
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException(nameof(url));

            try
            {
                var sw = Stopwatch.StartNew();
                var response = await Client.GetAsync(url);
                sw.Stop();

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine(string.Format("[HttpClientBase] ❌ GET {0} → {1} ({2} ms)",
                            url, response.StatusCode, sw.ElapsedMilliseconds));
                        return null;
                    }

                    string json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<T>(json);

                    Debug.WriteLine(string.Format("[HttpClientBase] ✅ GET {0} → OK ({1} ms)",
                        url, sw.ElapsedMilliseconds));

                    return result;
                }
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine(string.Format("[HttpClientBase] ⚠️ GET {0} timeout after {1}s",
                    url, Client.Timeout.TotalSeconds));
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("[HttpClientBase] ⚠️ GET {0} failed: {1}", url, ex.Message));
                return null;
            }
        }

        protected async Task<bool> PostJsonAsync(string url, object payload)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException(nameof(url));

            try
            {
                string json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var sw = Stopwatch.StartNew();
                var response = await Client.PostAsync(url, content);
                sw.Stop();

                using (response)
                {
                    Debug.WriteLine(string.Format("[HttpClientBase] POST {0} → {1} ({2} ms)",
                        url, response.StatusCode, sw.ElapsedMilliseconds));

                    return response.IsSuccessStatusCode;
                }
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine(string.Format("[HttpClientBase] ⚠️ POST {0} timeout after {1}s",
                    url, Client.Timeout.TotalSeconds));
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("[HttpClientBase] ⚠️ POST {0} failed: {1}", url, ex.Message));
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Client.Dispose();
        }
    }
}
