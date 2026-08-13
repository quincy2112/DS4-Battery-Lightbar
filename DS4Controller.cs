using System;
using HidLibrary;

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
                System.Diagnostics.Debug.WriteLine($"SetLightbar called for {DeviceName} with RGB=({red},{green},{blue})");

                if (!_device.IsConnected)
                {
                    System.Diagnostics.Debug.WriteLine("SetLightbar: device not connected");
                    return;
                }

                if (!_device.IsOpen)
                    _device.OpenDevice();

                // Log capabilities for debugging
                try
                {
                    var caps = _device.Capabilities;
                    System.Diagnostics.Debug.WriteLine($"Device capabilities: OutputReportByteLength={caps?.OutputReportByteLength}, FeatureReportByteLength={caps?.FeatureReportByteLength}");
                }
                catch (Exception) { /* ignore */ }

                int outLen = _device.Capabilities?.OutputReportByteLength ?? 0;
                if (outLen <= 0) outLen = 32; // fallback size
                var report = new byte[outLen];

                // Common offset candidates for DS4 (report ID included at index 0)
                int[][] offsetCandidates = new[]
                {
                    new[] { 6, 7, 8 }, // common USB layout
                    new[] { 5, 6, 7 },
                    new[] { 4, 6, 7 }, // previous attempt
                    new[] { 7, 8, 9 }  // sometimes shifted
                };

                bool wrote = false;
                foreach (var off in offsetCandidates)
                {
                    Array.Clear(report, 0, report.Length);
                    report[0] = 0x05; // report id commonly used for DS4
                    if (off[0] < report.Length) report[off[0]] = red;
                    if (off[1] < report.Length) report[off[1]] = green;
                    if (off[2] < report.Length) report[off[2]] = blue;

                    try
                    {
                        var ok = _device.Write(report);
                        System.Diagnostics.Debug.WriteLine($"SetLightbar try offsets {off[0]},{off[1]},{off[2]} -> Write returned {ok} (outLen={outLen})");
                        if (ok) { wrote = true; break; }
                    }
                    catch (Exception wex)
                    {
                        System.Diagnostics.Debug.WriteLine($"SetLightbar write exception for offsets {off[0]},{off[1]},{off[2]}: {wex}");
                    }
                }

                if (!wrote)
                {
                    System.Diagnostics.Debug.WriteLine("SetLightbar: none of the standard offset attempts reported success; device may require a different report format or transport-specific handling.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetLightbar: unexpected error: {ex}");
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
