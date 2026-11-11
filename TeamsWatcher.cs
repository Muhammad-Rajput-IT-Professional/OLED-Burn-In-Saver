using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OLEDSaver
{
    public class TeamsStatusChangedEventArgs : EventArgs
    {
        public string Status { get; private set; }
        public string DisplayName { get; private set; }
        public bool IsPreventingBlackout { get; private set; }

        public TeamsStatusChangedEventArgs(string status, string displayName, bool isPreventing)
        {
            Status = status;
            DisplayName = displayName;
            IsPreventingBlackout = isPreventing;
        }
    }

    public class TeamsWatcher : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private System.Threading.Timer _pollTimer;
        private object _lock = new object();
        private bool _isDisposed = false;

        public string CurrentStatus { get; private set; }
        public string StatusDisplayName { get; private set; }
        public string StatusDetails { get; private set; }

        public event EventHandler<TeamsStatusChangedEventArgs> StatusChanged;

        public TeamsWatcher()
        {
            CurrentStatus = "Unknown";
            StatusDisplayName = "Checking...";
            StatusDetails = "";

            // Poll every 1.5 seconds on background thread pool
            _pollTimer = new System.Threading.Timer(PollCallback, null, 500, 1500);
        }

        private void PollCallback(object state)
        {
            if (_isDisposed) return;

            string details;
            string newStatus = DetectStatus(out details);
            string newDisplayName = FormatStatusName(newStatus);

            bool changed = false;
            lock (_lock)
            {
                if (newStatus != CurrentStatus || newDetailsDifferent(details))
                {
                    CurrentStatus = newStatus;
                    StatusDisplayName = newDisplayName;
                    StatusDetails = details;
                    changed = true;
                }
            }

            if (changed && StatusChanged != null)
            {
                try
                {
                    StatusChanged(this, new TeamsStatusChangedEventArgs(newStatus, newDisplayName, false));
                }
                catch
                {
                }
            }
        }

        private bool newDetailsDifferent(string details)
        {
            return StatusDetails != details;
        }

        public string DetectStatus(out string details)
        {
            details = "";

            // 1. Check if Microsoft Teams process is running at all
            bool isTeamsRunning = false;
            try
            {
                Process[] p1 = Process.GetProcessesByName("ms-teams");
                Process[] p2 = Process.GetProcessesByName("teams");
                if ((p1 != null && p1.Length > 0) || (p2 != null && p2.Length > 0))
                {
                    isTeamsRunning = true;
                }
            }
            catch
            {
            }

            if (!isTeamsRunning)
            {
                details = "Teams is not running";
                return "Offline";
            }

            // 2. High Priority: Check if Teams is currently in an active Call / Meeting
            // A) Windows CapabilityAccessManager (Microphone & Webcam Active Usage)
            if (IsTeamsMicrophoneOrCameraActive())
            {
                details = "Active call / meeting detected (Hardware audio/video active)";
                return "InACall";
            }

            // B) Window Title Check for Meeting/Call windows
            if (HasActiveMeetingWindow())
            {
                details = "Active meeting window detected";
                return "InACall";
            }

            // 3. Check Teams Local Log Files for Presence (Available, Busy, Away, DoNotDisturb, Presenting)
            string logPresence = CheckLogsForPresence();
            if (!string.IsNullOrEmpty(logPresence))
            {
                details = "Presence detected from Teams log: " + logPresence;
                return logPresence;
            }

            details = "Teams running (idle/ready)";
            return "Available";
        }

        private bool IsTeamsMicrophoneOrCameraActive()
        {
            try
            {
                // Check Packaged New Teams microphone
                if (CheckConsentStoreKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone\MSTeams_8wekyb3d8bbwe"))
                    return true;

                // Check Packaged New Teams webcam
                if (CheckConsentStoreKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\MSTeams_8wekyb3d8bbwe"))
                    return true;

                // Check Classic Teams / NonPackaged entries
                using (RegistryKey baseKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone\NonPackaged"))
                {
                    if (baseKey != null)
                    {
                        foreach (string subName in baseKey.GetSubKeyNames())
                        {
                            if (subName.IndexOf("teams.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                using (RegistryKey appKey = baseKey.OpenSubKey(subName))
                                {
                                    if (appKey != null && IsConsentKeyActive(appKey))
                                        return true;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool CheckConsentStoreKey(string path)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path))
                {
                    if (key != null)
                    {
                        return IsConsentKeyActive(key);
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        private bool IsConsentKeyActive(RegistryKey key)
        {
            try
            {
                object startObj = key.GetValue("LastUsedTimeStart");
                object stopObj = key.GetValue("LastUsedTimeStop");

                if (startObj != null && stopObj != null)
                {
                    long start = Convert.ToInt64(startObj);
                    long stop = Convert.ToInt64(stopObj);

                    // When currently active: stop is either 0 or start is greater than stop
                    if (stop == 0 && start > 0) return true;
                    if (start > stop) return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private bool HasActiveMeetingWindow()
        {
            bool meetingFound = false;

            try
            {
                List<uint> teamsPids = new List<uint>();
                foreach (Process p in Process.GetProcessesByName("ms-teams")) teamsPids.Add((uint)p.Id);
                foreach (Process p in Process.GetProcessesByName("teams")) teamsPids.Add((uint)p.Id);

                if (teamsPids.Count == 0) return false;

                EnumWindows((hWnd, lParam) =>
                {
                    if (!IsWindowVisible(hWnd)) return true;

                    uint pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    if (teamsPids.Contains(pid))
                    {
                        StringBuilder sb = new StringBuilder(512);
                        GetWindowText(hWnd, sb, 512);
                        string title = sb.ToString();

                        if (!string.IsNullOrEmpty(title))
                        {
                            // Detect meeting window patterns in Microsoft Teams
                            // E.g. "Meeting | Microsoft Teams", "Call with John | Microsoft Teams", "| Microsoft Teams, meeting"
                            if (title.IndexOf("Meeting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf("Call with", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf(", meeting", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                meetingFound = true;
                                return false; // Stop enumeration
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }

            return meetingFound;
        }

        private string CheckLogsForPresence()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string newTeamsLogsDir = Path.Combine(localAppData, @"Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\Logs");

                if (Directory.Exists(newTeamsLogsDir))
                {
                    DirectoryInfo dir = new DirectoryInfo(newTeamsLogsDir);
                    FileInfo[] logFiles = dir.GetFiles("MSTeams_*.log");
                    Array.Sort(logFiles, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

                    // Inspect the 2 most recent log files
                    int filesToCheck = Math.Min(2, logFiles.Length);
                    for (int f = 0; f < filesToCheck; f++)
                    {
                        string presence = ReadLogTailForPresence(logFiles[f].FullName);
                        if (!string.IsNullOrEmpty(presence))
                        {
                            return presence;
                        }
                    }
                }

                // Check classic Teams log if present
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string classicLog = Path.Combine(appData, @"Microsoft\Teams\logs.txt");
                if (File.Exists(classicLog))
                {
                    string presence = ReadLogTailForPresence(classicLog);
                    if (!string.IsNullOrEmpty(presence)) return presence;
                }
            }
            catch
            {
            }

            return null;
        }

        private string ReadLogTailForPresence(string logFilePath)
        {
            try
            {
                using (FileStream fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    int bytesToRead = 48 * 1024; // Read last 48 KB
                    long offset = Math.Max(0, fs.Length - bytesToRead);
                    fs.Seek(offset, SeekOrigin.Begin);

                    using (StreamReader reader = new StreamReader(fs, Encoding.UTF8))
                    {
                        string text = reader.ReadToEnd();
                        string[] lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                        // Scan backwards for newest presence update
                        for (int i = lines.Length - 1; i >= 0; i--)
                        {
                            string line = lines[i];

                            // Check UserPresenceAction: {..., availability: XYZ}
                            Match m = Regex.Match(line, @"UserPresenceAction:.*availability:\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                            if (m.Success)
                            {
                                return NormalizePresence(m.Groups[1].Value);
                            }

                            // Check UserDataGlobalState presence
                            Match m2 = Regex.Match(line, @"UserDataGlobalState.*availability:\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                            if (m2.Success)
                            {
                                string val = m2.Groups[1].Value;
                                if (val != "PresenceUnknown" && val != "Offline")
                                {
                                    return NormalizePresence(val);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private string NormalizePresence(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Available";
            string s = raw.Trim().ToLowerInvariant();

            if (s.Contains("call") || s.Contains("meeting")) return "InACall";
            if (s.Contains("present")) return "Presenting";
            if (s.Contains("donotdisturb") || s.Contains("dnd")) return "DoNotDisturb";
            if (s.Contains("busy")) return "Busy";
            if (s.Contains("away") || s.Contains("berightback") || s.Contains("idle")) return "Away";
            if (s.Contains("available") || s.Contains("active")) return "Available";
            if (s.Contains("offline")) return "Offline";

            return raw;
        }

        public static string FormatStatusName(string status)
        {
            switch (status)
            {
                case "InACall":
                    return "🔴 In a Call / Meeting";
                case "Presenting":
                    return "🟣 Presenting / Sharing Screen";
                case "Busy":
                    return "🔴 Busy";
                case "DoNotDisturb":
                    return "⛔ Do Not Disturb";
                case "Away":
                    return "🟡 Away";
                case "Available":
                    return "🟢 Available";
                case "Offline":
                    return "⚪ Offline / Not Running";
                default:
                    return "⚪ " + status;
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                if (_pollTimer != null)
                {
                    _pollTimer.Dispose();
                    _pollTimer = null;
                }
            }
        }
    }
}
