using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HidLibrary;

namespace DS4BatteryMapper
{
    public class DS4ControllerManager : IDisposable
    {
        private readonly object _lock = new object();
        // keyed by dedupe key (bluetooth address or device path fallback)
        private readonly Dictionary<string, DS4Controller> _controllers = new Dictionary<string, DS4Controller>(StringComparer.OrdinalIgnoreCase);

        public DS4ControllerManager()
        {
        }

        // Lightweight enumeration that deduplicates multiple HID collections that belong to the same physical device.
        // Returns the current set of DS4Controller objects (reused across calls when possible).
        public List<DS4Controller> GetConnectedControllers()
        {
            var log = new List<string>();

            try
            {
                var devices = HidDevices.Enumerate().ToList();
                log.Add($"[DS4Manager] Total HID devices: {devices.Count}");

                foreach (var d in devices)
                {
                    try
                    {
                        var desc = d.Description ?? "(no desc)";
                        var vid = d.Attributes?.VendorId ?? 0;
                        var pid = d.Attributes?.ProductId ?? 0;
                        var path = d.DevicePath ?? "";
                        var isConn = d.IsConnected;

                        log.Add($"[DS4Manager] Device: {desc}, VID: 0x{vid:X4}, PID: 0x{pid:X4}, Path={path}, IsConnected={isConn}");

                        // Recognize DS4 by VID/PID for Sony (0x054C: PlayStation) and DS4 wireless PID 0x09CC
                        if (vid == 0x054C && (pid == 0x09CC || pid == 0x09C0 || pid == 0x05C4))
                        {
                            // extract a dedupe key (prefer Bluetooth address-like substring if present)
                            var key = ExtractDeviceKey(path) ?? path;

                            if (!_controllers.ContainsKey(key))
                            {
                                try
                                {
                                    var controller = new DS4Controller(d);
                                    _controllers[key] = controller;
                                    log.Add($"[DS4Manager] Created new controller for {path}");
                                }
                                catch (Exception cex)
                                {
                                    log.Add($"[DS4Manager] Failed to create controller for {path}: {cex}");
                                }
                            }
                            else
                            {
                                // Update the underlying HidDevice reference if necessary
                                // (HidLibrary may return a new HidDevice instance for the same path; replace it)
                                try
                                {
                                    var existing = _controllers[key];
                                    // If the device path differs (shouldn't) keep existing. We'll keep existing HidDevice instance.
                                }
                                catch { }

                            }

                            log.Add($"[DS4Manager] Recognized DS4: {path}");
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Add($"[DS4Manager] Error inspecting HID device: {ex}");
                    }
                }

                // Build the result list from the dictionary values
                var result = new List<DS4Controller>();
                lock (_lock)
                {
                    result = _controllers.Values.ToList();
                }

                // Persist device enumeration for debugging
                try
                {
                    File.WriteAllText("ds4-devices.log", string.Join(Environment.NewLine, log));
                    Trace.WriteLine($"[DS4Manager] Wrote devices log to {Path.GetFullPath("ds4-devices.log")}");
                }
                catch (Exception wex)
                {
                    Trace.WriteLine($"[DS4Manager] Failed to write devices log: {wex}");
                }

                return result;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DS4Manager] Enumeration failed: {ex}");
                return _controllers.Values.ToList();
            }
        }

        // Try to extract an 8-hex Bluetooth address or a stable identifier from the device path.
        // Examples of device paths include segments like "...pid&09cc#9&22f644e1&0&0000#..." where 22f644e1 is useful to dedupe.
        private string? ExtractDeviceKey(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return null;

            // Look for an 8-hex group surrounded by ampersands
            var m = Regex.Match(devicePath, "&([0-9A-Fa-f]{8})&");
            if (m.Success && m.Groups.Count > 1)
            {
                return m.Groups[1].Value.ToLowerInvariant();
            }

            // Fallback: look for "pid&xxxx#<instance>#" and return the instance between pid# and next #
            var pidIndex = devicePath.IndexOf("pid&", StringComparison.OrdinalIgnoreCase);
            if (pidIndex >= 0)
            {
                var hashIndex = devicePath.IndexOf('#', pidIndex);
                if (hashIndex >= 0)
                {
                    var nextHash = devicePath.IndexOf('#', hashIndex + 1);
                    if (nextHash > hashIndex)
                    {
                        var instance = devicePath.Substring(hashIndex + 1, nextHash - hashIndex - 1);
                        return instance.ToLowerInvariant();
                    }
                }
            }

            return null;
        }

        public void Dispose()
        {
            try
            {
                foreach (var kv in _controllers.Values)
                {
                    kv.Dispose();
                }
                _controllers.Clear();
            }
            catch { }
        }
    }
}
