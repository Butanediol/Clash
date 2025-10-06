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

        // Cache for menu state
        private JsonObject? _cachedConfigs;
        private ProxiesResponse? _cachedProxies;

        public MainWindow()
        {
            InitializeComponent();
            ConfigManager.EnsureDefaultConfigExists();

            _executablePath = Path.Combine(AppContext.BaseDirectory, "ClashAssets", "clash.exe");

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

            // Show cached state immediately if available
            if (_cachedConfigs != null)
            {
                UpdateStateFromConfigs(_cachedConfigs);
            }
            if (_cachedProxies != null)
            {
                UpdateProxyGroupsUI(_cachedProxies);
            }

            // Fetch fresh data in background and update
            try
            {
                var configsTask = _apiService.GetConfigsAsync();
                var proxiesTask = _apiService.GetProxiesAsync();

                await Task.WhenAll(configsTask, proxiesTask);

                _cachedConfigs = await configsTask;
                _cachedProxies = await proxiesTask;

                if (_cachedConfigs != null)
                {
                    UpdateStateFromConfigs(_cachedConfigs);
                }
                if (_cachedProxies != null)
                {
                    UpdateProxyGroupsUI(_cachedProxies);
                }
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
            // Remove only config items, keep separator and buttons at the end
            var itemsToRemove = ConfigMenu.Items.OfType<MenuItem>()
                .Where(item => item.Tag is string)
                .ToList();
            foreach (var item in itemsToRemove)
            {
                ConfigMenu.Items.Remove(item);
            }

            var configs = ConfigManager.GetAvailableConfigs();
            int insertIndex = 0;
            foreach (var configPath in configs)
            {
                var menuItem = new MenuItem
                {
                    Header = Path.GetFileName(configPath),
                    Tag = configPath,
                    IsChecked = configPath.Equals(_currentConfigPath, StringComparison.OrdinalIgnoreCase)
                };
                menuItem.Click += OnConfigSelected;
                ConfigMenu.Items.Insert(insertIndex++, menuItem);
            }
        }

        private async void OnConfigSelected(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string newPath }) return;
            if (_apiService == null) return;

            // Close context menu immediately
            NotifyIcon.ContextMenu.IsOpen = false;

            try
            {
                await _apiService.ReloadConfigAsync(newPath);
                _currentConfigPath = newPath;
                ConfigManager.SetCurrentConfigPath(newPath);
                InitializeApiService();
            }
            catch (Exception ex)
            {
                NotifyIcon.ShowBalloonTip("Error", $"Failed to switch configuration: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
            }
        }

        private void UpdateStateFromConfigs(JsonObject configs)
        {
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

            // Close context menu immediately
            NotifyIcon.ContextMenu.IsOpen = false;

            try
            {
                await _apiService.UpdateModeAsync(newMode);
            }
            catch (Exception ex)
            {
                NotifyIcon.ShowBalloonTip("Error", $"Failed to set mode: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
            }
        }

        private void UpdateProxyGroupsUI(ProxiesResponse response)
        {
            _proxyGroupMenus.ForEach(item => NotifyIcon.ContextMenu.Items.Remove(item));
            _proxyGroupMenus.Clear();

            if (response?.Proxies == null) return;

            var orderedGroups = response.Proxies.TryGetValue("GLOBAL", out var globalGroup) && globalGroup.All != null
                ? globalGroup.All
                    .Select(groupName => response.Proxies.TryGetValue(groupName, out var pg) ? pg : null)
                    .Where(pg => pg != null)
                    .Cast<ProxyGroup>()
                    .ToList()
                : response.Proxies.Values.ToList();

            var selectorGroups = orderedGroups
                .Where(p => p.Type.Equals("Selector", StringComparison.OrdinalIgnoreCase))
                .Select(group => CreateProxyGroupMenuItem(group))
                .ToList();

            _proxyGroupMenus.AddRange(selectorGroups);

            selectorGroups
                .Select((menu, index) => (menu, index))
                .ToList()
                .ForEach(pair => NotifyIcon.ContextMenu.Items.Insert(2 + pair.index, pair.menu));
        }

        private MenuItem CreateProxyGroupMenuItem(ProxyGroup group)
        {
            var groupMenu = new MenuItem { Header = $"{group.Name} ({group.Now})" };
            
            var nodeItems = group.All
                .Select(nodeName => CreateProxyNodeMenuItem(group.Name, nodeName, group.Now))
                .ToList();
            
            nodeItems.ForEach(item => groupMenu.Items.Add(item));
            
            return groupMenu;
        }

        private MenuItem CreateProxyNodeMenuItem(string groupName, string nodeName, string currentNode)
        {
            var nodeItem = new MenuItem
            {
                Header = nodeName,
                Tag = new Tuple<string, string>(groupName, nodeName),
                IsChecked = nodeName.Equals(currentNode, StringComparison.OrdinalIgnoreCase)
            };
            nodeItem.Click += OnProxyNodeSelected;
            return nodeItem;
        }

        private async void OnProxyNodeSelected(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: Tuple<string, string> selection }) return;
            if (_apiService == null) return;
            var (groupName, nodeName) = selection;

            // Close context menu immediately
            NotifyIcon.ContextMenu.IsOpen = false;

            try
            {
                await _apiService.SelectProxyNodeAsync(groupName, nodeName);
            }
            catch (Exception ex)
            {
                NotifyIcon.ShowBalloonTip("Error", $"Failed to set proxy node: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
            }
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
            if (_apiService == null)
            {
                menuItem.IsChecked = !menuItem.IsChecked;
                return;
            }

            var configs = await _apiService.GetConfigsAsync();
            if (configs == null)
            {
                menuItem.IsChecked = !menuItem.IsChecked;
                return;
            }

            var expectedProxy = GetProxyAddress(configs);
            if (expectedProxy == null)
            {
                NotifyIcon.ShowBalloonTip("Error", "Proxy port not configured in Clash.", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
                menuItem.IsChecked = false;
                return;
            }

            if (menuItem.IsChecked)
            {
                SystemProxyManager.SetProxy(expectedProxy);
            }
            else
            {
                SystemProxyManager.DisableProxy();
            }
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
            if (_apiService == null) return;

            var targetState = menuItem.IsChecked;

            // Close context menu immediately
            NotifyIcon.ContextMenu.IsOpen = false;

            try
            {
                await _apiService.UpdateTunModeAsync(targetState);
            }
            catch (Exception ex)
            {
                NotifyIcon.ShowBalloonTip("Error", $"Failed to set TUN mode: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
            }
        }

        private void OnOpenDashboard(object? sender, RoutedEventArgs e)
        {
            var apiDetails = ConfigManager.ReadApiDetails(_currentConfigPath);
            if (apiDetails == null || string.IsNullOrEmpty(apiDetails.DashboardUrl)) return;
            try { Process.Start(new ProcessStartInfo(apiDetails.DashboardUrl) { UseShellExecute = true }); }
            catch (Exception ex) { NotifyIcon.ShowBalloonTip("Error", $"Failed to open dashboard: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error); }
        }

        private async void OnReloadConfig(object? sender, RoutedEventArgs e)
        {
            if (_apiService == null) return;
            if (string.IsNullOrEmpty(_currentConfigPath)) return;

            // Close context menu immediately
            NotifyIcon.ContextMenu.IsOpen = false;

            try
            {
                await _apiService.ReloadConfigAsync(_currentConfigPath);
                NotifyIcon.ShowBalloonTip("Success", "Configuration reloaded", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
            }
            catch (Exception ex)
            {
                NotifyIcon.ShowBalloonTip("Error", $"Failed to reload configuration: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
            }
        }

        private void OnEditConfig(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentConfigPath) || !File.Exists(_currentConfigPath)) { NotifyIcon.ShowBalloonTip("Error", $"Config file not found at: {_currentConfigPath}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error); return; }
            try { Process.Start(new ProcessStartInfo("notepad.exe", _currentConfigPath) { UseShellExecute = true }); }
            catch (Exception ex) { NotifyIcon.ShowBalloonTip("Error", $"Failed to open config file: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error); }
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
                NotifyIcon.ShowBalloonTip("Error", $"Failed to open config folder: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
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
                    case Key.R:
                        OnReloadConfig(sender, e);
                        break;
                    case Key.S:
                        SystemProxyMenuItem.IsChecked = !SystemProxyMenuItem.IsChecked;
                        OnSystemProxyClicked(SystemProxyMenuItem, e);
                        break;
                    case Key.E:
                        TunModeMenuItem.IsChecked = !TunModeMenuItem.IsChecked;
                        OnTunModeClicked(TunModeMenuItem, e);
                        break;
                    case Key.D:
                        OnOpenDashboard(sender, e);
                        break;
                }
            }
            else if (e.KeyboardDevice.Modifiers == ModifierKeys.Alt)
            {
                switch (e.Key)
                {
                    case Key.G:
                        OnModeSelected(GlobalModeItem, e);
                        break;
                    case Key.R:
                        OnModeSelected(RuleModeItem, e);
                        break;
                    case Key.D:
                        OnModeSelected(DirectModeItem, e);
                        break;
                }
            }
        }
    }
}
