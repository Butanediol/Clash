using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ClashXW
{
    public class ClashApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _apiBaseUrl;

        public ClashApiService(string? apiBaseUrl, string? apiSecret)
        {
            _apiBaseUrl = apiBaseUrl;
            _httpClient = new HttpClient();
            if (!string.IsNullOrEmpty(apiSecret))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiSecret);
            }
        }

        public Task<JsonObject?> GetConfigsAsync()
        {
            if (string.IsNullOrEmpty(_apiBaseUrl)) return Task.FromResult<JsonObject?>(null);
            return _httpClient.GetFromJsonAsync<JsonObject>($"{_apiBaseUrl}/configs");
        }

        public Task<ProxiesResponse?> GetProxiesAsync()
        {
            if (string.IsNullOrEmpty(_apiBaseUrl)) return Task.FromResult<ProxiesResponse?>(null);
            return _httpClient.GetFromJsonAsync<ProxiesResponse>($"{_apiBaseUrl}/proxies");
        }

        public Task UpdateModeAsync(string newMode)
        {
            if (string.IsNullOrEmpty(_apiBaseUrl)) return Task.CompletedTask;
            var payload = new { mode = newMode };
            return _httpClient.PatchAsJsonAsync($"{_apiBaseUrl}/configs", payload);
        }

        public Task UpdateTunModeAsync(bool isEnabled)
        {
            if (string.IsNullOrEmpty(_apiBaseUrl)) return Task.CompletedTask;
            var payload = new { tun = new { enable = isEnabled } };
            return _httpClient.PatchAsJsonAsync($"{_apiBaseUrl}/configs", payload);
        }

        public Task SelectProxyNodeAsync(string groupName, string nodeName)
        {
            if (string.IsNullOrEmpty(_apiBaseUrl)) return Task.CompletedTask;
            var payload = new { name = nodeName };
            return _httpClient.PutAsJsonAsync($"{_apiBaseUrl}/proxies/{Uri.EscapeDataString(groupName)}", payload);
        }
    }
}
