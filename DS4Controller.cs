using System;
using HidLibrary;
using System.Diagnostics;
using System.Reflection;

namespace DS4BatteryMapper
{
    public class DS4Controller : IDisposable
    {
        private HidDevice _device;
        private byte[] _lastInputReport;

        public string DeviceName => _device?.Description ?? "Unknown";
        public int BatteryPercentage { get; private set; }

        public DS4Controller(HidDevice device)
        {
            _device = device;
            _lastInputReport = new byte[64];
            UpdateBatteryStatus();
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
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading DS4 data: {ex.Message}");
            }
        }

        public void SetLightbar(byte red, byte green, byte blue)
        {
            try
            {
                Debug.WriteLine($"SetLightbar called for {DeviceName} with RGB=({red},{green},{blue})");

                if (!_device.IsConnected)
                {
                    Debug.WriteLine("SetLightbar: device not connected");
                    return;
                }

                if (!_device.IsOpen)
                    _device.OpenDevice();

                // Log capabilities for debugging
                try
                {
                    var caps = _device.Capabilities;
                    Debug.WriteLine($"Device capabilities: OutputReportByteLength={caps?.OutputReportByteLength}, FeatureReportByteLength={caps?.FeatureReportByteLength}");
                }
                catch (Exception) { /* ignore */ }

                // Try Bluetooth-specific 78-byte output report (common for DS4 over BT) first,
                // then fall back to other lengths/IDs we already attempted.
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
                                    Debug.WriteLine($"SetLightbar attempt: len={realLen}, rid=0x{rid:X2}, header=0x{header:X2}, offs={off[0]},{off[1]},{off[2]} -> Write returned {ok}");
                                    if (ok) { wrote = true; break; }
                                }
                                catch (Exception wex)
                                {
                                    Debug.WriteLine($"SetLightbar Write exception (len={realLen}, rid=0x{rid:X2}): {wex}");
                                }

                                // Try feature write if available (some BT stacks expect a feature report)
                                try
                                {
                                    var mi = _device.GetType().GetMethod("WriteFeatureData") ?? _device.GetType().GetMethod("WriteFeature");
                                    if (mi != null)
                                    {
                                        var result = mi.Invoke(_device, new object[] { report });
                                        Debug.WriteLine($"SetLightbar feature attempt via {mi.Name}: len={realLen}, rid=0x{rid:X2}, offs={off[0]},{off[1]},{off[2]} -> result={result ?? "null"}");
                                        // We can't always determine success from the reflected call, but assume it attempted
                                        wrote = true;
                                        break;
                                    }
                                }
                                catch (TargetInvocationException tie)
                                {
                                    Debug.WriteLine($"SetLightbar feature invocation error: {tie.InnerException?.Message ?? tie.Message}");
                                }
                                catch (Exception fim)
                                {
                                    Debug.WriteLine($"SetLightbar feature-write exception: {fim}");
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
                    Debug.WriteLine("SetLightbar: all attempts failed; Bluetooth DS4 likely needs a specific 78-byte structure or the device is controlled by another process.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetLightbar: unexpected error: {ex}");
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
}
