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
        public int WakeSensitivityPixels { get; set; } // default 10
        public bool StartWithWindows { get; set; }
        public bool MinimizeToTrayOnClose { get; set; }
        public bool HotkeyEnabled { get; set; }
        public List<string> SelectedDisplayIds { get; set; }

        public AppSettings()
        {
            TimeoutMinutes = 5;
            IdleMode = "KeyboardOnly";
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
                const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKeyPath, true))
                {
                    if (key != null)
                    {
                        string appPath = "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --minimized";
                        if (enable)
                        {
                            key.SetValue("OLEDSaver", appPath);
                        }
                        else
                        {
                            if (key.GetValue("OLEDSaver") != null)
                            {
                                key.DeleteValue("OLEDSaver", false);
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
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static int ParseInt(string json, string key, int defaultValue)
        {
            string match = FindValue(json, key);
            int val;
            if (match != null && int.TryParse(match.Trim(), out val))
            {
                return val;
            }
            return defaultValue;
        }

        private static bool ParseBool(string json, string key, bool defaultValue)
        {
            string match = FindValue(json, key);
            if (match != null)
            {
                string s = match.Trim().ToLowerInvariant();
                if (s == "true") return true;
                if (s == "false") return false;
            }
            return defaultValue;
        }

        private static string ParseString(string json, string key, string defaultValue)
        {
            string match = FindValue(json, key);
            if (match != null)
            {
                string trimmed = match.Trim();
                if (trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Length >= 2)
                {
                    return trimmed.Substring(1, trimmed.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
                }
                return trimmed;
            }
            return defaultValue;
        }

        private static List<string> ParseStringList(string json, string key)
        {
            List<string> list = new List<string>();
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return list;

            int openBracket = json.IndexOf('[', idx);
            if (openBracket == -1) return list;
            int closeBracket = json.IndexOf(']', openBracket);
            if (closeBracket == -1) return list;

            string content = json.Substring(openBracket + 1, closeBracket - openBracket - 1);
            string[] items = content.Split(',');
            foreach (string item in items)
            {
                string t = item.Trim();
                if (t.StartsWith("\"") && t.EndsWith("\"") && t.Length >= 2)
                {
                    list.Add(t.Substring(1, t.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\"));
                }
            }
            return list;
        }

        private static string FindValue(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return null;

            int colon = json.IndexOf(':', idx + pattern.Length);
            if (colon == -1) return null;

            int start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;

            int end = start;
            if (start < json.Length && json[start] == '\"')
            {
                end = start + 1;
                while (end < json.Length)
                {
                    if (json[end] == '\"' && json[end - 1] != '\\')
                    {
                        end++;
                        break;
                    }
                    end++;
                }
            }
            else
            {
                while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\r' && json[end] != '\n')
                {
                    end++;
                }
            }

            return json.Substring(start, end - start).Trim();
        }
    }
}
