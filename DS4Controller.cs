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

                // We'll try an ordered set of permutations geared toward Bluetooth DS4 expectations.
                // 1) 65-byte feature reports with small rumble values and RGB at offsets (8,9,10) or (6,7,8)
                // 2) Native HidD_SetFeature fallback for the same 65-byte payloads
                // 3) 78-byte output reports with the same rumble+RGB permutations and different report ids/headers

                // Candidate rumble byte pairs to include (left, right). 0x00,0x00 is a valid attempt but some firmwares ignore LED-only if rumble is 0.
                var rumblePairs = new (byte l, byte r)[] { (0x00, 0x00), (0x10, 0x10), (0x40, 0x40) };
                // Candidate rumble offsets inside the 65-byte report
                var rumbleOffsetCandidates = new[] { new[] { 3, 4 }, new[] { 4, 5 }, new[] { 6, 7 } };
                // Candidate RGB offset positions
                var rgbOffsetCandidates = new[] { new[] { 8, 9, 10 }, new[] { 6, 7, 8 } };

                var writeFeatureMethod = _device.GetType().GetMethod("WriteFeatureData");

                // Try 65-byte feature report permutations
                foreach (var rumble in rumblePairs)
                {
                    foreach (var rumbleOff in rumbleOffsetCandidates)
                    {
                        foreach (var rgbOff in rgbOffsetCandidates)
                        {
                            var report65 = new byte[65];
                            report65[0] = 0x11; // common DS4 report id
                            report65[1] = 0xC0; // header

                            // set rumble bytes if within bounds
                            if (rumbleOff[0] < report65.Length) report65[rumbleOff[0]] = rumble.l;
                            if (rumbleOff[1] < report65.Length) report65[rumbleOff[1]] = rumble.r;

                            if (rgbOff[0] < report65.Length) report65[rgbOff[0]] = red;
                            if (rgbOff[1] < report65.Length) report65[rgbOff[1]] = green;
                            if (rgbOff[2] < report65.Length) report65[rgbOff[2]] = blue;

                            bool featureResult = false;

                            if (writeFeatureMethod != null)
                            {
                                try
                                {
                                    var ret = writeFeatureMethod.Invoke(_device, new object[] { report65 });
                                    // Treat only an explicit boolean 'true' as success. Some HidLibrary implementations
                                    // return non-bool values or boxed bool false; we must not treat non-bool/false as success.
                                    if (ret is bool b)
                                    {
                                        featureResult = b;
                                        Trace.WriteLine($"SetLightbar attempt via WriteFeatureData (65): rumble={rumble.l:X2},{rumble.r:X2} rumbleOff={rumbleOff[0]},{rumbleOff[1]} rgbOff={rgbOff[0]},{rgbOff[1]},{rgbOff[2]} -> returned=(bool){b}");
                                    }
                                    else
                                    {
                                        Trace.WriteLine($"SetLightbar attempt via WriteFeatureData (65): rumble={rumble.l:X2},{rumble.r:X2} rumbleOff={rumbleOff[0]},{rumbleOff[1]} rgbOff={rgbOff[0]},{rgbOff[1]},{rgbOff[2]} -> returned=(non-bool){ret ?? "null"} (treated as failure)");
                                        featureResult = false;
                                    }
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

                            if (featureResult)
                            {
                                Trace.WriteLine("SetLightbar: 65-byte WriteFeatureData reported success");
                                return;
                            }

                            // Try native HidD_SetFeature fallback by opening native handle using CreateFile
                            try
                            {
                                string devicePath = null;
                                var pathProp = _device.GetType().GetProperty("DevicePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (pathProp != null) devicePath = pathProp.GetValue(_device) as string;

                                if (!string.IsNullOrEmpty(devicePath))
                                {
                                    var h = NativeMethods.CreateFile(devicePath, NativeMethods.GENERIC_WRITE | NativeMethods.GENERIC_READ,
                                        NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);

                                    if (h != NativeMethods.INVALID_HANDLE_VALUE)
                                    {
                                        try
                                        {
                                            var nativeResult = HidD_SetFeature(h, report65, report65.Length);
                                            Trace.WriteLine($"SetLightbar HidD_SetFeature(native) attempt: rumble={rumble.l:X2},{rumble.r:X2} rgbOff={rgbOff[0]},{rgbOff[1]},{rgbOff[2]} returned: {nativeResult}");
                                            NativeMethods.CloseHandle(h);
                                            if (nativeResult) return;
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
                    }
                }

                // If the 65-byte feature permutations didn't work, fall back to the 78-byte and other output attempts (include rumble here too)
                int[] reportLens = new[] { 78, _device.Capabilities?.OutputReportByteLength ?? 32, 32 };
                byte[] reportIdCandidates = new[] { (byte)0x11, (byte)0x05 };
                int[][] offsetCandidates = new[]
                {
                    new[] { 6, 7, 8 },
                    new[] { 8, 9, 10 },
                    new[] { 5, 6, 7 },
                    new[] { 4, 6, 7 }
                };
                byte[] headerCandidates = new[] { (byte)0xC0, (byte)0x02, (byte)0x00 };

                bool wrote = false;

                foreach (var len in reportLens)
                {
                    int realLen = len > 0 ? len : 32;
                    foreach (var rumble in rumblePairs)
                    {
                        foreach (var rid in reportIdCandidates)
                        {
                            foreach (var header in headerCandidates)
                            {
                                foreach (var off in offsetCandidates)
                                {
                                    var report = new byte[realLen];
                                    Array.Clear(report, 0, report.Length);

                                    if (realLen > 0) report[0] = rid;
                                    if (report.Length > 1) report[1] = header;

                                    // place rumble near beginning if there's space (use offsets 3/4 as common)
                                    if (report.Length > 4)
                                    {
                                        report[3] = rumblePairs[1].l; // intentionally pick a small rumble variant (second pair)
                                        report[4] = rumblePairs[1].r;
                                    }

                                    if (off[0] < report.Length) report[off[0]] = red;
                                    if (off[1] < report.Length) report[off[1]] = green;
                                    if (off[2] < report.Length) report[off[2]] = blue;

                                    try
                                    {
                                        var ok = _device.Write(report);
                                        Trace.WriteLine($"SetLightbar attempt: outLen={realLen}, rid=0x{rid:X2}, header=0x{header:X2}, offs={off[0]},{off[1]},{off[2]} rumble={report[3]:X2},{report[4]:X2} -> Write returned {ok}");
                                        if (ok) { wrote = true; break; }
                                    }
                                    catch (Exception wex)
                                    {
                                        Trace.WriteLine($"SetLightbar Write exception (outLen={realLen}, rid=0x{rid:X2}): {wex}");
                                    }

                                    try
                                    {
                                        var mi = _device.GetType().GetMethod("WriteFeatureData") ?? _device.GetType().GetMethod("WriteFeature");
                                        if (mi != null)
                                        {
                                            var result = mi.Invoke(_device, new object[] { report });
                                            Trace.WriteLine($"SetLightbar attempt via {mi.Name}: outLen={realLen}, rid=0x{rid:X2}, offs={off[0]},{off[1]},{off[2]} -> returned={result ?? "null"}");
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
                    if (wrote) break;
                }

                if (!wrote)
                {
                    Trace.WriteLine("SetLightbar: none of the standard permutations succeeded; device may require a different transport or firmware-specific format.");
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
