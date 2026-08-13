using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DS4BatteryLightbar
{
    /// <summary>
    /// Helper to retrieve a controller serial without depending on a single HID library API.
    /// This uses reflection to try common property/method names (SerialNumber, GetSerialNumber, DevicePath, etc.)
    /// so it compiles even if the HidDevice type in the user's project doesn't expose SerialNumber directly.
    /// </summary>
    public static class DS4ControllerManager
    {
        /// <summary>
        /// Attempts to obtain a serial/unique identifier from a HID device object using reflection.
        /// Returns null if none found.
        /// </summary>
        public static string GetSerialNumber(object device)
        {
            if (device == null) return null;
            Type t = device.GetType();

            // Try common property names first.
            string[] propNames = new[] { "SerialNumber", "Serial", "DevicePath", "Path", "DeviceId" };
            foreach (var name in propNames)
            {
                var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null)
                {
                    try
                    {
                        var val = prop.GetValue(device);
                        if (val != null) return val.ToString();
                    }
                    catch { /* ignore and continue trying other options */ }
                }
            }

            // Try common method names that return serials.
            string[] methodNames = new[] { "GetSerialNumber", "GetSerialNumberString", "GetSerial", "ReadSerialNumber" };
            foreach (var mname in methodNames)
            {
                var method = t.GetMethod(mname, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase, Type.DefaultBinder, Type.EmptyTypes, null);
                if (method != null)
                {
                    try
                    {
                        var result = method.Invoke(device, null);
                        if (result is Task task)
                        {
                            // Wait for the async call to complete and try to read Result
                            task.Wait();
                            var resProp = task.GetType().GetProperty("Result");
                            if (resProp != null)
                            {
                                var res = resProp.GetValue(task);
                                if (res != null) return res.ToString();
                            }
                        }
                        else if (result != null)
                        {
                            return result.ToString();
                        }
                    }
                    catch { /* ignore and try next */ }
                }
            }

            // Fallback: try to extract an identifier-like segment from a DevicePath/ToString using regex.
            string path = null;
            var dp = t.GetProperty("DevicePath", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                     ?? t.GetProperty("Path", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                     ?? t.GetProperty("DeviceId", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (dp != null)
            {
                try { var v = dp.GetValue(device); path = v?.ToString(); } catch { }
            }

            if (string.IsNullOrEmpty(path))
            {
                // Last resort: use ToString()
                try { path = device.ToString(); } catch { path = null; }
            }

            if (!string.IsNullOrEmpty(path))
            {
                var m = Regex.Match(path, @"([^\\&:]+)$");
                if (m.Success) return m.Groups[1].Value;
            }

            return null;
        }
    }
}
