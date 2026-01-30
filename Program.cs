using System;
using System.Linq;
using System.Security.Principal;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;
using HidLibrary;

namespace Pumpt
{
    class Program
    {
        private static NotifyIcon trayIcon;
        private static Computer computer;
        private static Timer updateTimer;

        // Temperature values
        private static float currentCpuTemp = 0;
        private static string displayTemp = "N/A";

        // Device VID and PID for Husky Glacier cooler
        private static readonly int PUMPVID = 0xAA88;
        private static readonly int PUMPPID = 0x8666;
        private static HidDevice pumpDevice;

        [STAThread]
        static void Main()
        {
            try
            {
                // Check if running as administrator
                if (!IsRunningAsAdministrator())
                {
                    MessageBox.Show(
                        "This application requires administrator privileges to access hardware sensors.\n\n" +
                        "Please run this program as Administrator.",
                        "Pumpt",
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
                MessageBox.Show($"Fatal error: {ex.Message}", "Pumpt - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void InitializeHardwareMonitoring()
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsMotherboardEnabled = true
            };
            computer.Open();
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
                Icon = LoadIconFromResource() ?? SystemIcons.Application,
                Visible = true,
                Text = "Pumpt - Initializing..."
            };

            // Create context menu
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Close", null, OnClose);
            trayIcon.ContextMenuStrip = contextMenu;
        }

        private static void SetupUpdateTimer()
        {
            updateTimer = new Timer
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
                foreach (var hardware in computer.Hardware)
                    hardware.Update();

                // Get CPU temperature (original logic)
                var cpuTemp = computer.Hardware
                    .SelectMany(hw => hw.Sensors)
                    .FirstOrDefault(s => s.SensorType == SensorType.Temperature &&
                               s.Value.HasValue && s.Value.Value > 0 &&
                               (s.Name.Contains("Tctl") || s.Name.Contains("CPU")))?.Value;

                // Update CPU temperature
                if (cpuTemp.HasValue)
                {
                    currentCpuTemp = cpuTemp.Value;
                }

                displayTemp = $"CPU: {currentCpuTemp:F0}°C";
                trayIcon.Text = displayTemp;
            }
            catch (Exception ex)
            {
                displayTemp = "Error reading temperatures";
                trayIcon.Text = $"Pumpt - Error: {ex.Message}";
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

        private static Icon LoadIconFromResource()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("Pumpt.icon.ico");
                return stream != null ? new Icon(stream) : null;
            }
            catch
            {
                return null;
            }
        }

        private static void Cleanup()
        {
            updateTimer?.Stop();
            updateTimer?.Dispose();
            pumpDevice?.CloseDevice();
            pumpDevice?.Dispose();
            trayIcon?.Dispose();
            computer?.Close();
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