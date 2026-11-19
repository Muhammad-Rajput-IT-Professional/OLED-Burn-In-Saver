using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OLEDSaver
{
    public class MediaPlaybackChangedEventArgs : EventArgs
    {
        public bool IsPlaying { get; private set; }
        public string SourceName { get; private set; }
        public string Details { get; private set; }

        public MediaPlaybackChangedEventArgs(bool isPlaying, string sourceName, string details)
        {
            IsPlaying = isPlaying;
            SourceName = sourceName;
            Details = details;
        }
    }

    public class MediaPlaybackWatcher : IDisposable
    {
        // ====================================================================
        // Windows Core Audio COM Interfaces (WASAPI)
        // ====================================================================
        public enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
        public enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }
        public enum AudioSessionState { AudioSessionStateInactive = 0, AudioSessionStateActive = 1, AudioSessionStateExpired = 2 }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);
            [PreserveSig]
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid riid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [ComImport]
        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            [PreserveSig]
            int GetAudioSessionControl(ref Guid AudioSessionGuid, uint StreamFlags, out IntPtr SessionControl);
            [PreserveSig]
            int GetSimpleAudioVolume(ref Guid AudioSessionGuid, uint StreamFlags, out IntPtr AudioVolume);
            [PreserveSig]
            int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
        }

        [ComImport]
        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig]
            int GetCount(out int SessionCount);
            [PreserveSig]
            int GetSession(int SessionIndex, out IAudioSessionControl Session);
        }

        [ComImport]
        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            [PreserveSig]
            int GetState(out AudioSessionState pRetVal);
            [PreserveSig]
            int GetDisplayName(out IntPtr pRetVal);
            [PreserveSig]
            int SetDisplayName(string Value, ref Guid EventContext);
            [PreserveSig]
            int GetIconPath(out IntPtr pRetVal);
            [PreserveSig]
            int SetIconPath(string Value, ref Guid EventContext);
            [PreserveSig]
            int GetGroupingParam(out Guid pRetVal);
            [PreserveSig]
            int SetGroupingParam(ref Guid Override, ref Guid EventContext);
            [PreserveSig]
            int RegisterAudioSessionNotification(IntPtr NewNotifications);
            [PreserveSig]
            int UnregisterAudioSessionNotification(IntPtr NewNotifications);
        }

        [ComImport]
        [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2 : IAudioSessionControl
        {
            [PreserveSig]
            new int GetState(out AudioSessionState pRetVal);
            [PreserveSig]
            new int GetDisplayName(out IntPtr pRetVal);
            [PreserveSig]
            new int SetDisplayName(string Value, ref Guid EventContext);
            [PreserveSig]
            new int GetIconPath(out IntPtr pRetVal);
            [PreserveSig]
            new int SetIconPath(string Value, ref Guid EventContext);
            [PreserveSig]
            new int GetGroupingParam(out Guid pRetVal);
            [PreserveSig]
            new int SetGroupingParam(ref Guid Override, ref Guid EventContext);
            [PreserveSig]
            new int RegisterAudioSessionNotification(IntPtr NewNotifications);
            [PreserveSig]
            new int UnregisterAudioSessionNotification(IntPtr NewNotifications);

            [PreserveSig]
            int GetSessionIdentifier(out IntPtr pRetVal);
            [PreserveSig]
            int GetSessionInstanceIdentifier(out IntPtr pRetVal);
            [PreserveSig]
            int GetProcessId(out uint pRetVal);
            [PreserveSig]
            int IsSystemSoundsSession();
            [PreserveSig]
            int SetDuckingPreference(bool optOut);
        }

        [ComImport]
        [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioMeterInformation
        {
            [PreserveSig]
            int GetPeakValue(out float peak);
        }

        private sealed class PlaybackCandidate
        {
            public uint FirstAudibleTick;
            public uint LastAudibleTick;
            public bool Confirmed;
        }

        private const uint PlaybackConfirmationMs = 5000;
        private const uint SilenceGraceMs = 2000;
        private const float MinimumAudiblePeak = 0.0001f;
        private readonly Dictionary<string, PlaybackCandidate> _playbackCandidates = new Dictionary<string, PlaybackCandidate>();
        private System.Threading.Timer _pollTimer;
        private object _lock = new object();
        private bool _isDisposed = false;

        public bool IsMediaPlaying { get; private set; }
        public string ActiveMediaSource { get; private set; }
        public string StatusDisplayName { get; private set; }

        public event EventHandler<MediaPlaybackChangedEventArgs> PlaybackChanged;

        public MediaPlaybackWatcher()
        {
            IsMediaPlaying = false;
            ActiveMediaSource = "";
            StatusDisplayName = "No active video or media playback";

            // Poll every 1.0 second on background thread pool
            _pollTimer = new System.Threading.Timer(PollCallback, null, 500, 1000);
        }

        private void PollCallback(object state)
        {
            bool changed = false;
            bool isPlaying;
            string detectedSource;
            lock (_lock)
            {
                if (_isDisposed) return;
                isPlaying = CheckIfMediaPlaying(out detectedSource);
                if (isPlaying != IsMediaPlaying || detectedSource != ActiveMediaSource)
                {
                    IsMediaPlaying = isPlaying;
                    ActiveMediaSource = detectedSource;
                    StatusDisplayName = isPlaying
                        ? "▶ Playing: " + detectedSource
                        : "⏹ No active video or media playback";
                    changed = true;
                }
            }

            if (changed && PlaybackChanged != null)
            {
                try
                {
                    PlaybackChanged(this, new MediaPlaybackChangedEventArgs(isPlaying, detectedSource, StatusDisplayName));
                }
                catch
                {
                }
            }
        }

        public bool CheckIfMediaPlaying(out string detectedSource)
        {
            lock (_lock)
            {
                if (_isDisposed)
                {
                    detectedSource = "";
                    return false;
                }
                return CheckIfMediaPlayingCore(out detectedSource);
            }
        }

        private bool CheckIfMediaPlayingCore(out string detectedSource)
        {
            detectedSource = "";
            bool mediaPlaying = false;
            HashSet<string> observedSessions = new HashSet<string>();
            uint now = unchecked((uint)Environment.TickCount);

            // WASAPI CoreAudio Check: Inspect active audio streams.
            // Specifically target recognized web browsers (YouTube, Netflix, Twitch)
            // and dedicated media players (VLC, MPC, Spotify, etc.).
            // Video games are intentionally NOT matched so the timer continues counting down!
            IMMDeviceEnumerator enumerator = null;
            IMMDevice dev = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out dev);
                if (hr == 0 && dev != null)
                {
                    Guid iidMgr = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
                    object mgrObj = null;
                    try
                    {
                        if (dev.Activate(ref iidMgr, 0x17, IntPtr.Zero, out mgrObj) != 0) return false;
                        var mgr = mgrObj as IAudioSessionManager2;

                        if (mgr != null)
                        {
                            IAudioSessionEnumerator sessionEnum = null;
                            try
                            {
                                if (mgr.GetSessionEnumerator(out sessionEnum) != 0) return false;
                                if (sessionEnum != null)
                                {
                                    int sessionCount = 0;
                                    if (sessionEnum.GetCount(out sessionCount) != 0) return false;

                                    for (int i = 0; i < sessionCount; i++)
                                    {
                                        IAudioSessionControl ctl = null;
                                        try
                                        {
                                            if (sessionEnum.GetSession(i, out ctl) != 0) continue;
                                            var ctl2 = ctl as IAudioSessionControl2;
                                            if (ctl2 != null)
                                            {
                                                AudioSessionState st;
                                                if (ctl2.GetState(out st) != 0 || ctl2.IsSystemSoundsSession() == 0) continue;

                                                if (st == AudioSessionState.AudioSessionStateActive)
                                                {
                                                    uint pid;
                                                    if (ctl2.GetProcessId(out pid) != 0) continue;

                                                    if (pid > 0)
                                                    {
                                                        try
                                                        {
                                                            using (Process proc = Process.GetProcessById((int)pid))
                                                            {
                                                                string friendly;
                                                                if (!TryGetMediaSource(proc, out friendly)) continue;

                                                                IAudioMeterInformation meter = ctl as IAudioMeterInformation;
                                                                float peak;
                                                                if (meter == null || meter.GetPeakValue(out peak) != 0) continue;

                                                                IntPtr sessionIdPtr = IntPtr.Zero;
                                                                string sessionId;
                                                                try
                                                                {
                                                                    if (ctl2.GetSessionInstanceIdentifier(out sessionIdPtr) != 0) continue;
                                                                    sessionId = Marshal.PtrToStringUni(sessionIdPtr);
                                                                }
                                                                finally
                                                                {
                                                                    if (sessionIdPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(sessionIdPtr);
                                                                }
                                                                if (string.IsNullOrEmpty(sessionId)) continue;

                                                                observedSessions.Add(sessionId);
                                                                if (IsSustainedPlayback(sessionId, peak > MinimumAudiblePeak, now) && !mediaPlaying)
                                                                {
                                                                    detectedSource = friendly;
                                                                    mediaPlaying = true;
                                                                }
                                                            }
                                                        }
                                                        catch
                                                        {
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            if (ctl != null)
                                            {
                                                try { Marshal.ReleaseComObject(ctl); } catch { }
                                            }
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                if (sessionEnum != null)
                                {
                                    try { Marshal.ReleaseComObject(sessionEnum); } catch { }
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (mgrObj != null)
                        {
                            try { Marshal.ReleaseComObject(mgrObj); } catch { }
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                foreach (string sessionId in new List<string>(_playbackCandidates.Keys))
                {
                    if (!observedSessions.Contains(sessionId)) _playbackCandidates.Remove(sessionId);
                }
                if (dev != null)
                {
                    try { Marshal.ReleaseComObject(dev); } catch { }
                }
                if (enumerator != null)
                {
                    try { Marshal.ReleaseComObject(enumerator); } catch { }
                }
            }

            return mediaPlaying;
        }

        private bool IsSustainedPlayback(string sessionId, bool audible, uint now)
        {
            PlaybackCandidate candidate;
            if (_playbackCandidates.TryGetValue(sessionId, out candidate) &&
                unchecked(now - candidate.LastAudibleTick) > SilenceGraceMs)
            {
                _playbackCandidates.Remove(sessionId);
                candidate = null;
            }

            if (candidate == null)
            {
                if (!audible) return false;
                candidate = new PlaybackCandidate { FirstAudibleTick = now };
                _playbackCandidates.Add(sessionId, candidate);
            }

            if (audible)
            {
                candidate.LastAudibleTick = now;
                if (unchecked(now - candidate.FirstAudibleTick) >= PlaybackConfirmationMs)
                {
                    candidate.Confirmed = true;
                }
            }

            return candidate.Confirmed;
        }

        private static bool TryGetMediaSource(Process proc, out string friendlyName)
        {
            friendlyName = "";
            string name = proc.ProcessName.ToLowerInvariant();

            // 1. Web Browsers (YouTube, Twitch, Netflix, Web Video Players)
            if (name == "chrome") { friendlyName = FormatBrowserTitle(proc, "Google Chrome"); return true; }
            if (name == "msedge" || name == "msedgewebview2") { friendlyName = FormatBrowserTitle(proc, "Microsoft Edge"); return true; }
            if (name == "firefox") { friendlyName = FormatBrowserTitle(proc, "Mozilla Firefox"); return true; }
            if (name == "brave") { friendlyName = FormatBrowserTitle(proc, "Brave Browser"); return true; }
            if (name == "opera" || name == "opera_gx") { friendlyName = FormatBrowserTitle(proc, "Opera Browser"); return true; }
            if (name == "vivaldi") { friendlyName = FormatBrowserTitle(proc, "Vivaldi Browser"); return true; }
            if (name == "arc") { friendlyName = FormatBrowserTitle(proc, "Arc Browser"); return true; }
            if (name == "waterfox") { friendlyName = FormatBrowserTitle(proc, "Waterfox"); return true; }

            // 2. Dedicated Video & Media Players
            if (name == "vlc") { friendlyName = FormatPlayerTitle(proc, "VLC Media Player"); return true; }
            if (name.StartsWith("mpc-hc") || name.StartsWith("mpc-be")) { friendlyName = FormatPlayerTitle(proc, "Media Player Classic"); return true; }
            if (name == "wmplayer") { friendlyName = "Windows Media Player"; return true; }
            if (name.Contains("potplayer")) { friendlyName = "PotPlayer"; return true; }
            if (name == "mpv") { friendlyName = "MPV Player"; return true; }
            if (name == "kmplayer") { friendlyName = "KMPlayer"; return true; }
            if (name == "gom") { friendlyName = "GOM Player"; return true; }
            if (name == "spotify") { friendlyName = FormatPlayerTitle(proc, "Spotify"); return true; }
            if (name == "foobar2000") { friendlyName = "Foobar2000"; return true; }
            if (name == "music.ui" || name.Contains("zune")) { friendlyName = "Media Player"; return true; }
            if (name == "plex" || name == "plexmediaplayer") { friendlyName = "Plex"; return true; }
            if (name == "kodi") { friendlyName = "Kodi"; return true; }
            if (name == "itunes") { friendlyName = "iTunes"; return true; }
            if (name == "tidal") { friendlyName = "TIDAL"; return true; }
            if (name == "deezer") { friendlyName = "Deezer"; return true; }

            // 3. Official Streaming Apps
            if (name == "netflix") { friendlyName = "Netflix"; return true; }
            if (name == "primevideo") { friendlyName = "Prime Video"; return true; }
            if (name == "hulu") { friendlyName = "Hulu"; return true; }
            if (name == "disneyplus") { friendlyName = "Disney+"; return true; }

            // Not a media playback app (e.g. video games, IDEs, office apps)
            return false;
        }

        private static string GetProcessOrSiblingTitle(Process proc)
        {
            string title = "";
            try { title = proc.MainWindowTitle; } catch { }

            if (string.IsNullOrEmpty(title))
            {
                try
                {
                    Process[] siblings = Process.GetProcessesByName(proc.ProcessName);
                    for (int i = 0; i < siblings.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(siblings[i].MainWindowTitle))
                        {
                            title = siblings[i].MainWindowTitle;
                            break;
                        }
                    }
                }
                catch
                {
                }
            }

            return title ?? "";
        }

        private static string FormatPlayerTitle(Process proc, string playerName)
        {
            string title = GetProcessOrSiblingTitle(proc);
            if (!string.IsNullOrEmpty(title))
            {
                return playerName + ": " + title;
            }
            return playerName;
        }

        private static string FormatBrowserTitle(Process proc, string browserName)
        {
            string title = GetProcessOrSiblingTitle(proc);
            if (!string.IsNullOrEmpty(title))
            {
                if (title.IndexOf("YouTube", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string clean = CleanBrowserSuffix(title);
                    return "YouTube (" + clean + ")";
                }
                if (title.IndexOf("Netflix", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Netflix (" + browserName + ")";
                }
                if (title.IndexOf("Twitch", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Twitch (" + browserName + ")";
                }
                if (title.IndexOf("Disney+", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Disney+ (" + browserName + ")";
                }
                if (title.IndexOf("Prime Video", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Prime Video (" + browserName + ")";
                }

                string simplified = CleanBrowserSuffix(title);
                if (simplified.Length <= 45)
                {
                    return simplified + " (" + browserName + ")";
                }
                return simplified.Substring(0, 42) + "... (" + browserName + ")";
            }

            return browserName + " (Media Playback)";
        }

        private static string CleanBrowserSuffix(string title)
        {
            string t = title;
            string[] suffixes = new string[] {
                " - Google Chrome",
                " - Microsoft Edge",
                " - Mozilla Firefox",
                " - Brave",
                " - Opera",
                " - Vivaldi",
                " - Arc"
            };
            foreach (var s in suffixes)
            {
                if (t.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                {
                    t = t.Substring(0, t.Length - s.Length);
                }
            }
            return t.Trim();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                _playbackCandidates.Clear();
                if (_pollTimer != null)
                {
                    _pollTimer.Dispose();
                    _pollTimer = null;
                }
            }
        }
    }
}
