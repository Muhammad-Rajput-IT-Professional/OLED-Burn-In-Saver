using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OLEDSaver
{
    public class BlackoutManager : IDisposable
    {
        private List<BlackoutForm> _activeForms = new List<BlackoutForm>();
        private Timer _wakeWatchTimer;
        private Point _blankedCursorPos;
        private int _sensitivityPixels = 10;
        private bool _isCursorHidden = false;
        private long _blackoutStartTick = 0;
        private const int GRACE_PERIOD_MS = 1200; // 1.2s grace period to ignore synthetic window events
        private object _lock = new object();

        public bool IsBlackoutActive { get; private set; }

        public event EventHandler BlackoutStarted;
        public event EventHandler BlackoutEnded;

        public BlackoutManager()
        {
            _wakeWatchTimer = new Timer();
            _wakeWatchTimer.Interval = 50;
            _wakeWatchTimer.Tick += WakeWatchTimer_Tick;
        }

        public void StartBlackout(List<Rectangle> targetScreenBounds, int sensitivityPixels)
        {
            lock (_lock)
            {
                if (IsBlackoutActive) return;
                IsBlackoutActive = true;
                _sensitivityPixels = Math.Max(4, sensitivityPixels);
                _blackoutStartTick = Environment.TickCount;
                _blankedCursorPos = Cursor.Position;

                // Create a blackout form on each selected monitor only
                foreach (Rectangle bounds in targetScreenBounds)
                {
                    BlackoutForm form = new BlackoutForm(bounds, _sensitivityPixels, RequestWakeUp);
                    _activeForms.Add(form);
                    form.Show();
                }

                // Hide cursor while blackout is active
                if (!_isCursorHidden)
                {
                    Cursor.Hide();
                    _isCursorHidden = true;
                }

                _wakeWatchTimer.Start();

                if (BlackoutStarted != null)
                {
                    BlackoutStarted(this, EventArgs.Empty);
                }
            }
        }

        public void RequestWakeUp()
        {
            lock (_lock)
            {
                if (!IsBlackoutActive) return;

                _wakeWatchTimer.Stop();

                // Close all blackout forms immediately
                foreach (BlackoutForm form in _activeForms)
                {
                    try
                    {
                        form.Close();
                        form.Dispose();
                    }
                    catch
                    {
                    }
                }
                _activeForms.Clear();

                // Restore cursor
                if (_isCursorHidden)
                {
                    Cursor.Show();
                    _isCursorHidden = false;
                }

                IsBlackoutActive = false;

                if (BlackoutEnded != null)
                {
                    BlackoutEnded(this, EventArgs.Empty);
                }
            }
        }

        private void WakeWatchTimer_Tick(object sender, EventArgs e)
        {
            if (!IsBlackoutActive) return;

            // Grace period: ignore mouse movement during initial 1.2 seconds so settling motion doesn't cause instant wake-up
            long elapsed = Environment.TickCount - _blackoutStartTick;
            if (elapsed < GRACE_PERIOD_MS)
            {
                _blankedCursorPos = Cursor.Position;
                return;
            }

            Point current = Cursor.Position;
            int dx = Math.Abs(current.X - _blankedCursorPos.X);
            int dy = Math.Abs(current.Y - _blankedCursorPos.Y);

            if (dx >= _sensitivityPixels || dy >= _sensitivityPixels)
            {
                RequestWakeUp();
            }
        }

        public void Dispose()
        {
            RequestWakeUp();
            if (_wakeWatchTimer != null)
            {
                _wakeWatchTimer.Dispose();
                _wakeWatchTimer = null;
            }
        }
    }
}
