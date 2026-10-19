using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LumaTray
{
    public class OsdForm : Form
    {
        private readonly Timer _hideTimer;
        private string _monitorName = "";
        private int _brightness = 50;

        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        public OsdForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = ThemeManager.BackgroundColor;
            Size = new Size(240, 50);
            DoubleBuffered = true;

            _hideTimer = new Timer { Interval = 1200 };
            _hideTimer.Tick += delegate
            {
                _hideTimer.Stop();
                Hide();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                cp.ClassStyle |= 0x00020000; // Drop shadow
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int preference = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

                int darkAttr = ThemeManager.IsDarkMode ? 1 : 0;
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkAttr, sizeof(int));
            }
            catch { }
        }

        public void ShowBrightness(string monitorName, int brightness)
        {
            _monitorName = monitorName;
            _brightness = Math.Max(0, Math.Min(100, brightness));
            BackColor = ThemeManager.BackgroundColor;

            Screen activeScreen = Screen.FromPoint(Cursor.Position);
            Rectangle work = activeScreen.WorkingArea;

            int x = work.Left + (work.Width - Width) / 2;
            int y = work.Bottom - Height - 50;
            Location = new Point(x, y);

            if (!Visible)
            {
                Show();
            }

            Invalidate();
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Background & Border
            using (GraphicsPath path = ModernSlider.CreateRoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 10))
            {
                using (SolidBrush bgBrush = new SolidBrush(ThemeManager.BackgroundColor))
                {
                    g.FillPath(bgBrush, path);
                }
                using (Pen borderPen = new Pen(ThemeManager.BorderColor, 1))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            // Draw Sun Icon
            using (SolidBrush sunBrush = new SolidBrush(Color.FromArgb(255, 204, 0)))
            {
                g.FillEllipse(sunBrush, 14, 17, 14, 14);
            }
            using (Pen rayPen = new Pen(Color.FromArgb(255, 215, 64), 1.4f))
            {
                rayPen.StartCap = LineCap.Round;
                rayPen.EndCap = LineCap.Round;
                g.DrawLine(rayPen, 21, 12, 21, 14); // Top
                g.DrawLine(rayPen, 21, 33, 21, 35); // Bottom
                g.DrawLine(rayPen, 9, 24, 11, 24);  // Left
                g.DrawLine(rayPen, 30, 24, 32, 24); // Right
            }

            // Monitor Name Text
            using (Font nameFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(ThemeManager.TextColor))
            {
                string displayTitle = !string.IsNullOrEmpty(_monitorName) ? _monitorName : "Brightness";
                g.DrawString(displayTitle, nameFont, textBrush, 38, 9);
            }

            // Percentage Text
            using (Font pctFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (SolidBrush pctBrush = new SolidBrush(ThemeManager.AccentColor))
            {
                string pct = _brightness + "%";
                SizeF sz = g.MeasureString(pct, pctFont);
                g.DrawString(pct, pctFont, pctBrush, Width - 14 - sz.Width, 9);
            }

            // Progress Bar Track
            int barX = 38;
            int barY = 29;
            int barW = Width - barX - 14;
            int barH = 6;

            using (GraphicsPath trackPath = ModernSlider.CreateRoundedRect(new Rectangle(barX, barY, barW, barH), barH / 2))
            using (SolidBrush trackBrush = new SolidBrush(ThemeManager.TrackColor))
            {
                g.FillPath(trackBrush, trackPath);
            }

            // Progress Bar Fill
            int fillW = (int)((float)_brightness / 100f * barW);
            if (fillW > 0)
            {
                using (GraphicsPath fillPath = ModernSlider.CreateRoundedRect(new Rectangle(barX, barY, fillW, barH), barH / 2))
                using (SolidBrush fillBrush = new SolidBrush(ThemeManager.AccentColor))
                {
                    g.FillPath(fillBrush, fillPath);
                }
            }
        }
    }
}
