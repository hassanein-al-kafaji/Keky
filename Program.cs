using System;
using System.Windows.Forms;

namespace KekyNotepad;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // These calls must happen before the first window is created.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new MainForm());
    }
}
