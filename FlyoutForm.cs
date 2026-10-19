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
        private readonly DoubleBufferedPanel _cardsViewport;
        private readonly DoubleBufferedPanel _cardsContainer;
        private readonly Label _titleLabel;
        private readonly Label _countBadge;
        private readonly Label _tipLabel;
        private readonly ModernIconButton _btnLink;
        private readonly ModernIconButton _btnSettings;
        private readonly ModernIconButton _btnUpdate;
        private readonly ToolTip _toolTip;
        private readonly List<MonitorRowCard> _cards = new List<MonitorRowCard>();
        private readonly Timer _scrollAnimTimer;

        private int _focusedIndex = 0;
        private int _scrollIndex = 0;
        private int _targetScrollTop = 0;
        private bool _isLinked = false;
        private bool _isUpdatingLinked = false;

        public bool CanScrollCards
        {
            get { return _cards.Count > 2; }
        }

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

            _scrollAnimTimer = new Timer { Interval = 15 };
            _scrollAnimTimer.Tick += ScrollAnimTimer_Tick;

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

            _btnUpdate = new ModernIconButton
            {
                Text = "⬆",
                Size = new Size(22, 22),
                Location = new Point(Width - 84, 6),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Visible = UpdateManager.HasUpdate
            };
            _btnUpdate.Click += delegate
            {
                if (UpdateManager.AvailableUpdate != null)
                {
                    UpdateManager.PromptAndInstallUpdate(UpdateManager.AvailableUpdate, this);
                }
            };

            _toolTip = new ToolTip();
            _toolTip.SetToolTip(_btnUpdate, "Update available! Click to install");
            _toolTip.SetToolTip(_btnLink, "Link display brightness sliders");
            _toolTip.SetToolTip(_btnSettings, "Settings & menu");

            headerPanel.Controls.Add(_titleLabel);
            headerPanel.Controls.Add(_countBadge);
            headerPanel.Controls.Add(_btnUpdate);
            headerPanel.Controls.Add(_btnLink);
            headerPanel.Controls.Add(_btnSettings);

            // Footer Panel (22px)
            Panel footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                BackColor = Color.Transparent,
                Padding = new Padding(8, 0, 8, 2)
            };

            _tipLabel = new Label
            {
                Text = "← → or scroll to adjust • Esc to close",
                Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
                ForeColor = ThemeManager.DimTextColor,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            footerPanel.Controls.Add(_tipLabel);

            // Scrollbar-free Viewport Panel
            _cardsViewport = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                AutoScroll = false
            };

            // Internal Cards Container Panel (moves vertically within Viewport)
            _cardsContainer = new DoubleBufferedPanel
            {
                Location = new Point(0, 0),
                Width = Width,
                BackColor = Color.Transparent,
                AutoScroll = false
            };
            _cardsViewport.Controls.Add(_cardsContainer);

            // Add docked controls
            Controls.Add(_cardsViewport);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);
            headerPanel.SendToBack();
            footerPanel.SendToBack();
            _cardsViewport.BringToFront();

            // Mouse wheel forwarding to scroll monitors when >2
            MouseWheel += (s, e) => HandleFlyoutMouseWheel(e);
            headerPanel.MouseWheel += (s, e) => HandleFlyoutMouseWheel(e);
            _titleLabel.MouseWheel += (s, e) => HandleFlyoutMouseWheel(e);
            _countBadge.MouseWheel += (s, e) => HandleFlyoutMouseWheel(e);
            footerPanel.MouseWheel += (s, e) => HandleFlyoutMouseWheel(e);
            _tipLabel.MouseWheel += (s, e) => HandleFlyoutMouseWheel(e);
            _cardsViewport.MouseWheel += (s, e) => HandleFlyoutMouseWheel(e);
            _cardsContainer.MouseWheel += (s, e) => HandleFlyoutMouseWheel(e);

            _manager.MonitorsRefreshed += OnMonitorsRefreshed;
            UpdateManager.UpdateFound += OnUpdateFound;
            BuildMonitorCards();
        }

        private void OnUpdateFound(UpdateInfo info)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<UpdateInfo>(OnUpdateFound), info);
                return;
            }
            if (_btnUpdate != null)
            {
                _btnUpdate.Visible = true;
                _btnUpdate.IsActive = true;
                _btnUpdate.Invalidate();
            }
        }

        public void ApplyTheme()
        {
            BackColor = ThemeManager.BackgroundColor;
            ForeColor = ThemeManager.TextColor;
            if (_titleLabel != null) _titleLabel.ForeColor = ThemeManager.TextColor;
            if (_countBadge != null) _countBadge.ForeColor = ThemeManager.DimTextColor;
            if (_tipLabel != null) _tipLabel.ForeColor = ThemeManager.DimTextColor;

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

            var currentMonitors = _manager.GetMonitors();
            bool monitorSetChanged = false;
            if (_cards.Count != currentMonitors.Count)
            {
                monitorSetChanged = true;
            }
            else
            {
                for (int i = 0; i < currentMonitors.Count; i++)
                {
                    if (_cards[i].Monitor == null || _cards[i].Monitor.Id != currentMonitors[i].Id)
                    {
                        monitorSetChanged = true;
                        break;
                    }
                }
            }

            if (monitorSetChanged)
            {
                BuildMonitorCards();
                if (Visible)
                {
                    PositionNearTray();
                }
            }
            else
            {
                SyncCardsFromMonitors();
            }
        }

        private void BuildMonitorCards()
        {
            _scrollAnimTimer.Stop();
            _cardsContainer.SuspendLayout();
            foreach (var oldCard in _cards)
            {
                oldCard.Dispose();
            }
            _cards.Clear();
            _cardsContainer.Controls.Clear();
            _scrollIndex = 0;
            _targetScrollTop = 0;
            _cardsContainer.Top = 0;

            var monitors = _manager.GetMonitors();

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
                    Location = new Point(16, 4)
                };
                _cardsContainer.Controls.Add(emptyLabel);
                _cardsContainer.Height = 60;
                Height = 34 + 60 + 22; // 116
            }
            else
            {
                int cardWidth = Width - 20; // 304 - 20 = 284
                int cardHeight = 52;
                int cardGap = 6;
                int topPadding = 4;

                for (int i = 0; i < monitors.Count; i++)
                {
                    var mon = monitors[i];
                    MonitorRowCard card = new MonitorRowCard(mon, _manager, this, i);
                    card.Location = new Point(10, topPadding + (i * (cardHeight + cardGap)));
                    card.Size = new Size(cardWidth, cardHeight);
                    _cards.Add(card);
                    _cardsContainer.Controls.Add(card);
                }

                int totalContentHeight = topPadding + (monitors.Count * (cardHeight + cardGap)) + 4;
                _cardsContainer.Height = totalContentHeight;

                if (monitors.Count == 1)
                {
                    // 1 block tall
                    Height = 34 + (topPadding + cardHeight + 4) + 22; // 116
                }
                else
                {
                    // 2 blocks tall (up to 2 cards natively visible, max height!)
                    Height = 34 + (topPadding + (2 * cardHeight) + cardGap + 4) + 22; // 174
                }
            }

            _cardsContainer.ResumeLayout(true);
            _focusedIndex = 0;
            UpdateFocusVisual();
            UpdateBadgeAndTip();
        }

        private void ScrollAnimTimer_Tick(object sender, EventArgs e)
        {
            int current = _cardsContainer.Top;
            int diff = _targetScrollTop - current;

            if (Math.Abs(diff) <= 3)
            {
                _cardsContainer.Top = _targetScrollTop;
                _scrollAnimTimer.Stop();
            }
            else
            {
                _cardsContainer.Top = current + (diff / 2);
            }
        }

        public void HandleFlyoutMouseWheel(MouseEventArgs e)
        {
            if (!CanScrollCards) return;

            int maxIndex = Math.Max(0, _cards.Count - 2);
            if (e.Delta < 0)
            {
                // Wheel down -> scroll down (reveal next displays)
                if (_scrollIndex < maxIndex)
                {
                    _scrollIndex++;
                    AnimateScrollToTarget();
                }
            }
            else if (e.Delta > 0)
            {
                // Wheel up -> scroll up (reveal previous displays)
                if (_scrollIndex > 0)
                {
                    _scrollIndex--;
                    AnimateScrollToTarget();
                }
            }
        }

        private void AnimateScrollToTarget()
        {
            _targetScrollTop = -(_scrollIndex * 58);
            _scrollAnimTimer.Start();
            UpdateBadgeAndTip();
        }

        private void UpdateBadgeAndTip()
        {
            int count = _cards.Count;
            if (count == 0)
            {
                _countBadge.Text = "0 Displays";
                _tipLabel.Text = "Esc to close";
            }
            else if (count == 1)
            {
                _countBadge.Text = "1 Display";
                _tipLabel.Text = "← → or scroll to adjust • Esc to close";
            }
            else if (count == 2)
            {
                _countBadge.Text = "2 Displays";
                _tipLabel.Text = "← → or scroll to adjust • Esc to close";
            }
            else
            {
                int first = _scrollIndex + 1;
                int last = Math.Min(_scrollIndex + 2, count);
                _countBadge.Text = string.Format("Displays {0}-{1} of {2}", first, last, count);
                _tipLabel.Text = "Scroll or ↑↓ to view all • Esc to close";
            }
        }

        public void SetFocusedCardIndex(int index)
        {
            if (index >= 0 && index < _cards.Count)
            {
                _focusedIndex = index;
                UpdateFocusVisual();
            }
        }

        private void UpdateFocusVisual()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                _cards[i].IsCardFocused = (i == _focusedIndex && _cards.Count > 1);
            }
        }

        private void EnsureCardVisible(int index)
        {
            if (!CanScrollCards) return;

            if (index < _scrollIndex)
            {
                _scrollIndex = index;
                AnimateScrollToTarget();
            }
            else if (index >= _scrollIndex + 2)
            {
                _scrollIndex = index - 1;
                AnimateScrollToTarget();
            }
        }

        public void NotifyCardSliderChanged(MonitorInfo sourceMonitor, int delta)
        {
            if (!_isLinked || _isUpdatingLinked) return;

            try
            {
                _isUpdatingLinked = true;
                foreach (var card in _cards)
                {
                    if (card.Monitor != sourceMonitor)
                    {
                        card.AdjustBrightness(delta);
                    }
                }
            }
            finally
            {
                _isUpdatingLinked = false;
            }
        }

        public void SyncCardsFromMonitors()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(SyncCardsFromMonitors));
                return;
            }
            var monitors = _manager.GetMonitors();
            for (int i = 0; i < _cards.Count && i < monitors.Count; i++)
            {
                _cards[i].UpdateMonitor(monitors[i]);
            }
        }

        public void ShowNearTray()
        {
            ApplyTheme();
            SyncCardsFromMonitors();
            _scrollIndex = 0;
            _targetScrollTop = 0;
            _scrollAnimTimer.Stop();
            _cardsContainer.Top = 0;
            _focusedIndex = 0;
            UpdateFocusVisual();
            UpdateBadgeAndTip();
            PositionNearTray();
            Show();
            NativeMethods.SetForegroundWindow(Handle);
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
                        EnsureCardVisible(_focusedIndex);
                        UpdateFocusVisual();
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
                        EnsureCardVisible(_focusedIndex);
                        UpdateFocusVisual();
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
                        EnsureCardVisible(_focusedIndex);
                        UpdateFocusVisual();
                    }
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void HideAndTrim()
        {
            _scrollAnimTimer.Stop();
            Hide();
            try
            {
                SettingsManager.SaveAllBrightness(_manager.GetMonitors());
                BufferedGraphicsManager.Current.Invalidate();
                GC.Collect(1, GCCollectionMode.Optimized);
                NativeMethods.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_scrollAnimTimer != null)
                {
                    _scrollAnimTimer.Stop();
                    _scrollAnimTimer.Dispose();
                }
                UpdateManager.UpdateFound -= OnUpdateFound;
                foreach (var card in _cards)
                {
                    card.Dispose();
                }
                _cards.Clear();
                if (_toolTip != null)
                {
                    _toolTip.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }

    public class MonitorRowCard : Panel
    {
        public MonitorInfo Monitor { get; private set; }
        public ModernSlider Slider { get { return _slider; } }
        public Label TitleLabel { get { return _titleLabel; } }
        public Label PctLabel { get { return _pctLabel; } }

        private readonly MonitorManager _manager;
        private readonly FlyoutForm _parentFlyout;
        private readonly ModernSlider _slider;
        private readonly Label _titleLabel;
        private readonly Label _pctLabel;
        private readonly int _index;
        private int _lastValue;
        private bool _isCardFocused = false;

        public bool IsCardFocused
        {
            get { return _isCardFocused; }
            set
            {
                if (_isCardFocused != value)
                {
                    _isCardFocused = value;
                    Invalidate();
                }
            }
        }

        public MonitorRowCard(MonitorInfo monitor, MonitorManager manager, FlyoutForm parentFlyout, int index)
        {
            Monitor = monitor;
            _manager = manager;
            _parentFlyout = parentFlyout;
            _index = index;
            _lastValue = monitor.CurrentBrightness;

            Height = 52;
            BackColor = Color.Transparent;
            Padding = new Padding(0);
            DoubleBuffered = true;

            // Monitor Title (with ellipsis protection so long names never clip percentage)
            _titleLabel = new Label
            {
                Text = monitor.FriendlyName,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = ThemeManager.TextColor,
                Location = new Point(28, 4),
                AutoSize = true,
                MaximumSize = new Size(Width - 88, 20),
                AutoEllipsis = true,
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

            // Forward mouse wheel appropriately
            MouseWheel += (s, e) => OnCardMouseWheel(e);
            _titleLabel.MouseWheel += (s, e) => OnCardMouseWheel(e);
            _pctLabel.MouseWheel += (s, e) => OnCardMouseWheel(e);
            _slider.WheelScrolled += (s, e) => OnCardMouseWheel(e);

            // Focus on click
            MouseDown += (s, e) => _parentFlyout.SetFocusedCardIndex(_index);
            _titleLabel.MouseDown += (s, e) => _parentFlyout.SetFocusedCardIndex(_index);
            _pctLabel.MouseDown += (s, e) => _parentFlyout.SetFocusedCardIndex(_index);
            _slider.MouseDown += (s, e) => _parentFlyout.SetFocusedCardIndex(_index);
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
            if (_pctLabel != null && _slider != null && _titleLabel != null)
            {
                _titleLabel.MaximumSize = new Size(Width - 88, 20);
                _pctLabel.Location = new Point(Width - 54, 3);
                _slider.Size = new Size(Width, 26);
            }
        }

        private void OnCardMouseWheel(MouseEventArgs e)
        {
            if (_parentFlyout.CanScrollCards && (ModifierKeys & Keys.Control) == 0)
            {
                _parentFlyout.HandleFlyoutMouseWheel(e);
            }
            else
            {
                int step = (e.Delta > 0) ? 5 : -5;
                AdjustBrightness(step);
            }
        }

        public void AdjustBrightness(int delta)
        {
            _slider.Value = Math.Max(_slider.Minimum, Math.Min(_slider.Maximum, _slider.Value + delta));
        }

        public void UpdateMonitor(MonitorInfo monitor)
        {
            Monitor = monitor;
            SyncWithMonitor();
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

            // Accent focus pill on the left border if focused
            if (_isCardFocused)
            {
                using (GraphicsPath pillPath = ModernSlider.CreateRoundedRect(new Rectangle(2, 9, 3, 34), 1))
                using (SolidBrush pillBrush = new SolidBrush(ThemeManager.AccentColor))
                {
                    g.FillPath(pillBrush, pillPath);
                }
            }

            // Crisp mini vector monitor icon
            using (Pen iconPen = new Pen(ThemeManager.AccentColor, 1.4f))
            {
                g.DrawRectangle(iconPen, 8, 7, 14, 10);
                g.DrawLine(iconPen, 15, 17, 15, 19);
                g.DrawLine(iconPen, 11, 19, 19, 19);
            }

            base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_slider != null)
                {
                    _slider.ValueChanged -= OnSliderValueChanged;
                    _slider.Dispose();
                }
                if (_titleLabel != null) _titleLabel.Dispose();
                if (_pctLabel != null) _pctLabel.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
