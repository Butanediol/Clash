using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClashXW
{
    public record ProxyGroup(string Name, string Type, string Now, [property: JsonPropertyName("all")] IReadOnlyList<string> All);
    public record ProxiesResponse([property: JsonPropertyName("proxies")] Dictionary<string, ProxyGroup> Proxies);

    public partial class MainWindow : Window
    {
        private ClashApiService? _apiService;
        private Process? _clashProcess;
        private readonly string? _executablePath;
        private string _currentConfigPath;

        private readonly List<MenuItem> _proxyGroupMenus = new List<MenuItem>();
        private MenuItem? _connectionErrorMenuItem;

        public MainWindow()
        {
            InitializeComponent();
            ConfigManager.EnsureDefaultConfigExists();

            var parentDir = Directory.GetParent(AppContext.BaseDirectory)?.FullName;
            if (parentDir != null)
            {
                _executablePath = Path.Combine(parentDir, "ClashAssets", "clash.exe");
            }

            _currentConfigPath = ConfigManager.GetCurrentConfigPath();
            StartClashCore();
            InitializeApiService();

            NotifyIcon.ContextMenu.Opened += OnContextMenuOpening;
        }

        private void StartClashCore()
        {
            if (string.IsNullOrEmpty(_executablePath) || !File.Exists(_executablePath))
            {
                MessageBox.Show($"Executable not found at: {_executablePath}", "Error");
                Application.Current.Shutdown();
                return;
            }
            try
            {
                var assetsDir = Path.GetDirectoryName(_executablePath);
                _clashProcess = new Process { StartInfo = new ProcessStartInfo { FileName = _executablePath, Arguments = $"-d \"{assetsDir}\" -f \"{_currentConfigPath}\"", UseShellExecute = false, CreateNoWindow = true } };
                _clashProcess.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start Clash process:\n{ex.Message}", "Error");
                Application.Current.Shutdown();
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Hide();
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
                MessageBox.Show($"Failed to read API details from {_currentConfigPath}. API features will be disabled.", "Config Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void OnContextMenuOpening(object? sender, RoutedEventArgs e)
        {
            if (_connectionErrorMenuItem != null)
            {
                NotifyIcon.ContextMenu.Items.Remove(_connectionErrorMenuItem);
                _connectionErrorMenuItem = null;
            }

            UpdateConfigsMenu();
            if (_apiService == null) return;

            try
            {
                await Task.WhenAll(UpdateStateFromConfigsAsync(), UpdateProxyGroupsAsync());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update state from API: {ex.Message}");
                foreach (var item in _proxyGroupMenus) { NotifyIcon.ContextMenu.Items.Remove(item); }
                _proxyGroupMenus.Clear();
                _connectionErrorMenuItem = new MenuItem { Header = "Failed to connect to Clash core", IsEnabled = false };
                NotifyIcon.ContextMenu.Items.Insert(3, _connectionErrorMenuItem);
            }
        }

        private void UpdateConfigsMenu()
        {
            ConfigMenu.Items.Clear();
            var configs = ConfigManager.GetAvailableConfigs();
            foreach (var configPath in configs)
            {
                var menuItem = new MenuItem
                {
                    Header = Path.GetFileName(configPath),
                    Tag = configPath,
                    IsChecked = configPath.Equals(_currentConfigPath, StringComparison.OrdinalIgnoreCase)
                };
                menuItem.Click += OnConfigSelected;
                ConfigMenu.Items.Add(menuItem);
            }
        }

        private async void OnConfigSelected(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string newPath }) return;
            if (_apiService == null) return;

            try
            {
                await _apiService.ReloadConfigAsync(newPath);
                _currentConfigPath = newPath;
                ConfigManager.SetCurrentConfigPath(newPath);
                InitializeApiService();
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

            RuleModeItem.IsChecked = mode.Equals("rule", StringComparison.OrdinalIgnoreCase);
            DirectModeItem.IsChecked = mode.Equals("direct", StringComparison.OrdinalIgnoreCase);
            GlobalModeItem.IsChecked = mode.Equals("global", StringComparison.OrdinalIgnoreCase);
            ModeMenu.Header = $"Mode ({mode})";
        }

        private async void OnModeSelected(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string newMode }) return;
            if (_apiService == null) return;
            try
            {
                await _apiService.UpdateModeAsync(newMode);
                ModeMenu.Header = $"Mode ({newMode})";
            }
            catch (Exception ex) { MessageBox.Show($"Failed to set mode: {ex.Message}", "Error"); }
        }

        private async Task UpdateProxyGroupsAsync()
        {
            if (_apiService == null) return;

            foreach (var item in _proxyGroupMenus) { NotifyIcon.ContextMenu.Items.Remove(item); }
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
                    var groupMenu = new MenuItem { Header = $"{group.Name} ({group.Now})" };

                    foreach (var nodeName in group.All)
                    {
                        var nodeItem = new MenuItem
                        {
                            Header = nodeName,
                            Tag = new Tuple<string, string>(group.Name, nodeName),
                            IsChecked = nodeName.Equals(group.Now, StringComparison.OrdinalIgnoreCase)
                        };
                        nodeItem.Click += OnProxyNodeSelected;
                        groupMenu.Items.Add(nodeItem);
                    }

                    _proxyGroupMenus.Add(groupMenu);
                }

                for (int i = 0; i < _proxyGroupMenus.Count; i++) { NotifyIcon.ContextMenu.Items.Insert(3 + i, _proxyGroupMenus[i]); }
            }
            catch (Exception ex) { Debug.WriteLine($"Failed to get proxy groups: {ex.Message}"); }
        }

        private async void OnProxyNodeSelected(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: Tuple<string, string> selection }) return;
            if (_apiService == null) return;
            var (groupName, nodeName) = selection;

            try
            {
                await _apiService.SelectProxyNodeAsync(groupName, nodeName);

                foreach (var groupMenu in _proxyGroupMenus)
                {
                    if (groupMenu.Header is string headerString && headerString.StartsWith(groupName))
                    {
                        groupMenu.Header = $"{groupName} ({nodeName})";
                        foreach (MenuItem nodeItem in groupMenu.Items)
                        {
                            if (nodeItem.Tag is Tuple<string, string> tag) { nodeItem.IsChecked = tag.Item2 == nodeName; }
                        }
                        break;
                    }
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
            if (expectedProxy == null) { SystemProxyMenuItem.IsEnabled = false; return; }
            
            SystemProxyMenuItem.IsEnabled = true;
            SystemProxyMenuItem.IsChecked = SystemProxyManager.IsProxyEnabled(expectedProxy);
        }

        private async void OnSystemProxyClicked(object? sender, RoutedEventArgs e)
        {
            var menuItem = (MenuItem)sender!;
            if (_apiService == null) { menuItem.IsChecked = !menuItem.IsChecked; return; }

            var configs = await _apiService.GetConfigsAsync();
            if (configs == null) { menuItem.IsChecked = !menuItem.IsChecked; return; } 

            var expectedProxy = GetProxyAddress(configs);
            if (expectedProxy == null) 
            {
                MessageBox.Show("Proxy port not configured in Clash.", "Error");
                menuItem.IsChecked = false;
                return; 
            }

            if (menuItem.IsChecked) { SystemProxyManager.SetProxy(expectedProxy); }
            else { SystemProxyManager.DisableProxy(); }
        }

        private void UpdateTunModeState(JsonObject configs)
        {
            try
            {
                var tunEnabled = configs?["tun"]?["enable"]?.GetValue<bool>() ?? false;
                TunModeMenuItem.IsChecked = tunEnabled;
            }
            catch { TunModeMenuItem.IsChecked = false; }
        }

        private async void OnTunModeClicked(object? sender, RoutedEventArgs e)
        {
            var menuItem = (MenuItem)sender!;
            if (_apiService == null) { menuItem.IsChecked = !menuItem.IsChecked; return; }

            try
            {
                await _apiService.UpdateTunModeAsync(menuItem.IsChecked);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to set TUN mode: {ex.Message}", "Error");
                menuItem.IsChecked = !menuItem.IsChecked; // Revert on failure
            }
        }

        private void OnOpenDashboard(object? sender, RoutedEventArgs e)
        {
            var apiDetails = ConfigManager.ReadApiDetails(_currentConfigPath);
            if (apiDetails == null || string.IsNullOrEmpty(apiDetails.DashboardUrl)) return;
            try { Process.Start(new ProcessStartInfo(apiDetails.DashboardUrl) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show($"Failed to open dashboard:\n{ex.Message}", "Error"); }
        }

        private void OnEditConfig(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentConfigPath) || !File.Exists(_currentConfigPath)) { MessageBox.Show($"Config file not found at: {_currentConfigPath}", "Error"); return; }
            try { Process.Start(new ProcessStartInfo("notepad.exe", _currentConfigPath) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show($"Failed to open config file:\n{ex.Message}", "Error"); }
        }

        private void OnOpenConfigFolder(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentConfigPath)) return;
            var configFolder = Path.GetDirectoryName(_currentConfigPath);
            if (configFolder == null || !Directory.Exists(configFolder)) return;
            try
            {
                Process.Start(new ProcessStartInfo(configFolder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open config folder:\n{ex.Message}", "Error");
            }
        }

        private void OnExit(object? sender, RoutedEventArgs e)
        {
            if (SystemProxyMenuItem.IsChecked) { SystemProxyManager.DisableProxy(); }
            if (_clashProcess != null && !_clashProcess.HasExited) { _clashProcess.Kill(); }
            NotifyIcon.Dispose();
            Application.Current.Shutdown();
        }

        private void OnContextMenuKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.O:
                        OnOpenConfigFolder(sender, e);
                        break;
                    case Key.S:
                        SystemProxyMenuItem.IsChecked = !SystemProxyMenuItem.IsChecked;
                        OnSystemProxyClicked(SystemProxyMenuItem, e);
                        break;
                    case Key.E:
                        TunModeMenuItem.IsChecked = !TunModeMenuItem.IsChecked;
                        OnTunModeClicked(TunModeMenuItem, e);
                        break;
                }
            }
        }
    }
}
