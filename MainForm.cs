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
        private Timer? _uiTimer;
        private Timer? _pollTimer;
        private volatile bool _isEnumerating = false;
        private volatile bool _isPolling = false;
        private Color _lowBatteryColor = Color.Red;
        private Color _highBatteryColor = Color.Blue;
        private Button? _lowBatteryColorButton;
        private Button? _highBatteryColorButton;
        private Label? _statusLabel;
        private FlowLayoutPanel? _controllerPanel;
        private Dictionary<string, Panel> _controllerUIPanels = new Dictionary<string, Panel>();
        private NotifyIcon? _trayIcon;
        private ContextMenuStrip? _trayMenu;
        private bool _allowClose = false;

        public MainForm()
        {
            InitializeComponent();
            _controllerManager = new DS4ControllerManager();
            InitializeUI();
            InitializeTrayIcon();
            StartMonitoring();
        }

        private void InitializeComponent()
        {
            // Designer placeholder
        }

        private void InitializeUI()
        {
            this.Text = "DS4 Battery Lightbar Mapper";
            this.Size = new Size(650, 900);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(45, 45, 48);
            this.ForeColor = Color.White;

            // Root table: 4 rows (header, controllers, color pickers, status)
            var mainTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 4,
                ColumnCount = 1,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(45, 45, 48)
            };

            // Configure row styles
            mainTable.RowStyles.Clear();
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // Header
            mainTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Controllers (fill)
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));   // Color pickers
            mainTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // Status

            // Row 0: Header (title + refresh button)
            var headerPanel = new Panel
            {
                BackColor = Color.FromArgb(45, 45, 48),
                Dock = DockStyle.Fill
            };

            var titleLabel = new Label
            {
                Text = "Connected DS4 Controllers",
                Location = new Point(0, 5),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                AutoSize = true,
                ForeColor = Color.White
            };
            headerPanel.Controls.Add(titleLabel);

            var refreshBtn = new Button
            {
                Text = "Refresh",
                Location = new Point(560, 5),
                Size = new Size(80, 28)
            };
            refreshBtn.Click += (s, e) =>
            {
                Trace.WriteLine("[MainForm] Refresh clicked");
                PollControllersTick(null, EventArgs.Empty);
            };
            headerPanel.Controls.Add(refreshBtn);
            mainTable.Controls.Add(headerPanel, 0, 0);

            // Row 1: Controllers (fill remaining space)
            _controllerPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(45, 45, 48),
                Padding = new Padding(5)
            };
            mainTable.Controls.Add(_controllerPanel, 0, 1);

            // Row 2: Color pickers
            var colorPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 48),
                Padding = new Padding(0, 5, 0, 5)
            };

            var lowLabel = new Label
            {
                Text = "Low Battery:",
                Location = new Point(10, 10),
                AutoSize = true,
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.White
            };
            colorPanel.Controls.Add(lowLabel);

            _lowBatteryColorButton = CreateColorButton(_lowBatteryColor, (s, e) => SelectGradientColor(true));
            _lowBatteryColorButton.Location = new Point(100, 8);
            colorPanel.Controls.Add(_lowBatteryColorButton);

            var highLabel = new Label
            {
                Text = "High Battery:",
                Location = new Point(160, 10),
                AutoSize = true,
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.White
            };
            colorPanel.Controls.Add(highLabel);

            _highBatteryColorButton = CreateColorButton(_highBatteryColor, (s, e) => SelectGradientColor(false));
            _highBatteryColorButton.Location = new Point(250, 8);
            colorPanel.Controls.Add(_highBatteryColorButton);

            mainTable.Controls.Add(colorPanel, 0, 2);

            // Row 3: Status
            _statusLabel = new Label
            {
                Name = "StatusLabel",
                Text = "Initializing...",
                Dock = DockStyle.Fill,
                ForeColor = Color.LimeGreen,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9),
                BackColor = Color.FromArgb(30, 30, 30)
            };
            mainTable.Controls.Add(_statusLabel, 0, 3);

            this.Controls.Add(mainTable);
        }

        private void InitializeTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Show", null, (s, e) => ShowWindow());
            _trayMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

            _trayIcon = new NotifyIcon()
            {
                Icon = SystemIcons.Application,
                ContextMenuStrip = _trayMenu,
                Visible = true,
                Text = "DS4 Battery Lightbar Mapper"
            };

            _trayIcon.DoubleClick += (s, e) => ShowWindow();
        }

        private void ShowWindow()
        {
            Trace.WriteLine("[MainForm] ShowWindow called");
            _allowClose = false;
            this.WindowState = FormWindowState.Normal;
            this.Show();
            this.Activate();
            this.Focus();
            
            // Allow close only after a brief delay to avoid race condition with tray menu
            Task.Delay(200).ContinueWith(_ => _allowClose = true);
            Trace.WriteLine("[MainForm] ShowWindow complete");
        }

        private void ExitApplication()
        {
            Trace.WriteLine("[MainForm] ExitApplication called");
            _allowClose = true;
            // Properly dispose before exiting
            _uiTimer?.Stop();
            _uiTimer?.Dispose();
            _pollTimer?.Stop();
            _pollTimer?.Dispose();
            _controllerManager?.Dispose();
            _trayIcon?.Dispose();
            _trayMenu?.Dispose();
            Application.Exit();
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
                return;

            if (isLowBatteryColor)
            {
                _lowBatteryColor = colorDialog.Color;
                if (_lowBatteryColorButton != null)
                    _lowBatteryColorButton.BackColor = _lowBatteryColor;
            }
            else
            {
                _highBatteryColor = colorDialog.Color;
                if (_highBatteryColorButton != null)
                    _highBatteryColorButton.BackColor = _highBatteryColor;
            }

            RefreshControllerPanel();
        }

        private void RefreshControllerPanel()
        {
            if (_controllerManager == null) return;

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
            _uiTimer = new Timer { Interval = 2000 };
            _uiTimer.Tick += UIUpdateTick!;
            _uiTimer.Start();

            _pollTimer = new Timer { Interval = 15000 };
            _pollTimer.Tick += PollControllersTick!;
            _pollTimer.Start();

            Trace.WriteLine("Monitoring started: UI=2000ms, Poll=15000ms");
        }

        private async void UIUpdateTick(object? sender, EventArgs e)
        {
            if (_controllerManager == null) return;
            if (_isEnumerating) return;

            _isEnumerating = true;
            try
            {
                var controllers = await Task.Run(() => _controllerManager.GetConnectedControllers());
                if (_statusLabel != null)
                    _statusLabel.Text = $"Last enum: {DateTime.Now:HH:mm:ss} — {controllers.Count} controller(s)";
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

        private async void PollControllersTick(object? sender, EventArgs e)
        {
            if (_controllerManager == null) return;
            if (_isPolling) return;

            _isPolling = true;
            try
            {
                var controllers = await Task.Run(() => _controllerManager.GetConnectedControllers());

                foreach (var controller in controllers)
                {
                    try
                    {
                        var batteryTask = Task.Run(() => controller.UpdateBatteryStatus());
                        await Task.WhenAny(batteryTask, Task.Delay(1000));
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("[MainForm] Error updating battery: " + ex);
                    }

                    try
                    {
                        var battery = Math.Max(0, Math.Min(100, controller.BatteryPercentage));
                        var color = BatteryToColor(battery);
                        var lightTask = Task.Run(() => controller.SetLightbar((byte)color.R, (byte)color.G, (byte)color.B));
                        await Task.WhenAny(lightTask, Task.Delay(1000));
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("[MainForm] Error setting lightbar: " + ex);
                    }
                }
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
            if (_controllerPanel == null) return;

            // Only rebuild if the controller count changed or if no UI panels exist
            bool countChanged = controllers.Count != _controllerUIPanels.Count;
            
            if (countChanged)
            {
                // Rebuild the entire UI
                _controllerPanel.SuspendLayout();
                try
                {
                    _controllerPanel.Controls.Clear();
                    _controllerUIPanels.Clear();

                    if (controllers == null || controllers.Count == 0)
                    {
                        var noLabel = new Label
                        {
                            Text = "No DS4 controllers detected",
                            AutoSize = true,
                            ForeColor = Color.Gray,
                            Font = new Font("Segoe UI", 10)
                        };
                        _controllerPanel.Controls.Add(noLabel);
                        if (_statusLabel != null)
                            _statusLabel.Text = "No DS4 controllers detected";
                        return;
                    }

                    foreach (var controller in controllers)
                    {
                        try
                        {
                            var panel = CreateControllerUI(controller);
                            _controllerUIPanels[controller.DeviceName] = panel;
                            _controllerPanel.Controls.Add(panel);
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine("[MainForm] Error creating controller UI: " + ex);
                        }
                    }

                    if (_statusLabel != null)
                        _statusLabel.Text = $"Monitoring {controllers.Count} controller(s)";
                }
                finally
                {
                    _controllerPanel.ResumeLayout(true);
                }
            }
            else if (controllers.Count > 0)
            {
                // Just update battery values in existing panels (no rebuild)
                foreach (var controller in controllers)
                {
                    if (_controllerUIPanels.TryGetValue(controller.DeviceName, out var panel))
                    {
                        UpdateControllerBatteryUI(panel, controller);
                    }
                }
            }
        }

        private void UpdateControllerBatteryUI(Panel panel, DS4Controller controller)
        {
            var battery = Math.Max(0, Math.Min(100, controller.BatteryPercentage));
            var color = BatteryToColor(battery);

            // Update battery label
            var batteryLabel = panel.Controls.OfType<Label>()
                .FirstOrDefault(l => l.Text.StartsWith("Battery:"));
            if (batteryLabel != null)
                batteryLabel.Text = $"Battery: {battery}%";

            // Update battery bar background panel
            var barBg = panel.Controls.OfType<Panel>()
                .FirstOrDefault(p => p.BorderStyle == BorderStyle.FixedSingle && p.Location.Y == 65);
            if (barBg != null)
            {
                var barFill = barBg.Controls.OfType<Panel>().FirstOrDefault();
                if (barFill != null)
                {
                    barFill.Size = new Size((int)(298 * battery / 100.0), 13);
                    barFill.BackColor = color;
                }
            }

            // Update lightbar preview
            var lightbarPreview = panel.Controls.OfType<Panel>()
                .FirstOrDefault(p => p.BorderStyle == BorderStyle.FixedSingle && p.Location.Y == 40 && p.Location.X == 350);
            if (lightbarPreview != null)
                lightbarPreview.BackColor = color;
        }

        private Panel CreateControllerUI(DS4Controller controller)
        {
            var battery = Math.Max(0, Math.Min(100, controller.BatteryPercentage));
            var color = BatteryToColor(battery);

            var panel = new Panel
            {
                Width = 600,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(37, 37, 38),
                Margin = new Padding(0, 5, 0, 5)
            };

            var nameLabel = new Label
            {
                Text = $"Controller: {controller.DeviceName}",
                Location = new Point(10, 10),
                Size = new Size(400, 25),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.White
            };
            panel.Controls.Add(nameLabel);

            var batteryLabel = new Label
            {
                Text = $"Battery: {battery}%",
                Location = new Point(10, 40),
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.White
            };
            panel.Controls.Add(batteryLabel);

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
            Trace.WriteLine($"[MainForm] OnFormClosing: CloseReason={e.CloseReason}, _allowClose={_allowClose}");
            
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                // Minimize to tray instead of closing
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
                this.Hide();
                Trace.WriteLine("[MainForm] Hiding window to tray");
            }
            else if (_allowClose)
            {
                // Allow exit
                _uiTimer?.Stop();
                _uiTimer?.Dispose();
                _pollTimer?.Stop();
                _pollTimer?.Dispose();
                _controllerManager?.Dispose();
                _trayIcon?.Dispose();
                _trayMenu?.Dispose();
                Trace.WriteLine("[MainForm] Allowing application exit");
            }
        }
    }
}
