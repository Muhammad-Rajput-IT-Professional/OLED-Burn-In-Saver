using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace OLEDSaver
{
    public class AppSettings
    {
        public int TimeoutMinutes { get; set; }
        public string IdleMode { get; set; } // "KeyboardOnly" or "KeyboardAndMouse"
        public string ScreenAction { get; set; } // "BlackoutOverlay" or "HardwarePowerOff"
        public int WakeSensitivityPixels { get; set; } // default 10
        public bool StartWithWindows { get; set; }
        public bool MinimizeToTrayOnClose { get; set; }
        public bool HotkeyEnabled { get; set; }
        public List<string> SelectedDisplayIds { get; set; }
        public bool TeamsDetectionEnabled { get; set; }
        public List<string> TeamsPreventStatuses { get; set; }
        public bool MediaPlaybackDetectionEnabled { get; set; }

        public AppSettings()
        {
            TimeoutMinutes = 5;
            IdleMode = "KeyboardOnly";
            ScreenAction = "BlackoutOverlay";
            WakeSensitivityPixels = 10;
            StartWithWindows = false;
            MinimizeToTrayOnClose = true;
            HotkeyEnabled = true;
            SelectedDisplayIds = null; // No saved selection yet; default to all connected displays.
            TeamsDetectionEnabled = true;
            TeamsPreventStatuses = new List<string> { "InACall", "Presenting", "Busy" };
            MediaPlaybackDetectionEnabled = true;
        }

        private static string GetConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "OLEDSaver");
            return Path.Combine(folder, "settings.json");
        }

        public static AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            try
            {
                string path = GetConfigPath();
                if (!File.Exists(path))
                {
                    string legacyPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        @"OLEDSaver\settings.json");
                    if (!File.Exists(legacyPath))
                    {
                        return settings;
                    }

                    // Copy once; never write to or keep synchronizing the roaming file.
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.Copy(legacyPath, path, false);
                }

                string text = File.ReadAllText(path, Encoding.UTF8);
                settings.TimeoutMinutes = ParseInt(text, "TimeoutMinutes", 5);
                settings.IdleMode = ParseString(text, "IdleMode", "KeyboardOnly");
                settings.ScreenAction = ParseString(text, "ScreenAction", "BlackoutOverlay");
                settings.WakeSensitivityPixels = ParseInt(text, "WakeSensitivityPixels", 10);
                settings.StartWithWindows = ParseBool(text, "StartWithWindows", false);
                settings.MinimizeToTrayOnClose = ParseBool(text, "MinimizeToTrayOnClose", true);
                settings.HotkeyEnabled = ParseBool(text, "HotkeyEnabled", true);
                if (text.IndexOf("\"SelectedDisplayIds\":") >= 0)
                {
                    settings.SelectedDisplayIds = ParseStringList(text, "SelectedDisplayIds");
                }
                settings.TeamsDetectionEnabled = ParseBool(text, "TeamsDetectionEnabled", true);
                settings.MediaPlaybackDetectionEnabled = ParseBool(text, "MediaPlaybackDetectionEnabled", true);
                List<string> parsedTeamsStatuses = ParseStringList(text, "TeamsPreventStatuses");
                if (text.IndexOf("\"TeamsPreventStatuses\":") >= 0)
                {
                    settings.TeamsPreventStatuses = parsedTeamsStatuses;
                }
            }
            catch
            {
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                string path = GetConfigPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendFormat("  \"TimeoutMinutes\": {0},\n", TimeoutMinutes);
                sb.AppendFormat("  \"IdleMode\": \"{0}\",\n", Escape(IdleMode));
                sb.AppendFormat("  \"ScreenAction\": \"{0}\",\n", Escape(ScreenAction));
                sb.AppendFormat("  \"WakeSensitivityPixels\": {0},\n", WakeSensitivityPixels);
                sb.AppendFormat("  \"StartWithWindows\": {0},\n", StartWithWindows ? "true" : "false");
                sb.AppendFormat("  \"MinimizeToTrayOnClose\": {0},\n", MinimizeToTrayOnClose ? "true" : "false");
                sb.AppendFormat("  \"HotkeyEnabled\": {0},\n", HotkeyEnabled ? "true" : "false");
                sb.AppendFormat("  \"TeamsDetectionEnabled\": {0},\n", TeamsDetectionEnabled ? "true" : "false");
                sb.AppendFormat("  \"MediaPlaybackDetectionEnabled\": {0},\n", MediaPlaybackDetectionEnabled ? "true" : "false");
                sb.AppendLine("  \"TeamsPreventStatuses\": [");
                for (int i = 0; i < TeamsPreventStatuses.Count; i++)
                {
                    sb.AppendFormat("    \"{0}\"{1}\n", Escape(TeamsPreventStatuses[i]), (i < TeamsPreventStatuses.Count - 1) ? "," : "");
                }
                sb.AppendLine("  ],");
                sb.AppendLine("  \"SelectedDisplayIds\": [");
                for (int i = 0; SelectedDisplayIds != null && i < SelectedDisplayIds.Count; i++)
                {
                    sb.AppendFormat("    \"{0}\"{1}\n", Escape(SelectedDisplayIds[i]), (i < SelectedDisplayIds.Count - 1) ? "," : "");
                }
                sb.AppendLine("  ]");
                sb.AppendLine("}");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

                UpdateStartupRegistry(StartWithWindows);
            }
            catch
            {
            }
        }

        private static void UpdateStartupRegistry(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        // Clean up all legacy / variant keys
                        string[] legacyKeys = new string[] { "OLEDSaver", "OLED screen saver" };
                        foreach (string legacyKey in legacyKeys)
                        {
                            try
                            {
                                if (key.GetValue(legacyKey) != null)
                                {
                                    key.DeleteValue(legacyKey);
                                }
                            }
                            catch { }
                        }

                        string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        if (enable)
                        {
                            key.SetValue("OLEDBlackoutSaver", "\"" + appPath + "\" --minimized");
                        }
                        else
                        {
                            if (key.GetValue("OLEDBlackoutSaver") != null)
                            {
                                key.DeleteValue("OLEDBlackoutSaver");
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static int ParseInt(string json, string key, int fallback)
        {
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return fallback;
            int start = idx + pattern.Length;
            int end = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, start);
            if (end < 0) end = json.Length;
            string val = json.Substring(start, end - start).Trim();
            int res;
            if (int.TryParse(val, out res)) return res;
            return fallback;
        }

        private static bool ParseBool(string json, string key, bool fallback)
        {
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return fallback;
            int start = idx + pattern.Length;
            int end = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, start);
            if (end < 0) end = json.Length;
            string val = json.Substring(start, end - start).Trim().ToLowerInvariant();
            if (val == "true") return true;
            if (val == "false") return false;
            return fallback;
        }

        private static string ParseString(string json, string key, string fallback)
        {
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return fallback;
            int start = json.IndexOf('"', idx + pattern.Length);
            if (start < 0) return fallback;
            return ReadQuotedString(json, ref start) ?? fallback;
        }

        private static string ReadQuotedString(string text, ref int position)
        {
            StringBuilder value = new StringBuilder();
            for (position++; position < text.Length; position++)
            {
                char c = text[position];
                if (c == '"')
                {
                    position++;
                    return value.ToString();
                }
                if (c == '\\' && position + 1 < text.Length)
                {
                    c = text[++position];
                }
                value.Append(c);
            }
            return null;
        }

        private static List<string> ParseStringList(string json, string key)
        {
            List<string> list = new List<string>();
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return list;
            int openBracket = json.IndexOf('[', idx);
            if (openBracket < 0) return list;
            int closeBracket = json.IndexOf(']', openBracket);
            if (closeBracket < 0) return list;

            string arrayContent = json.Substring(openBracket + 1, closeBracket - openBracket - 1);
            int cur = 0;
            while (cur < arrayContent.Length)
            {
                int q1 = arrayContent.IndexOf('"', cur);
                if (q1 < 0) break;
                string value = ReadQuotedString(arrayContent, ref q1);
                if (value == null) break;
                list.Add(value);
                cur = q1;
            }
            return list;
        }
    }
}
