using System;
using System.Linq;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

namespace Pumpt
{
    class Program
    {
        static void Main()
        {
            // Check if running as administrator
            if (!IsRunningAsAdministrator())
            {
                Console.WriteLine("═══════════════════════════════════════════════════════════════");
                Console.WriteLine("                  ADMINISTRATOR RIGHTS REQUIRED                  ");
                Console.WriteLine("═══════════════════════════════════════════════════════════════");
                Console.WriteLine();
                Console.WriteLine("This application requires administrator privileges to access");
                Console.WriteLine("hardware sensors.");
                Console.WriteLine();
                Console.WriteLine("Please run this program as Administrator:");
                Console.WriteLine("  1. Right-click on PowerShell");
                Console.WriteLine("  2. Select 'Run as administrator'");
                Console.WriteLine("  3. Navigate to this folder and run 'dotnet run'");
                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsMotherboardEnabled = true
            };
            computer.Open();

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
                    Console.WriteLine($"CPU: {cpuTemp.Value:F1} °C");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                computer?.Close();
            }
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