using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OLEDSaver
{
    public class DisplayInfo
    {
        public string DeviceName { get; set; }
        public string FriendlyName { get; set; }
        public Rectangle Bounds { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsSelected { get; set; }
        public string DeviceId { get; set; }

        public string DisplayText
        {
            get
            {
                string tag = IsPrimary ? " [Primary]" : " [External]";
                return string.Format("{0}{1} - {2}x{3} at ({4},{5})",
                    FriendlyName, tag, Bounds.Width, Bounds.Height, Bounds.X, Bounds.Y);
            }
        }
    }

    public static class DisplayHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        public static List<DisplayInfo> GetConnectedDisplays(List<string> savedSelectedIds)
        {
            List<DisplayInfo> list = new List<DisplayInfo>();
            Screen[] screens = Screen.AllScreens;

            for (int i = 0; i < screens.Length; i++)
            {
                Screen s = screens[i];
                string friendlyName = GetFriendlyMonitorName(s.DeviceName, i);
                string id = s.DeviceName;

                bool selected = true;
                if (savedSelectedIds != null && savedSelectedIds.Count > 0)
                {
                    selected = savedSelectedIds.Contains(id) || savedSelectedIds.Contains(friendlyName);
                }

                DisplayInfo info = new DisplayInfo();
                info.DeviceName = s.DeviceName;
                info.FriendlyName = friendlyName;
                info.Bounds = s.Bounds;
                info.IsPrimary = s.Primary;
                info.IsSelected = selected;
                info.DeviceId = id;

                list.Add(info);
            }

            return list;
        }

        private static string GetFriendlyMonitorName(string deviceName, int index)
        {
            try
            {
                DISPLAY_DEVICE d = new DISPLAY_DEVICE();
                d.cb = Marshal.SizeOf(d);

                // First call: gets monitor device attached to adapter
                if (EnumDisplayDevices(deviceName, 0, ref d, 0))
                {
                    if (!string.IsNullOrEmpty(d.DeviceString) &&
                        !d.DeviceString.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase))
                    {
                        return d.DeviceString;
                    }
                    if (!string.IsNullOrEmpty(d.DeviceID))
                    {
                        // DeviceID looks like: MONITOR\AUS2723\{GUID}
                        string[] parts = d.DeviceID.Split('\\');
                        if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                        {
                            return "Monitor " + parts[1];
                        }
                    }
                }
            }
            catch
            {
            }

            return "Display " + (index + 1);
        }
    }
}
