using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LumaTray
{
    public class ModernSlider : Control
    {
        private int _minimum = 0;
        private int _maximum = 100;
        private int _value = 50;
        private bool _isDragging = false;
        private bool _isHovered = false;

        public event EventHandler ValueChanged;

        public int Minimum
        {
            get { return _minimum; }
            set { _minimum = value; Invalidate(); }
        }

        public int Maximum
        {
            get { return _maximum; }
            set { _maximum = value; Invalidate(); }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                if (_value != clamped)
                {
                    _value = clamped;
                    Invalidate();
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                }
            }
        }

        public ModernSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.Selectable, true);

            BackColor = Color.Transparent;
            Height = 26;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                UpdateValueFromPoint(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDragging)
            {
                UpdateValueFromPoint(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isDragging = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int step = (e.Delta > 0) ? 5 : -5;
            Value += step;
        }

        private void UpdateValueFromPoint(int x)
        {
            int trackPadding = 14;
            int trackWidth = Width - (trackPadding * 2);
            if (trackWidth <= 0) return;

            int relX = Math.Max(0, Math.Min(trackWidth, x - trackPadding));
            float pct = (float)relX / trackWidth;
            int newVal = (int)Math.Round(_minimum + pct * (_maximum - _minimum));
            Value = newVal;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int trackHeight = 5;
            int trackPadding = 14;
            int trackY = (Height - trackHeight) / 2;
            int trackWidth = Width - (trackPadding * 2);

            if (trackWidth <= 0) return;

            // Hover pill hugging the slider track (exactly like Test2)
            if (_isHovered || _isDragging)
            {
                int pillPadY = 6;
                int pillPadX = 8;
                Rectangle hoverRect = new Rectangle(
                    trackPadding - pillPadX,
                    trackY - pillPadY,
                    trackWidth + (pillPadX * 2),
                    trackHeight + (pillPadY * 2));

                using (GraphicsPath hoverPath = CreateRoundedRect(hoverRect, hoverRect.Height / 2))
                {
                    Color hoverColor = ThemeManager.IsDarkMode
                        ? Color.FromArgb(18, 255, 255, 255)
                        : Color.FromArgb(14, 0, 0, 0);

                    using (SolidBrush hoverBrush = new SolidBrush(hoverColor))
                    {
                        g.FillPath(hoverBrush, hoverPath);
                    }
                }
            }

            float pct = (_maximum > _minimum) ? (float)(_value - _minimum) / (_maximum - _minimum) : 0;
            int fillWidth = (int)(pct * trackWidth);

            // Background Track
            using (GraphicsPath trackPath = CreateRoundedRect(new Rectangle(trackPadding, trackY, trackWidth, trackHeight), trackHeight / 2))
            using (SolidBrush trackBrush = new SolidBrush(ThemeManager.TrackColor))
            {
                g.FillPath(trackBrush, trackPath);
            }

            // Filled Track (Accent)
            if (fillWidth > 0)
            {
                using (GraphicsPath fillPath = CreateRoundedRect(new Rectangle(trackPadding, trackY, fillWidth, trackHeight), trackHeight / 2))
                using (SolidBrush fillBrush = new SolidBrush(ThemeManager.AccentColor))
                {
                    g.FillPath(fillBrush, fillPath);
                }
            }

            // Knob Dimensions (with generous 14px padding so it is NEVER clipped)
            int thumbRadius = (_isDragging || _isHovered) ? 8 : 7;
            int thumbX = trackPadding + fillWidth;
            int thumbY = Height / 2;

            Rectangle thumbRect = new Rectangle(thumbX - thumbRadius, thumbY - thumbRadius, thumbRadius * 2, thumbRadius * 2);

            // Subtle drop shadow under thumb
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
            {
                g.FillEllipse(shadowBrush, thumbX - thumbRadius, thumbY - thumbRadius + 1, thumbRadius * 2, thumbRadius * 2);
            }

            // Thumb Circle
            Color knobColor = ThemeManager.IsDarkMode ? Color.White : Color.FromArgb(250, 250, 250);
            using (SolidBrush thumbBrush = new SolidBrush(knobColor))
            {
                g.FillEllipse(thumbBrush, thumbRect);
            }

            // Inner accent dot when dragging or hovering
            if (_isDragging || _isHovered)
            {
                int innerR = 3;
                using (SolidBrush innerBrush = new SolidBrush(ThemeManager.AccentColor))
                {
                    g.FillEllipse(innerBrush, thumbX - innerR, thumbY - innerR, innerR * 2, innerR * 2);
                }
            }
        }

        public static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0) return path;
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            int diameter = radius * 2;
            Size size = new Size(diameter, diameter);
            Rectangle arc = new Rectangle(rect.Location, size);

            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }
    }

    public class ModernIconButton : Control
    {
        private bool _isHovered = false;
        private bool _isPressed = false;
        private bool _isActive = false;

        public bool IsActive
        {
            get { return _isActive; }
            set { _isActive = value; Invalidate(); }
        }

        public ModernIconButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            Size = new Size(24, 24);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _isHovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _isHovered = false; _isPressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _isPressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _isPressed = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color bg = Color.Transparent;
            if (_isActive)
                bg = Color.FromArgb(70, ThemeManager.AccentColor);
            else if (_isPressed)
                bg = ThemeManager.HoverColor;
            else if (_isHovered)
                bg = ThemeManager.HoverColor;

            if (bg != Color.Transparent)
            {
                using (GraphicsPath path = ModernSlider.CreateRoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 4))
                using (SolidBrush brush = new SolidBrush(bg))
                {
                    g.FillPath(brush, path);
                }
            }

            Color textColor = _isActive ? ThemeManager.AccentColor : ThemeManager.DimTextColor;
            if (_isHovered) textColor = ThemeManager.TextColor;

            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (SolidBrush textBrush = new SolidBrush(textColor))
            {
                g.DrawString(Text, Font, textBrush, new RectangleF(0, 0, Width, Height), sf);
            }
        }
    }
}
