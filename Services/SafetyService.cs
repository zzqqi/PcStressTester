using PcStressTester.Models;

namespace PcStressTester.Services;

public sealed class SafetyService
{
    private readonly SafetyLimits _limits;

    public SafetyService(SafetyLimits limits)
    {
        _limits = limits;
    }

    public bool IsOverheat(float? cpuTemp, float? gpuTemp, out string reason)
    {
        if (cpuTemp.HasValue && cpuTemp.Value >= _limits.CpuMaxTemp)
        {
            reason = $"CPU overheating: {cpuTemp.Value:F1} °C";
            return true;
        }

        if (gpuTemp.HasValue && gpuTemp.Value >= _limits.GpuMaxTemp)
        {
            reason = $"GPU overheating: {gpuTemp.Value:F1} °C";
            return true;
        }

        reason = string.Empty;
        return false;
    }
}