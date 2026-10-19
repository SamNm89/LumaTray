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
                    using (var key = Registry.CurrentUser.OpenSubKey(RootKey, true))
                    {
                        if (key != null)
                        {
                            string s = key.GetValue("CachedPatchNotes") as string;
                            if (!string.IsNullOrEmpty(s))
                            {
                                if (IsCorruptedOrRaw(s))
                                {
                                    try { key.DeleteValue("CachedPatchNotes", false); } catch { }
                                    return DefaultPatchNotes;
                                }
                                return s;
                            }
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

        private static bool IsCorruptedOrRaw(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            foreach (char c in s)
            {
                if (c == '\uFFFD' || c == 0x00F0 || c == 0x0178 || (c >= 0x0080 && c <= 0x009F))
                    return true;
            }
            if (s.StartsWith("##") || s.Contains("<kbd>"))
                return true;
            return false;
        }

        public static string DefaultPatchNotes
        {
            get
            {
                return "Recent Changes (v1.0.2):\n" +
                       "• Hardened Auto-Updater: SHA-256 verification & resilient atomic swap.\n" +
                       "• Debounced Brightness Persistence: Background thread saves with zero UI lag.\n" +
                       "• Unique Monitor Hardware Keys: EDID serial & DeviceID tracking.\n" +
                       "• Optimized Card Lifecycle: Skips UI card teardown when displays unchanged.\n" +
                       "• Fast Network & Cancel: Instant proxy-free queries with interactive dialog.\n" +
                       "• Dynamic Window Height & Wheel Scroll: Fits up to 2 screens & scrolls 3+.";
            }
        }

        public static string SanitizeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "Unknown";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in key)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            return sb.ToString();
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
                        string safeName = SanitizeKey(monitorKey);
                        key.SetValue(safeName, brightness, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        public static bool TryGetBrightness(string monitorKey, out int brightness)
        {
            brightness = 0;
            if (string.IsNullOrEmpty(monitorKey)) return false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(BrightnessKey, false))
                {
                    if (key != null)
                    {
                        string safeName = SanitizeKey(monitorKey);
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
                if (!string.IsNullOrEmpty(m.Id))
                {
                    SaveBrightness(m.Id, m.CurrentBrightness);
                }
            }
        }
    }
}
