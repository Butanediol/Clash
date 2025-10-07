using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ClashXW.Models;

namespace ClashXW.Services
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

        public Task<ClashConfig?> GetConfigsAsync()
        {
            if (string.IsNullOrEmpty(_apiBaseUrl)) return Task.FromResult<ClashConfig?>(null);
            return _httpClient.GetFromJsonAsync<ClashConfig>($"{_apiBaseUrl}/configs");
        }

        public Task<ProxiesResponse?> GetProxiesAsync()
        {
            if (string.IsNullOrEmpty(_apiBaseUrl)) return Task.FromResult<ProxiesResponse?>(null);
            return _httpClient.GetFromJsonAsync<ProxiesResponse>($"{_apiBaseUrl}/proxies");
        }

        public Task UpdateModeAsync(string newMode)
        {
            if (string.IsNullOrEmpty(_apiBaseUrl)) return Task.CompletedTask;
            var payload = new ModeUpdateRequest(newMode);
            return _httpClient.PatchAsJsonAsync($"{_apiBaseUrl}/configs", payload);
        }

        public Task UpdateTunModeAsync(bool isEnabled)
        {
            if (string.IsNullOrEmpty(_apiBaseUrl)) return Task.CompletedTask;
            var payload = new TunUpdateRequest(new TunEnableRequest(isEnabled));
            return _httpClient.PatchAsJsonAsync($"{_apiBaseUrl}/configs", payload);
        }

        public Task SelectProxyNodeAsync(string groupName, string nodeName)
        {
            if (string.IsNullOrEmpty(_apiBaseUrl)) return Task.CompletedTask;
            var payload = new ProxySelectionRequest(nodeName);
            return _httpClient.PutAsJsonAsync($"{_apiBaseUrl}/proxies/{Uri.EscapeDataString(groupName)}", payload);
        }

        public Task ReloadConfigAsync(string configPath)
        {
            if (string.IsNullOrEmpty(_apiBaseUrl)) return Task.CompletedTask;
            var payload = new ConfigReloadRequest(configPath);
            return _httpClient.PutAsJsonAsync($"{_apiBaseUrl}/configs?force=true", payload);
        }
    }
}
