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

        public AppSettings()
        {
            TimeoutMinutes = 5;
            IdleMode = "KeyboardOnly";
            ScreenAction = "BlackoutOverlay";
            WakeSensitivityPixels = 10;
            StartWithWindows = false;
            MinimizeToTrayOnClose = true;
            HotkeyEnabled = true;
            SelectedDisplayIds = new List<string>();
        }

        private static string GetConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "OLEDSaver");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            return Path.Combine(folder, "settings.json");
        }

        public static AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            string path = GetConfigPath();
            if (!File.Exists(path))
            {
                return settings;
            }

            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                settings.TimeoutMinutes = ParseInt(text, "TimeoutMinutes", 5);
                settings.IdleMode = ParseString(text, "IdleMode", "KeyboardOnly");
                settings.ScreenAction = ParseString(text, "ScreenAction", "BlackoutOverlay");
                settings.WakeSensitivityPixels = ParseInt(text, "WakeSensitivityPixels", 10);
                settings.StartWithWindows = ParseBool(text, "StartWithWindows", false);
                settings.MinimizeToTrayOnClose = ParseBool(text, "MinimizeToTrayOnClose", true);
                settings.HotkeyEnabled = ParseBool(text, "HotkeyEnabled", true);
                settings.SelectedDisplayIds = ParseStringList(text, "SelectedDisplayIds");
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
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendFormat("  \"TimeoutMinutes\": {0},\n", TimeoutMinutes);
                sb.AppendFormat("  \"IdleMode\": \"{0}\",\n", Escape(IdleMode));
                sb.AppendFormat("  \"ScreenAction\": \"{0}\",\n", Escape(ScreenAction));
                sb.AppendFormat("  \"WakeSensitivityPixels\": {0},\n", WakeSensitivityPixels);
                sb.AppendFormat("  \"StartWithWindows\": {0},\n", StartWithWindows ? "true" : "false");
                sb.AppendFormat("  \"MinimizeToTrayOnClose\": {0},\n", MinimizeToTrayOnClose ? "true" : "false");
                sb.AppendFormat("  \"HotkeyEnabled\": {0},\n", HotkeyEnabled ? "true" : "false");
                sb.AppendLine("  \"SelectedDisplayIds\": [");
                for (int i = 0; i < SelectedDisplayIds.Count; i++)
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
            int end = json.IndexOf('"', start + 1);
            if (end < 0) return fallback;
            return json.Substring(start + 1, end - start - 1);
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
                int q2 = arrayContent.IndexOf('"', q1 + 1);
                if (q2 < 0) break;
                list.Add(arrayContent.Substring(q1 + 1, q2 - q1 - 1));
                cur = q2 + 1;
            }
            return list;
        }
    }
}
