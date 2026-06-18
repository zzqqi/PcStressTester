using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PcStressTester.Services;

public sealed class CpuStressService
{
    private CancellationTokenSource? _cts;
    private readonly List<Task> _workers = new();
    private readonly object _sync = new();

    private volatile int _currentLoadPercent;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _cts is { IsCancellationRequested: false };
            }
        }
    }

    public int CurrentLoadPercent => _currentLoadPercent;
    public int CurrentWorkerCount { get; private set; }

    public void Start(int loadPercent = 10, int? usedCores = null)
    {
        lock (_sync)
        {
            if (_cts is { IsCancellationRequested: false })
                return;

            _cts = new CancellationTokenSource();
            _workers.Clear();

            int cpuCount = Environment.ProcessorCount;
            int safeLoad = Math.Clamp(loadPercent, 1, 95);
            int safeWorkers = usedCores ?? Math.Max(1, cpuCount / 2);
            safeWorkers = Math.Clamp(safeWorkers, 1, Math.Max(1, cpuCount));

            _currentLoadPercent = safeLoad;
            CurrentWorkerCount = safeWorkers;

            var token = _cts.Token;

            for (int i = 0; i < safeWorkers; i++)
            {
                _workers.Add(Task.Factory.StartNew(
                    () => WorkerAsync(token).GetAwaiter().GetResult(),
                    token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default));
            }
        }
    }

    public void SetLoadPercent(int loadPercent)
    {
        _currentLoadPercent = Math.Clamp(loadPercent, 1, 95);
    }

    /// <summary>
    /// Плавный запуск нагрузки:
    /// startLoadPercent - стартовая нагрузка
    /// maxLoadPercent   - максимальная нагрузка
    /// stepPercent      - шаг увеличения
    /// stepDurationSec  - сколько секунд держать один уровень
    /// usedCores        - сколько потоков нагружать
    /// </summary>
    public void StartGradual(
        int startLoadPercent = 20,
        int maxLoadPercent = 60,
        int stepPercent = 10,
        int stepDurationSec = 10,
        int? usedCores = null)
    {
        lock (_sync)
        {
            if (_cts is { IsCancellationRequested: false })
                return;

            _cts = new CancellationTokenSource();
            _workers.Clear();

            int cpuCount = Environment.ProcessorCount;

            int safeStart = Math.Clamp(startLoadPercent, 5, 80);
            int safeMax = Math.Clamp(maxLoadPercent, safeStart, 90);
            int safeStep = Math.Clamp(stepPercent, 1, 30);
            int safeStepDuration = Math.Clamp(stepDurationSec, 2, 120);

            int safeWorkers = usedCores ?? Math.Max(1, cpuCount / 2);
            safeWorkers = Math.Clamp(safeWorkers, 1, Math.Max(1, cpuCount));

            _currentLoadPercent = safeStart;
            CurrentWorkerCount = safeWorkers;

            var token = _cts.Token;

            for (int i = 0; i < safeWorkers; i++)
            {
                _workers.Add(Task.Factory.StartNew(
                    () => WorkerAsync(token).GetAwaiter().GetResult(),
                    token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default));
            }

            _workers.Add(Task.Factory.StartNew(
                () => RampControllerAsync(
                    token,
                    safeStart,
                    safeMax,
                    safeStep,
                    safeStepDuration).GetAwaiter().GetResult(),
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default));
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _cts?.Cancel();
            _workers.Clear();
            _currentLoadPercent = 0;
            CurrentWorkerCount = 0;
        }
    }

    private async Task RampControllerAsync(
        CancellationToken token,
        int startLoadPercent,
        int maxLoadPercent,
        int stepPercent,
        int stepDurationSec)
    {
        _currentLoadPercent = startLoadPercent;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(stepDurationSec), token);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (token.IsCancellationRequested)
                break;

            int next = _currentLoadPercent + stepPercent;
            if (next > maxLoadPercent)
                next = maxLoadPercent;

            _currentLoadPercent = next;
        }
    }

    private async Task WorkerAsync(CancellationToken token)
    {
        double x = 0.0001;
        const int cycleMs = 100;

        while (!token.IsCancellationRequested)
        {
            int loadPercent = Math.Clamp(_currentLoadPercent, 1, 95);
            int busyMs = cycleMs * loadPercent / 100;
            int idleMs = cycleMs - busyMs;

            int start = Environment.TickCount;

            while (!token.IsCancellationRequested &&
                   Environment.TickCount - start < busyMs)
            {
                for (int i = 1; i < 12000; i++)
                {
                    x += Math.Sqrt(i) * Math.Sin(i) * Math.Cos(x);

                    if (x > 1_000_000 || double.IsNaN(x) || double.IsInfinity(x))
                        x = 0.0001;
                }
            }

            if (idleMs > 0)
            {
                try
                {
                    await Task.Delay(idleMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
