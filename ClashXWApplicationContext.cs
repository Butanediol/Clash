
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;

namespace ClashXW
{
    // API response models
    public record ProxyGroup(string Name, string Type, string Now, [property: JsonPropertyName("all")] IReadOnlyList<string> All);
    public record ProxiesResponse([property: JsonPropertyName("proxies")] Dictionary<string, ProxyGroup> Proxies);

    public class ClashXWApplicationContext : ApplicationContext
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly NotifyIcon _notifyIcon;
        private readonly IConfiguration _configuration;
        private Process? _clashProcess;

        private readonly string? _executablePath;
        private readonly string? _configPath;
        private readonly string? _apiBaseUrl;
        private readonly string? _dashboardUrl;

        private readonly ToolStripMenuItem _modeMenu;
        private readonly ToolStripMenuItem _ruleModeItem;
        private readonly ToolStripMenuItem _directModeItem;
        private readonly ToolStripMenuItem _globalModeItem;
        private readonly ToolStripMenuItem _systemProxyMenuItem;
        private readonly ToolStripMenuItem _tunModeMenuItem;

        private readonly List<ToolStripMenuItem> _proxyGroupMenus = new List<ToolStripMenuItem>();
        private readonly ContextMenuStrip _contextMenu;

        public ClashXWApplicationContext()
        {
            _configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _executablePath = _configuration["Clash:ExecutablePath"];
            _configPath = _configuration["Clash:ConfigPath"];
            _apiBaseUrl = _configuration["Clash:ApiBaseUrl"];
            _dashboardUrl = _configuration["Clash:DashboardUrl"];

            // --- Create Menu Structure ---
            _ruleModeItem = new ToolStripMenuItem("Rule", null, OnModeSelected) { Tag = "rule" };
            _directModeItem = new ToolStripMenuItem("Direct", null, OnModeSelected) { Tag = "direct" };
            _globalModeItem = new ToolStripMenuItem("Global", null, OnModeSelected) { Tag = "global" };
            _modeMenu = new ToolStripMenuItem("Mode", null, new ToolStripItem[] { _ruleModeItem, _directModeItem, _globalModeItem });

            _systemProxyMenuItem = new ToolStripMenuItem("Set System Proxy", null, OnSystemProxyClicked) { CheckOnClick = true };
            _tunModeMenuItem = new ToolStripMenuItem("TUN Mode", null, OnTunModeClicked) { CheckOnClick = true };

            _contextMenu = new ContextMenuStrip();
            _contextMenu.Opening += OnContextMenuOpening;
            _contextMenu.Items.Add(_modeMenu);
            _contextMenu.Items.Add(new ToolStripSeparator()); // Separator after Mode
            // Proxy group menus will be inserted here at index 2
            _contextMenu.Items.Add(new ToolStripSeparator()); // Separator after proxy groups
            _contextMenu.Items.Add(_systemProxyMenuItem);
            _contextMenu.Items.Add(_tunModeMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("Start", null, OnStart);
            _contextMenu.Items.Add("Stop", null, OnStop);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("Open Dashboard", null, OnOpenDashboard);
            _contextMenu.Items.Add("Edit Config", null, OnEditConfig);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("Exit", null, OnExit);

            _notifyIcon = new NotifyIcon
            {
                Icon = new System.Drawing.Icon("icon.ico"),
                ContextMenuStrip = _contextMenu,
                Text = "ClashXW",
                Visible = true
            };
        }

        private async void OnContextMenuOpening(object? sender, CancelEventArgs e)
        {
            await Task.WhenAll(UpdateStateFromConfigsAsync(), UpdateProxyGroupsAsync());
        }

        private async Task<JsonObject?> GetConfigsAsync()
        {
            if (string.IsNullOrEmpty(_apiBaseUrl)) return null;
            try
            {
                return await _httpClient.GetFromJsonAsync<JsonObject>($"{_apiBaseUrl}/configs");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get configs: {ex.Message}");
                return null;
            }
        }

        private async Task UpdateStateFromConfigsAsync()
        {
            var configs = await GetConfigsAsync();
            if (configs == null) return;

            UpdateModeUI(configs);
            UpdateSystemProxyState(configs);
            UpdateTunModeState(configs);
        }

        private void UpdateModeUI(JsonObject configs)
        {
            var mode = configs?["mode"]?.GetValue<string>();
            if (string.IsNullOrEmpty(mode)) return;

            _ruleModeItem.Checked = mode.Equals("rule", StringComparison.OrdinalIgnoreCase);
            _directModeItem.Checked = mode.Equals("direct", StringComparison.OrdinalIgnoreCase);
            _globalModeItem.Checked = mode.Equals("global", StringComparison.OrdinalIgnoreCase);
            _modeMenu.Text = $"Mode ({mode})";
        }

        private async void OnModeSelected(object? sender, EventArgs e)
        {
            if (sender is not ToolStripMenuItem { Tag: string newMode }) return;
            if (string.IsNullOrEmpty(_apiBaseUrl)) return;
            try
            {
                var payload = new { mode = newMode };
                await _httpClient.PatchAsJsonAsync($"{_apiBaseUrl}/configs", payload);
                _modeMenu.Text = $"Mode ({newMode})";
            }
            catch (Exception ex) { MessageBox.Show($"Failed to set mode: {ex.Message}", "Error"); }
        }

        private async Task UpdateProxyGroupsAsync()
        {
            if (string.IsNullOrEmpty(_apiBaseUrl)) return;

            foreach (var item in _proxyGroupMenus) { _contextMenu.Items.Remove(item); }
            _proxyGroupMenus.Clear();

            try
            {
                var response = await _httpClient.GetFromJsonAsync<ProxiesResponse>($"{_apiBaseUrl}/proxies");
                if (response?.Proxies == null) return;

                var orderedGroups = new List<ProxyGroup>();
                if (response.Proxies.TryGetValue("GLOBAL", out var globalGroup) && globalGroup.All != null)
                {
                    foreach (var groupName in globalGroup.All) { if (response.Proxies.TryGetValue(groupName, out var pg)) { orderedGroups.Add(pg); } }
                }
                else { orderedGroups.AddRange(response.Proxies.Values); }

                var selectorGroups = orderedGroups.Where(p => p.Type.Equals("Selector", StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var group in selectorGroups)
                {
                    var groupSubItems = group.All.Select(nodeName => new ToolStripMenuItem(nodeName, null, OnProxyNodeSelected)
                    { Tag = new Tuple<string, string>(group.Name, nodeName), Checked = nodeName.Equals(group.Now, StringComparison.OrdinalIgnoreCase) }).ToArray<ToolStripItem>();

                    var groupMenu = new ToolStripMenuItem($"{group.Name} ({group.Now})", null, groupSubItems);
                    _proxyGroupMenus.Add(groupMenu);
                }

                for (int i = 0; i < _proxyGroupMenus.Count; i++) { _contextMenu.Items.Insert(2 + i, _proxyGroupMenus[i]); }
            }
            catch (Exception ex) { Debug.WriteLine($"Failed to get proxy groups: {ex.Message}"); }
        }

        private async void OnProxyNodeSelected(object? sender, EventArgs e)
        {
            if (sender is not ToolStripMenuItem { Tag: Tuple<string, string> selection }) return;
            var (groupName, nodeName) = selection;
            if (string.IsNullOrEmpty(_apiBaseUrl)) return;

            try
            {
                var payload = new { name = nodeName };
                await _httpClient.PutAsJsonAsync($"{_apiBaseUrl}/proxies/{Uri.EscapeDataString(groupName)}", payload);

                foreach (var groupMenu in _proxyGroupMenus.Where(m => m.Text?.StartsWith(groupName) == true))
                {
                    groupMenu.Text = $"{groupName} ({nodeName})";
                    foreach (ToolStripMenuItem nodeItem in groupMenu.DropDownItems)
                    {
                        if (nodeItem.Tag is Tuple<string, string> tag) { nodeItem.Checked = tag.Item2 == nodeName; }
                    }
                    break;
                }
            }
            catch (Exception ex) { MessageBox.Show($"Failed to set proxy node: {ex.Message}", "Error"); }
        }

        private string? GetProxyAddress(JsonObject configs)
        {
            var mixedPort = configs?["mixed-port"]?.GetValue<int>();
            if (mixedPort > 0) return $"127.0.0.1:{mixedPort}";

            var socksPort = configs?["socks-port"]?.GetValue<int>();
            if (socksPort > 0) return $"socks=127.0.0.1:{socksPort}";

            return null;
        }

        private void UpdateSystemProxyState(JsonObject configs)
        {
            var expectedProxy = GetProxyAddress(configs);
            if (expectedProxy == null) { _systemProxyMenuItem.Enabled = false; return; }
            
            _systemProxyMenuItem.Enabled = true;
            _systemProxyMenuItem.Checked = SystemProxyManager.IsProxyEnabled(expectedProxy);
        }

        private async void OnSystemProxyClicked(object? sender, EventArgs e)
        {
            var menuItem = (ToolStripMenuItem)sender!;
            var configs = await GetConfigsAsync();
            if (configs == null) { menuItem.Checked = !menuItem.Checked; return; } // Revert checkmark on failure

            var expectedProxy = GetProxyAddress(configs);
            if (expectedProxy == null) 
            {
                MessageBox.Show("Proxy port not configured in Clash.", "Error");
                menuItem.Checked = false;
                return; 
            }

            if (menuItem.Checked) { SystemProxyManager.SetProxy(expectedProxy); }
            else { SystemProxyManager.DisableProxy(); }
        }

        private void UpdateTunModeState(JsonObject configs)
        {
            try
            {
                var tunEnabled = configs?["tun"]?["enable"]?.GetValue<bool>() ?? false;
                _tunModeMenuItem.Checked = tunEnabled;
            }
            catch { _tunModeMenuItem.Checked = false; }
        }

        private async void OnTunModeClicked(object? sender, EventArgs e)
        {
            var menuItem = (ToolStripMenuItem)sender!;
            if (string.IsNullOrEmpty(_apiBaseUrl)) return;

            try
            {
                var payload = new { tun = new { enable = menuItem.Checked } };
                await _httpClient.PatchAsJsonAsync($"{_apiBaseUrl}/configs", payload);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to set TUN mode: {ex.Message}", "Error");
                menuItem.Checked = !menuItem.Checked; // Revert on failure
            }
        }

        private void OnStart(object? sender, EventArgs e)
        {
            if (_clashProcess != null && !_clashProcess.HasExited) { MessageBox.Show("Clash process is already running.", "Info"); return; }
            if (string.IsNullOrEmpty(_executablePath) || !File.Exists(_executablePath)) { MessageBox.Show($"Executable not found at: {_executablePath}", "Error"); return; }
            try
            {
                _clashProcess = new Process { StartInfo = new ProcessStartInfo { FileName = _executablePath, Arguments = $"-f {_configPath}", UseShellExecute = false, CreateNoWindow = true } };
                _clashProcess.Start();
            }
            catch (Exception ex) { MessageBox.Show($"Failed to start Clash process:\n{ex.Message}", "Error"); }
        }

        private void OnStop(object? sender, EventArgs e)
        {
            if (_clashProcess == null || _clashProcess.HasExited) { MessageBox.Show("Clash process is not running.", "Info"); return; }
            try { _clashProcess.Kill(); _clashProcess = null; }
            catch (Exception ex) { MessageBox.Show($"Failed to stop Clash process:\n{ex.Message}", "Error"); }
        }

        private void OnOpenDashboard(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_dashboardUrl)) return;
            try { Process.Start(new ProcessStartInfo(_dashboardUrl) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show($"Failed to open dashboard:\n{ex.Message}", "Error"); }
        }

        private void OnEditConfig(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_configPath) || !File.Exists(_configPath)) { MessageBox.Show($"Config file not found at: {_configPath}", "Error"); return; }
            try { Process.Start(new ProcessStartInfo("notepad.exe", _configPath) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show($"Failed to open config file:\n{ex.Message}", "Error"); }
        }

        private void OnExit(object? sender, EventArgs e)
        {
            if (_systemProxyMenuItem.Checked) { SystemProxyManager.DisableProxy(); }
            if (_clashProcess != null && !_clashProcess.HasExited) { _clashProcess.Kill(); }
            _notifyIcon.Visible = false;
            Application.Exit();
        }
    }
}
