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
        public string PrimaryStatus { get; private set; }
        public string DisplayName { get; private set; }
        public List<string> ActiveStatuses { get; private set; }

        public TeamsStatusChangedEventArgs(string primaryStatus, string displayName, List<string> activeStatuses)
        {
            PrimaryStatus = primaryStatus;
            DisplayName = displayName;
            ActiveStatuses = activeStatuses;
        }
    }

    public class TeamsWatcher : IDisposable
    {
        // WASAPI Audio Capture Interop to detect active microphone streaming in calls
        private enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
        private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }
        private enum AudioSessionState { AudioSessionStateInactive = 0, AudioSessionStateActive = 1, AudioSessionStateExpired = 2 }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid riid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [ComImport]
        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            int GetAudioSessionControl(ref Guid AudioSessionGuid, uint StreamFlags, out IntPtr SessionControl);
            int GetSimpleAudioVolume(ref Guid AudioSessionGuid, uint StreamFlags, out IntPtr AudioVolume);
            int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
        }

        [ComImport]
        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            int GetCount(out int SessionCount);
            int GetSession(int SessionIndex, out IAudioSessionControl Session);
        }

        [ComImport]
        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            int GetState(out AudioSessionState pRetVal);
        }

        [ComImport]
        [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2 : IAudioSessionControl
        {
            new int GetState(out AudioSessionState pRetVal);
            int GetDisplayName(out IntPtr pRetVal);
            int SetDisplayName(string Value, ref Guid EventContext);
            int GetIconPath(out IntPtr pRetVal);
            int SetIconPath(string Value, ref Guid EventContext);
            int GetGroupingParam(out Guid pRetVal);
            int SetGroupingParam(ref Guid Override, ref Guid EventContext);
            int RegisterAudioSessionNotification(IntPtr NewNotifications);
            int UnregisterAudioSessionNotification(IntPtr NewNotifications);

            int GetSessionIdentifier(out IntPtr pRetVal);
            int GetSessionInstanceIdentifier(out IntPtr pRetVal);
            int GetProcessId(out uint pRetVal);
        }

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

        public string PrimaryStatus { get; private set; }
        public List<string> ActiveStatuses { get; private set; }
        public string StatusDisplayName { get; private set; }
        public string StatusDetails { get; private set; }
        public string BlockingReasonDisplayName { get; private set; }

        public event EventHandler<TeamsStatusChangedEventArgs> StatusChanged;

        public TeamsWatcher()
        {
            PrimaryStatus = "Unknown";
            ActiveStatuses = new List<string>();
            StatusDisplayName = "Checking...";
            StatusDetails = "";
            BlockingReasonDisplayName = "";

            // Poll every 1.5 seconds on background thread pool
            _pollTimer = new System.Threading.Timer(PollCallback, null, 250, 1500);
        }

        private void PollCallback(object state)
        {
            if (_isDisposed) return;

            string details;
            List<string> detectedStatuses;
            string newPrimaryStatus = DetectStatuses(out detectedStatuses, out details);
            string newDisplayName = FormatMultiStatusDisplayName(detectedStatuses);

            bool changed = false;
            lock (_lock)
            {
                if (newPrimaryStatus != PrimaryStatus || !AreListsEqual(detectedStatuses, ActiveStatuses) || StatusDetails != details)
                {
                    PrimaryStatus = newPrimaryStatus;
                    ActiveStatuses = detectedStatuses;
                    StatusDisplayName = newDisplayName;
                    StatusDetails = details;
                    changed = true;
                }
            }

            if (changed && StatusChanged != null)
            {
                try
                {
                    StatusChanged(this, new TeamsStatusChangedEventArgs(newPrimaryStatus, newDisplayName, detectedStatuses));
                }
                catch
                {
                }
            }
        }

        private bool AreListsEqual(List<string> a, List<string> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        public bool IsPreventingBlackout(List<string> preventStatuses)
        {
            if (preventStatuses == null || preventStatuses.Count == 0) return false;

            lock (_lock)
            {
                // Check if ANY active account's status is in the prevent list
                foreach (string status in ActiveStatuses)
                {
                    if (preventStatuses.Contains(status))
                    {
                        BlockingReasonDisplayName = FormatSingleStatusName(status);
                        return true;
                    }
                }

                if (preventStatuses.Contains(PrimaryStatus))
                {
                    BlockingReasonDisplayName = FormatSingleStatusName(PrimaryStatus);
                    return true;
                }
            }

            return false;
        }

        public string DetectStatuses(out List<string> allStatuses, out string details)
        {
            allStatuses = new List<string>();
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
                allStatuses.Add("Offline");
                return "Offline";
            }

            // 2. High Priority: Check if Teams is currently in an active Call / Meeting
            bool isAudioVideoCall = IsTeamsMicrophoneOrCameraActive();
            bool isMeetingWindow = HasActiveMeetingWindow();

            if (isAudioVideoCall || isMeetingWindow)
            {
                allStatuses.Add("InACall");
                details = isAudioVideoCall
                    ? "Active call / meeting detected (Microphone/Camera active)"
                    : "Active meeting window detected";
            }

            // 3. Multi-Account Presence from Logs
            List<string> logPresences = GetPresencesFromLogs();
            foreach (string p in logPresences)
            {
                if (!allStatuses.Contains(p))
                {
                    allStatuses.Add(p);
                }
            }

            if (allStatuses.Count == 0)
            {
                allStatuses.Add("Available");
                details = "Teams running (idle)";
            }

            // Sort all detected statuses by importance/severity
            allStatuses.Sort((a, b) => GetStatusPriority(b).CompareTo(GetStatusPriority(a)));

            string primary = allStatuses[0];
            return primary;
        }

        private static int GetStatusPriority(string status)
        {
            switch (status)
            {
                case "InACall": return 100;
                case "Presenting": return 90;
                case "DoNotDisturb": return 80;
                case "Busy": return 70;
                case "Away": return 60;
                case "Available": return 50;
                case "Offline": return 10;
                default: return 0;
            }
        }

        private bool IsTeamsMicrophoneOrCameraActive()
        {
            // 1. Real-time WASAPI Microphone Capture session check
            // When Teams is in a call/meeting, its eCapture audio session is AudioSessionStateActive!
            // When not in a call/meeting, the session is AudioSessionStateInactive.
            IMMDeviceEnumerator enumerator = null;
            IMMDevice dev = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out dev);
                if (hr != 0 || dev == null)
                {
                    hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out dev);
                }

                if (hr == 0 && dev != null)
                {
                    Guid iidMgr = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
                    object mgrObj = null;
                    dev.Activate(ref iidMgr, 0x17, IntPtr.Zero, out mgrObj);
                    var mgr = mgrObj as IAudioSessionManager2;
                    if (mgr != null)
                    {
                        IAudioSessionEnumerator sessionEnum = null;
                        try
                        {
                            mgr.GetSessionEnumerator(out sessionEnum);
                            if (sessionEnum != null)
                            {
                                int count = 0;
                                sessionEnum.GetCount(out count);
                                for (int i = 0; i < count; i++)
                                {
                                    IAudioSessionControl ctl = null;
                                    try
                                    {
                                        sessionEnum.GetSession(i, out ctl);
                                        var ctl2 = ctl as IAudioSessionControl2;
                                        if (ctl2 != null)
                                        {
                                            AudioSessionState st;
                                            ctl2.GetState(out st);
                                            if (st == AudioSessionState.AudioSessionStateActive)
                                            {
                                                uint pid;
                                                ctl2.GetProcessId(out pid);
                                                if (pid > 0)
                                                {
                                                    try
                                                    {
                                                        Process p = Process.GetProcessById((int)pid);
                                                        string name = p.ProcessName.ToLowerInvariant();
                                                        if (name == "ms-teams" || name == "teams")
                                                        {
                                                            return true;
                                                        }
                                                    }
                                                    catch { }
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        if (ctl != null) try { Marshal.ReleaseComObject(ctl); } catch { }
                                    }
                                }
                            }
                        }
                        finally
                        {
                            if (sessionEnum != null) try { Marshal.ReleaseComObject(sessionEnum); } catch { }
                            if (mgrObj != null) try { Marshal.ReleaseComObject(mgrObj); } catch { }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (dev != null) try { Marshal.ReleaseComObject(dev); } catch { }
                if (enumerator != null) try { Marshal.ReleaseComObject(enumerator); } catch { }
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
                            // Match real active meeting or call windows (e.g. "Meeting | Microsoft Teams, meeting", "Call with John | Microsoft Teams, call", "Microsoft Teams Call")
                            // Do NOT match regular chat windows like "Team Meeting | Microsoft Teams"!
                            if (title.EndsWith(", meeting", StringComparison.OrdinalIgnoreCase) ||
                                title.EndsWith(", call", StringComparison.OrdinalIgnoreCase) ||
                                title.IndexOf(" | Microsoft Teams, meeting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf(" | Microsoft Teams, call", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.Equals("Microsoft Teams Call", StringComparison.OrdinalIgnoreCase))
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

        private List<string> GetPresencesFromLogs()
        {
            List<string> presences = new List<string>();

            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string newTeamsLogsDir = Path.Combine(localAppData, @"Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\Logs");

                if (Directory.Exists(newTeamsLogsDir))
                {
                    DirectoryInfo dir = new DirectoryInfo(newTeamsLogsDir);
                    FileInfo[] logFiles = dir.GetFiles("MSTeams_*.log");
                    Array.Sort(logFiles, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

                    int filesToCheck = Math.Min(2, logFiles.Length);
                    for (int f = 0; f < filesToCheck; f++)
                    {
                        List<string> filePresences = ReadLogTailForPresences(logFiles[f].FullName);
                        foreach (string p in filePresences)
                        {
                            if (!presences.Contains(p))
                            {
                                presences.Add(p);
                            }
                        }

                        if (presences.Count > 0)
                        {
                            break;
                        }
                    }
                }

                // Check classic Teams log if needed
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string classicLog = Path.Combine(appData, @"Microsoft\Teams\logs.txt");
                if (File.Exists(classicLog))
                {
                    List<string> classicPresences = ReadLogTailForPresences(classicLog);
                    foreach (string p in classicPresences)
                    {
                        if (!presences.Contains(p)) presences.Add(p);
                    }
                }
            }
            catch
            {
            }

            return presences;
        }

        private List<string> ReadLogTailForPresences(string logFilePath)
        {
            List<string> found = new List<string>();

            try
            {
                using (FileStream fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    int bytesToRead = 64 * 1024; // Read last 64 KB
                    long offset = Math.Max(0, fs.Length - bytesToRead);
                    fs.Seek(offset, SeekOrigin.Begin);

                    using (StreamReader reader = new StreamReader(fs, Encoding.UTF8))
                    {
                        string text = reader.ReadToEnd();
                        string[] lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                        // Track presence per cloud context to support multi-account setups accurately
                        // E.g. https://teams.microsoft.com (Work) vs https://teams.live.com (Personal)
                        Dictionary<string, string> contextPresences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        // Scan backwards for the latest presence updates
                        for (int i = lines.Length - 1; i >= 0; i--)
                        {
                            string line = lines[i];

                            // Check UserPresenceAction with cloud_context
                            Match actionMatch = Regex.Match(line, @"UserPresenceAction:.*cloud_context:\s*([^,]+).*availability:\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                            if (actionMatch.Success)
                            {
                                string context = actionMatch.Groups[1].Value.Trim();
                                string avail = NormalizePresence(actionMatch.Groups[2].Value);
                                if (!contextPresences.ContainsKey(context))
                                {
                                    contextPresences[context] = avail;
                                }
                            }

                            // Check UserDataGlobalState for all accounts on the machine
                            if (line.IndexOf("UserDataGlobalState", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                MatchCollection mc = Regex.Matches(line, @"availability:\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
                                foreach (Match m in mc)
                                {
                                    string val = m.Groups[1].Value;
                                    if (val != "PresenceUnknown" && val != "Offline")
                                    {
                                        string norm = NormalizePresence(val);
                                        if (!found.Contains(norm))
                                        {
                                            found.Add(norm);
                                        }
                                    }
                                }

                                if (found.Count > 0)
                                {
                                    break;
                                }
                            }
                        }

                        // Add presences captured by cloud context
                        foreach (KeyValuePair<string, string> kvp in contextPresences)
                        {
                            if (!found.Contains(kvp.Value) && kvp.Value != "PresenceUnknown" && kvp.Value != "Offline")
                            {
                                found.Add(kvp.Value);
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return found;
        }

        private string NormalizePresence(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Available";
            string s = raw.Trim().ToLowerInvariant();

            if (s == "inmeeting" || s == "inacall" || s == "incall" || s == "in_a_meeting" || s == "in_a_call" || s == "onacal" || s == "onacall") return "InACall";
            if (s.Contains("present")) return "Presenting";
            if (s.Contains("donotdisturb") || s.Contains("dnd") || s.Contains("outofoffice") || s.Contains("oof") || s.Contains("focusing")) return "DoNotDisturb";
            if (s.Contains("busy")) return "Busy";
            if (s.Contains("away") || s.Contains("berightback") || s.Contains("idle") || s.Contains("beshortlyrightback")) return "Away";
            if (s.Contains("available") || s.Contains("active")) return "Available";
            if (s.Contains("offline")) return "Offline";

            return raw;
        }

        public static string FormatSingleStatusName(string status)
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
                    return "⛔ Do Not Disturb / Out of Office";
                case "Away":
                    return "🟡 Away / Be Right Back";
                case "Available":
                    return "🟢 Available";
                case "Offline":
                    return "⚪ Offline / Not Running";
                default:
                    return "⚪ " + status;
            }
        }

        public static string FormatMultiStatusDisplayName(List<string> statuses)
        {
            if (statuses == null || statuses.Count == 0) return "⚪ Unknown";

            List<string> formatted = new List<string>();
            foreach (string st in statuses)
            {
                formatted.Add(FormatSingleStatusName(st));
            }

            return string.Join(" • ", formatted.ToArray());
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
