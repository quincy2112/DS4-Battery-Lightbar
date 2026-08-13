using System;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;

namespace DS4BatteryMapper
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                // Attach console and file trace listeners so Trace.WriteLine output appears
                Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "ds4.log");
                Trace.Listeners.Add(new TextWriterTraceListener(logPath));
                Trace.AutoFlush = true;
                Console.WriteLine($"Trace listeners attached: console and {logPath}");
            }
            catch (Exception)
            {
                // ignore if unable to attach listeners
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
