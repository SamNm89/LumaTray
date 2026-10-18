using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LumaTray
{
    public class FlyoutForm : Form
    {
        private readonly MonitorManager _manager;
        private readonly FlowLayoutPanel _cardPanel;
        private readonly Label _titleLabel;
        private readonly Label _countBadge;
        private readonly ModernIconButton _btnLink;
        private readonly ModernIconButton _btnSettings;
        private readonly List<MonitorRowCard> _cards = new List<MonitorRowCard>();
        private int _focusedIndex = 0;

        private bool _isLinked = false;

        public bool IsLinked
        {
            get { return _isLinked; }
            set
            {
                _isLinked = value;
                if (_btnLink != null)
                {
                    _btnLink.IsActive = value;
                    _btnLink.Invalidate();
                }
            }
        }

        public event Action RequestSettings;

        public FlyoutForm(MonitorManager manager)
        {
            _manager = manager;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Width = 304;

            ApplyTheme();

            // Header Panel (Slim 34px)
            Panel headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 34,
                BackColor = Color.Transparent,
                Padding = new Padding(12, 6, 12, 2)
            };

            _titleLabel = new Label
            {
                Text = "LumaTray",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = ThemeManager.TextColor,
                AutoSize = true,
                Location = new Point(12, 8)
            };

            _countBadge = new Label
            {
                Text = "1 Display",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = ThemeManager.DimTextColor,
                AutoSize = true,
                Location = new Point(78, 10)
            };

            _btnSettings = new ModernIconButton
            {
                Text = "⚙",
                Size = new Size(22, 22),
                Location = new Point(Width - 32, 6),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            _btnSettings.Click += delegate { if (RequestSettings != null) RequestSettings(); };

            _btnLink = new ModernIconButton
            {
                Text = "🔗",
                Size = new Size(22, 22),
                Location = new Point(Width - 58, 6),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };
            _btnLink.Click += delegate
            {
                IsLinked = !IsLinked;
            };

            headerPanel.Controls.Add(_titleLabel);
            headerPanel.Controls.Add(_countBadge);
            headerPanel.Controls.Add(_btnLink);
            headerPanel.Controls.Add(_btnSettings);

            // Container for monitor rows
            _cardPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Padding = new Padding(6, 2, 6, 2)
            };

            // Ultra-slim micro footer (18px)
            Panel footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 18,
                BackColor = Color.Transparent,
                Padding = new Padding(8, 0, 8, 2)
            };

            Label tipLabel = new Label
            {
                Text = "← → or scroll to adjust • Esc to close",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
                ForeColor = ThemeManager.DimTextColor,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            footerPanel.Controls.Add(tipLabel);

            Controls.Add(_cardPanel);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            _manager.MonitorsRefreshed += OnMonitorsRefreshed;
            BuildMonitorCards();
        }

        public void ApplyTheme()
        {
            BackColor = ThemeManager.BackgroundColor;
            ForeColor = ThemeManager.TextColor;
            if (_titleLabel != null) _titleLabel.ForeColor = ThemeManager.TextColor;
            if (_countBadge != null) _countBadge.ForeColor = ThemeManager.DimTextColor;

            foreach (var card in _cards)
            {
                card.ApplyTheme();
            }

            Invalidate(true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // Drop shadow
                return cp;
            }
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen borderPen = new Pen(ThemeManager.BorderColor, 1))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }

        private void OnMonitorsRefreshed()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(OnMonitorsRefreshed));
                return;
            }
            BuildMonitorCards();
        }

        private void BuildMonitorCards()
        {
            _cardPanel.SuspendLayout();
            _cardPanel.Controls.Clear();
            _cards.Clear();

            var monitors = _manager.GetMonitors();
            _countBadge.Text = monitors.Count == 1 ? "1 Display" : monitors.Count + " Displays";

            if (monitors.Count == 0)
            {
                Label emptyLabel = new Label
                {
                    Text = "No DDC/CI or WMI monitors found.\nCheck monitor OSD menu for DDC/CI.",
                    ForeColor = ThemeManager.DimTextColor,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                    AutoSize = false,
                    Size = new Size(Width - 32, 60),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Margin = new Padding(8, 10, 8, 8)
                };
                _cardPanel.Controls.Add(emptyLabel);
                Height = 120;
            }
            else
            {
                int totalHeight = 34 + 18 + 6;
                for (int i = 0; i < monitors.Count; i++)
                {
                    var mon = monitors[i];
                    MonitorRowCard card = new MonitorRowCard(mon, _manager, this, i);
                    card.Width = Width - 16;
                    _cards.Add(card);
                    _cardPanel.Controls.Add(card);
                    totalHeight += card.Height + 2;
                }
                Height = Math.Min(480, totalHeight);
                _focusedIndex = 0;
            }

            _cardPanel.ResumeLayout(true);
        }

        public void NotifyCardSliderChanged(MonitorInfo sourceMonitor, int delta)
        {
            if (!_isLinked) return;

            foreach (var card in _cards)
            {
                if (card.Monitor != sourceMonitor)
                {
                    card.AdjustBrightness(delta);
                }
            }
        }

        public void SyncCardsFromMonitors()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(SyncCardsFromMonitors));
                return;
            }
            foreach (var card in _cards)
            {
                card.SyncWithMonitor();
            }
        }

        public void ShowNearTray()
        {
            ApplyTheme();
            SyncCardsFromMonitors();
            PositionNearTray();
            Show();
            NativeMethods.SetForegroundWindow(Handle);
            _focusedIndex = 0;
        }

        public void ToggleVisibility()
        {
            if (Visible)
            {
                HideAndTrim();
            }
            else
            {
                ShowNearTray();
            }
        }

        private void PositionNearTray()
        {
            Screen activeScreen = Screen.FromPoint(Cursor.Position);
            Rectangle workArea = activeScreen.WorkingArea;
            Rectangle screenBounds = activeScreen.Bounds;

            int margin = 12;
            int x = workArea.Right - Width - margin;
            int y = workArea.Bottom - Height - margin;

            if (workArea.Top > screenBounds.Top)
            {
                y = workArea.Top + margin;
            }
            if (workArea.Left > screenBounds.Left)
            {
                x = workArea.Left + margin;
            }

            Location = new Point(x, y);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HideAndTrim();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                HideAndTrim();
                return true;
            }

            if (_cards.Count > 0 && _focusedIndex >= 0 && _focusedIndex < _cards.Count)
            {
                var currentCard = _cards[_focusedIndex];

                if (keyData == Keys.Left)
                {
                    currentCard.AdjustBrightness(-2);
                    return true;
                }
                if (keyData == Keys.Right)
                {
                    currentCard.AdjustBrightness(2);
                    return true;
                }
                if (keyData == (Keys.Left | Keys.Shift) || keyData == (Keys.Left | Keys.Control))
                {
                    currentCard.AdjustBrightness(-5);
                    return true;
                }
                if (keyData == (Keys.Right | Keys.Shift) || keyData == (Keys.Right | Keys.Control))
                {
                    currentCard.AdjustBrightness(5);
                    return true;
                }
                if (keyData == Keys.Down)
                {
                    if (_cards.Count > 1)
                    {
                        _focusedIndex = (_focusedIndex + 1) % _cards.Count;
                    }
                    else
                    {
                        currentCard.AdjustBrightness(-2);
                    }
                    return true;
                }
                if (keyData == Keys.Up)
                {
                    if (_cards.Count > 1)
                    {
                        _focusedIndex = (_focusedIndex - 1 + _cards.Count) % _cards.Count;
                    }
                    else
                    {
                        currentCard.AdjustBrightness(2);
                    }
                    return true;
                }
                if (keyData == Keys.Tab)
                {
                    if (_cards.Count > 1)
                    {
                        _focusedIndex = (_focusedIndex + 1) % _cards.Count;
                    }
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void HideAndTrim()
        {
            Hide();
            try
            {
                GC.Collect(1, GCCollectionMode.Optimized);
                NativeMethods.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch { }
        }
    }

    public class MonitorRowCard : Panel
    {
        public MonitorInfo Monitor { get; private set; }
        public ModernSlider Slider { get { return _slider; } }

        private readonly MonitorManager _manager;
        private readonly FlyoutForm _parentFlyout;
        private readonly ModernSlider _slider;
        private readonly Label _titleLabel;
        private readonly Label _pctLabel;
        private int _lastValue;

        public MonitorRowCard(MonitorInfo monitor, MonitorManager manager, FlyoutForm parentFlyout, int index)
        {
            Monitor = monitor;
            _manager = manager;
            _parentFlyout = parentFlyout;
            _lastValue = monitor.CurrentBrightness;

            Height = 52;
            BackColor = Color.Transparent;
            Padding = new Padding(0);
            DoubleBuffered = true;

            // Monitor Title
            _titleLabel = new Label
            {
                Text = monitor.FriendlyName,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = ThemeManager.TextColor,
                Location = new Point(28, 4),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            // Percentage Badge
            _pctLabel = new Label
            {
                Text = monitor.CurrentBrightness + "%",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = ThemeManager.AccentColor,
                Location = new Point(Width - 54, 3),
                Size = new Size(50, 18),
                TextAlign = ContentAlignment.TopRight,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            // Modern Slider (Clean, spacious bounds - no shaved edges)
            _slider = new ModernSlider
            {
                Minimum = (int)monitor.MinBrightness,
                Maximum = (int)monitor.MaxBrightness,
                Value = monitor.CurrentBrightness,
                Location = new Point(0, 24),
                Size = new Size(Width, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _slider.ValueChanged += OnSliderValueChanged;

            Controls.Add(_titleLabel);
            Controls.Add(_pctLabel);
            Controls.Add(_slider);

            // Mouse wheel adjustments
            MouseWheel += OnCardMouseWheel;
            _titleLabel.MouseWheel += OnCardMouseWheel;
            _pctLabel.MouseWheel += OnCardMouseWheel;
        }

        public void ApplyTheme()
        {
            _titleLabel.ForeColor = ThemeManager.TextColor;
            _pctLabel.ForeColor = ThemeManager.AccentColor;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_pctLabel != null && _slider != null)
            {
                _pctLabel.Location = new Point(Width - 54, 3);
                _slider.Size = new Size(Width, 26);
            }
        }

        private void OnCardMouseWheel(object sender, MouseEventArgs e)
        {
            int step = (e.Delta > 0) ? 5 : -5;
            AdjustBrightness(step);
        }

        public void AdjustBrightness(int delta)
        {
            _slider.Value = Math.Max(_slider.Minimum, Math.Min(_slider.Maximum, _slider.Value + delta));
        }

        public void SyncWithMonitor()
        {
            if (_slider.Value != Monitor.CurrentBrightness)
            {
                _slider.Value = Monitor.CurrentBrightness;
                _pctLabel.Text = Monitor.CurrentBrightness + "%";
                _lastValue = Monitor.CurrentBrightness;
            }
        }

        private void OnSliderValueChanged(object sender, EventArgs e)
        {
            int val = _slider.Value;
            _pctLabel.Text = val + "%";

            int delta = val - _lastValue;
            _lastValue = val;

            _manager.SetBrightnessAsync(Monitor, val);

            if (_parentFlyout.IsLinked && delta != 0)
            {
                _parentFlyout.NotifyCardSliderChanged(Monitor, delta);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Crisp mini vector monitor icon
            using (Pen iconPen = new Pen(ThemeManager.AccentColor, 1.4f))
            {
                g.DrawRectangle(iconPen, 8, 7, 14, 10);
                g.DrawLine(iconPen, 15, 17, 15, 19);
                g.DrawLine(iconPen, 11, 19, 19, 19);
            }

            base.OnPaint(e);
        }
    }
}
