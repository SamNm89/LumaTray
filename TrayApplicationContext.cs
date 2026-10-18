using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LumaTray
{
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly MonitorManager _manager;
        private readonly FlyoutForm _flyout;
        private readonly OsdForm _osd;
        private readonly HotkeyMessageWindow _msgWindow;

        private MenuItem _menuItemLink;
        private MenuItem _menuItemHotkeys;
        private MenuItem _menuItemStartup;

        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "LumaTray";

        public TrayApplicationContext()
        {
            ThemeManager.ReloadTheme();

            _manager = new MonitorManager();
            _flyout = new FlyoutForm(_manager);
            _osd = new OsdForm();
            _flyout.RequestSettings += ShowContextMenu;

            _manager.BrightnessChanged += OnBrightnessChanged;
            _manager.MonitorsRefreshed += UpdateTrayTooltip;

            // Message host Form for Hotkeys, Display Changes, and Theme Changes
            _msgWindow = new HotkeyMessageWindow(this);

            // Context Menu
            ContextMenu menu = new ContextMenu();

            MenuItem itemOpen = new MenuItem("Open LumaTray", delegate { _flyout.ToggleVisibility(); });
            itemOpen.DefaultItem = true;
            menu.MenuItems.Add(itemOpen);

            menu.MenuItems.Add("-");

            _menuItemLink = new MenuItem("Link All Displays", delegate
            {
                _flyout.IsLinked = !_flyout.IsLinked;
                _menuItemLink.Checked = _flyout.IsLinked;
            });
            menu.MenuItems.Add(_menuItemLink);

            _menuItemHotkeys = new MenuItem("Global Hotkeys (Ctrl+Alt+Up/Down)", delegate
            {
                ToggleHotkeys();
            });
            _menuItemHotkeys.Checked = true;
            menu.MenuItems.Add(_menuItemHotkeys);

            _menuItemStartup = new MenuItem("Start with Windows", delegate
            {
                ToggleStartup();
            });
            _menuItemStartup.Checked = IsStartupEnabled();
            menu.MenuItems.Add(_menuItemStartup);

            MenuItem itemRefresh = new MenuItem("Refresh Displays", delegate
            {
                _manager.RefreshMonitors();
            });
            menu.MenuItems.Add(itemRefresh);

            menu.MenuItems.Add("-");

            MenuItem itemAbout = new MenuItem("About LumaTray...", delegate
            {
                long mem = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                MessageBox.Show(
                    "LumaTray v2.0 (Compact)\nUltra-lightweight monitor brightness controller for Windows.\n\n" +
                    "• RAM usage: ~" + mem + " MB (vs 150-200MB in Electron)\n" +
                    "• Supports DDC/CI external monitors & WMI laptop screens\n" +
                    "• In-panel controls: Arrow keys (Left/Right to adjust, Up/Down/Tab to switch row)\n" +
                    "• Hotkeys: Ctrl+Alt+Up (increase), Ctrl+Alt+Down (decrease)\n" +
                    "• Automatic Windows Dark/Light theme & Accent Color sync",
                    "About LumaTray",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
            menu.MenuItems.Add(itemAbout);

            MenuItem itemExit = new MenuItem("Exit", delegate { ExitApp(); });
            menu.MenuItems.Add(itemExit);

            // Initialize Tray Icon
            _trayIcon = new NotifyIcon
            {
                Icon = CreateTrayIcon(),
                ContextMenu = menu,
                Visible = true,
                Text = "LumaTray"
            };

            _trayIcon.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    _flyout.ToggleVisibility();
                }
            };

            RegisterHotkeys();
            UpdateTrayTooltip();

            // Initial memory trim
            NativeMethods.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }

        private Icon CreateTrayIcon()
        {
            try
            {
                Icon appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIcon != null) return appIcon;
            }
            catch { }

            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Draw Sun Center
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 204, 0)))
                {
                    g.FillEllipse(brush, 10, 10, 12, 12);
                }

                // Draw Sun Rays
                using (Pen rayPen = new Pen(Color.FromArgb(255, 215, 64), 2f))
                {
                    rayPen.StartCap = LineCap.Round;
                    rayPen.EndCap = LineCap.Round;

                    g.DrawLine(rayPen, 16, 3, 16, 7);   // Top
                    g.DrawLine(rayPen, 16, 25, 16, 29); // Bottom
                    g.DrawLine(rayPen, 3, 16, 7, 16);   // Left
                    g.DrawLine(rayPen, 25, 16, 29, 16); // Right

                    g.DrawLine(rayPen, 7, 7, 10, 10);   // Top-Left
                    g.DrawLine(rayPen, 22, 10, 25, 7);  // Top-Right
                    g.DrawLine(rayPen, 7, 25, 10, 22);  // Bottom-Left
                    g.DrawLine(rayPen, 22, 22, 25, 25); // Bottom-Right
                }

                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        private void OnBrightnessChanged(MonitorInfo monitor, int brightness)
        {
            UpdateTrayTooltip();
        }

        private void UpdateTrayTooltip()
        {
            var monitors = _manager.GetMonitors();
            if (monitors.Count == 0)
            {
                _trayIcon.Text = "LumaTray\nNo displays detected";
            }
            else if (monitors.Count == 1)
            {
                string text = "LumaTray\n" + monitors[0].FriendlyName + ": " + monitors[0].CurrentBrightness + "%";
                if (text.Length > 63) text = text.Substring(0, 60) + "...";
                _trayIcon.Text = text;
            }
            else
            {
                string text = "LumaTray (" + monitors.Count + " Displays)";
                _trayIcon.Text = text;
            }
        }

        private void ShowContextMenu()
        {
            ThemeManager.ReloadTheme();
            Point pt = Cursor.Position;
            _trayIcon.ContextMenu.Show(_flyout, _flyout.PointToClient(pt));
        }

        private void RegisterHotkeys()
        {
            if (_msgWindow != null && _msgWindow.Handle != IntPtr.Zero)
            {
                NativeMethods.RegisterHotKey(_msgWindow.Handle, 1, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_UP);
                NativeMethods.RegisterHotKey(_msgWindow.Handle, 2, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_DOWN);
            }
        }

        private void UnregisterHotkeys()
        {
            if (_msgWindow != null && _msgWindow.Handle != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(_msgWindow.Handle, 1);
                NativeMethods.UnregisterHotKey(_msgWindow.Handle, 2);
            }
        }

        private void ToggleHotkeys()
        {
            if (_menuItemHotkeys.Checked)
            {
                UnregisterHotkeys();
                _menuItemHotkeys.Checked = false;
            }
            else
            {
                RegisterHotkeys();
                _menuItemHotkeys.Checked = true;
            }
        }

        public void HandleHotkey(int id)
        {
            int step = (id == 1) ? 5 : -5;
            var monitors = _manager.GetMonitors();

            if (monitors.Count == 0) return;

            int lastBrightness = 0;
            string title = "";

            foreach (var m in monitors)
            {
                int newVal = Math.Max((int)m.MinBrightness, Math.Min((int)m.MaxBrightness, m.CurrentBrightness + step));
                _manager.SetBrightnessAsync(m, newVal);
                lastBrightness = newVal;
                title = m.FriendlyName;
            }

            _flyout.SyncCardsFromMonitors();

            if (monitors.Count == 1)
            {
                _osd.ShowBrightness(title, lastBrightness);
            }
            else
            {
                _osd.ShowBrightness("All Displays", lastBrightness);
            }
        }

        public void HandleDisplayChange()
        {
            _manager.RefreshMonitors();
        }

        public void HandleThemeChange()
        {
            ThemeManager.ReloadTheme();
            _flyout.ApplyTheme();
            _trayIcon.Icon = CreateTrayIcon();
        }

        private bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
                {
                    if (key != null)
                    {
                        return key.GetValue(AppName) != null;
                    }
                }
            }
            catch { }
            return false;
        }

        private void ToggleStartup()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key != null)
                    {
                        if (_menuItemStartup.Checked)
                        {
                            key.DeleteValue(AppName, false);
                            _menuItemStartup.Checked = false;
                        }
                        else
                        {
                            key.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
                            _menuItemStartup.Checked = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not modify startup settings: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExitApp()
        {
            UnregisterHotkeys();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _osd.Dispose();
            _flyout.Dispose();
            _manager.Dispose();
            _msgWindow.Dispose();
            ExitThread();
        }

        private class HotkeyMessageWindow : Form
        {
            private readonly TrayApplicationContext _parent;

            public HotkeyMessageWindow(TrayApplicationContext parent)
            {
                _parent = parent;
                Text = "LumaTray_MessageHost";
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                WindowState = FormWindowState.Minimized;
                Opacity = 0;
                Size = new Size(0, 0);
                IntPtr h = Handle; // Force handle creation
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == NativeMethods.WM_HOTKEY)
                {
                    int id = m.WParam.ToInt32();
                    _parent.HandleHotkey(id);
                }
                else if (m.Msg == NativeMethods.WM_DISPLAYCHANGE)
                {
                    _parent.HandleDisplayChange();
                }
                else if (m.Msg == NativeMethods.WM_SETTINGCHANGE || m.Msg == NativeMethods.WM_THEMECHANGED)
                {
                    _parent.HandleThemeChange();
                }

                base.WndProc(ref m);
            }
        }
    }
}
