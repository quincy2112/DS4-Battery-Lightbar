using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using HidLibrary;

namespace DS4BatteryMapper
{
    public class DS4ControllerManager : IDisposable
    {
        private Dictionary<string, DS4Controller> _controllers = new Dictionary<string, DS4Controller>();
        private const int DS4_VID = 0x054C; // Sony VID
        private const int DS4_PID = 0x05C4; // DS4 PID (USB)
        private const int DS4_PID_2 = 0x09CC; // DS4 PID (Wireless)
        private StringBuilder _debugLog = new StringBuilder();
        private bool _loggedOnce = false;

        private const string DevicesLogFile = "ds4-devices.log";

        public List<DS4Controller> GetConnectedControllers()
        {
            var devices = HidDevices.Enumerate();
            var connectedDevices = new Dictionary<string, DS4Controller>();

            _debugLog.Clear();
            _debugLog.AppendLine("=== DS4 Controller Detection ===");
            _debugLog.AppendLine($"Total HID devices found: {devices.Count()}");

            Trace.WriteLine($"[DS4Manager] Total HID devices: {devices.Count()}");

            foreach (var device in devices)
            {
                try
                {
                    _debugLog.AppendLine($"\nDevice: {device.Description}");
                    _debugLog.AppendLine($"  VID: 0x{device.Attributes.VendorId:X4}");
                    _debugLog.AppendLine($"  PID: 0x{device.Attributes.ProductId:X4}");

                    // Safely get an identifier for the device. HidLibrary's HidDevice doesn't expose SerialNumber on all platforms,
                    // so fall back to DevicePath which is usually available and unique per device instance.
                    string serial = null;
                    try
                    {
                        serial = device.DevicePath;
                    }
                    catch
                    {
                        try { serial = device.ToString(); } catch { serial = null; }
                    }

                    _debugLog.AppendLine($"  DevicePath/Serial: {serial}");
                    _debugLog.AppendLine($"  IsConnected: {device.IsConnected}");

                    Trace.WriteLine($"[DS4Manager] Device: {device.Description}, VID: 0x{device.Attributes.VendorId:X4}, PID: 0x{device.Attributes.ProductId:X4}, Path={serial}, IsConnected={device.IsConnected}");

                    // Check for Sony VID first
                    if (device.Attributes.VendorId != DS4_VID)
                    {
                        _debugLog.AppendLine($"  -> Skipped (Not Sony VID)");
                        continue;
                    }

                    // Check if it's a DS4 (original or v2)
                    if (device.Attributes.ProductId != DS4_PID && device.Attributes.ProductId != DS4_PID_2)
                    {
                        _debugLog.AppendLine($"  -> Skipped (Not DS4 PID. Expected 0x{DS4_PID:X4} or 0x{DS4_PID_2:X4})");
                        Trace.WriteLine($"[DS4Manager] Skipping - not a DS4 (expected 0x{DS4_PID:X4} or 0x{DS4_PID_2:X4})");
                        continue;
                    }

                    // Use stable device identifier (serial/device path) as the dictionary key to avoid repeated create/dispose cycles
                    string devicePath = serial ?? device.Description ?? $"DS4_{device.Attributes.ProductId}";
                    _debugLog.AppendLine($"  -> Recognized as DS4! key={devicePath}");
                    Trace.WriteLine($"[DS4Manager] Recognized DS4: {devicePath}");

                    // Reuse existing controller or create new one
                    if (!_controllers.ContainsKey(devicePath))
                    {
                        var controller = new DS4Controller(device);
                        _controllers[devicePath] = controller;
                        _debugLog.AppendLine($"  -> Created new controller");
                        Trace.WriteLine($"[DS4Manager] Created new controller for {devicePath}");
                    }

                    var existing = _controllers[devicePath];
                    connectedDevices[devicePath] = existing;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[DS4Manager] Error processing device entry: {ex}");
                }
            }

            // Remove disconnected controllers
            var disconnected = _controllers.Keys.Except(connectedDevices.Keys).ToList();
            foreach (var path in disconnected)
            {
                _debugLog.AppendLine($"\nRemoving disconnected: {path}");
                Trace.WriteLine($"[DS4Manager] Removing disconnected controller: {path}");
                _controllers[path]?.Dispose();
                _controllers.Remove(path);
            }

            _debugLog.AppendLine($"\n=== Result: {connectedDevices.Count} DS4 controller(s) found ===");
            Trace.WriteLine($"[DS4Manager] Returning {connectedDevices.Count} controllers");

            // Write device debug log to disk and trace output (non-blocking)
            try
            {
                File.WriteAllText(DevicesLogFile, _debugLog.ToString());
                Trace.WriteLine($"[DS4Manager] Wrote devices log to {Path.GetFullPath(DevicesLogFile)}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DS4Manager] Failed to write devices log: {ex}");
            }

            // Do NOT show a MessageBox from this background/worker context; callers (UI) may display messages if needed.
            _loggedOnce = true; // avoid repeated logging/popups; we already wrote the file

            return connectedDevices.Values.ToList();
        }

        public void Dispose()
        {
            foreach (var controller in _controllers.Values)
            {
                controller?.Dispose();
            }
            _controllers.Clear();
        }
    }
}
