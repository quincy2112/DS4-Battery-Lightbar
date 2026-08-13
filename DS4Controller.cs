using System;
using HidLibrary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DS4BatteryMapper
{
    public class DS4Controller : IDisposable
    {
        private HidDevice _device;
        private byte[] _lastInputReport;

        public string DeviceName => _device?.Description ?? "Unknown";
        // -1 means unknown / not yet read
        public int BatteryPercentage { get; private set; }
        public DateTime? LastSeen { get; private set; }

        public DS4Controller(HidDevice device)
        {
            _device = device;
            _lastInputReport = new byte[64];
            BatteryPercentage = -1; // default until first successful read
            LastSeen = null;
            // IMPORTANT: Do NOT perform blocking I/O (UpdateBatteryStatus) in the constructor.
            // Reads/writes are performed during the scheduled poll with timeouts.
        }

        public void UpdateBatteryStatus()
        {
            try
            {
                if (!_device.IsConnected)
                    return;

                if (!_device.IsOpen)
                    _device.OpenDevice();

                var data = _device.Read();
                if (data.Status == HidDeviceData.ReadStatus.Success && data.Data.Length > 0)
                {
                    _lastInputReport = data.Data;
                    // Battery level is in byte 12 (0-indexed)
                    // Range is 0-255, map to 0-100
                    if (_lastInputReport.Length > 12)
                    {
                        BatteryPercentage = (int)(_lastInputReport[12] / 2.55);
                        BatteryPercentage = Math.Min(100, Math.Max(0, BatteryPercentage));
                        LastSeen = DateTime.Now;
                        Trace.WriteLine($"[DS4Controller] {DeviceName} battery read: {BatteryPercentage}%");
                        Trace.WriteLine($"[DS4Controller] Raw input ({_lastInputReport.Length}): {BitConverter.ToString(_lastInputReport)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error reading DS4 data: {ex.Message}");
            }
        }

        // P/Invoke fallback for HidD_SetFeature in case managed WriteFeatureData doesn't work
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetFeature(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        public void SetLightbar(byte red, byte green, byte blue)
        {
            try
            {
                Trace.WriteLine($"SetLightbar called for {DeviceName} with RGB=({red},{green},{blue})");

                if (!_device.IsConnected)
                {
                    Trace.WriteLine("SetLightbar: device not connected");
                    return;
                }

                if (!_device.IsOpen)
                    _device.OpenDevice();

                // Log capabilities for debugging
                try
                {
                    var caps = _device.Capabilities;
                    Trace.WriteLine($"Device capabilities: OutputReportByteLength={caps?.OutputReportByteLength}, FeatureReportByteLength={caps?.FeatureReportByteLength}");
                }
                catch (Exception) { /* ignore */ }

                // First attempt: explicit 65-byte feature report (report id 0x11, header 0xC0), RGB at offsets 8/9/10
                try
                {
                    var report65 = new byte[65];
                    report65[0] = 0x11; // report id
                    report65[1] = 0xC0; // header
                    // Optional rumble bytes can be left zero for now
                    if (8 < report65.Length) report65[8] = red;
                    if (9 < report65.Length) report65[9] = green;
                    if (10 < report65.Length) report65[10] = blue;

                    bool featureResult = false;

                    // Prefer HidLibrary's WriteFeatureData if available
                    var writeFeatureMethod = _device.GetType().GetMethod("WriteFeatureData");
                    if (writeFeatureMethod != null)
                    {
                        try
                        {
                            var ret = writeFeatureMethod.Invoke(_device, new object[] { report65 });
                            // Many implementations return bool
                            if (ret is bool b)
                            {
                                featureResult = b;
                            }
                            else if (ret != null)
                            {
                                // treat non-null as success (best-effort)
                                featureResult = true;
                            }

                            Trace.WriteLine($"SetLightbar attempt via WriteFeatureData (65): returned={ret ?? "null"}");
                        }
                        catch (TargetInvocationException tie)
                        {
                            Trace.WriteLine($"WriteFeatureData invocation threw: {tie.InnerException?.Message ?? tie.Message}");
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"WriteFeatureData reflection error: {ex.Message}");
                        }
                    }
                    else
                    {
                        Trace.WriteLine("WriteFeatureData method not found on HidDevice - will try P/Invoke fallback");
                    }

                    if (featureResult)
                    {
                        Trace.WriteLine("SetLightbar: 65-byte WriteFeatureData reported success");
                        return;
                    }

                    // If the managed feature write didn't report success, try native HidD_SetFeature as fallback
                    try
                    {
                        // Get underlying handle via reflection - HidLibrary exposes a Handle property on HidDevice
                        var handleProp = _device.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        IntPtr handle = IntPtr.Zero;
                        if (handleProp != null)
                        {
                            try
                            {
                                var val = handleProp.GetValue(_device);
                                if (val is IntPtr ip) handle = ip;
                            }
                            catch { }
                        }

                        if (handle == IntPtr.Zero)
                        {
                            // Try field backing name
                            var field = _device.GetType().GetField("_devicePath", BindingFlags.Instance | BindingFlags.NonPublic);
                            // Not ideal - we need a valid handle; HidLibrary doesn't publicly expose it in all versions.
                        }

                        // If we don't have a handle, still attempt to call HidD_SetFeature by opening a native handle using CreateFile on the device path.
                        // Get DevicePath via property
                        string devicePath = null;
                        var pathProp = _device.GetType().GetProperty("DevicePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (pathProp != null)
                        {
                            devicePath = pathProp.GetValue(_device) as string;
                        }

                        if (!string.IsNullOrEmpty(devicePath))
                        {
                            // Open native handle
                            var h = NativeMethods.CreateFile(devicePath, NativeMethods.GENERIC_WRITE | NativeMethods.GENERIC_READ,
                                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);

                            if (h != NativeMethods.INVALID_HANDLE_VALUE)
                            {
                                try
                                {
                                    var nativeResult = HidD_SetFeature(h, report65, report65.Length);
                                    Trace.WriteLine($"SetLightbar HidD_SetFeature(native) returned: {nativeResult}");
                                    NativeMethods.CloseHandle(h);
                                    if (nativeResult)
                                        return;
                                }
                                catch (Exception nex)
                                {
                                    Trace.WriteLine($"HidD_SetFeature native call failed: {nex.Message}");
                                }
                            }
                            else
                            {
                                Trace.WriteLine($"CreateFile failed for path {devicePath}");
                            }
                        }
                        else
                        {
                            Trace.WriteLine("Could not obtain device path for native HidD_SetFeature fallback");
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Native HidD_SetFeature fallback error: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"65-byte feature attempt failed: {ex}");
                }

                // If the explicit 65-byte feature attempt didn't succeed, fall back to previous multi-format attempts
                // Try Bluetooth-specific 78-byte output report first, then fallbacks
                int[] reportLens = new[] { 78, _device.Capabilities?.OutputReportByteLength ?? 32, 32 };
                byte[] reportIdCandidates = new[] { (byte)0x11, (byte)0x05 };

                // Offsets to try for RGB within the report
                int[][] offsetCandidates = new[]
                {
                    new[] { 6, 7, 8 },   // common USB
                    new[] { 8, 9, 10 },  // sometimes shifted for BT
                    new[] { 5, 6, 7 },
                    new[] { 4, 6, 7 }
                };

                byte[] headerCandidates = new[] { (byte)0xC0, (byte)0x02, (byte)0x00 };

                bool wrote = false;

                foreach (var len in reportLens)
                {
                    int realLen = len > 0 ? len : 32;
                    var report = new byte[realLen];

                    foreach (var rid in reportIdCandidates)
                    {
                        foreach (var header in headerCandidates)
                        {
                            foreach (var off in offsetCandidates)
                            {
                                Array.Clear(report, 0, report.Length);

                                // Some transports want the report id at index 0
                                if (realLen > 0) report[0] = rid;

                                // Some DS4 BT reports use a header/flags byte in index 1 - try a few values
                                if (report.Length > 1) report[1] = header;

                                if (off[0] < report.Length) report[off[0]] = red;
                                if (off[1] < report.Length) report[off[1]] = green;
                                if (off[2] < report.Length) report[off[2]] = blue;

                                try
                                {
                                    // First try a normal Write
                                    var ok = _device.Write(report);
                                    Trace.WriteLine($"SetLightbar attempt: outLen={realLen}, rid=0x{rid:X2}, header=0x{header:X2}, offs={off[0]},{off[1]},{off[2]} -> Write returned {ok}");
                                    if (ok) { wrote = true; break; }
                                }
                                catch (Exception wex)
                                {
                                    Trace.WriteLine($"SetLightbar Write exception (outLen={realLen}, rid=0x{rid:X2}): {wex}");
                                }

                                // If Write didn't work or isn't appropriate for this transport, try feature/write-feature methods if available
                                try
                                {
                                    var mi = _device.GetType().GetMethod("WriteFeatureData") ?? _device.GetType().GetMethod("WriteFeature");
                                    if (mi != null)
                                    {
                                        var result = mi.Invoke(_device, new object[] { report });
                                        Trace.WriteLine($"SetLightbar attempt via {mi.Name}: outLen={realLen}, rid=0x{rid:X2}, offs={off[0]},{off[1]},{off[2]} -> returned={result ?? "null"}");
                                        wrote = true; // we attempted - can't always determine success from reflection result shape, assume attempted
                                        break;
                                    }
                                }
                                catch (Exception fim)
                                {
                                    Trace.WriteLine($"SetLightbar feature-write exception: {fim}");
                                }
                            }

                            if (wrote) break;
                        }

                        if (wrote) break;
                    }

                    if (wrote) break;
                }

                if (!wrote)
                {
                    Trace.WriteLine("SetLightbar: none of the standard attempts reported success; device may require a different report format or transport-specific handling.");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SetLightbar: unexpected error: {ex}");
            }
        }

        public void Dispose()
        {
            try
            {
                _device?.CloseDevice();
            }
            catch { }
        }
    }

    internal static class NativeMethods
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
