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
            // Attach a console trace listener so logs appear when running from PowerShell/Command Prompt
            try
            {
                Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
                Trace.AutoFlush = true;
                Console.WriteLine("Console trace listener attached: Trace.WriteLine will appear in the console.");
            }
            catch (Exception)
            {
                // Ignore if attaching a listener fails
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
