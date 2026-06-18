using System;
using System.Globalization;
using System.IO;
using System.Text;
using PcStressTester.Models;

namespace PcStressTester.Services;

public sealed class LoggingService
{
    private readonly string _filePath;
    private readonly object _sync = new();

    public LoggingService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "stress_log.csv");
        EnsureFileExists();
    }

    private void EnsureFileExists()
    {
        if (File.Exists(_filePath))
            return;

        var header = "Time,CpuTemp,CpuLoad,CpuClock,GpuTemp,GpuLoad,RamUsedGb,Status" + Environment.NewLine;
        File.WriteAllText(_filePath, header, Encoding.UTF8);
    }

    public void Write(TestLogEntry entry)
    {
        var line = string.Join(",",
            entry.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Format(entry.CpuTemp),
            Format(entry.CpuLoad),
            Format(entry.CpuClock),
            Format(entry.GpuTemp),
            Format(entry.GpuLoad),
            Format(entry.RamUsedGb),
            Escape(entry.Status));

        lock (_sync)
        {
            File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    public string GetFilePath() => _filePath;

    private static string Format(float? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }
}