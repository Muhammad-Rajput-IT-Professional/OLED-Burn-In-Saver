using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OLEDSaver
{
    public class BlackoutForm : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOACTIVATE = 0x0010;

        private Point _initialCursorPos;
        private int _sensitivity;
        private Action _onWakeRequested;
        private bool _isClosing = false;
        private long _showTick = 0;
        private Rectangle _targetBounds;

        public BlackoutForm(Rectangle bounds, int sensitivityPixels, Action onWakeRequested)
        {
            // Set bounds 1 pixel shorter at bottom so Windows Shell does not classify
            // the blackout as an exclusive full-screen app (which would trigger Windows Do Not Disturb and mute notification sounds)
            _targetBounds = new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Max(1, bounds.Height - 1));
            _sensitivity = Math.Max(2, sensitivityPixels);
            _onWakeRequested = onWakeRequested;
            _showTick = Environment.TickCount;

            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = _targetBounds;
            this.BackColor = Color.Black;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);

            this.MouseMove += BlackoutForm_MouseMove;
            this.MouseDown += BlackoutForm_MouseDown;
            this.KeyDown += BlackoutForm_KeyDown;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _showTick = Environment.TickCount;
            _initialCursorPos = Cursor.Position;

            // Place overlay on top of Z-order covering screen without stealing focus
            // (Preserves active foreground state so Windows Do Not Disturb / Focus Assist does not suppress notification sounds!)
            try
            {
                SetWindowPos(this.Handle, HWND_TOPMOST, _targetBounds.X, _targetBounds.Y, _targetBounds.Width, _targetBounds.Height, SWP_SHOWWINDOW | SWP_NOACTIVATE);
            }
            catch
            {
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(Color.Black))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        private void BlackoutForm_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isClosing) return;

            // Grace period: ignore synthetic mouse move messages during window opening
            if (Environment.TickCount - _showTick < 1200)
            {
                _initialCursorPos = Cursor.Position;
                return;
            }

            Point current = Cursor.Position;
            int dx = Math.Abs(current.X - _initialCursorPos.X);
            int dy = Math.Abs(current.Y - _initialCursorPos.Y);

            if (dx >= _sensitivity || dy >= _sensitivity)
            {
                TriggerWake();
            }
        }

        private void BlackoutForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (Environment.TickCount - _showTick < 300) return;
            TriggerWake();
        }

        private void BlackoutForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (Environment.TickCount - _showTick < 300) return;
            TriggerWake();
        }

        private void TriggerWake()
        {
            if (_isClosing) return;
            _isClosing = true;

            if (_onWakeRequested != null)
            {
                _onWakeRequested();
            }
        }
    }
}
