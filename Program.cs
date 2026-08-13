using System;
using System.Windows.Forms;
using System.Diagnostics;

namespace DS4BatteryMapper
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Ensure Debug.WriteLine goes to the console when running from PowerShell/Command Prompt
            try
            {
                Debug.Listeners.Add(new TextWriterTraceListener(Console.Out));
                Debug.AutoFlush = true;
                Console.WriteLine("Debug listener attached: Debug.WriteLine will also appear in console output.");
            }
            catch (Exception)
            {
                // If attaching a console listener isn't possible, ignore and continue - Debug output will still go to attached debuggers
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
