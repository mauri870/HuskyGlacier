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
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        private static NotifyIcon trayIcon;
        private static Computer computer;
        private static System.Windows.Forms.Timer updateTimer;
        private static List<HidDevice> pumpDevices = new List<HidDevice>(); // Try multiple devices

        // Temperature values
        private static float currentCpuTemp = 0;
        private static string displayTemp = "N/A";

        [STAThread]
        static void Main()
        {
            try
            {
                // Allocate console window for debug output
                AllocConsole();
                Console.WriteLine("DEBUG: Console allocated, starting Pumpt application...");

                // Check if running as administrator
                if (!IsRunningAsAdministrator())
                {
                    MessageBox.Show(
                        "This application requires administrator privileges to access hardware sensors.\n\n" +
                        "Please run this program as Administrator.",
                        "Administrator Rights Required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                Console.WriteLine("DEBUG: Administrator check passed");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Console.WriteLine("DEBUG: Initializing hardware monitoring...");
                // Initialize hardware monitoring
                InitializeHardwareMonitoring();

                Console.WriteLine("DEBUG: Initializing HID device...");
                // Initialize HID device
                InitializeHidDevice();

                Console.WriteLine("DEBUG: Creating tray icon...");
                // Create system tray icon
                CreateTrayIcon();

                Console.WriteLine("DEBUG: Setting up update timer...");
                // Setup timer for temperature updates
                SetupUpdateTimer();

                Console.WriteLine("DEBUG: Reading initial temperatures...");
                // Initial temperature reading
                UpdateTemperatures();

                Console.WriteLine("DEBUG: Starting application loop...");
                // Run the application
                Application.Run();

                Console.WriteLine("DEBUG: Application shutting down...");
                // Cleanup
                Cleanup();
            }
            catch (Exception ex)
            {
                string errorMsg = $"Fatal error during startup:\n\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}";
                Console.WriteLine($"FATAL ERROR: {errorMsg}");
                MessageBox.Show(errorMsg, "Pumpt - Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                Console.WriteLine("DEBUG: Connecting to water cooler (VID=0xAA88, PID=0x8666)...");

                var devices = HidDevices.Enumerate(0xAA88, 0x8666);
                var pumpDevice = devices.FirstOrDefault();

                if (pumpDevice != null)
                {
                    pumpDevice.OpenDevice();
                    if (pumpDevice.IsOpen)
                    {
                        Console.WriteLine("DEBUG: Water cooler connected successfully!");
                        pumpDevices.Add(pumpDevice);
                        ShowTrayMessage("Water Cooler Connected", "Temperature monitoring active", ToolTipIcon.Info);
                        return;
                    }
                    else
                    {
                        Console.WriteLine("DEBUG: Could not open water cooler device - may be in use by vendor software");
                    }
                }
                else
                {
                    Console.WriteLine("DEBUG: Water cooler device not found - check USB connection");
                }

                ShowTrayMessage("Water Cooler Not Found", "Device not found. Check USB connection and close vendor software.", ToolTipIcon.Warning);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: Error connecting to water cooler: {ex.Message}");
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
            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = 1000; // 1 second - as per specification
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

                trayIcon.Text = $"CPU: {currentCpuTemp:F0}°C";
            }
            catch (Exception ex)
            {
                displayTemp = "Error reading temperatures";
                trayIcon.Text = $"Pumpt - Error: {ex.Message}";
            }
        }

        private static void SendTemperatureToPump()
        {
            if (pumpDevices.Count == 0)
            {
                Console.WriteLine("DEBUG: No pump devices open, skipping packet send");
                return;
            }

            try
            {
                // Build HID report - CPU temp is in BYTE 3, not byte 0!
                // Packet 1: 32 22 00 00 24 11 00 00 00 00 
                // Packet 2: 2f 22 00 32 00 05 05 b2 00 2c (0x32 in byte 3 = 50°C)
                byte[] reportData = new byte[10];

                // Use packet 1 structure - try CPU temp in byte 1 instead of byte 3
                reportData[0] = 0x32; // Constant from real app packet 1
                reportData[1] = (byte)Math.Round(currentCpuTemp); // Try CPU temperature HERE! (was 0x22 = 34°C)
                reportData[2] = 0x00; // Constant
                reportData[3] = 0x00; // Constant (back to 0x00)
                reportData[4] = 0x24; // Constant from real app
                reportData[5] = 0x11; // Constant from real app
                reportData[6] = 0x00; // Constant
                reportData[7] = 0x00; // Constant
                reportData[8] = 0x00; // Constant
                reportData[9] = 0x00; // Constant

                // Log the packet being sent
                string hexPacket = BitConverter.ToString(reportData).Replace("-", " ");
                Console.WriteLine($"DEBUG: Broadcasting packet to {pumpDevices.Count} devices: {hexPacket}");
                Console.WriteLine($"DEBUG: CPU temp in byte 1: {reportData[1]}°C ({currentCpuTemp:F1})");

                // Send to ALL opened devices
                int successCount = 0;
                var devicesToRemove = new List<HidDevice>();

                foreach (var device in pumpDevices)
                {
                    try
                    {
                        if (device.IsOpen)
                        {
                            // Create HidReport and send
                            var report = new HidReport(10, new HidDeviceData(reportData, HidDeviceData.ReadStatus.Success));
                            bool success = device.WriteReport(report);

                            if (success)
                            {
                                successCount++;
                                Console.WriteLine($"DEBUG: Successfully sent to VID=0x{device.Attributes.VendorId:X4}, PID=0x{device.Attributes.ProductId:X4}");
                            }
                            else
                            {
                                Console.WriteLine($"DEBUG: Failed to send to VID=0x{device.Attributes.VendorId:X4}, PID=0x{device.Attributes.ProductId:X4}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"DEBUG: Device VID=0x{device.Attributes.VendorId:X4}, PID=0x{device.Attributes.ProductId:X4} is no longer open");
                            devicesToRemove.Add(device);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"DEBUG: Error sending to device VID=0x{device.Attributes.VendorId:X4}, PID=0x{device.Attributes.ProductId:X4}: {ex.Message}");
                        devicesToRemove.Add(device);
                    }
                }

                // Remove failed devices
                foreach (var device in devicesToRemove)
                {
                    pumpDevices.Remove(device);
                    device?.Dispose();
                }

                Console.WriteLine($"DEBUG: Broadcast complete. Sent to {successCount}/{pumpDevices.Count} devices");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Exception in SendTemperatureToPump: {ex.Message}");
                Console.WriteLine($"ERROR: Stack trace: {ex.StackTrace}");
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
                using (var stream = assembly.GetManifestResourceStream("Pumpt.icon.ico"))
                {
                    return stream != null ? new Icon(stream) : null;
                }
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

            foreach (var device in pumpDevices)
            {
                device?.CloseDevice();
                device?.Dispose();
            }
            pumpDevices.Clear();

            trayIcon?.Dispose();
            computer?.Close();
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool IsRunningAsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}