using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HidLibrary;

namespace DS4BatteryMapper
{
    public class DS4ControllerManager : IDisposable
    {
        private Dictionary<string, DS4Controller> _controllers = new Dictionary<string, DS4Controller>();
        private const int DS4_VID = 0x054C; // Sony VID
        private const int DS4_PID = 0x05C4; // DS4 PID (USB)
        private const int DS4_PID_2 = 0x09CC; // DS4 PID (Wireless)

        public List<DS4Controller> GetConnectedControllers()
        {
            var devices = HidDevices.Enumerate(DS4_VID);
            var connectedDevices = new Dictionary<string, DS4Controller>();

            Debug.WriteLine($"[DS4Manager] Found {devices.Count()} Sony devices");

            foreach (var device in devices)
            {
                Debug.WriteLine($"[DS4Manager] Device: {device.Description}, VID: 0x{device.Attributes.VendorId:X4}, PID: 0x{device.Attributes.ProductId:X4}");

                // Check if it's a DS4 (original or v2)
                if (device.Attributes.ProductId != DS4_PID && device.Attributes.ProductId != DS4_PID_2)
                {
                    Debug.WriteLine($"[DS4Manager] Skipping - not a DS4 (expected 0x{DS4_PID:X4} or 0x{DS4_PID_2:X4})");
                    continue;
                }

                string devicePath = device.Description ?? $"DS4_{device.Attributes.ProductId}";
                Debug.WriteLine($"[DS4Manager] Recognized DS4: {devicePath}");

                // Reuse existing controller or create new one
                if (!_controllers.ContainsKey(devicePath))
                {
                    var controller = new DS4Controller(device);
                    _controllers[devicePath] = controller;
                    Debug.WriteLine($"[DS4Manager] Created new controller for {devicePath}");
                }

                var existing = _controllers[devicePath];
                connectedDevices[devicePath] = existing;
            }

            // Remove disconnected controllers
            var disconnected = _controllers.Keys.Except(connectedDevices.Keys).ToList();
            foreach (var path in disconnected)
            {
                Debug.WriteLine($"[DS4Manager] Removing disconnected controller: {path}");
                _controllers[path]?.Dispose();
                _controllers.Remove(path);
            }

            Debug.WriteLine($"[DS4Manager] Returning {connectedDevices.Count} controllers");
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
