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
            int GetDisplayName(out IntPtr pRetVal);
            int SetDisplayName(string Value, ref Guid EventContext);
            int GetIconPath(out IntPtr pRetVal);
            int SetIconPath(string Value, ref Guid EventContext);
            int GetGroupingParam(out Guid pRetVal);
            int SetGroupingParam(ref Guid Override, ref Guid EventContext);
            int RegisterAudioSessionNotification(IntPtr NewNotifications);
            int UnregisterAudioSessionNotification(IntPtr NewNotifications);
        }

        [ComImport]
        [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2 : IAudioSessionControl
        {
            new int GetState(out AudioSessionState pRetVal);
            new int GetDisplayName(out IntPtr pRetVal);
            new int SetDisplayName(string Value, ref Guid EventContext);
            new int GetIconPath(out IntPtr pRetVal);
            new int SetIconPath(string Value, ref Guid EventContext);
            new int GetGroupingParam(out Guid pRetVal);
            new int SetGroupingParam(ref Guid Override, ref Guid EventContext);
            new int RegisterAudioSessionNotification(IntPtr NewNotifications);
            new int UnregisterAudioSessionNotification(IntPtr NewNotifications);

            int GetSessionIdentifier(out IntPtr pRetVal);
            int GetSessionInstanceIdentifier(out IntPtr pRetVal);
            int GetProcessId(out uint pRetVal);
            int IsSystemSoundsSession();
            int SetDuckingPreference(bool optOut);
        }

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
            if (_isDisposed) return;

            string detectedSource;
            bool isPlaying = CheckIfMediaPlaying(out detectedSource);

            bool changed = false;
            lock (_lock)
            {
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
            detectedSource = "";

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
                                    int sessionCount = 0;
                                    sessionEnum.GetCount(out sessionCount);

                                    for (int i = 0; i < sessionCount; i++)
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
                                                            Process proc = Process.GetProcessById((int)pid);
                                                            string friendly;
                                                            // Only recognize video/media players and browsers.
                                                            // Games will return false and will not pause the timer.
                                                            if (TryGetMediaSource(proc, out friendly))
                                                            {
                                                                detectedSource = friendly;
                                                                return true;
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
                if (dev != null)
                {
                    try { Marshal.ReleaseComObject(dev); } catch { }
                }
                if (enumerator != null)
                {
                    try { Marshal.ReleaseComObject(enumerator); } catch { }
                }
            }

            return false;
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
