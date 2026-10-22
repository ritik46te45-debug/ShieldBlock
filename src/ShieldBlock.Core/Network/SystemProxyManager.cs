using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ShieldBlock.Core.Network
{
    public static class SystemProxyManager
    {
        private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        /// <summary>
        /// Enables the system-level HTTP/HTTPS proxy pointing to the local ShieldBlock filtering engine.
        /// Broadcasts the change to Windows and all browsers immediately without needing a restart.
        /// </summary>
        public static bool EnableSystemProxy(string proxyServer = "127.0.0.1:8888", string? proxyBypass = null)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true);
                if (key == null) return false;

                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);

                string bypass = proxyBypass ?? "localhost;127.*;10.*;172.16.*;192.168.*;<local>";
                key.SetValue("ProxyOverride", bypass, RegistryValueKind.String);

                NotifyWinINet();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SystemProxyManager] Error enabling system proxy: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disables the system proxy and restores direct network access.
        /// </summary>
        public static bool DisableSystemProxy()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true);
                if (key == null) return false;

                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);

                NotifyWinINet();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SystemProxyManager] Error disabling system proxy: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks whether the system proxy is currently pointing to our local loopback proxy.
        /// </summary>
        public static bool IsProxyEnabled(string proxyServer = "127.0.0.1:8888")
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, false);
                if (key == null) return false;

                int enabled = (int)(key.GetValue("ProxyEnable") ?? 0);
                string? server = key.GetValue("ProxyServer") as string;

                return enabled == 1 && string.Equals(server, proxyServer, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Notifies Windows (WinINet) that internet settings changed so all running browsers pick it up instantly.
        /// </summary>
        private static void NotifyWinINet()
        {
            try
            {
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            }
            catch { }
        }
    }
}
