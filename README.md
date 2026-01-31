# Husky Glacier HWT700PT Driver

## Overview

The [official HKCF300 | HCF300 driver](https://drive.google.com/file/d/1h8q4DvG9Mrbzw4FKTSndB5N1Rydi-l0O/view) for the **Husky Glacier HWT700PT** water-cooler is outdated, relies on a kernel `.sys` driver, and has known security vulnerabilities. Recently, Windows Defender started flagging it more aggressively. On top of that, it consumes nearly **200 MB of RAM** while running.

Tired of waiting for an updated version, I decided to make a lightweight replacement. This involved reverse-engineering the pump's USB protocol using **Wireshark** and **USBPcap**, which turned out to be surprisingly straightforward.

The app integrates LibreHardwareMonitor to provide accurate CPU temperature readings directly on the pump, consuming around 20 MB of RAM.

I have only tested the driver with the **HWT700PT (360 mm)** model, but the **HW600PT (240 mm)** may also work. The `HKCF300 | HCF300 driver` is used by other models such as the `Ice Comet`, so they might work out of the box. If they don't, support may be as simple as figuring out the correct USB Vendor ID and Product ID for your model, updating the source code, and recompiling it yourself.

## Running

Download a compiled version from the releases page, or build it yourself. It requires administrative privileges to gather sensor data.

To run the app automatically when Windows starts:

1. Open **Task Scheduler**.
2. Create a new task.
3. Set it to **run with highest privileges**.
4. Trigger it **"At log on"** for your user.
5. Point the task to the app's executable.

Requires **.NET 10.0 SDK**:

```powershell
# Run in development mode
dotnet run

# Build self-contained executable with .NET runtime
dotnet publish --self-contained

# Build packaged zip in 'package' folder
dotnet build -t:Package
```

