using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OLEDSaver
{
    public class InputWatcher : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_INPUT = 0x00FF;
        private const uint RIDEV_INPUTSINK = 0x00000100;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc _proc;
        private long _lastKeyboardTick = 0;
        private long _lastMouseTick = 0;
        private Point _lastKnownCursorPos;
        private bool _isDisposed = false;

        public event EventHandler KeyboardActivityDetected;
        public event EventHandler MouseActivityDetected;

        public InputWatcher(IntPtr windowHandle)
        {
            _lastKeyboardTick = Environment.TickCount;
            _lastMouseTick = Environment.TickCount;
            _lastKnownCursorPos = Cursor.Position;

            // 1. Register RawInput for keyboard sink
            try
            {
                RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
                rid[0].usUsagePage = 0x01; // Generic Desktop Controls
                rid[0].usUsage = 0x06;     // Keyboard
                rid[0].dwFlags = RIDEV_INPUTSINK;
                rid[0].hwndTarget = windowHandle;
                RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            }
            catch
            {
            }

            // 2. Install Low-Level Keyboard Hook as a robust dual-layer monitor
            // Note: zero key-logging. We strictly timestamp the event.
            _proc = HookCallback;
            try
            {
                using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
                using (var curModule = curProcess.MainModule)
                {
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                }
            }
            catch
            {
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                _lastKeyboardTick = Environment.TickCount;
                if (KeyboardActivityDetected != null)
                {
                    KeyboardActivityDetected(this, EventArgs.Empty);
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void ProcessWndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                _lastKeyboardTick = Environment.TickCount;
                if (KeyboardActivityDetected != null)
                {
                    KeyboardActivityDetected(this, EventArgs.Empty);
                }
            }
        }

        public void NotifyKeyboardActivity()
        {
            _lastKeyboardTick = Environment.TickCount;
            if (KeyboardActivityDetected != null)
            {
                KeyboardActivityDetected(this, EventArgs.Empty);
            }
        }

        public void NotifyMouseActivity()
        {
            _lastMouseTick = Environment.TickCount;
            if (MouseActivityDetected != null)
            {
                MouseActivityDetected(this, EventArgs.Empty);
            }
        }

        public TimeSpan GetKeyboardIdleTime()
        {
            long currentTick = Environment.TickCount;
            long diff = currentTick - _lastKeyboardTick;
            if (diff < 0) diff = 0; // Handle tick rollover
            return TimeSpan.FromMilliseconds(diff);
        }

        public TimeSpan GetSystemInputIdleTime()
        {
            LASTINPUTINFO lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(lii);
            if (GetLastInputInfo(ref lii))
            {
                uint currentTick = (uint)Environment.TickCount;
                uint diff = currentTick - lii.dwTime;
                return TimeSpan.FromMilliseconds(diff);
            }
            return GetKeyboardIdleTime();
        }

        public void ResetTimer()
        {
            _lastKeyboardTick = Environment.TickCount;
            _lastMouseTick = Environment.TickCount;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                if (_hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookId);
                    _hookId = IntPtr.Zero;
                }
            }
            GC.SuppressFinalize(this);
        }

        ~InputWatcher()
        {
            Dispose();
        }
    }
}
