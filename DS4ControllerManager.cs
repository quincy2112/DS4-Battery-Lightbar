using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HidLibrary;

namespace DS4BatteryMapper
{
    public class DS4ControllerManager : IDisposable
    {
        private readonly object _lock = new object();
        // keyed by device instance ID (hardware identifier unique to each physical device)
        private readonly Dictionary<string, DS4Controller> _controllers = new Dictionary<string, DS4Controller>(StringComparer.OrdinalIgnoreCase);

        public DS4ControllerManager()
        {
        }

        public List<DS4Controller> GetConnectedControllers()
        {
            var log = new List<string>();

            try
            {
                var devices = HidDevices.Enumerate().ToList();
                log.Add($"[DS4Manager] Total HID devices: {devices.Count}");

                var foundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                        // Recognize DS4 by VID/PID
                        if (vid == 0x054C && (pid == 0x09CC || pid == 0x09C0 || pid == 0x05C4))
                        {
                            // Test read with timeout to verify device is functional
                            bool isHealthy = false;
                            try
                            {
                                if (!d.IsOpen)
                                    d.OpenDevice();

                                var testReadTask = Task.Run(() => d.Read());
                                if (testReadTask.Wait(TimeSpan.FromMilliseconds(2000)))
                                {
                                    var testRead = testReadTask.Result;
                                    if (testRead.Status == HidDeviceData.ReadStatus.Success && testRead.Data.Length > 0)
                                    {
                                        byte reportId = testRead.Data[0];
                                        if (reportId == 0x01 || reportId == 0x11)
                                        {
                                            isHealthy = true;
                                            log.Add($"[DS4Manager] Test read succeeded with valid report_id=0x{reportId:X2}");
                                        }
                                        else
                                        {
                                            log.Add($"[DS4Manager] Test read got invalid report_id=0x{reportId:X2}, skipping device");
                                        }
                                    }
                                    else
                                    {
                                        log.Add($"[DS4Manager] Test read failed with status={testRead.Status}, skipping device");
                                    }
                                }
                                else
                                {
                                    log.Add($"[DS4Manager] Test read timed out (2000ms), skipping device");
                                }
                            }
                            catch (Exception testEx)
                            {
                                log.Add($"[DS4Manager] Test read exception: {testEx.Message}, skipping device");
                            }

                            if (!isHealthy)
                            {
                                log.Add($"[DS4Manager] Skipping unhealthy DS4: {path}");
                                continue;
                            }

                            // Extract device instance ID - the unique hardware identifier
                            // Format: #9&XXXXXXXX& (Bluetooth) or #8&XXXXXXXX& (USB)
                            var key = ExtractDeviceInstanceId(path);
                            log.Add($"[DS4Manager] Extracted key: {key}");
                            foundKeys.Add(key);

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
                                log.Add($"[DS4Manager] Reusing existing controller for {path}");
                            }

                            log.Add($"[DS4Manager] Recognized DS4: {path}");
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Add($"[DS4Manager] Error inspecting HID device: {ex}");
                    }
                }

                // Remove stale controllers that are no longer in the device list
                var stalesToRemove = _controllers.Keys.Where(k => !foundKeys.Contains(k)).ToList();
                foreach (var key in stalesToRemove)
                {
                    log.Add($"[DS4Manager] Removing stale controller: {key}");
                    _controllers[key].Dispose();
                    _controllers.Remove(key);
                }

                var result = new List<DS4Controller>();
                lock (_lock)
                {
                    result = _controllers.Values.ToList();
                }

                log.Add($"[DS4Manager] Returning {result.Count} controller(s) (total cached: {_controllers.Count})");

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

        // Extract device instance ID from the device path.
        // Both Bluetooth and USB DS4 paths contain an 8-hex identifier that is unique to the physical device.
        // Bluetooth: ...#9&22f644e1&0&0000#...
        // USB:       ...#8&13fd67a1&0&0000#...
        // We extract the 8-hex value after the # and & to get the instance ID.
        private string ExtractDeviceInstanceId(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath))
                return devicePath ?? "unknown";

            // Match pattern: #<digit>&<8-hex>&
            // This covers both #9&XXXXXXXX& and #8&XXXXXXXX&
            var m = Regex.Match(devicePath, @"#\d&([0-9A-Fa-f]{8})&");
            if (m.Success && m.Groups.Count > 1)
            {
                return m.Groups[1].Value.ToLowerInvariant();
            }

            // Fallback: return the full path if we can't extract the ID
            return devicePath.ToLowerInvariant();
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
