using System;
using System.Linq;
using System.Security.Principal;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace Pumpt
{
    class Program
    {
        private static NotifyIcon trayIcon;
        private static Computer computer;
        private static System.Windows.Forms.Timer updateTimer;
        private static string currentTemp = "N/A";

        [STAThread]
        static void Main()
        {
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

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Initialize hardware monitoring
            InitializeHardwareMonitoring();

            // Create system tray icon
            CreateTrayIcon();

            // Setup timer for temperature updates
            SetupUpdateTimer();

            // Initial temperature reading
            UpdateTemperature();

            // Run the application
            Application.Run();

            // Cleanup
            Cleanup();
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

        private static void CreateTrayIcon()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = LoadIconFromResource() ?? SystemIcons.Application,
                Visible = true,
                Text = "CPU Temperature Monitor"
            };

            // Create context menu
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Close", null, OnClose);
            trayIcon.ContextMenuStrip = contextMenu;
        }

        private static void SetupUpdateTimer()
        {
            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = 5000; // 5 seconds
            updateTimer.Tick += OnTimerTick;
            updateTimer.Start();
        }

        private static void OnTimerTick(object sender, EventArgs e)
        {
            UpdateTemperature();
        }

        private static void UpdateTemperature()
        {
            try
            {
                foreach (var hardware in computer.Hardware)
                    hardware.Update();

                var cpuTemp = computer.Hardware
                    .SelectMany(hw => hw.Sensors)
                    .FirstOrDefault(s => s.SensorType == SensorType.Temperature &&
                               s.Value.HasValue && s.Value.Value > 0 &&
                               (s.Name.Contains("Tctl") || s.Name.Contains("CPU")))?.Value;

                if (cpuTemp.HasValue)
                {
                    currentTemp = $"{cpuTemp.Value:F1} °C";
                }
                else
                {
                    currentTemp = "N/A";
                }

                // Update tray icon tooltip
                trayIcon.Text = $"CPU: {currentTemp}";
            }
            catch (Exception ex)
            {
                currentTemp = "Error";
                trayIcon.Text = $"CPU Temperature Monitor - Error: {ex.Message}";
            }
        }

        private static void OnClose(object sender, EventArgs e)
        {
            Application.Exit();
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