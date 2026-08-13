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
        private int _consecutiveReadFailures = 0;
        private const int MAX_CONSECUTIVE_FAILURES = 3;  // Allow a few failures before marking unhealthy

        public string DeviceName => _device?.Description ?? "Unknown";
        public int BatteryPercentage { get; private set; }
        public DateTime? LastSeen { get; private set; }
        public bool IsHealthy { get; private set; } = true;

        public DS4Controller(HidDevice device)
        {
            _device = device;
            _lastInputReport = new byte[256];
            BatteryPercentage = -1;
            LastSeen = null;
        }

        public void UpdateBatteryStatus()
        {
            try
            {
                if (!_device.IsConnected)
                {
                    Trace.WriteLine($"[DS4Controller] {DeviceName} not connected");
                    IsHealthy = false;
                    return;
                }

                if (!_device.IsOpen)
                    _device.OpenDevice();

                var data = _device.Read();
                if (data.Status == HidDeviceData.ReadStatus.Success && data.Data.Length > 0)
                {
                    _lastInputReport = data.Data;

                    // Determine report type and extract battery from the correct offset
                    byte reportId = _lastInputReport[0];

                    // Skip malformed reports (report_id=0x00 indicates garbage/feature report)
                    if (reportId == 0x00)
                    {
                        _consecutiveReadFailures++;
                        Trace.WriteLine($"[DS4Controller] {DeviceName} got malformed report (id=0x00, len={_lastInputReport.Length}), failures={_consecutiveReadFailures}");
                        
                        if (_consecutiveReadFailures >= MAX_CONSECUTIVE_FAILURES)
                        {
                            Trace.WriteLine($"[DS4Controller] {DeviceName} exceeded max consecutive failures, marking unhealthy");
                            IsHealthy = false;
                        }
                        return;
                    }

                    // Reset failure counter on successful read
                    _consecutiveReadFailures = 0;
                    IsHealthy = true;

                    int dataStart = -1;

                    if (reportId == 0x01)
                    {
                        // USB report
                        dataStart = 1;
                    }
                    else if (reportId == 0x11)
                    {
                        // Bluetooth report
                        dataStart = 3;
                    }

                    // Extract battery
                    if (dataStart >= 0 && _lastInputReport.Length > dataStart + 29)
                    {
                        byte batByte = _lastInputReport[dataStart + 29];
                        bool charging = (batByte & 0x10) != 0;
                        byte rawLevel = (byte)(batByte & 0x0F);

                        // Convert raw level to percentage
                        int percentage = charging
                            ? (int)((rawLevel * 100) / 11)
                            : (int)((rawLevel * 100) / 8);
                        percentage = Math.Min(100, Math.Max(0, percentage));

                        BatteryPercentage = percentage;
                        LastSeen = DateTime.Now;
                        Trace.WriteLine($"[DS4Controller] {DeviceName} battery read: {BatteryPercentage}% (report_id=0x{reportId:X2}, charging={charging}, raw_level={rawLevel})");
                    }
                    else
                    {
                        // Fallback: report format not recognized
                        _consecutiveReadFailures++;
                        Trace.WriteLine($"[DS4Controller] {DeviceName} battery read failed: unknown report format (report_id=0x{reportId:X2}, length={_lastInputReport.Length})");
                        
                        if (_consecutiveReadFailures >= MAX_CONSECUTIVE_FAILURES)
                        {
                            IsHealthy = false;
                        }
                    }
                }
                else
                {
                    _consecutiveReadFailures++;
                    Trace.WriteLine($"[DS4Controller] {DeviceName} read failed with status={data.Status}, failures={_consecutiveReadFailures}");
                    
                    if (_consecutiveReadFailures >= MAX_CONSECUTIVE_FAILURES)
                    {
                        Trace.WriteLine($"[DS4Controller] {DeviceName} marked unhealthy after failed read");
                        IsHealthy = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _consecutiveReadFailures++;
                Trace.WriteLine($"Error reading DS4 data: {ex}");
                
                if (_consecutiveReadFailures >= MAX_CONSECUTIVE_FAILURES)
                {
                    IsHealthy = false;
                }
            }
        }

        /// <summary>
        /// Compute CRC-32 (ISO_HDLC) checksum.
        /// Based on the polynomial used in zlib and the ds4-dashboard implementation.
        /// </summary>
        private static uint ComputeCrc32(byte[] data, int length)
        {
            // CRC-32 ISO_HDLC polynomial: 0x04C11DB7
            uint crc = 0xFFFFFFFF;

            for (int i = 0; i < length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320;
                    else
                        crc = crc >> 1;
                }
            }

            return crc ^ 0xFFFFFFFF;
        }

        public void SetLightbar(byte red, byte green, byte blue)
        {
            try
            {
                Trace.WriteLine($"SetLightbar called for {DeviceName} with RGB=({red},{green},{blue})");

                if (!_device.IsConnected)
                {
                    Trace.WriteLine("SetLightbar: device not connected");
                    IsHealthy = false;
                    return;
                }

                if (!_device.IsOpen)
                    _device.OpenDevice();

                try
                {
                    var caps = _device.Capabilities;
                    Trace.WriteLine($"Device capabilities: OutputReportByteLength={caps?.OutputReportByteLength}, FeatureReportByteLength={caps?.FeatureReportByteLength}");
                }
                catch (Exception) { /* ignore */ }

                // Detect connection type (USB vs Bluetooth) from the input report structure
                // USB reports start with 0x01, Bluetooth with 0x11
                bool isBluetooth = (_lastInputReport.Length > 0 && _lastInputReport[0] == 0x11);
                Trace.WriteLine($"SetLightbar: Detected connection type = {(isBluetooth ? "Bluetooth" : "USB")}");

                if (isBluetooth)
                {
                    // Bluetooth: 78-byte report with CRC-32 checksum
                    var report = new byte[78];
                    report[0] = 0x11;       // Report ID
                    report[1] = 0x80;       // Header (enables output mode)
                    report[3] = 0xFF;       // Enable flags (Rumble + Lightbar + others)
                    
                    report[6] = 0x00;       // Small rumble
                    report[7] = 0x00;       // Large rumble
                    report[8] = red;
                    report[9] = green;
                    report[10] = blue;

                    // Compute CRC-32 checksum over first 75 bytes (prepended with 0xA2)
                    var crcBuf = new byte[75];
                    crcBuf[0] = 0xA2;       // Magic prefix
                    Buffer.BlockCopy(report, 0, crcBuf, 1, 74);

                    uint crc = ComputeCrc32(crcBuf, 75);
                    report[74] = (byte)(crc & 0xFF);
                    report[75] = (byte)((crc >> 8) & 0xFF);
                    report[76] = (byte)((crc >> 16) & 0xFF);
                    report[77] = (byte)((crc >> 24) & 0xFF);

                    Trace.WriteLine($"SetLightbar BT: Sending 78-byte report with CRC 0x{crc:X8}");

                    bool ok = _device.Write(report);
                    Trace.WriteLine($"SetLightbar BT: Write returned {ok}");

                    if (ok)
                    {
                        Trace.WriteLine("SetLightbar: Bluetooth lightbar update sent successfully!");
                        return;
                    }
                }
                else
                {
                    // USB: 32-byte report, no CRC needed
                    var report = new byte[32];
                    report[0] = 0x05;       // Report ID for USB
                    report[1] = 0xFF;       // Enable Rumble, Lightbar, and Flash
                    
                    report[4] = 0x00;       // Small rumble
                    report[5] = 0x00;       // Large rumble
                    report[6] = red;
                    report[7] = green;
                    report[8] = blue;

                    Trace.WriteLine($"SetLightbar USB: Sending 32-byte report");

                    bool ok = _device.Write(report);
                    Trace.WriteLine($"SetLightbar USB: Write returned {ok}");

                    if (ok)
                    {
                        Trace.WriteLine("SetLightbar: USB lightbar update sent successfully!");
                        return;
                    }
                }

                Trace.WriteLine("SetLightbar: Write failed for both USB and Bluetooth formats.");
                IsHealthy = false;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SetLightbar: unexpected error: {ex}");
                IsHealthy = false;
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
