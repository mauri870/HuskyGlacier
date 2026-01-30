# Husky Glacier HWT700PT App

## Overview

The [official app](https://drive.google.com/file/d/1h8q4DvG9Mrbzw4FKTSndB5N1Rydi-l0O/view) for the **Husky Glacier HWT700PT** cooler is outdated, relies on a `.sys` driver, and contains known security vulnerabilities. It also consumes nearly **200 MB of RAM** while running.

Tired of waiting for an updated version, I decided to fix the problem myself. This involved reverse-engineering the pump's USB protocol to understand how to communicate with the device. Fortunately, this turned out to be relatively straightforward with the help of Wireshark and USBPcap.

This project is a lightweight replacement that uses **LibreHardwareMonitor** to read CPU temperature and update the pump display every second.

I was only able to test the app with the **HWT700PT** model. Other Husky Glacier models, such as the **HW600PT (240 mm)**, may also work. If they do not, support may be as simple as adjusting the USB Vendor ID and Product ID in the source code and recompiling it yourself.

## Running

You need dotnet 10.0 SDK to build and run the app, it also requires administrative privileges to gather sensor data.

```powershell
dotnet run # For development
dotnet publish --self-contained # build + dotnet runtime
```
