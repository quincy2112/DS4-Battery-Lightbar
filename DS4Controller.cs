using System;
using HidLibrary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;

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

        // P/Invoke fallbacks for native HID APIs
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetFeature(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetOutputReport(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

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

                var writeFeatureMethod = _device.GetType().GetMethod("WriteFeatureData");
                var writeFeatureAvailable = writeFeatureMethod != null;

                // Candidates for permutations
                var rumblePairs = new (byte l, byte r)[] { (0x00, 0x00), (0x10, 0x10), (0x40, 0x40), (0x7F, 0x7F) };
                var rumbleOffsetCandidates = new[] { new[] { 3, 4 }, new[] { 4, 5 }, new[] { 6, 7 } };
                var rgbOffsetCandidates = new[] { new[] { 8, 9, 10 }, new[] { 6, 7, 8 }, new[] { 9, 10, 11 }, new[] { 10, 11, 12 } };
                var reportIdCandidates = new byte[] { 0x11, 0x05 };
                var headerCandidates = new byte[] { 0xC0, 0x02, 0x00 };
                var outputReportLens = new[] { 78, 64, (_device.Capabilities?.OutputReportByteLength ?? 32) };

                // 1) Try 65-byte feature report permutations (WriteFeatureData + native HidD_SetFeature)
                foreach (var rumble in rumblePairs)
                {
                    foreach (var rumbleOff in rumbleOffsetCandidates)
                    {
                        foreach (var rgbOff in rgbOffsetCandidates)
                        {
                            var report65 = new byte[65];
                            report65[0] = 0x11; // typical report id
                            report65[1] = 0xC0; // typical header

                            if (rumbleOff[0] < report65.Length) report65[rumbleOff[0]] = rumble.l;
                            if (rumbleOff[1] < report65.Length) report65[rumbleOff[1]] = rumble.r;

                            if (rgbOff[0] < report65.Length) report65[rgbOff[0]] = red;
                            if (rgbOff[1] < report65.Length) report65[rgbOff[1]] = green;
                            if (rgbOff[2] < report65.Length) report65[rgbOff[2]] = blue;

                            // Managed WriteFeatureData
                            if (writeFeatureAvailable)
                            {
                                try
                                {
                                    var ret = writeFeatureMethod.Invoke(_device, new object[] { report65 });
                                    if (ret is bool b && b)
                                    {
                                        Trace.WriteLine($"SetLightbar success via WriteFeatureData (65): rumble={rumble.l:X2},{rumble.r:X2} rumbleOff={rumbleOff[0]},{rumbleOff[1]} rgbOff={rgbOff[0]},{rgbOff[1]},{rgbOff[2]}");
                                        return;
                                    }
                                    else
                                    {
                                        Trace.WriteLine($"SetLightbar attempt via WriteFeatureData (65): returned={(ret == null ? "null" : ret.ToString())}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Trace.WriteLine($"WriteFeatureData invocation error: {ex.Message}");
                                }
                            }

                            // Native HidD_SetFeature fallback
                            try
                            {
                                var devicePath = GetDevicePath();
                                if (!string.IsNullOrEmpty(devicePath))
                                {
                                    var h = NativeMethods.CreateFile(devicePath, NativeMethods.GENERIC_WRITE | NativeMethods.GENERIC_READ,
                                        NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);

                                    if (h != NativeMethods.INVALID_HANDLE_VALUE)
                                    {
                                        try
                                        {
                                            var nativeResult = HidD_SetFeature(h, report65, report65.Length);
                                            Trace.WriteLine($"SetLightbar HidD_SetFeature(native) attempt (65): rumble={rumble.l:X2},{rumble.r:X2} rgbOff={rgbOff[0]},{rgbOff[1]},{rgbOff[2]} returned: {nativeResult}");
                                            NativeMethods.CloseHandle(h);
                                            if (nativeResult) return;
                                        }
                                        catch (Exception nex)
                                        {
                                            Trace.WriteLine($"HidD_SetFeature native error: {nex.Message}");
                                        }
                                    }
                                    else
                                    {
                                        Trace.WriteLine($"CreateFile for HidD_SetFeature failed: {devicePath}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"HidD_SetFeature fallback exception: {ex.Message}");
                            }
                        }
                    }
                }

                // 2) Native HidD_SetOutputReport permutations (try with and without leading report id)
                foreach (var len in outputReportLens.Distinct())
                {
                    int realLen = Math.Max(32, len);
                    foreach (var rid in reportIdCandidates)
                    {
                        foreach (var header in headerCandidates)
                        {
                            foreach (var rumble in rumblePairs)
                            {
                                foreach (var rgbOff in rgbOffsetCandidates)
                                {
                                    var report = new byte[realLen];
                                    report[0] = rid;
                                    if (report.Length > 1) report[1] = header;

                                    // place rumble at 3/4 if possible
                                    if (report.Length > 5)
                                    {
                                        report[3] = rumble.l;
                                        report[4] = rumble.r;
                                    }

                                    if (rgbOff[0] < report.Length) report[rgbOff[0]] = red;
                                    if (rgbOff[1] < report.Length) report[rgbOff[1]] = green;
                                    if (rgbOff[2] < report.Length) report[rgbOff[2]] = blue;

                                    try
                                    {
                                        var devicePath = GetDevicePath();
                                        if (!string.IsNullOrEmpty(devicePath))
                                        {
                                            var h = NativeMethods.CreateFile(devicePath, NativeMethods.GENERIC_WRITE | NativeMethods.GENERIC_READ,
                                                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);

                                            if (h != NativeMethods.INVALID_HANDLE_VALUE)
                                            {
                                                try
                                                {
                                                    // First try the full buffer
                                                    var outRes = HidD_SetOutputReport(h, report, report.Length);
                                                    Trace.WriteLine($"SetLightbar HidD_SetOutputReport native attempt: len={report.Length}, rid=0x{rid:X2}, header=0x{header:X2}, rgbOff={rgbOff[0]},{rgbOff[1]},{rgbOff[2]}, rumble={rumble.l:X2},{rumble.r:X2} -> {outRes}");
                                                    if (outRes)
                                                    {
                                                        NativeMethods.CloseHandle(h);
                                                        return;
                                                    }

                                                    // Also try omitting leading report id (some stacks expect that)
                                                    if (report.Length > 1)
                                                    {
                                                        var shorter = report.Skip(1).ToArray();
                                                        var outRes2 = HidD_SetOutputReport(h, shorter, shorter.Length);
                                                        Trace.WriteLine($"SetLightbar HidD_SetOutputReport native attempt (no-report-id): len={shorter.Length}, rgbOffAdjusted={rgbOff[0]-1},{rgbOff[1]-1},{rgbOff[2]-1} -> {outRes2}");
                                                        if (outRes2)
                                                        {
                                                            NativeMethods.CloseHandle(h);
                                                            return;
                                                        }
                                                    }

                                                    NativeMethods.CloseHandle(h);
                                                }
                                                catch (Exception nex)
                                                {
                                                    Trace.WriteLine($"HidD_SetOutputReport native call failed: {nex.Message}");
                                                }
                                            }
                                            else
                                            {
                                                Trace.WriteLine($"CreateFile failed for HidD_SetOutputReport: {devicePath}");
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Trace.WriteLine($"Native output-report permutation exception: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }

                // 3) Fallback to managed Write attempts (existing permutations)
                int[] reportLens = new[] { 78, _device.Capabilities?.OutputReportByteLength ?? 32, 32 };
                byte[] reportIdCandidates2 = new[] { (byte)0x11, (byte)0x05 };
                int[][] offsetCandidates = new[]
                {
                    new[] { 6, 7, 8 },
                    new[] { 8, 9, 10 },
                    new[] { 5, 6, 7 },
                    new[] { 4, 6, 7 }
                };
                byte[] headerCandidates2 = new[] { (byte)0xC0, (byte)0x02, (byte)0x00 };

                bool wrote = false;
                foreach (var len2 in reportLens)
                {
                    int realLen = len2 > 0 ? len2 : 32;
                    var report = new byte[realLen];

                    foreach (var rid2 in reportIdCandidates2)
                    {
                        foreach (var header2 in headerCandidates2)
                        {
                            foreach (var off in offsetCandidates)
                            {
                                Array.Clear(report, 0, report.Length);

                                if (realLen > 0) report[0] = rid2;
                                if (report.Length > 1) report[1] = header2;

                                if (off[0] < report.Length) report[off[0]] = red;
                                if (off[1] < report.Length) report[off[1]] = green;
                                if (off[2] < report.Length) report[off[2]] = blue;

                                try
                                {
                                    var ok = _device.Write(report);
                                    Trace.WriteLine($"SetLightbar attempt: outLen={realLen}, rid=0x{rid2:X2}, header=0x{header2:X2}, offs={off[0]},{off[1]},{off[2]} -> Write returned {ok}");
                                    if (ok) { wrote = true; break; }
                                }
                                catch (Exception wex)
                                {
                                    Trace.WriteLine($"SetLightbar Write exception (outLen={realLen}, rid=0x{rid2:X2}): {wex}");
                                }

                                try
                                {
                                    var mi = _device.GetType().GetMethod("WriteFeatureData") ?? _device.GetType().GetMethod("WriteFeature");
                                    if (mi != null)
                                    {
                                        var result = mi.Invoke(_device, new object[] { report });
                                        Trace.WriteLine($"SetLightbar attempt via {mi.Name}: outLen={realLen}, rid=0x{rid2:X2}, offs={off[0]},{off[1]},{off[2]} -> returned={result ?? "null"}");
                                        wrote = true; // assume attempted
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
                    Trace.WriteLine("SetLightbar: none of the permutations succeeded; device may require a driver-specific or firmware-specific report format.");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SetLightbar: unexpected error: {ex}");
            }
        }

        private string? GetDevicePath()
        {
            try
            {
                var pathProp = _device.GetType().GetProperty("DevicePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pathProp != null)
                {
                    return pathProp.GetValue(_device) as string;
                }
            }
            catch { }
            return null;
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
