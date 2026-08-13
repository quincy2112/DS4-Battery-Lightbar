using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DS4BatteryMapper
{
    public partial class MainForm : Form
    {
        private DS4ControllerManager? _controllerManager;
        private Timer? _uiTimer;               // frequent UI refresh (no blocking I/O)
        private Timer? _pollTimer;             // infrequent device polling (battery, lightbar)
        private volatile bool _isEnumerating = false;
        private volatile bool _isPolling = false;
        private Color _lowBatteryColor = Color.Red;
        private Color _highBatteryColor = Color.LimeGreen;
        private Button? _lowBatteryColorButton;
        private Button? _highBatteryColorButton;

        public MainForm()
        {
            InitializeComponent();
            _controllerManager = new DS4ControllerManager();
            InitializeUI();
            StartMonitoring();
        }

        private void InitializeComponent()
        {
            // WinForms designer placeholder - all UI initialized in InitializeUI
        }

        private void InitializeUI()
        {
            this.Text = "DS4 Battery Lightbar Mapper";
            this.Size = new Size(600, 460);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(45, 45, 48);
            this.ForeColor = Color.White;

            var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = Color.FromArgb(45, 45, 48)
            };

            var titleLabel = new Label
            {
                Text = "Connected DS4 Controllers",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                AutoSize = true,
                ForeColor = Color.White
            };
            topPanel.Controls.Add(titleLabel);

            var refreshBtn = new Button
            {
                Text = "Refresh",
                Location = new Point(480, 10),
                Size = new Size(80, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            refreshBtn.Click += (s, e) =>
            {
                Trace.WriteLine("[MainForm] Refresh clicked - forcing poll");
                // Call poll tick directly to force immediate enumeration + poll
                PollControllersTick(null, EventArgs.Empty);
            };
            topPanel.Controls.Add(refreshBtn);

            var lowBatteryLabel = new Label
            {
                Text = "Low Battery Color (0%)",
                Location = new Point(10, 45),
                Size = new Size(160, 20),
                ForeColor = Color.White
            };
            topPanel.Controls.Add(lowBatteryLabel);

            _lowBatteryColorButton = CreateColorButton(_lowBatteryColor, (s, e) => SelectGradientColor(true));
            _lowBatteryColorButton.Location = new Point(175, 42);
            topPanel.Controls.Add(_lowBatteryColorButton);

            var highBatteryLabel = new Label
            {
                Text = "High Battery Color (100%)",
                Location = new Point(280, 45),
                Size = new Size(170, 20),
                ForeColor = Color.White
            };
            topPanel.Controls.Add(highBatteryLabel);

            _highBatteryColorButton = CreateColorButton(_highBatteryColor, (s, e) => SelectGradientColor(false));
            _highBatteryColorButton.Location = new Point(455, 42);
            topPanel.Controls.Add(_highBatteryColorButton);

            var controllerPanel = new Panel
            {
                Name = "ControllerPanel",
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(45, 45, 48)
            };

            var statusLabel = new Label
            {
                Name = "StatusLabel",
                Text = "Initializing...",
                Dock = DockStyle.Bottom,
                Height = 30,
                ForeColor = Color.LimeGreen,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(30, 30, 30)
            };
            mainPanel.Controls.Add(topPanel);
            mainPanel.Controls.Add(statusLabel);
            mainPanel.Controls.Add(controllerPanel);

            this.Controls.Add(mainPanel);
        }

        private Button CreateColorButton(Color color, EventHandler onClick)
        {
            var button = new Button
            {
                Size = new Size(40, 24),
                BackColor = color,
                FlatStyle = FlatStyle.Popup,
                UseVisualStyleBackColor = false
            };
            button.Click += onClick;
            return button;
        }

        private void SelectGradientColor(bool isLowBatteryColor)
        {
            using var colorDialog = new ColorDialog
            {
                Color = isLowBatteryColor ? _lowBatteryColor : _highBatteryColor,
                FullOpen = true
            };

            if (colorDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            if (isLowBatteryColor)
            {
                _lowBatteryColor = colorDialog.Color;
                if (_lowBatteryColorButton != null)
                {
                    _lowBatteryColorButton.BackColor = _lowBatteryColor;
                }
            }
            else
            {
                _highBatteryColor = colorDialog.Color;
                if (_highBatteryColorButton != null)
                {
                    _highBatteryColorButton.BackColor = _highBatteryColor;
                }
            }

            RefreshControllerPanel();
        }

        private void RefreshControllerPanel()
        {
            if (_controllerManager == null)
            {
                return;
            }

            try
            {
                UpdateControllerPanel(_controllerManager.GetConnectedControllers());
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[MainForm] RefreshControllerPanel failed: " + ex);
            }
        }

        private void StartMonitoring()
        {
            // UI timer: less frequent to avoid racing enumeration (2s)
            _uiTimer = new Timer { Interval = 2000 };
            _uiTimer.Tick += UIUpdateTick!;
            _uiTimer.Start();

            // Poll timer: infrequent (15s) device polling for battery and lightbar writes
            _pollTimer = new Timer { Interval = 15000 };
            _pollTimer.Tick += PollControllersTick!;
            _pollTimer.Start();

            Trace.WriteLine("Monitoring started: UI=2000ms, Poll=15000ms");
        }

        // UI-only update (fast). Enumerates controllers but does not perform blocking reads/writes.
        private async void UIUpdateTick(object? sender, EventArgs e)
        {
            if (_controllerManager == null) return;
            if (_isEnumerating)
            {
                Trace.WriteLine("[MainForm] UIUpdateTick skipped - enumeration already running");
                return;
            }

            _isEnumerating = true;
            try
            {
                Trace.WriteLine("[MainForm] UIUpdateTick - enumerating controllers");
                // Run enumeration on background thread briefly
                var controllers = await Task.Run(() => _controllerManager.GetConnectedControllers());

                Trace.WriteLine($"[MainForm] UIUpdateTick enumerated {controllers.Count} controller(s)");

                // Update status label immediately so the user can see count/time even if detailed UI fails
                var statusLabel = this.Controls.Find("StatusLabel", true).FirstOrDefault() as Label;
                if (statusLabel != null)
                {
                    statusLabel.Text = $"Last enum: {DateTime.Now:HH:mm:ss} — {controllers.Count} controller(s)";
                }

                Trace.WriteLine($"[MainForm] Calling UpdateControllerPanel with {controllers.Count} controller(s)");
                // Update UI with current cached battery values (no blocking calls here)
                UpdateControllerPanel(controllers);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[MainForm] UIUpdateTick error: " + ex);
            }
            finally
            {
                _isEnumerating = false;
            }
        }

        // Poll controllers less frequently: perform battery reads and SetLightbar with timeouts
        private async void PollControllersTick(object? sender, EventArgs e)
        {
            if (_controllerManager == null) return;
            if (_isPolling)
            {
                Trace.WriteLine("[MainForm] PollControllersTick skipped - polling already running");
                return;
            }

            _isPolling = true;

            try
            {
                Trace.WriteLine("[MainForm] PollControllersTick start");
                var controllers = await Task.Run(() => _controllerManager.GetConnectedControllers());

                Trace.WriteLine($"[MainForm] PollControllersTick found {controllers.Count} controller(s)");

                foreach (var controller in controllers)
                {
                    // Update battery with a 1s timeout
                    try
                    {
                        var batteryTask = Task.Run(() => controller.UpdateBatteryStatus());
                        var finished = await Task.WhenAny(batteryTask, Task.Delay(1000));
                        if (finished != batteryTask)
                        {
                            Trace.WriteLine($"[MainForm] UpdateBatteryStatus timed out for {controller.DeviceName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("[MainForm] Error updating battery during poll: " + ex);
                    }

                    // Set lightbar with a 1s timeout
                    try
                    {
                        var battery = Math.Max(0, Math.Min(100, controller.BatteryPercentage));
                        var color = BatteryToColor(battery);

                        Trace.WriteLine($"[MainForm] SetLightbar called for {controller.DeviceName} with battery {battery}%");

                        var lightTask = Task.Run(() => controller.SetLightbar((byte)color.R, (byte)color.G, (byte)color.B));
                        var finished2 = await Task.WhenAny(lightTask, Task.Delay(1000));
                        if (finished2 != lightTask)
                        {
                            Trace.WriteLine($"[MainForm] SetLightbar timed out for {controller.DeviceName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("[MainForm] Error setting lightbar during poll: " + ex);
                    }
                }

                Trace.WriteLine("[MainForm] PollControllersTick finished");
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[MainForm] PollControllersTick failed: " + ex);
            }
            finally
            {
                _isPolling = false;
            }
        }

        private void UpdateControllerPanel(List<DS4Controller> controllers)
        {
            Trace.WriteLine($"[MainForm] Entering UpdateControllerPanel with {controllers?.Count ?? 0} controller(s)");

            var controllerPanel = this.Controls.Find("ControllerPanel", true).FirstOrDefault() as Panel;
            var statusLabel = this.Controls.Find("StatusLabel", true).FirstOrDefault() as Label;

            if (controllerPanel == null)
            {
                Trace.WriteLine("[MainForm] ControllerPanel not found");
                if (statusLabel != null) statusLabel.Text = "UI error: Controller panel not found";
                return;
            }

            controllerPanel.Controls.Clear();

            if (controllers == null || controllers.Count == 0)
            {
                var noLabel = new Label
                {
                    Text = "No DS4 controllers detected",
                    AutoSize = true,
                    ForeColor = Color.Gray,
                    Font = new Font("Segoe UI", 10)
                };
                controllerPanel.Controls.Add(noLabel);

                if (statusLabel != null)
                    statusLabel.Text = "No DS4 controllers detected";

                Trace.WriteLine("[MainForm] No controllers found (UI)");
                return;
            }

            int yOffset = 10;
            foreach (var controller in controllers)
            {
                try
                {
                    var controllerUI = CreateControllerUI(controller, yOffset);
                    controllerPanel.Controls.Add(controllerUI);
                    yOffset += 130;
                }
                catch (Exception uiEx)
                {
                    Trace.WriteLine("[MainForm] Error creating controller UI: " + uiEx);
                }
            }

            if (statusLabel != null)
                statusLabel.Text = $"Monitoring {controllers.Count} controller(s)";

            Trace.WriteLine($"[MainForm] Displaying {controllers.Count} controller(s)");
        }

        private Panel CreateControllerUI(DS4Controller controller, int yPosition)
        {
            var battery = Math.Max(0, Math.Min(100, controller.BatteryPercentage));
            var color = BatteryToColor(battery);

            var panel = new Panel
            {
                Location = new Point(10, yPosition),
                Size = new Size(550, 110),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(37, 37, 38)
            };

            // Controller name
            var nameLabel = new Label
            {
                Text = $"Controller: {controller.DeviceName}",
                Location = new Point(10, 10),
                Size = new Size(400, 25),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.White
            };
            panel.Controls.Add(nameLabel);

            // Battery percentage
            var batteryLabel = new Label
            {
                Text = $"Battery: {battery}%",
                Location = new Point(10, 40),
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.White
            };
            panel.Controls.Add(batteryLabel);

            // Battery bar
            var barBg = new Panel
            {
                Location = new Point(10, 65),
                Size = new Size(300, 15),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(20, 20, 20)
            };

            var barFill = new Panel
            {
                Location = new Point(1, 1),
                Size = new Size((int)(298 * battery / 100.0), 13),
                BackColor = color
            };
            barBg.Controls.Add(barFill);
            panel.Controls.Add(barBg);

            // Lightbar preview
            var lightbarPreview = new Panel
            {
                Location = new Point(350, 40),
                Size = new Size(50, 40),
                BackColor = color,
                BorderStyle = BorderStyle.FixedSingle
            };
            panel.Controls.Add(lightbarPreview);

            var lightbarLabel = new Label
            {
                Text = "Lightbar",
                Location = new Point(350, 82),
                Size = new Size(50, 15),
                Font = new Font("Segoe UI", 8),
                TextAlign = ContentAlignment.TopCenter,
                ForeColor = Color.Gray
            };
            panel.Controls.Add(lightbarLabel);

            return panel;
        }

        private Color BatteryToColor(int percentage)
        {
            percentage = Math.Max(0, Math.Min(100, percentage));
            double ratio = percentage / 100.0;

            int r = (int)Math.Round(_lowBatteryColor.R + ((_highBatteryColor.R - _lowBatteryColor.R) * ratio));
            int g = (int)Math.Round(_lowBatteryColor.G + ((_highBatteryColor.G - _lowBatteryColor.G) * ratio));
            int b = (int)Math.Round(_lowBatteryColor.B + ((_highBatteryColor.B - _lowBatteryColor.B) * ratio));

            return Color.FromArgb(r, g, b);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _uiTimer?.Stop();
            _uiTimer?.Dispose();
            _pollTimer?.Stop();
            _pollTimer?.Dispose();
            _controllerManager?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
