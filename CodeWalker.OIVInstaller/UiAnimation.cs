using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CodeWalker.OIVInstaller
{
    /// <summary>
    /// Lightweight WinForms animation helpers — easings, a generic timer-driven tween,
    /// smooth button hover/press color blending, a thin animated progress bar, and a
    /// container cross-fade transition. Pure WinForms, no external dependencies.
    /// </summary>
    internal static class Easing
    {
        public static float Linear(float t) => t;
        public static float EaseOutCubic(float t) => 1f - (float)Math.Pow(1f - t, 3);
        public static float EaseInOutCubic(float t) =>
            t < 0.5f ? 4f * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 3) / 2f;
    }

    internal sealed class Animator : IDisposable
    {
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private DateTime _startUtc;
        private int _durationMs;
        private Action<float> _onTick;
        private Action _onDone;
        private Func<float, float> _easing;
        private bool _running;

        public Animator(int frameMs = 16)
        {
            _timer.Interval = Math.Max(1, frameMs);
            _timer.Tick += OnTick;
        }

        public void Tween(int durationMs, Action<float> onTick, Action onDone = null, Func<float, float> easing = null)
        {
            _timer.Stop();
            _durationMs = Math.Max(1, durationMs);
            _onTick = onTick;
            _onDone = onDone;
            _easing = easing ?? Easing.EaseOutCubic;
            _startUtc = DateTime.UtcNow;
            _running = true;
            // Fire an immediate frame so the first paint reflects the easing's t=0 value.
            try { _onTick?.Invoke(_easing(0f)); } catch { }
            _timer.Start();
        }

        public void Stop()
        {
            _running = false;
            _timer.Stop();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_running) return;
            float t = (float)((DateTime.UtcNow - _startUtc).TotalMilliseconds / _durationMs);
            if (t >= 1f)
            {
                try { _onTick?.Invoke(_easing(1f)); } catch { }
                Stop();
                try { _onDone?.Invoke(); } catch { }
                return;
            }
            try { _onTick?.Invoke(_easing(t)); } catch { }
        }

        public void Dispose()
        {
            _running = false;
            _timer.Tick -= OnTick;
            _timer.Dispose();
        }
    }

    internal static class ColorBlend
    {
        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            int r = (int)(a.R + (b.R - a.R) * t);
            int g = (int)(a.G + (b.G - a.G) * t);
            int bl = (int)(a.B + (b.B - a.B) * t);
            return Color.FromArgb(r, g, bl);
        }
    }

    /// <summary>
    /// Attaches a smooth color-blend hover/press animation to an existing Button.
    /// Overrides FlatAppearance.MouseOverBackColor / MouseDownBackColor each frame so
    /// the WinForms-default instant flash does not fight the tween.
    /// </summary>
    internal static class ButtonHoverAnimator
    {
        public static void Attach(Button button, Color idle, Color hover, Color pressed, int durationMs = 130)
        {
            if (button == null) return;

            var animator = new Animator();
            Color current = idle;
            bool isHovering = false;
            bool isPressed = false;

            void Apply(Color c)
            {
                if (button.IsDisposed) return;
                button.BackColor = c;
                // Match Flat hover/down colors so WinForms doesn't paint over our tween.
                button.FlatAppearance.MouseOverBackColor = c;
                button.FlatAppearance.MouseDownBackColor = c;
            }

            void TweenTo(Color target)
            {
                Color start = current;
                current = target;
                animator.Tween(durationMs, t => Apply(ColorBlend.Lerp(start, target, t)));
            }

            // Initial paint
            Apply(idle);

            button.MouseEnter += (s, e) => { isHovering = true; TweenTo(isPressed ? pressed : hover); };
            button.MouseLeave += (s, e) => { isHovering = false; TweenTo(isPressed ? pressed : idle); };
            button.MouseDown += (s, e) => { isPressed = true; TweenTo(pressed); };
            button.MouseUp += (s, e) => { isPressed = false; TweenTo(isHovering ? hover : idle); };
            button.EnabledChanged += (s, e) =>
            {
                // When a disabled->enabled transition happens, snap to idle so we don't
                // leave a stale hover color on screen.
                if (!button.Enabled) { isHovering = false; isPressed = false; }
                Apply(button.Enabled ? (isHovering ? (isPressed ? pressed : hover) : idle) : idle);
            };
            button.Disposed += (s, e) => animator.Dispose();
        }
    }

    /// <summary>
    /// Thin custom-drawn progress bar that eases toward its target value instead of
    /// jumping. Double-buffered to avoid flicker.
    /// </summary>
    internal sealed class SmoothProgressBar : Control
    {
        private float _displayed = 0f;   // 0..100
        private float _target = 0f;      // 0..100
        private readonly Animator _animator = new Animator();

        public Color BarColor { get; set; } = Color.FromArgb(0, 120, 215);
        public Color TrackColor { get; set; } = Color.FromArgb(232, 232, 232);
        public int AnimationDurationMs { get; set; } = 320;

        public SmoothProgressBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);
            Height = 4;
            TabStop = false;
        }

        public float Value
        {
            get => _target;
            set => SetValue(value);
        }

        public void SetValue(float value)
        {
            value = Math.Max(0f, Math.Min(100f, value));
            if (Math.Abs(value - _target) < 0.01f) return;

            float start = _displayed;
            _target = value;
            _animator.Tween(AnimationDurationMs, t =>
            {
                if (IsDisposed) return;
                _displayed = start + (value - start) * t;
                Invalidate();
            }, easing: Easing.EaseOutCubic);
        }

        public void Reset()
        {
            _animator.Stop();
            _displayed = 0f;
            _target = 0f;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var trackBrush = new SolidBrush(TrackColor))
                g.FillRectangle(trackBrush, 0, 0, Width, Height);

            int barWidth = (int)Math.Round(Width * _displayed / 100f);
            if (barWidth > 0)
            {
                using (var barBrush = new SolidBrush(BarColor))
                    g.FillRectangle(barBrush, 0, 0, barWidth, Height);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animator.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Slides a control into place from a vertical offset, easing on Y.
    /// Fire-and-forget: animator self-disposes when done. Safe to call repeatedly;
    /// the previous slide for the same control is interrupted by the next one.
    /// </summary>
    internal static class SlideAnimator
    {
        public static void SlideUp(Control control, int fromOffsetY = 14, int durationMs = 280, int delayMs = 0)
        {
            if (control == null || control.IsDisposed) return;
            int targetY = control.Top;
            control.Top = targetY + fromOffsetY;

            var animator = new Animator();
            void Start()
            {
                if (control.IsDisposed) { animator.Dispose(); return; }
                animator.Tween(durationMs, t =>
                {
                    if (control.IsDisposed) return;
                    control.Top = targetY + (int)Math.Round((1f - t) * fromOffsetY);
                }, () =>
                {
                    if (!control.IsDisposed) control.Top = targetY;
                    animator.Dispose();
                }, Easing.EaseOutCubic);
            }

            if (delayMs > 0)
            {
                var delay = new System.Windows.Forms.Timer { Interval = Math.Max(1, delayMs) };
                delay.Tick += (s, e) => { delay.Stop(); delay.Dispose(); Start(); };
                delay.Start();
            }
            else
            {
                Start();
            }
        }
    }

    /// <summary>
    /// Sub-pixel-positioned marquee renderer that paints onto a host container.
    /// Replaces the integer-pixel Label-shifting approach with a time-based, 60 Hz
    /// custom paint so scrolling reads as fluid rather than chunky.
    /// </summary>
    internal sealed class MarqueePainter : IDisposable
    {
        private readonly Control _host;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 16 };
        private DateTime _lastTickUtc = DateTime.UtcNow;
        private float _offsetX = 0f;
        private float _waitSecondsRemaining = 0f;
        private int _direction = -1; // -1 left, +1 right
        private float _textWidth = 0f;

        public string Text { get; set; } = "";
        public Font Font { get; set; }
        public Color ForeColor { get; set; } = Color.White;
        public float ScrollSpeed { get; set; } = 26f;     // pixels per second
        public float PauseSeconds { get; set; } = 1.6f;   // pause at each end

        public MarqueePainter(Control host, Font font)
        {
            _host = host;
            Font = font;
            EnableDoubleBuffer(host);
            _host.Paint += OnPaint;
            _timer.Tick += OnTick;
        }

        // Control.DoubleBuffered is protected; flip it via reflection so Panel hosts
        // (like pnlTitleClipping) repaint the scrolling text without flicker.
        private static void EnableDoubleBuffer(Control control)
        {
            try
            {
                var prop = typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                prop?.SetValue(control, true);
            }
            catch { /* best-effort; flicker without is mild */ }
        }

        public void Start()
        {
            _lastTickUtc = DateTime.UtcNow;
            _timer.Start();
            _host.Invalidate();
        }

        public void Stop() => _timer.Stop();

        public void ResetPosition()
        {
            _offsetX = 0f;
            _direction = -1;
            _waitSecondsRemaining = PauseSeconds;
            _host.Invalidate();
        }

        private void OnTick(object sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            float dt = (float)(now - _lastTickUtc).TotalSeconds;
            _lastTickUtc = now;

            if (string.IsNullOrEmpty(Text) || _host.IsDisposed) return;
            if (_textWidth <= _host.Width)
            {
                if (_offsetX != 0f) { _offsetX = 0f; _host.Invalidate(); }
                return;
            }

            if (_waitSecondsRemaining > 0f)
            {
                _waitSecondsRemaining -= dt;
                return;
            }

            _offsetX += _direction * ScrollSpeed * dt;

            float minOffset = _host.Width - _textWidth; // negative — text overhangs left
            if (_offsetX <= minOffset)
            {
                _offsetX = minOffset;
                _direction = 1;
                _waitSecondsRemaining = PauseSeconds;
            }
            else if (_offsetX >= 0f)
            {
                _offsetX = 0f;
                _direction = -1;
                _waitSecondsRemaining = PauseSeconds;
            }

            _host.Invalidate();
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            if (string.IsNullOrEmpty(Text) || Font == null) return;

            var g = e.Graphics;
            // AntiAlias gives true sub-pixel glyph positioning at the cost of slight
            // softness; ClearTypeGridFit snaps to whole pixels and undoes the smoothness.
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            var measured = g.MeasureString(Text, Font);
            _textWidth = measured.Width;

            // Center vertically on the host.
            float y = (_host.Height - measured.Height) / 2f;

            using (var brush = new SolidBrush(ForeColor))
                g.DrawString(Text, Font, brush, _offsetX, y);
        }

        public void Dispose()
        {
            _timer.Tick -= OnTick;
            _timer.Stop();
            _timer.Dispose();
            if (!_host.IsDisposed) _host.Paint -= OnPaint;
        }
    }

    /// <summary>
    /// Loops a target control's BackColor through an N-stop color cycle (linear
    /// segments, wrapping at the end). Used to give the OIV installer's header
    /// a slow rainbow shift while no package is loaded — stops the moment a
    /// package is picked so the loaded theme color takes over cleanly.
    /// </summary>
    internal sealed class HeaderColorPulser : IDisposable
    {
        private readonly Control _host;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 33 };
        private DateTime _startUtc = DateTime.UtcNow;

        public Color[] Stops { get; set; }
        public int CycleMs { get; set; } = 10000;

        public HeaderColorPulser(Control host, params Color[] stops)
        {
            _host = host;
            Stops = stops != null && stops.Length >= 2
                ? stops
                : new[] { Color.Red, Color.Lime, Color.Blue };
            _timer.Tick += OnTick;
        }

        public void Start()
        {
            _startUtc = DateTime.UtcNow;
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        public bool IsRunning => _timer.Enabled;

        private void OnTick(object sender, EventArgs e)
        {
            if (_host.IsDisposed) { _timer.Stop(); return; }
            double phase = ((DateTime.UtcNow - _startUtc).TotalMilliseconds / CycleMs) % 1.0;
            if (phase < 0) phase += 1.0;

            int n = Stops.Length;
            double scaled = phase * n;          // 0..n
            int seg = (int)scaled;              // index of the current segment's "from" stop
            if (seg >= n) seg = n - 1;
            float localT = (float)(scaled - seg); // 0..1 within the segment

            Color a = Stops[seg];
            Color b = Stops[(seg + 1) % n];     // wraps so the last segment crossfades back to Stops[0]
            _host.BackColor = ColorBlend.Lerp(a, b, localT);
        }

        public void Dispose()
        {
            _timer.Tick -= OnTick;
            _timer.Stop();
            _timer.Dispose();
        }
    }

    /// <summary>
    /// Cross-fades the contents of a container while a swap action toggles which
    /// children are visible. Captures the current state to a bitmap, overlays it,
    /// performs the swap underneath, then fades the overlay's alpha to zero.
    /// </summary>
    internal static class ViewTransitions
    {
        public static void CrossFade(Control container, Action swapAction, int durationMs = 220)
        {
            if (container == null || container.Width <= 0 || container.Height <= 0)
            {
                swapAction?.Invoke();
                return;
            }

            Bitmap snapshot;
            try
            {
                snapshot = new Bitmap(container.Width, container.Height);
                container.DrawToBitmap(snapshot, new Rectangle(0, 0, container.Width, container.Height));
            }
            catch
            {
                // If we can't snapshot for any reason, fall back to a hard swap.
                swapAction?.Invoke();
                return;
            }

            var overlay = new PictureBox
            {
                Size = container.Size,
                Location = Point.Empty,
                Image = (Image)snapshot.Clone(),
                SizeMode = PictureBoxSizeMode.Normal,
                BackColor = Color.Transparent,
                Enabled = false, // pass-through for input during the brief fade
            };

            container.Controls.Add(overlay);
            overlay.BringToFront();

            // Toggle the real children behind the overlay.
            try { swapAction?.Invoke(); } catch { /* don't break the animation on a swap throw */ }

            var animator = new Animator();
            animator.Tween(durationMs, t =>
            {
                if (overlay.IsDisposed) return;
                var faded = new Bitmap(snapshot.Width, snapshot.Height);
                using (var g = Graphics.FromImage(faded))
                {
                    var matrix = new ColorMatrix { Matrix33 = Math.Max(0f, 1f - t) };
                    using (var attr = new ImageAttributes())
                    {
                        attr.SetColorMatrix(matrix);
                        g.DrawImage(snapshot,
                            new Rectangle(0, 0, snapshot.Width, snapshot.Height),
                            0, 0, snapshot.Width, snapshot.Height, GraphicsUnit.Pixel, attr);
                    }
                }
                var prev = overlay.Image;
                overlay.Image = faded;
                prev?.Dispose();
            }, () =>
            {
                if (!overlay.IsDisposed)
                {
                    container.Controls.Remove(overlay);
                    overlay.Image?.Dispose();
                    overlay.Dispose();
                }
                snapshot.Dispose();
                animator.Dispose();
            }, Easing.EaseInOutCubic);
        }
    }
}
