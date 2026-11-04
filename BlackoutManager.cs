using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OLEDSaver
{
    public class BlackoutManager : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MONITORPOWER = 0xF170;
        private const int MONITOR_OFF = 2;
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        private List<BlackoutForm> _activeForms = new List<BlackoutForm>();
        private Timer _wakeWatchTimer;
        private Point _blankedCursorPos;
        private int _sensitivityPixels = 10;
        private bool _isCursorHidden = false;
        private bool _isHardwarePowerOff = false;
        private IntPtr _hostHwnd = IntPtr.Zero;
        private long _blackoutStartTick = 0;
        private const int GRACE_PERIOD_MS = 1500; // 1.5s grace period to ignore click settling and synthetic events
        private object _lock = new object();

        public bool IsBlackoutActive { get; private set; }
        public bool IsHardwarePowerOff { get { return _isHardwarePowerOff; } }

        public event EventHandler BlackoutStarted;
        public event EventHandler BlackoutEnded;

        public BlackoutManager(IntPtr hostHwnd)
        {
            _hostHwnd = hostHwnd;
            _wakeWatchTimer = new Timer();
            _wakeWatchTimer.Interval = 75;
            _wakeWatchTimer.Tick += WakeWatchTimer_Tick;
        }

        public void StartAction(string screenAction, List<Rectangle> targetScreenBounds, int sensitivityPixels)
        {
            lock (_lock)
            {
                if (IsBlackoutActive) return;
                IsBlackoutActive = true;
                _sensitivityPixels = Math.Max(4, sensitivityPixels);
                _blackoutStartTick = Environment.TickCount;
                _blankedCursorPos = Cursor.Position;
                _isHardwarePowerOff = (screenAction == "HardwarePowerOff");

                if (_isHardwarePowerOff)
                {
                    // Hardware Screen Power Off (Standby / No Signal)
                    // Windows GPU stops sending video signal to all displays.
                    // We run on a 350ms delay so the physical mouse button release
                    // has completely settled and does not abort display sleep!
                    System.Threading.ThreadPool.QueueUserWorkItem((state) =>
                    {
                        System.Threading.Thread.Sleep(350);
                        try
                        {
                            SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)MONITOR_OFF);
                        }
                        catch
                        {
                        }
                    });
                }
                else
                {
                    // Pure OLED Blackout Overlay (#000000)
                    // Creates true pitch-black window on selected monitors only
                    foreach (Rectangle bounds in targetScreenBounds)
                    {
                        BlackoutForm form = new BlackoutForm(bounds, _sensitivityPixels, RequestWakeUp);
                        _activeForms.Add(form);
                        form.Show();
                    }

                    if (!_isCursorHidden)
                    {
                        Cursor.Hide();
                        _isCursorHidden = true;
                    }
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

                // Close all blackout forms
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

                if (_isCursorHidden)
                {
                    Cursor.Show();
                    _isCursorHidden = false;
                }

                if (_isHardwarePowerOff)
                {
                    // Wake hardware monitors smoothly by simulating a 1-pixel micro-jitter.
                    // This triggers the Windows kernel power manager to wake up all displays naturally,
                    // without driver renegotiation loops or DWM crashes!
                    try
                    {
                        mouse_event(MOUSEEVENTF_MOVE, 0, 1, 0, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(20);
                        mouse_event(MOUSEEVENTF_MOVE, 0, unchecked((uint)-1), 0, UIntPtr.Zero);
                    }
                    catch
                    {
                    }
                    _isHardwarePowerOff = false;
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

            long elapsed = Environment.TickCount - _blackoutStartTick;
            if (elapsed < GRACE_PERIOD_MS)
            {
                return;
            }

            if (_isHardwarePowerOff)
            {
                // In hardware standby mode, displays disconnect and cursor coordinates jump.
                // Therefore, we MUST NOT compare Cursor.Position coordinates!
                // Instead, query GetLastInputInfo which strictly tracks true physical keyboard/mouse events.
                LASTINPUTINFO lii = new LASTINPUTINFO();
                lii.cbSize = (uint)Marshal.SizeOf(lii);
                if (GetLastInputInfo(ref lii))
                {
                    long lastInputTick = (long)lii.dwTime;
                    // If physical human input occurred after the sleep command and grace period:
                    if (lastInputTick > _blackoutStartTick + 1200)
                    {
                        RequestWakeUp();
                    }
                }
            }
            else
            {
                // In pure overlay mode, check if the mouse moved more than sensitivity threshold
                Point cur = Cursor.Position;
                int dx = Math.Abs(cur.X - _blankedCursorPos.X);
                int dy = Math.Abs(cur.Y - _blankedCursorPos.Y);

                if (dx >= _sensitivityPixels || dy >= _sensitivityPixels)
                {
                    RequestWakeUp();
                }
            }
        }

        public void Dispose()
        {
            RequestWakeUp();
            if (_wakeWatchTimer != null)
            {
                _wakeWatchTimer.Stop();
                _wakeWatchTimer.Dispose();
                _wakeWatchTimer = null;
            }
        }
    }
}
