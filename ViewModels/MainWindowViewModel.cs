using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using PcStressTester.Models;
using PcStressTester.Services;
using PcStressTester.Views;

namespace PcStressTester.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private const int InitialCpuLoadPercent = 65;
    private const int TargetCpuLoadPercent = 95;
    private const int CpuRampDurationSeconds = 15;
    private static readonly TimeSpan CpuTestDuration = TimeSpan.FromMinutes(3);
    private const int InitialGpuLoadPercent = 100;
    private const int TargetGpuLoadPercent = 100;
    private const int GpuRampDurationSeconds = 1;

    private readonly HardwareMonitorService _hardwareMonitor;
    private readonly CpuStressService _cpuStressService;
    private readonly GpuStressService _gpuStressService;
    private readonly DatabaseService _databaseService;
    private readonly DispatcherTimer _timer;

    private string _status = "Чтение датчиков...";
    private string _cpuTempText = "-";
    private string _cpuLoadText = "-";
    private string _gpuTempText = "-";
    private string _gpuLoadText = "-";
    private bool _isCpuTestRunning;
    private bool _isGpuTestRunning;
    private int _cpuStressLoadPercent;
    private int _gpuStressLoadPercent;
    private string _testStateText = "Тест не запущен";
    private string _testElapsedText = "00:00:00";
    private DateTime? _cpuTestStartTime;
    private DateTime? _gpuTestStartTime;
    private float? _cpuMaxTemp;
    private float? _cpuMaxLoad;
    private double _cpuLoadSum;
    private int _cpuLoadSamples;
    private long? _currentTestRunId;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SensorInfo> Sensors { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand StartCpuTestCommand { get; }
    public ICommand StartGpuTestCommand { get; }
    public ICommand StopTestCommand { get; }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string CpuTempText
    {
        get => _cpuTempText;
        set => SetField(ref _cpuTempText, value);
    }

    public string CpuLoadText
    {
        get => _cpuLoadText;
        set => SetField(ref _cpuLoadText, value);
    }

    public string GpuTempText
    {
        get => _gpuTempText;
        set => SetField(ref _gpuTempText, value);
    }

    public string GpuLoadText
    {
        get => _gpuLoadText;
        set => SetField(ref _gpuLoadText, value);
    }

    public bool IsCpuTestRunning
    {
        get => _isCpuTestRunning;
        set
        {
            if (SetField(ref _isCpuTestRunning, value))
            {
                OnPropertyChanged(nameof(TestBannerText));
                OnPropertyChanged(nameof(TestBannerOpacity));
                OnPropertyChanged(nameof(TestProgressIsIndeterminate));
                OnPropertyChanged(nameof(IsAnyTestRunning));
                OnPropertyChanged(nameof(ActiveStressTitle));
                OnPropertyChanged(nameof(ActiveStressDescription));
                OnPropertyChanged(nameof(ActiveStressLoadPercent));
                OnPropertyChanged(nameof(IsCpuStressPreviewVisible));
                OnPropertyChanged(nameof(IsGpuStressPreviewVisible));
            }
        }
    }

    public bool IsGpuTestRunning
    {
        get => _isGpuTestRunning;
        set
        {
            if (SetField(ref _isGpuTestRunning, value))
            {
                OnPropertyChanged(nameof(TestBannerText));
                OnPropertyChanged(nameof(TestBannerOpacity));
                OnPropertyChanged(nameof(TestProgressIsIndeterminate));
                OnPropertyChanged(nameof(IsAnyTestRunning));
                OnPropertyChanged(nameof(ActiveStressTitle));
                OnPropertyChanged(nameof(ActiveStressDescription));
                OnPropertyChanged(nameof(ActiveStressLoadPercent));
                OnPropertyChanged(nameof(IsCpuStressPreviewVisible));
                OnPropertyChanged(nameof(IsGpuStressPreviewVisible));
            }
        }
    }

    public bool IsAnyTestRunning => IsCpuTestRunning || IsGpuTestRunning;

    public int CpuStressLoadPercent
    {
        get => _cpuStressLoadPercent;
        set
        {
            if (SetField(ref _cpuStressLoadPercent, value))
                OnPropertyChanged(nameof(ActiveStressLoadPercent));
        }
    }

    public int GpuStressLoadPercent
    {
        get => _gpuStressLoadPercent;
        set
        {
            if (SetField(ref _gpuStressLoadPercent, value))
                OnPropertyChanged(nameof(ActiveStressLoadPercent));
        }
    }

    public string ActiveStressTitle
    {
        get
        {
            if (IsCpuTestRunning)
                return "Стресс-тест CPU";

            if (IsGpuTestRunning)
                return "Стресс-тест GPU";

            return "Стресс-тест";
        }
    }

    public string ActiveStressDescription
    {
        get
        {
            if (IsCpuTestRunning)
                return "Визуализация нагрузки процессора: активные ячейки показывают текущую целевую нагрузку CPU";

            if (IsGpuTestRunning)
                return "Предпросмотр интенсивности и текущего уровня графической нагрузки";

            return "Запустите CPU или GPU тест, чтобы увидеть активную визуализацию нагрузки";
        }
    }

    public int ActiveStressLoadPercent => IsCpuTestRunning ? CpuStressLoadPercent : GpuStressLoadPercent;

    public bool IsCpuStressPreviewVisible => IsCpuTestRunning;

    public bool IsGpuStressPreviewVisible => !IsCpuTestRunning;

    public string TestStateText
    {
        get => _testStateText;
        set => SetField(ref _testStateText, value);
    }

    public string TestElapsedText
    {
        get => _testElapsedText;
        set => SetField(ref _testElapsedText, value);
    }

    public string TestBannerText
    {
        get
        {
            if (IsCpuTestRunning)
                return "ИДЕТ ТЕСТ CPU";

            if (IsGpuTestRunning)
                return "ИДЕТ ТЕСТ GPU";

            return "ТЕСТ ОСТАНОВЛЕН";
        }
    }

    public double TestBannerOpacity => IsAnyTestRunning ? 1.0 : 0.65;

    public bool TestProgressIsIndeterminate => IsAnyTestRunning;

    public MainWindowViewModel()
    {
        _hardwareMonitor = new HardwareMonitorService();
        _cpuStressService = new CpuStressService();
        _gpuStressService = new GpuStressService();
        _gpuStressService.Finished += OnGpuStressFinished;
        _databaseService = new DatabaseService();

        RefreshCommand = new RelayCommand(UpdateSensors);
        StartCpuTestCommand = new RelayCommand(StartCpuTest);
        StartGpuTestCommand = new RelayCommand(StartGpuTest);
        StopTestCommand = new RelayCommand(StopTest);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => UpdateSensors();
        _timer.Start();

        UpdateSensors();
    }

    private void OnGpuStressFinished(object? sender, GpuStressFinishedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_gpuTestStartTime.HasValue)
                return;

            FinishCurrentTestRun(e.Completed ? "Завершен" : "Остановлен", e.Reason);
            IsGpuTestRunning = false;
            _gpuTestStartTime = null;
            GpuStressLoadPercent = 0;
            TestElapsedText = "00:00:00";
            TestStateText = e.Reason;
            Status = e.Reason;
        });
    }

    private void StartCpuTest()
    {
        if (_cpuStressService.IsRunning || _gpuStressService.IsRunning)
            return;

        int recommendedCores = Math.Max(1, Environment.ProcessorCount);
        ResetCpuMetrics();

        _cpuStressService.Start(loadPercent: InitialCpuLoadPercent, usedCores: recommendedCores);
        CpuStressLoadPercent = _cpuStressService.CurrentLoadPercent;

        _cpuTestStartTime = DateTime.Now;
        _currentTestRunId = _databaseService.StartTestRun("CPU", TargetCpuLoadPercent);
        IsCpuTestRunning = true;
        TestStateText = $"Стресс-тест CPU выполняется ({_cpuStressService.CurrentLoadPercent}% -> {TargetCpuLoadPercent}% / потоков: {_cpuStressService.CurrentWorkerCount})";
        Status = TestStateText;
        UpdateElapsedTime();
    }

    private void StartGpuTest()
    {
        if (_gpuStressService.IsRunning || _cpuStressService.IsRunning)
            return;

        _gpuStressService.Start(InitialGpuLoadPercent);
        GpuStressLoadPercent = _gpuStressService.CurrentLoadPercent;
        CpuStressLoadPercent = 0;

        _gpuTestStartTime = DateTime.Now;
        _currentTestRunId = _databaseService.StartTestRun("GPU", TargetGpuLoadPercent);
        IsGpuTestRunning = true;
        TestStateText = $"Стресс-тест GPU запущен: рост нагрузки {GpuStressLoadPercent}% -> {TargetGpuLoadPercent}%";
        Status = TestStateText;
        UpdateElapsedTime();
    }

    private void StopTest()
    {
        _cpuStressService.Stop();
        _gpuStressService.Stop();
        FinishCurrentTestRun("Остановлен", "Тест остановлен пользователем.");
        IsCpuTestRunning = false;
        IsGpuTestRunning = false;
        _cpuTestStartTime = null;
        _gpuTestStartTime = null;
        ResetCpuMetrics();
        CpuStressLoadPercent = 0;
        GpuStressLoadPercent = 0;
        TestElapsedText = "00:00:00";
        TestStateText = "Тест остановлен";
        Status = "Тест остановлен";
    }

    private void UpdateSensors()
    {
        try
        {
            var sensors = _hardwareMonitor.ReadSensors();

            Sensors.Clear();
            foreach (var sensor in sensors.OrderBy(s => s.Hardware).ThenBy(s => s.Type).ThenBy(s => s.Name))
            {
                Sensors.Add(sensor);
            }

            var cpuTemp = FindCpuTemperature(sensors);
            var cpuLoad = FindCpuLoad(sensors);
            var cpuClock = FindCpuClock(sensors);
            var gpuTemp = FindGpuTemperature(sensors);
            var gpuLoad = FindGpuLoad(sensors);
            var ramUsedGb = FindRamUsedGb(sensors);

            CpuTempText = cpuTemp.HasValue ? $"{cpuTemp.Value:F1} C" : "-";
            CpuLoadText = cpuLoad.HasValue ? $"{cpuLoad.Value:F1} %" : "-";
            GpuTempText = gpuTemp.HasValue ? $"{gpuTemp.Value:F1} C" : "-";
            GpuLoadText = gpuLoad.HasValue ? $"{gpuLoad.Value:F1} %" : "-";

            IsCpuTestRunning = _cpuStressService.IsRunning;
            IsGpuTestRunning = _gpuStressService.IsRunning;
            CpuStressLoadPercent = _cpuStressService.CurrentLoadPercent;
            GpuStressLoadPercent = _gpuStressService.CurrentLoadPercent;
            UpdateElapsedTime();

            if (IsCpuTestRunning)
                UpdateCpuMetrics(cpuTemp, cpuLoad);

            // Автоматическая остановка при перегреве.
            if (IsCpuTestRunning && cpuTemp.HasValue && cpuTemp.Value >= 85f)
            {
                FinishCpuTest(true, $"Тест CPU остановлен из-за перегрева: {cpuTemp.Value:F1} C");
                TestStateText = $"Аварийная остановка: CPU {cpuTemp.Value:F1} C";
                Status = TestStateText;
                return;
            }

            if (gpuTemp.HasValue && gpuTemp.Value >= 85f)
            {
                StopRunningTest();
                TestStateText = $"Аварийная остановка: GPU {gpuTemp.Value:F1} C";
                Status = TestStateText;
                FinishCurrentTestRun("Остановлен", TestStateText);
                SaveCurrentSnapshot(sensors, cpuTemp, cpuLoad, cpuClock, gpuTemp, gpuLoad, ramUsedGb);
                return;
            }

            if (IsGpuTestRunning)
            {
                UpdateGpuRamp();
                TestStateText = $"Стресс-тест GPU выполняется ({GpuStressLoadPercent}% -> {TargetGpuLoadPercent}%)";
                Status = $"Стресс-тест GPU выполняется | обновлено: {DateTime.Now:HH:mm:ss}";
            }
            else if (IsCpuTestRunning)
            {
                UpdateCpuRamp();
                if (_cpuTestStartTime.HasValue && DateTime.Now - _cpuTestStartTime.Value >= CpuTestDuration)
                {
                    FinishCpuTest(true, "Тест CPU успешно завершен.");
                    return;
                }
                TestStateText = $"Стресс-тест CPU выполняется ({_cpuStressService.CurrentLoadPercent}% -> {TargetCpuLoadPercent}% / потоков: {_cpuStressService.CurrentWorkerCount})";
                Status = $"Стресс-тест CPU выполняется | обновлено: {DateTime.Now:HH:mm:ss}";
            }
            else
            {
                if (_cpuTestStartTime is null && _gpuTestStartTime is null)
                    TestElapsedText = "00:00:00";

                Status = $"Датчики обновлены: {DateTime.Now:HH:mm:ss}";
            }

            SaveCurrentSnapshot(sensors, cpuTemp, cpuLoad, cpuClock, gpuTemp, gpuLoad, ramUsedGb);
        }
        catch (Exception ex)
        {
            Status = $"Ошибка чтения датчиков: {ex.Message}";
        }
    }

    private void ResetCpuMetrics()
    {
        _cpuMaxTemp = null;
        _cpuMaxLoad = null;
        _cpuLoadSum = 0;
        _cpuLoadSamples = 0;
    }

    private void UpdateCpuMetrics(float? cpuTemp, float? cpuLoad)
    {
        if (cpuTemp.HasValue)
            _cpuMaxTemp = !_cpuMaxTemp.HasValue ? cpuTemp.Value : Math.Max(_cpuMaxTemp.Value, cpuTemp.Value);

        if (cpuLoad.HasValue)
        {
            _cpuMaxLoad = !_cpuMaxLoad.HasValue ? cpuLoad.Value : Math.Max(_cpuMaxLoad.Value, cpuLoad.Value);
            _cpuLoadSum += cpuLoad.Value;
            _cpuLoadSamples++;
        }
    }

    private void FinishCpuTest(bool showResults, string reason)
    {
        var elapsed = _cpuTestStartTime.HasValue
            ? DateTime.Now - _cpuTestStartTime.Value
            : TimeSpan.Zero;

        if (elapsed > CpuTestDuration)
            elapsed = CpuTestDuration;

        double averageCpuLoad = _cpuLoadSamples > 0 ? _cpuLoadSum / _cpuLoadSamples : 0;
        int workerCount = _cpuStressService.CurrentWorkerCount;

        _cpuStressService.Stop();
        IsCpuTestRunning = false;
        if (_currentTestRunId.HasValue)
        {
            _databaseService.SaveTestMetric(_currentTestRunId.Value, "Длительность", elapsed.TotalSeconds, "секунды");
            if (_cpuMaxTemp.HasValue)
                _databaseService.SaveTestMetric(_currentTestRunId.Value, "Максимальная температура CPU", _cpuMaxTemp.Value, "C");
            if (_cpuMaxLoad.HasValue)
                _databaseService.SaveTestMetric(_currentTestRunId.Value, "Пиковая загрузка CPU", _cpuMaxLoad.Value, "%");
            _databaseService.SaveTestMetric(_currentTestRunId.Value, "Средняя загрузка CPU", averageCpuLoad, "%");
            _databaseService.SaveTestMetric(_currentTestRunId.Value, "Количество потоков", workerCount);
            FinishCurrentTestRun(showResults ? "Завершен" : "Остановлен", reason);
        }

        _cpuTestStartTime = null;
        CpuStressLoadPercent = 0;
        TestElapsedText = "00:00:00";
        TestStateText = reason;
        Status = reason;

        if (showResults)
        {
            TestResultForm.ShowResult(
                null,
                "Результаты теста CPU",
                reason,
                new List<(string Label, string Value)>
                {
                    ("Длительность", elapsed.ToString(@"mm\:ss")),
                    ("Максимальная температура CPU", FormatCpuMetric(_cpuMaxTemp, "C")),
                    ("Пиковая загрузка CPU", FormatCpuMetric(_cpuMaxLoad, "%")),
                    ("Средняя загрузка CPU", $"{averageCpuLoad:F1} %"),
                    ("Использовано потоков", workerCount.ToString())
                },
                System.Drawing.Color.FromArgb(130, 255, 170));
        }

        ResetCpuMetrics();
    }

    private void StopRunningTest()
    {
        _cpuStressService.Stop();
        _gpuStressService.Stop();
        IsCpuTestRunning = false;
        IsGpuTestRunning = false;
        _cpuTestStartTime = null;
        _gpuTestStartTime = null;
        ResetCpuMetrics();
        CpuStressLoadPercent = 0;
        GpuStressLoadPercent = 0;
        TestElapsedText = "00:00:00";
    }

    private void FinishCurrentTestRun(string status, string summary)
    {
        if (!_currentTestRunId.HasValue)
            return;

        _databaseService.FinishTestRun(_currentTestRunId.Value, status, summary);
        _currentTestRunId = null;
    }

    private void SaveCurrentSnapshot(
        List<SensorInfo> sensors,
        float? cpuTemp,
        float? cpuLoad,
        float? cpuClock,
        float? gpuTemp,
        float? gpuLoad,
        float? ramUsedGb)
    {
        _databaseService.SaveSensorSnapshot(
            _currentTestRunId,
            new TestLogEntry
            {
                Time = DateTime.Now,
                CpuTemp = cpuTemp,
                CpuLoad = cpuLoad,
                CpuClock = cpuClock,
                GpuTemp = gpuTemp,
                GpuLoad = gpuLoad,
                RamUsedGb = ramUsedGb,
                Status = Status
            },
            sensors);
    }

    private void UpdateCpuRamp()
    {
        if (!_cpuTestStartTime.HasValue)
            return;

        var elapsedSeconds = Math.Max(0, (DateTime.Now - _cpuTestStartTime.Value).TotalSeconds);
        var progress = Math.Min(1.0, elapsedSeconds / CpuRampDurationSeconds);
        int rampedLoad = InitialCpuLoadPercent +
                         (int)Math.Round((TargetCpuLoadPercent - InitialCpuLoadPercent) * progress);

        _cpuStressService.SetLoadPercent(rampedLoad);
        CpuStressLoadPercent = _cpuStressService.CurrentLoadPercent;
    }

    private void UpdateGpuRamp()
    {
        if (!_gpuTestStartTime.HasValue)
            return;

        var elapsedSeconds = Math.Max(0, (DateTime.Now - _gpuTestStartTime.Value).TotalSeconds);
        var progress = Math.Min(1.0, elapsedSeconds / GpuRampDurationSeconds);
        int rampedLoad = InitialGpuLoadPercent +
                         (int)Math.Round((TargetGpuLoadPercent - InitialGpuLoadPercent) * progress);

        _gpuStressService.SetLoadPercent(rampedLoad);
        GpuStressLoadPercent = _gpuStressService.CurrentLoadPercent;
    }

    private void UpdateElapsedTime()
    {
        if (IsCpuTestRunning && _cpuTestStartTime.HasValue)
        {
            var elapsed = DateTime.Now - _cpuTestStartTime.Value;
            if (elapsed > CpuTestDuration)
                elapsed = CpuTestDuration;

            TestElapsedText = $"{elapsed:hh\\:mm\\:ss} / {CpuTestDuration:hh\\:mm\\:ss}";
        }
        else if (IsGpuTestRunning && _gpuTestStartTime.HasValue)
        {
            var elapsed = DateTime.Now - _gpuTestStartTime.Value;
            TestElapsedText = elapsed.ToString(@"hh\:mm\:ss");
        }
    }

    private static float? FindCpuTemperature(List<SensorInfo> sensors)
    {
        var cpuTemps = sensors
            .Where(s => s.Type.Equals("Temperature", StringComparison.OrdinalIgnoreCase))
            .Where(IsCpuSensor)
            .Where(HasRealValue)
            .ToList();

        var preferred = cpuTemps.FirstOrDefault(s =>
            ContainsAny(s.Name, "CPU Package", "Package", "Tctl/Tdie", "Core Average", "CCD"));

        return preferred?.Value ?? cpuTemps.FirstOrDefault()?.Value;
    }

    private static float? FindCpuLoad(List<SensorInfo> sensors)
    {
        var cpuLoads = sensors
            .Where(s => s.Type.Equals("Load", StringComparison.OrdinalIgnoreCase))
            .Where(IsCpuSensor)
            .Where(HasRealValue)
            .ToList();

        var preferred = cpuLoads.FirstOrDefault(s =>
            ContainsAny(s.Name, "CPU Total", "Total", "Package"));

        return preferred?.Value ?? cpuLoads.FirstOrDefault()?.Value;
    }

    private static float? FindCpuClock(List<SensorInfo> sensors)
    {
        var cpuClocks = sensors
            .Where(s => s.Type.Equals("Clock", StringComparison.OrdinalIgnoreCase))
            .Where(IsCpuSensor)
            .Where(HasRealValue)
            .ToList();

        var preferred = cpuClocks.FirstOrDefault(s =>
            ContainsAny(s.Name, "CPU Core #1", "Core #1", "Core Average", "Bus Speed"));

        return preferred?.Value ?? cpuClocks.FirstOrDefault()?.Value;
    }

    private static float? FindRamUsedGb(List<SensorInfo> sensors)
    {
        var memorySensors = sensors
            .Where(s => s.Type.Equals("Data", StringComparison.OrdinalIgnoreCase))
            .Where(s => ContainsAny(s.Hardware, "memory", "ram") || ContainsAny(s.Name, "memory", "used", "ram"))
            .Where(HasRealValue)
            .ToList();

        var preferred = memorySensors.FirstOrDefault(s =>
            ContainsAny(s.Name, "Memory Used", "Used Memory", "Used"));

        return preferred?.Value ?? memorySensors.FirstOrDefault()?.Value;
    }

    private static float? FindGpuTemperature(List<SensorInfo> sensors)
    {
        var gpuTemps = sensors
            .Where(s => s.Type.Equals("Temperature", StringComparison.OrdinalIgnoreCase))
            .Where(IsGpuSensor)
            .Where(HasRealValue)
            .ToList();

        var preferred = gpuTemps.FirstOrDefault(s =>
            ContainsAny(s.Name, "GPU Core", "Core", "Edge"));

        if (preferred is not null)
            return preferred.Value;

        var secondary = gpuTemps.FirstOrDefault(s =>
            ContainsAny(s.Name, "Hot Spot", "Hotspot", "Memory", "Mem"));

        if (secondary is not null)
            return secondary.Value;

        return gpuTemps.FirstOrDefault()?.Value;
    }

    private static float? FindGpuLoad(List<SensorInfo> sensors)
    {
        var gpuLoads = sensors
            .Where(s => s.Type.Equals("Load", StringComparison.OrdinalIgnoreCase))
            .Where(IsGpuSensor)
            .Where(HasRealValue)
            .ToList();

        var preferred = gpuLoads.FirstOrDefault(s =>
            ContainsAny(s.Name, "GPU Core", "Core", "D3D 3D", "3D", "GPU"));

        return preferred?.Value ?? gpuLoads.FirstOrDefault()?.Value;
    }

    private static bool IsCpuSensor(SensorInfo sensor)
    {
        return ContainsAny(sensor.Hardware, "cpu", "intel", "amd", "ryzen", "core i", "threadripper") ||
               ContainsAny(sensor.Name, "cpu", "package", "core", "ccd", "tctl", "tdie");
    }

    private static bool IsGpuSensor(SensorInfo sensor)
    {
        return ContainsAny(sensor.Hardware, "gpu", "graphics", "nvidia", "geforce", "amd radeon", "radeon", "intel arc") ||
               ContainsAny(sensor.Name, "gpu", "graphics", "hot spot", "hotspot", "vram", "memory", "edge");
    }

    private static bool HasRealValue(SensorInfo sensor)
    {
        return sensor.Value.HasValue && sensor.Value.Value > 0.1f;
    }

    private static bool ContainsAny(string? text, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var part in parts)
        {
            if (text.Contains(part, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string FormatCpuMetric(float? value, string unit)
    {
        return value.HasValue ? $"{value.Value:F1} {unit}" : "-";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}

