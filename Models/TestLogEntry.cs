using System;

namespace PcStressTester.Models;

public class TestLogEntry
{
    public DateTime Time { get; set; }
    public float? CpuTemp { get; set; }
    public float? CpuLoad { get; set; }
    public float? CpuClock { get; set; }
    public float? GpuTemp { get; set; }
    public float? GpuLoad { get; set; }
    public float? RamUsedGb { get; set; }
    public string Status { get; set; } = string.Empty;
}