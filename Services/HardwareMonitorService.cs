using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;
using PcStressTester.Models;

namespace PcStressTester.Services;

public sealed class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private bool _disposed;

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsControllerEnabled = true
        };

        _computer.Open();
    }

    public List<SensorInfo> ReadSensors()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HardwareMonitorService));

        var sensors = new List<SensorInfo>();

        foreach (var hardware in _computer.Hardware)
        {
            if (hardware is null)
                continue;

            try
            {
                UpdateHardwareRecursive(hardware, sensors);
            }
            catch
            {
                // Some drivers expose unstable hardware nodes; skip them and keep the UI responsive.
            }
        }

        return sensors;
    }

    private static void UpdateHardwareRecursive(IHardware hardware, List<SensorInfo> sensors)
    {
        if (hardware is null)
            return;

        try
        {
            hardware.Update();
        }
        catch
        {
            return;
        }

        foreach (var sensor in hardware.Sensors ?? Array.Empty<ISensor>())
        {
            if (sensor is null)
                continue;

            try
            {
                sensors.Add(new SensorInfo
                {
                    Hardware = hardware.Name ?? "Unknown Hardware",
                    Name = sensor.Name ?? "Unknown Sensor",
                    Type = sensor.SensorType.ToString(),
                    Value = sensor.Value,
                    Timestamp = DateTime.Now
                });
            }
            catch
            {
                // Ignore a single broken sensor and continue reading the rest.
            }
        }

        foreach (var subHardware in hardware.SubHardware ?? Array.Empty<IHardware>())
        {
            if (subHardware is null)
                continue;

            try
            {
                UpdateHardwareRecursive(subHardware, sensors);
            }
            catch
            {
                // Ignore a broken child node and continue.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _computer.Close();
        _disposed = true;
    }
}
