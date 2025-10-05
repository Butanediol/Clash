
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private ClashApiService? _apiService;
        private readonly NotifyIcon _notifyIcon;
        private readonly IConfiguration _configuration;
        private Process? _clashProcess;

        private readonly string? _executablePath;
        private string _currentConfigPath;

        private readonly ToolStripMenuItem _modeMenu;
        private readonly ToolStripMenuItem _ruleModeItem;
        private readonly ToolStripMenuItem _directModeItem;
        private readonly ToolStripMenuItem _globalModeItem;
        private readonly ToolStripMenuItem _systemProxyMenuItem;
        private readonly ToolStripMenuItem _tunModeMenuItem;
        private readonly ToolStripMenuItem _configMenu;

        private readonly List<ToolStripMenuItem> _proxyGroupMenus = new List<ToolStripMenuItem>();
        private readonly ContextMenuStrip _contextMenu;

        public ClashXWApplicationContext()
        {
            // Ensure default config exists before anything else
            ConfigManager.EnsureDefaultConfigExists();

            _configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _executablePath = _configuration["Clash:ExecutablePath"];

            // Initialize services and state from YAML config
            _currentConfigPath = ConfigManager.GetCurrentConfigPath();
            InitializeApiService();

            // --- Create Menu Structure ---
            _ruleModeItem = new ToolStripMenuItem("Rule", null, OnModeSelected) { Tag = "rule" };
            _directModeItem = new ToolStripMenuItem("Direct", null, OnModeSelected) { Tag = "direct" };
            _globalModeItem = new ToolStripMenuItem("Global", null, OnModeSelected) { Tag = "global" };
            _modeMenu = new ToolStripMenuItem("Mode", null, new ToolStripItem[] { _ruleModeItem, _directModeItem, _globalModeItem });

            _configMenu = new ToolStripMenuItem("Configuration");
            _systemProxyMenuItem = new ToolStripMenuItem("Set System Proxy", null, OnSystemProxyClicked) { CheckOnClick = true };
            _tunModeMenuItem = new ToolStripMenuItem("TUN Mode", null, OnTunModeClicked) { CheckOnClick = true };

            _contextMenu = new ContextMenuStrip();
            _contextMenu.Opening += OnContextMenuOpening;
            _contextMenu.Items.Add(_configMenu);
            _contextMenu.Items.Add(_modeMenu);
            _contextMenu.Items.Add(new ToolStripSeparator()); // Separator after Mode
            // Proxy group menus will be inserted here
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

        private void InitializeApiService()
        {
            var apiDetails = ConfigManager.ReadApiDetails(_currentConfigPath);
            if (apiDetails != null)
            {
                _apiService = new ClashApiService(apiDetails.BaseUrl, apiDetails.Secret);
            }
            else
            {
                _apiService = null;
                MessageBox.Show($"Failed to read API details from {_currentConfigPath}. API features will be disabled.", "Config Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void OnContextMenuOpening(object? sender, CancelEventArgs e)
        {
            UpdateConfigsMenu();
            if (_apiService == null) return;
            await Task.WhenAll(UpdateStateFromConfigsAsync(), UpdateProxyGroupsAsync());
        }

        private void UpdateConfigsMenu()
        {
            _configMenu.DropDownItems.Clear();
            var configs = ConfigManager.GetAvailableConfigs();
            foreach (var configPath in configs)
            {
                var menuItem = new ToolStripMenuItem(Path.GetFileName(configPath), null, OnConfigSelected)
                {
                    Tag = configPath,
                    Checked = configPath.Equals(_currentConfigPath, StringComparison.OrdinalIgnoreCase)
                };
                _configMenu.DropDownItems.Add(menuItem);
            }
        }

        private async void OnConfigSelected(object? sender, EventArgs e)
        {
            if (sender is not ToolStripMenuItem { Tag: string newPath }) return;
            if (_apiService == null) return;

            try
            {
                await _apiService.ReloadConfigAsync(newPath);
                _currentConfigPath = newPath;
                ConfigManager.SetCurrentConfigPath(newPath);
                InitializeApiService(); // Re-initialize with new details from the new config
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to switch configuration: {ex.Message}", "Error");
            }
        }

        private async Task UpdateStateFromConfigsAsync()
        {
            if (_apiService == null) return;
            var configs = await _apiService.GetConfigsAsync();
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
            if (_apiService == null) return;
            try
            {
                await _apiService.UpdateModeAsync(newMode);
                _modeMenu.Text = $"Mode ({newMode})";
            }
            catch (Exception ex) { MessageBox.Show($"Failed to set mode: {ex.Message}", "Error"); }
        }

        private async Task UpdateProxyGroupsAsync()
        {
            if (_apiService == null) return;

            foreach (var item in _proxyGroupMenus) { _contextMenu.Items.Remove(item); }
            _proxyGroupMenus.Clear();

            try
            {
                var response = await _apiService.GetProxiesAsync();
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

                // Insert new menus at the correct position (after Mode and its separator)
                for (int i = 0; i < _proxyGroupMenus.Count; i++) { _contextMenu.Items.Insert(3 + i, _proxyGroupMenus[i]); }
            }
            catch (Exception ex) { Debug.WriteLine($"Failed to get proxy groups: {ex.Message}"); }
        }

        private async void OnProxyNodeSelected(object? sender, EventArgs e)
        {
            if (sender is not ToolStripMenuItem { Tag: Tuple<string, string> selection }) return;
            if (_apiService == null) return;
            var (groupName, nodeName) = selection;

            try
            {
                await _apiService.SelectProxyNodeAsync(groupName, nodeName);

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
            if (_apiService == null) { menuItem.Checked = !menuItem.Checked; return; }

            var configs = await _apiService.GetConfigsAsync();
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
            if (_apiService == null) { menuItem.Checked = !menuItem.Checked; return; }

            try
            {
                await _apiService.UpdateTunModeAsync(menuItem.Checked);
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
                _clashProcess = new Process { StartInfo = new ProcessStartInfo { FileName = _executablePath, Arguments = $"-f \"{_currentConfigPath}\"", UseShellExecute = false, CreateNoWindow = true } };
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
            var apiDetails = ConfigManager.ReadApiDetails(_currentConfigPath);
            if (apiDetails == null || string.IsNullOrEmpty(apiDetails.DashboardUrl)) return;
            try { Process.Start(new ProcessStartInfo(apiDetails.DashboardUrl) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show($"Failed to open dashboard:\n{ex.Message}", "Error"); }
        }

        private void OnEditConfig(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentConfigPath) || !File.Exists(_currentConfigPath)) { MessageBox.Show($"Config file not found at: {_currentConfigPath}", "Error"); return; }
            try { Process.Start(new ProcessStartInfo("notepad.exe", _currentConfigPath) { UseShellExecute = true }); }
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
