using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace LumaTray
{
    public static class SettingsManager
    {
        private const string RootKey = @"Software\LumaTray";
        private const string BrightnessKey = @"Software\LumaTray\Brightness";

        public static bool AutoCheckUpdates
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RootKey, false))
                    {
                        if (key != null)
                        {
                            object val = key.GetValue("AutoCheckUpdates");
                            if (val != null) return Convert.ToInt32(val) != 0;
                        }
                    }
                }
                catch { }
                return true; // Default enabled
            }
            set
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(RootKey))
                    {
                        if (key != null)
                        {
                            key.SetValue("AutoCheckUpdates", value ? 1 : 0, RegistryValueKind.DWord);
                        }
                    }
                }
                catch { }
            }
        }

        public static DateTime LastCheckTime
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RootKey, false))
                    {
                        if (key != null)
                        {
                            string s = key.GetValue("LastCheckTime") as string;
                            DateTime dt;
                            if (DateTime.TryParse(s, out dt)) return dt;
                        }
                    }
                }
                catch { }
                return DateTime.MinValue;
            }
            set
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(RootKey))
                    {
                        if (key != null)
                        {
                            key.SetValue("LastCheckTime", value.ToString("o"), RegistryValueKind.String);
                        }
                    }
                }
                catch { }
            }
        }

        public static string CachedPatchNotes
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RootKey, false))
                    {
                        if (key != null)
                        {
                            string s = key.GetValue("CachedPatchNotes") as string;
                            if (!string.IsNullOrEmpty(s)) return s;
                        }
                    }
                }
                catch { }

                return DefaultPatchNotes;
            }
            set
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(RootKey))
                    {
                        if (key != null && !string.IsNullOrEmpty(value))
                        {
                            key.SetValue("CachedPatchNotes", value, RegistryValueKind.String);
                        }
                    }
                }
                catch { }
            }
        }

        public static string DefaultPatchNotes
        {
            get
            {
                return "Recent Patch Notes (v1.0.1):\n" +
                       "• Dynamic Window Height: Fits up to 2 screens natively without scrollbars.\n" +
                       "• Scrollbar-Free Wheel Scrolling: Smooth mouse wheel navigation for 3+ displays.\n" +
                       "• Fully Asynchronous Engine: Non-blocking DDC/CI & WMI hardware discovery.\n" +
                       "• Zero-Leak Architecture: Clean WMI COM wrapper disposal and HWND lifecycle.\n" +
                       "• Per-Monitor V2 DPI Awareness: Pin-sharp rendering on mixed-DPI screens.\n" +
                       "• Brightness Persistence: Remembers your monitor brightness across updates.";
            }
        }

        public static void SaveBrightness(string monitorKey, int brightness)
        {
            if (string.IsNullOrEmpty(monitorKey)) return;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(BrightnessKey))
                {
                    if (key != null)
                    {
                        string safeName = monitorKey.Replace('\\', '_');
                        key.SetValue(safeName, brightness, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        public static bool TryGetBrightness(string monitorKey, out int brightness)
        {
            brightness = 50;
            if (string.IsNullOrEmpty(monitorKey)) return false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(BrightnessKey, false))
                {
                    if (key != null)
                    {
                        string safeName = monitorKey.Replace('\\', '_');
                        object val = key.GetValue(safeName);
                        if (val != null)
                        {
                            brightness = Convert.ToInt32(val);
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static void SaveAllBrightness(IEnumerable<MonitorInfo> monitors)
        {
            if (monitors == null) return;
            foreach (var m in monitors)
            {
                SaveBrightness(m.FriendlyName, m.CurrentBrightness);
                if (!string.IsNullOrEmpty(m.Id))
                {
                    SaveBrightness(m.Id, m.CurrentBrightness);
                }
            }
        }
    }
}
