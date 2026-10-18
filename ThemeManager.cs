using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LumaTray
{
    public static class ThemeManager
    {
        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        public static extern int SetPreferredAppMode(int mode); // 0=Default, 1=AllowDark, 2=ForceDark, 3=ForceLight

        public static bool IsDarkMode { get; private set; }
        public static Color BackgroundColor { get; private set; }
        public static Color CardColor { get; private set; }
        public static Color BorderColor { get; private set; }
        public static Color TextColor { get; private set; }
        public static Color DimTextColor { get; private set; }
        public static Color TrackColor { get; private set; }
        public static Color AccentColor { get; private set; }
        public static Color HoverColor { get; private set; }
        public static Color FocusHighlight { get; private set; }

        static ThemeManager()
        {
            ReloadTheme();
        }

        public static void ReloadTheme()
        {
            // 1. Detect Dark / Light Mode from Windows Personalize
            bool dark = true;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("SystemUsesLightTheme") ?? key.GetValue("AppsUseLightTheme");
                        if (val != null)
                        {
                            dark = Convert.ToInt32(val) == 0;
                        }
                    }
                }
            }
            catch { }
            IsDarkMode = dark;

            // Enable dark context menus via uxtheme
            try
            {
                SetPreferredAppMode(dark ? 2 : 0);
            }
            catch { }

            // 2. Read Windows Accent Color from DWM
            Color accent = Color.FromArgb(0, 120, 215); // Fallback Windows Fluent Blue
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AccentColor");
                        if (val != null)
                        {
                            uint abgr = Convert.ToUInt32(val);
                            byte r = (byte)(abgr & 0xFF);
                            byte g = (byte)((abgr >> 8) & 0xFF);
                            byte b = (byte)((abgr >> 16) & 0xFF);
                            if (r != 0 || g != 0 || b != 0)
                            {
                                accent = Color.FromArgb(r, g, b);
                            }
                        }
                    }
                }
            }
            catch { }

            if (dark)
            {
                BackgroundColor = Color.FromArgb(32, 32, 32);
                CardColor = Color.FromArgb(40, 40, 40);
                BorderColor = Color.FromArgb(50, 50, 50);
                TextColor = Color.FromArgb(245, 245, 245);
                DimTextColor = Color.FromArgb(170, 170, 170);
                TrackColor = Color.FromArgb(80, 80, 80);
                HoverColor = Color.FromArgb(48, 48, 48);
                // On dark theme, brighten accent slightly for vivid contrast
                AccentColor = BlendColor(accent, Color.White, 0.30f);
                FocusHighlight = Color.FromArgb(55, 55, 55);
            }
            else
            {
                BackgroundColor = Color.FromArgb(243, 243, 243);
                CardColor = Color.FromArgb(255, 255, 255);
                BorderColor = Color.FromArgb(215, 215, 215);
                TextColor = Color.FromArgb(24, 24, 24);
                DimTextColor = Color.FromArgb(100, 100, 100);
                TrackColor = Color.FromArgb(210, 210, 210);
                HoverColor = Color.FromArgb(232, 232, 232);
                AccentColor = accent;
                FocusHighlight = Color.FromArgb(228, 228, 228);
            }
        }

        private static Color BlendColor(Color a, Color b, float ratio)
        {
            int r = (int)(a.R + (b.R - a.R) * ratio);
            int g = (int)(a.G + (b.G - a.G) * ratio);
            int bl = (int)(a.B + (b.B - a.B) * ratio);
            return Color.FromArgb(Math.Min(255, Math.Max(0, r)), Math.Min(255, Math.Max(0, g)), Math.Min(255, Math.Max(0, bl)));
        }
    }
}
