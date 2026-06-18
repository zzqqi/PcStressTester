using System;

namespace PcStressTester.Models;

public class SensorInfo
{
    public string Hardware { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public float? Value { get; set; }
    public DateTime Timestamp { get; set; }
}