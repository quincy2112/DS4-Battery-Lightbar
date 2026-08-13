using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public List<DS4Controller> GetConnectedControllers()
        {
            var devices = HidDevices.Enumerate();
            var connectedDevices = new Dictionary<string, DS4Controller>();
            
            _debugLog.Clear();
            _debugLog.AppendLine("=== DS4 Controller Detection ===");
            _debugLog.AppendLine($"Total HID devices found: {devices.Count()}");

            Debug.WriteLine($"[DS4Manager] Total HID devices: {devices.Count()}");

            foreach (var device in devices)
            {
                _debugLog.AppendLine($"\nDevice: {device.Description}");
                _debugLog.AppendLine($"  VID: 0x{device.Attributes.VendorId:X4}");
                _debugLog.AppendLine($"  PID: 0x{device.Attributes.ProductId:X4}");

                // Safely get an identifier for the device. HidLibrary's HidDevice doesn't expose SerialNumber on all platforms,
                // so fall back to DevicePath which is always available and unique per device instance.
                string serial = null;
                try
                {
                    serial = device.DevicePath;
                }
                catch
                {
                    try { serial = device.ToString(); } catch { serial = null; }
                }

                _debugLog.AppendLine($"  Serial: {serial}");

                Debug.WriteLine($"[DS4Manager] Device: {device.Description}, VID: 0x{device.Attributes.VendorId:X4}, PID: 0x{device.Attributes.ProductId:X4}");

                // Check for Sony VID
                if (device.Attributes.VendorId != DS4_VID)
                {
                    _debugLog.AppendLine($"  -> Skipped (Not Sony VID)");
                    continue;
                }

                // Check if it's a DS4 (original or v2)
                if (device.Attributes.ProductId != DS4_PID && device.Attributes.ProductId != DS4_PID_2)
                {
                    _debugLog.AppendLine($"  -> Skipped (Not DS4 PID. Expected 0x{DS4_PID:X4} or 0x{DS4_PID_2:X4})");
                    Debug.WriteLine($"[DS4Manager] Skipping - not a DS4 (expected 0x{DS4_PID:X4} or 0x{DS4_PID_2:X4})");
                    continue;
                }

                // Use stable device identifier (serial/device path) as the dictionary key to avoid repeated create/dispose cycles
                string devicePath = serial ?? device.Description ?? $"DS4_{device.Attributes.ProductId}";
                _debugLog.AppendLine($"  -> Recognized as DS4!");
                Debug.WriteLine($"[DS4Manager] Recognized DS4: {devicePath}");

                // Reuse existing controller or create new one
                if (!_controllers.ContainsKey(devicePath))
                {
                    var controller = new DS4Controller(device);
                    _controllers[devicePath] = controller;
                    _debugLog.AppendLine($"  -> Created new controller");
                    Debug.WriteLine($"[DS4Manager] Created new controller for {devicePath}");
                }

                var existing = _controllers[devicePath];
                connectedDevices[devicePath] = existing;
            }

            // Remove disconnected controllers
            var disconnected = _controllers.Keys.Except(connectedDevices.Keys).ToList();
            foreach (var path in disconnected)
            {
                _debugLog.AppendLine($"\nRemoving disconnected: {path}");
                Debug.WriteLine($"[DS4Manager] Removing disconnected controller: {path}");
                _controllers[path]?.Dispose();
                _controllers.Remove(path);
            }

            _debugLog.AppendLine($"\n=== Result: {connectedDevices.Count} DS4 controller(s) found ===");
            Debug.WriteLine($"[DS4Manager] Returning {connectedDevices.Count} controllers");

            // Show debug info once on startup
            if (!_loggedOnce && devices.Any())
            {
                _loggedOnce = true;
                MessageBox.Show(_debugLog.ToString(), "DS4 Controller Detection Debug Info");
            }

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
