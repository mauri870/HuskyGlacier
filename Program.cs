using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace Pumpt
{
    class Program
    {
        static void Main()
        {
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
    }
}