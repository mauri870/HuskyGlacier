using System;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using HidLibrary;

namespace HuskyGlacier
{
    class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private static Mutex singleInstanceMutex;
        private static NotifyIcon trayIcon;
        private static Computer computer;
        private static System.Windows.Forms.Timer updateTimer;

        // Cache CPU sensor once
        private static ISensor cpuTempSensor;
        private static IHardware cpuHardware;

        // Temperature values
        private static float currentCpuTemp = 0;
        private static float previousCpuTemp = -1; // Track previous temp to avoid unnecessary icon updates
        private static string displayTemp = "N/A";

        // Device VID and PID for Husky Glacier HWT700PT pump
        private static readonly int PUMPVID = 0xAA88;
        private static readonly int PUMPPID = 0x8666;
        private static HidDevice pumpDevice;

        [STAThread]
        static void Main()
        {
            try
            {
                // Enforce single instance
                bool createdNew;
                singleInstanceMutex = new Mutex(true, "HuskyGlacier_SingleInstance_Mutex", out createdNew);

                if (!createdNew)
                {
                    MessageBox.Show(
                        "HuskyGlacier is already running. Check your system tray.",
                        "HuskyGlacier",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                if (!IsRunningAsAdministrator())
                {
                    MessageBox.Show(
                        "This application requires administrator privileges to access hardware sensors.\n\n" +
                        "Please run this program as Administrator.",
                        "HuskyGlacier",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                InitializeHardwareMonitoring();

                InitializeHidDevice();

                CreateTrayIcon();

                SetupUpdateTimer();

                UpdateTemperatures();

                Application.Run();

                Cleanup();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fatal error: {ex.Message}", "HuskyGlacier - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void InitializeHardwareMonitoring()
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsStorageEnabled = false,
                IsPsuEnabled = false,
                IsNetworkEnabled = false,
                IsMotherboardEnabled = false,
                IsMemoryEnabled = false,
                IsGpuEnabled = false,
                IsControllerEnabled = false,
                IsBatteryEnabled = false
            };
            computer.Open();

            cpuHardware = computer.Hardware
                .FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);

            cpuHardware?.Update();

            cpuTempSensor = cpuHardware?.Sensors
                .FirstOrDefault(s => s.SensorType == SensorType.Temperature &&
                                     (s.Name.Contains("Tctl") || s.Name.Contains("CPU")));
        }

        private static void InitializeHidDevice()
        {
            try
            {
                var devices = HidDevices.Enumerate(PUMPVID, PUMPPID);
                var device = devices.FirstOrDefault();

                if (device != null)
                {
                    device.OpenDevice();
                    if (device.IsOpen)
                    {
                        pumpDevice = device;
                        return;
                    }
                }

                ShowTrayMessage("Pump Not Found", "Device not found. Check USB connection and close vendor software.", ToolTipIcon.Warning);
            }
            catch (Exception ex)
            {
                ShowTrayMessage("Connection Error", $"Error: {ex.Message}", ToolTipIcon.Error);
            }
        }

        private static void CreateTrayIcon()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = CreateTempIcon("--", Color.Gray, 11, "Segoe UI"),
                Visible = true,
                Text = "HuskyGlacier - Initializing..."
            };

            // Create context menu
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Close", null, OnClose);
            trayIcon.ContextMenuStrip = contextMenu;
        }

        private static void SetupUpdateTimer()
        {
            updateTimer = new System.Windows.Forms.Timer
            {
                Interval = 1000 // 1 second
            };
            updateTimer.Tick += OnTimerTick;
            updateTimer.Start();
        }

        private static void OnTimerTick(object sender, EventArgs e)
        {
            UpdateTemperatures();
            SendTemperatureToPump();
        }

        private static void UpdateTemperatures()
        {
            try
            {
                cpuHardware?.Update();
                if (cpuTempSensor?.Value is float temp)
                    currentCpuTemp = temp;

                displayTemp = $"HuskyGlacier - CPU: {currentCpuTemp:F0}°C";
                trayIcon.Text = displayTemp;

                // Only update icon if temperature changed by at least 1 degree
                if (Math.Abs(currentCpuTemp - previousCpuTemp) >= 1.0f)
                {
                    var oldIcon = trayIcon.Icon;
                    string tempText = $"{currentCpuTemp:F0}";
                    Color tempColor = GetTempColor(currentCpuTemp);
                    trayIcon.Icon = CreateTempIcon(tempText, tempColor, 11, "Segoe UI");
                    oldIcon?.Dispose();
                    previousCpuTemp = currentCpuTemp;
                }
            }
            catch (Exception ex)
            {
                displayTemp = "Error reading temperatures";
                trayIcon.Text = $"HuskyGlacier - Error: {ex.Message}";
            }
        }

        private static void SendTemperatureToPump()
        {
            if (pumpDevice?.IsOpen != true)
                return;

            try
            {
                byte[] reportData = new byte[10];
                reportData[1] = (byte)Math.Round(currentCpuTemp);

                var report = new HidReport(10, new HidDeviceData(reportData, HidDeviceData.ReadStatus.Success));
                bool success = pumpDevice.WriteReport(report);

                if (!success)
                {
                    // Try to reconnect if write fails
                    InitializeHidDevice();
                }
            }
            catch (Exception)
            {
                // Reconnect on error
                pumpDevice?.CloseDevice();
                pumpDevice = null;
                InitializeHidDevice();
            }
        }

        private static void OnClose(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private static void ShowTrayMessage(string title, string message, ToolTipIcon icon)
        {
            trayIcon?.ShowBalloonTip(3000, title, message, icon);
        }

        private static Icon CreateTempIcon(string text, Color color, float baseFontSize, string fontFamily)
        {
            Size iconSize = System.Windows.Forms.SystemInformation.SmallIconSize;

            using var bmp = new Bitmap(iconSize.Width, iconSize.Height);
            using var g = Graphics.FromImage(bmp);
            float dpiScale = g.DpiX / 96f;
            float scaledFontSize = baseFontSize * dpiScale;

            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using (var font = new Font(fontFamily, scaledFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF textSize = g.MeasureString(text, font);
                float x = (iconSize.Width - textSize.Width) / 2;
                float y = (iconSize.Height - textSize.Height) / 2 + (dpiScale * 0.5f);

                using var brush = new SolidBrush(color);
                g.DrawString(text, font, brush, new PointF(x, y));
            }

            IntPtr hIcon = bmp.GetHicon();
            Icon newIcon = (Icon)Icon.FromHandle(hIcon).Clone();
            DestroyIcon(hIcon);
            return newIcon;
        }

        private static Color GetTempColor(float temp)
        {
            if (temp <= 60) return Color.LimeGreen;
            if (temp <= 75) return Color.Yellow;
            if (temp <= 90) return Color.OrangeRed;
            return Color.DarkRed;
        }

        private static void Cleanup()
        {
            updateTimer?.Stop();
            updateTimer?.Dispose();
            pumpDevice?.CloseDevice();
            pumpDevice?.Dispose();
            trayIcon?.Dispose();
            computer?.Close();

            singleInstanceMutex?.ReleaseMutex();
            singleInstanceMutex?.Dispose();
        }
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool IsRunningAsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

}

