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
        private Timer? _updateTimer;

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
            this.Size = new Size(600, 400);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(45, 45, 48);
            this.ForeColor = Color.White;

            var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            var titleLabel = new Label
            {
                Text = "Connected DS4 Controllers",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                AutoSize = true,
                ForeColor = Color.White
            };
            mainPanel.Controls.Add(titleLabel);

            var controllerPanel = new Panel
            {
                Name = "ControllerPanel",
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(45, 45, 48)
            };
            mainPanel.Controls.Add(controllerPanel);

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
            mainPanel.Controls.Add(statusLabel);

            this.Controls.Add(mainPanel);
        }

        private void StartMonitoring()
        {
            _updateTimer = new Timer { Interval = 500 };
            _updateTimer.Tick += UpdateControllerStatus!;
            _updateTimer.Start();
        }

        // Run device I/O off the UI thread to avoid freezing the form.
        private async void UpdateControllerStatus(object? sender, EventArgs e)
        {
            if (_controllerManager == null)
                return;

            List<DS4Controller> controllers = new List<DS4Controller>();

            try
            {
                // Perform enumeration and device I/O on a background thread
                controllers = await Task.Run(() =>
                {
                    var list = _controllerManager.GetConnectedControllers();

                    foreach (var controller in list)
                    {
                        try
                        {
                            controller.UpdateBatteryStatus();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[MainForm] Error updating battery (background): " + ex);
                        }

                        try
                        {
                            var battery = Math.Max(0, Math.Min(100, controller?.BatteryPercentage ?? 0));
                            var color = BatteryToColor(battery);
                            controller.SetLightbar((byte)color.R, (byte)color.G, (byte)color.B);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[MainForm] Error setting lightbar (background): " + ex);
                        }
                    }

                    return list;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MainForm] Background controller update failed: " + ex);
                var statusLabelErr = this.Controls.Find("StatusLabel", true).FirstOrDefault() as Label;
                if (statusLabelErr != null)
                    statusLabelErr.Text = "Error during device update";
                return;
            }

            // UI update must run on UI thread (we're back on UI thread after await)
            var controllerPanel = this.Controls.Find("ControllerPanel", true).FirstOrDefault() as Panel;
            if (controllerPanel == null)
                return;

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

                var statusLabel = this.Controls.Find("StatusLabel", true).FirstOrDefault() as Label;
                if (statusLabel != null)
                {
                    statusLabel.Text = "No DS4 controllers detected";
                }

                return;
            }

            int yOffset = 10;
            foreach (var controller in controllers)
            {
                var controllerUI = CreateControllerUI(controller, yOffset);
                controllerPanel.Controls.Add(controllerUI);
                yOffset += 130;
            }

            var statusLabelFinal = this.Controls.Find("StatusLabel", true).FirstOrDefault() as Label;
            if (statusLabelFinal != null)
            {
                statusLabelFinal.Text = $"Monitoring {controllers.Count} controller(s)";
            }
        }

        private Panel CreateControllerUI(DS4Controller controller, int yPosition)
        {
            var battery = Math.Max(0, Math.Min(100, controller?.BatteryPercentage ?? 0));
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
                Text = $"Controller: {controller?.DeviceName ?? "Unknown"}",
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
            // Red (0-33%), Yellow (33-66%), Green (66-100%)
            if (percentage <= 33)
            {
                // Red to Yellow
                int g = (int)(255 * (percentage / 33.0));
                return Color.FromArgb(255, g, 0);
            }
            else if (percentage <= 66)
            {
                // Yellow to Green
                int r = (int)(255 * ((66 - percentage) / 33.0));
                return Color.FromArgb(r, 255, 0);
            }
            else
            {
                // Green
                int r = (int)(255 * ((100 - percentage) / 34.0));
                return Color.FromArgb(r, 255, 0);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
            _controllerManager?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
