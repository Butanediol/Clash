using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;

namespace ClashXW.Services
{
    public static class SystemProxyManager
    {
        [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        public static void SetProxy(string proxyAddress)
        {
            using (var registry = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
            {
                if (registry == null) return;

                registry.SetValue("ProxyEnable", 1);
                registry.SetValue("ProxyServer", proxyAddress);
                registry.SetValue("ProxyOverride", "<local>"); // Bypass proxy for local addresses
            }

            NotifySystemOfChange();
        }

        public static void DisableProxy()
        {
            using (var registry = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
            {
                if (registry == null) return;

                registry.SetValue("ProxyEnable", 0);
            }

            NotifySystemOfChange();
        }

        public static bool IsProxyEnabled(string expectedProxyServer)
        {
            using var registry = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
            if (registry == null) return false;

            var proxyEnabledObj = registry.GetValue("ProxyEnable");
            int proxyEnabled = proxyEnabledObj != null ? Convert.ToInt32(proxyEnabledObj) : 0;

            return proxyEnabled == 1
                && registry.GetValue("ProxyServer") is string currentProxyServer
                && string.Equals(currentProxyServer, expectedProxyServer, StringComparison.OrdinalIgnoreCase);
        }

        private static void NotifySystemOfChange()
        {
            // These calls notify all running applications, including browsers, that the proxy settings have changed.
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
    }
}
