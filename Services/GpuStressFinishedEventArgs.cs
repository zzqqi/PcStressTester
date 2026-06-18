using System;

namespace PcStressTester.Services;

public sealed class GpuStressFinishedEventArgs : EventArgs
{
    public GpuStressFinishedEventArgs(bool completed, string reason)
    {
        Completed = completed;
        Reason = reason;
    }

    public bool Completed { get; }

    public string Reason { get; }
}
