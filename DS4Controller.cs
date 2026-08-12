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
                if (!_device.IsConnected || !_device.IsOpen)
                    return;

                byte[] report = new byte[32];
                report[0] = 0x05; // Report ID
                report[1] = 0xFF;
                report[4] = red;   // Red
                report[6] = green; // Green
                report[7] = blue;  // Blue

                _device.Write(report);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting lightbar: {ex.Message}");
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
