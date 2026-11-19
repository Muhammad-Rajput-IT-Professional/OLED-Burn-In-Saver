using System;
using System.Threading;
using System.Windows.Forms;

namespace OLEDSaver
{
    static class Program
    {
        private static Mutex _singleInstanceMutex;

        [STAThread]
        static void Main(string[] args)
        {
            bool startMinimized = false;
            if (args != null && args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                        args[i].Equals("-minimized", StringComparison.OrdinalIgnoreCase))
                    {
                        startMinimized = true;
                    }
                }
            }

            const string appGuid = "OLEDSaver-74A831E0-6D18-4FBE-B526-B817A32F9C01";
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, appGuid, out createdNew);

            if (!createdNew)
            {
                // App is already running. If launched with --minimized (e.g. at Windows startup),
                // exit silently without annoying the user with a popup dialog.
                if (!startMinimized)
                {
                    MessageBox.Show("OLED Blackout Saver is already running in your System Tray.",
                        "OLED Saver", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new MainForm(startMinimized));
        }
    }
}
