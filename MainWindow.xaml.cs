using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClashXW.Models;
using ClashXW.Services;

namespace ClashXW
{
    public partial class MainWindow : Window
    {
        private ClashApiService? _apiService;
        private ClashProcessService? _clashProcessService;
        private ProxyMenuService? _proxyMenuService;
        private ConfigMenuService? _configMenuService;
        private readonly string? _executablePath;
        private string _currentConfigPath;

        private MenuItem? _connectionErrorMenuItem;

        // Cache for menu state
        private ClashConfig? _cachedConfigs;
        private ProxiesResponse? _cachedProxies;

        public MainWindow()
        {
            InitializeComponent();
            Services.ConfigManager.EnsureDefaultConfigExists();

            _executablePath = Path.Combine(AppContext.BaseDirectory, "ClashAssets", "clash.exe");

            _currentConfigPath = Services.ConfigManager.GetCurrentConfigPath();

            // Initialize services
            _clashProcessService = new ClashProcessService(_executablePath);
            _proxyMenuService = new ProxyMenuService();
            _configMenuService = new ConfigMenuService();

            StartClashCore();
            InitializeApiService();

            NotifyIcon.ContextMenu.Opened += OnContextMenuOpening;
            NotifyIcon.ContextMenu.Closed += OnContextMenuClosed;
        }

        private void StartClashCore()
        {
            if (_clashProcessService == null) return;

            try
            {
                _clashProcessService.Start(_currentConfigPath);
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
            var apiDetails = Services.ConfigManager.ReadApiDetails(_currentConfigPath);
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
            if (_configMenuService != null)
            {
                _configMenuService.UpdateConfigsMenu(ConfigMenu, _currentConfigPath, OnConfigSelected);
            }
            if (_apiService == null) return;

            // Show cached state immediately if available
            if (_cachedConfigs != null)
            {
                UpdateStateFromConfigs(_cachedConfigs);
            }
            if (_cachedProxies != null && _proxyMenuService != null)
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
                if (_cachedProxies != null && _proxyMenuService != null)
                {
                    UpdateProxyGroupsUI(_cachedProxies);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update state from API: {ex.Message}");
                if (_proxyMenuService != null)
                {
                    foreach (var item in _proxyMenuService.ProxyGroupMenus)
                    {
                        NotifyIcon.ContextMenu.Items.Remove(item);
                    }
                }
                _connectionErrorMenuItem = new MenuItem { Header = "Failed to connect to Clash core", IsEnabled = false };
                NotifyIcon.ContextMenu.Items.Insert(3, _connectionErrorMenuItem);
            }
        }

        private void OnContextMenuClosed(object? sender, RoutedEventArgs e)
        {
            if (_connectionErrorMenuItem != null)
            {
                NotifyIcon.ContextMenu.Items.Remove(_connectionErrorMenuItem);
                _connectionErrorMenuItem = null;
            }
        }


        private async void OnConfigSelected(string newPath)
        {
            if (_apiService == null) return;

            // Close context menu immediately
            NotifyIcon.ContextMenu.IsOpen = false;

            try
            {
                await _apiService.ReloadConfigAsync(newPath);
                _currentConfigPath = newPath;
                Services.ConfigManager.SetCurrentConfigPath(newPath);
                InitializeApiService();
            }
            catch (Exception ex)
            {
                NotifyIcon.ShowBalloonTip("Error", $"Failed to switch configuration: {ex.Message}", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
            }
        }

        private void UpdateStateFromConfigs(ClashConfig configs)
        {
            UpdateModeUI(configs);
            UpdateSystemProxyState(configs);
            UpdateTunModeState(configs);
        }

        private void UpdateModeUI(ClashConfig configs)
        {
            var mode = configs.Mode;
            if (string.IsNullOrEmpty(mode)) return;

            RuleModeItem.IsChecked = mode.Equals("rule", StringComparison.OrdinalIgnoreCase);
            DirectModeItem.IsChecked = mode.Equals("direct", StringComparison.OrdinalIgnoreCase);
            GlobalModeItem.IsChecked = mode.Equals("global", StringComparison.OrdinalIgnoreCase);
            ModeMenu.Header = $"Mode ({mode.FirstCharToUpper()})";
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
            if (_proxyMenuService == null) return;

            // Remove existing proxy group menus
            foreach (var item in _proxyMenuService.ProxyGroupMenus)
            {
                NotifyIcon.ContextMenu.Items.Remove(item);
            }

            _proxyMenuService.UpdateProxyGroups(response, OnProxyNodeSelected);

            // Add new proxy group menus
            var proxyGroupMenus = _proxyMenuService.ProxyGroupMenus;
            proxyGroupMenus
                .Select((menu, index) => (menu, index))
                .ToList()
                .ForEach(pair => NotifyIcon.ContextMenu.Items.Insert(2 + pair.index, pair.menu));
        }


        private async void OnProxyNodeSelected(string groupName, string nodeName)
        {
            if (_apiService == null) return;

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

        private string? GetProxyAddress(ClashConfig configs)
        {
            var mixedPort = configs.MixedPort;
            if (mixedPort > 0) return $"127.0.0.1:{mixedPort}";

            var socksPort = configs.SocksPort;
            if (socksPort > 0) return $"socks=127.0.0.1:{socksPort}";

            return null;
        }

        private void UpdateSystemProxyState(ClashConfig configs)
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

        private void UpdateTunModeState(ClashConfig configs)
        {
            try
            {
                var tunEnabled = configs.Tun?.Enable ?? false;
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
            var apiDetails = Services.ConfigManager.ReadApiDetails(_currentConfigPath);
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
            _clashProcessService?.Dispose();
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
