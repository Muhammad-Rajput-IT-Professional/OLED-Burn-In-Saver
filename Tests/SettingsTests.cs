using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using OLEDSaver;

// These test-only shims isolate settings files and prevent changes to Windows startup.
namespace OLEDSaver
{
    internal static class Environment
    {
        internal static string Root;
        internal static int TickCount { get { return System.Environment.TickCount; } }
        internal static class SpecialFolder
        {
            internal const System.Environment.SpecialFolder LocalApplicationData = System.Environment.SpecialFolder.LocalApplicationData;
            internal const System.Environment.SpecialFolder ApplicationData = System.Environment.SpecialFolder.ApplicationData;
        }

        internal static string GetFolderPath(System.Environment.SpecialFolder folder)
        {
            return Path.Combine(Root, folder.ToString());
        }
    }

    internal static class Registry
    {
        internal static readonly TestRegistry CurrentUser = new TestRegistry();
    }

    internal class TestRegistry
    {
        internal Microsoft.Win32.RegistryKey OpenSubKey(string name, bool writable)
        {
            return null;
        }
    }
}

internal static class SettingsTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static string Config(System.Environment.SpecialFolder folder)
    {
        return Path.Combine(OLEDSaver.Environment.GetFolderPath(folder), @"OLEDSaver\settings.json");
    }

    [STAThread]
    private static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "OLEDSaver-Tests-" + Guid.NewGuid().ToString("N"));
        OLEDSaver.Environment.Root = root;
        try
        {
            string local = Config(System.Environment.SpecialFolder.LocalApplicationData);
            string roaming = Config(System.Environment.SpecialFolder.ApplicationData);
            AppSettings defaults = AppSettings.Load();
            Assert(defaults.TimeoutMinutes == 5 && defaults.SelectedDisplayIds == null, "First-run defaults changed.");
            Assert(!Directory.Exists(root), "Loading defaults should not create files.");

            Directory.CreateDirectory(Path.GetDirectoryName(roaming));
            string legacy = "{\"TimeoutMinutes\": 17,\"TeamsPreventStatuses\": [],\"HotkeyEnabled\": false}";
            File.WriteAllText(roaming, legacy);
            AppSettings migrated = AppSettings.Load();
            Assert(migrated.TimeoutMinutes == 17 && !migrated.HotkeyEnabled, "Legacy settings not loaded.");
            Assert(migrated.TeamsPreventStatuses.Count == 0, "Unchecked Teams options were reset.");
            Assert(File.ReadAllText(local) == legacy, "Migration did not preserve the file.");
            Assert(File.ReadAllText(roaming) == legacy, "Migration changed the legacy file.");

            migrated.IdleMode = "KeyboardAndMouse";
            migrated.ScreenAction = "HardwarePowerOff";
            migrated.WakeSensitivityPixels = 25;
            migrated.StartWithWindows = true;
            migrated.MinimizeToTrayOnClose = false;
            migrated.TeamsDetectionEnabled = false;
            migrated.MediaPlaybackDetectionEnabled = false;
            migrated.SelectedDisplayIds = new List<string> { @"\\.\DISPLAY2" };
            migrated.Save();
            AppSettings saved = AppSettings.Load();
            Assert(saved.TimeoutMinutes == 17 && saved.IdleMode == migrated.IdleMode &&
                saved.ScreenAction == migrated.ScreenAction && saved.WakeSensitivityPixels == 25,
                "Numeric or mode settings did not persist.");
            Assert(saved.StartWithWindows && !saved.MinimizeToTrayOnClose && !saved.HotkeyEnabled &&
                !saved.TeamsDetectionEnabled && !saved.MediaPlaybackDetectionEnabled,
                "Checkbox settings did not persist.");
            Assert(saved.SelectedDisplayIds.Count == 1 && saved.SelectedDisplayIds[0] == @"\\.\DISPLAY2",
                "Display IDs did not round-trip.");
            Assert(saved.TeamsPreventStatuses.Count == 0, "Empty Teams selection did not round-trip.");
            Assert(File.ReadAllText(roaming) == legacy, "Saving wrote to the roaming file.");

            File.WriteAllText(roaming, "{\"TimeoutMinutes\": 99}");
            Assert(AppSettings.Load().TimeoutMinutes == 17, "Legacy file overrode local settings.");
            string firstPcSettings = File.ReadAllText(local);
            OLEDSaver.Environment.Root = Path.Combine(root, "SecondPC");
            AppSettings secondPc = AppSettings.Load();
            Assert(secondPc.TimeoutMinutes == 5, "Second PC inherited the first PC's settings.");
            secondPc.TimeoutMinutes = 42;
            secondPc.Save();
            Assert(File.ReadAllText(local) == firstPcSettings, "Second PC changed the first PC's settings.");
            OLEDSaver.Environment.Root = root;

            saved.SelectedDisplayIds.Clear();
            saved.Save();
            Assert(AppSettings.Load().SelectedDisplayIds.Count == 0, "Empty display selection did not persist.");
            foreach (DisplayInfo display in DisplayHelper.GetConnectedDisplays(new List<string>()))
            {
                Assert(!display.IsSelected, "Unchecked display was reselected.");
            }

            TestTeamsCheckboxRestoration(saved);
            Console.WriteLine("PASS: local storage, migration, PC isolation, preference round-trips, checkbox restoration.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void TestTeamsCheckboxRestoration(AppSettings settings)
    {
        MainForm form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainForm).GetField("_settings", flags).SetValue(form, settings);
        MethodInfo changed = typeof(MainForm).GetMethod("TeamsStatusCheckbox_CheckedChanged", flags);
        string[] fields = { "_chkTeamsInCall", "_chkTeamsPresenting", "_chkTeamsBusy", "_chkTeamsDnd", "_chkTeamsAway" };
        List<CheckBox> controls = new List<CheckBox>();
        try
        {
            foreach (string field in fields)
            {
                CheckBox checkbox = new CheckBox();
                checkbox.CheckedChanged += delegate(object sender, EventArgs e) { changed.Invoke(form, new object[] { sender, e }); };
                typeof(MainForm).GetField(field, flags).SetValue(form, checkbox);
                controls.Add(checkbox);
            }
            settings.TeamsPreventStatuses = new List<string> { "InACall", "Presenting", "Busy", "Away" };
            typeof(MainForm).GetMethod("ApplyTeamsSettingsToUI", flags).Invoke(form, null);
            Assert(settings.TeamsPreventStatuses.Count == 4, "Restoring checkboxes overwrote saved choices.");
            Assert(controls[0].Checked && controls[1].Checked && controls[2].Checked &&
                !controls[3].Checked && controls[4].Checked, "Checkboxes did not match saved choices.");
        }
        finally
        {
            foreach (CheckBox control in controls) control.Dispose();
        }
    }
}
