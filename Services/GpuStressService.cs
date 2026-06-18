using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PcStressTester.Views;

namespace PcStressTester.Services;

public sealed class GpuStressService
{
    private readonly object _sync = new();
    private bool _isRunning;
    private bool _maximumLoadMode;
    private int _currentLoadPercent;
    private readonly List<GpuStressForm> _forms = new();
    private readonly List<Thread> _uiThreads = new();

    public event EventHandler<GpuStressFinishedEventArgs>? Finished;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _isRunning;
            }
        }
    }

    public int CurrentLoadPercent
    {
        get
        {
            lock (_sync)
            {
                return _currentLoadPercent;
            }
        }
        private set
        {
            lock (_sync)
            {
                _currentLoadPercent = value;
            }
        }
    }

    public bool MaximumLoadMode
    {
        get
        {
            lock (_sync)
            {
                return _maximumLoadMode;
            }
        }
    }

    public void Start(int loadPercent = 10)
    {
        int clampedLoad;

        lock (_sync)
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _maximumLoadMode = false;
            clampedLoad = Math.Clamp(loadPercent, 10, 100);
            CurrentLoadPercent = clampedLoad;
        }

        StartStressWindow(clampedLoad);
    }

    public void SetLoadPercent(int loadPercent)
    {
        int clamped;
        List<GpuStressForm> forms;

        lock (_sync)
        {
            if (!_isRunning)
                return;

            clamped = Math.Clamp(loadPercent, 10, 100);
            CurrentLoadPercent = clamped;
            forms = _forms.ToList();
        }

        foreach (var form in forms)
        {
            if (form.IsDisposed)
                continue;

            try
            {
                if (form.InvokeRequired)
                    form.BeginInvoke(new Action(() => form.SetIntensity(clamped)));
                else
                    form.SetIntensity(clamped);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    public void Stop()
    {
        List<GpuStressForm> formsToClose;

        lock (_sync)
        {
            _isRunning = false;
            _maximumLoadMode = false;
            CurrentLoadPercent = 0;
            formsToClose = _forms.ToList();
            _forms.Clear();
        }

        foreach (var form in formsToClose)
        {
            if (form.IsDisposed)
                continue;

            try
            {
                if (form.InvokeRequired)
                    form.BeginInvoke(new Action(form.Close));
                else
                    form.Close();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    public void SetMaximumLoadMode(bool enabled)
    {
        List<GpuStressForm> forms;

        lock (_sync)
        {
            _maximumLoadMode = enabled;
            forms = _forms.ToList();
        }

        foreach (var form in forms)
        {
            if (form.IsDisposed)
                continue;

            try
            {
                if (form.InvokeRequired)
                    form.BeginInvoke(new Action(() => form.SetMaximumLoadMode(enabled)));
                else
                    form.SetMaximumLoadMode(enabled);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private void StartStressWindow(int clampedLoad)
    {
        var thread = new Thread(() =>
        {
            System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += (_, e) =>
            {
                Finished?.Invoke(this, new GpuStressFinishedEventArgs(false, $"Тест видеокарты остановлен из-за ошибки: {e.Exception.Message}"));
                System.Windows.Forms.MessageBox.Show(
                    $"Тест видеокарты остановлен из-за ошибки:{Environment.NewLine}{e.Exception.Message}",
                    "Тест видеокарты",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
            };

            try
            {
                using var form = new GpuStressForm();
                form.SetIntensity(clampedLoad);
                form.SetMaximumLoadMode(MaximumLoadMode);
                form.TestFinished += (_, e) => Finished?.Invoke(this, e);
                form.FormClosed += (_, _) =>
                {
                    lock (_sync)
                    {
                        _forms.Remove(form);
                        if (_forms.Count == 0)
                        {
                            _uiThreads.Clear();
                            _isRunning = false;
                            _maximumLoadMode = false;
                            _currentLoadPercent = 0;
                        }
                    }
                };

                lock (_sync)
                {
                    _forms.Add(form);
                }

                System.Windows.Forms.Application.Run(form);
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _uiThreads.Clear();
                    _forms.Clear();
                    _isRunning = false;
                    _maximumLoadMode = false;
                    _currentLoadPercent = 0;
                }

                Finished?.Invoke(this, new GpuStressFinishedEventArgs(false, $"Не удалось запустить тест видеокарты: {ex.Message}"));
                System.Windows.Forms.MessageBox.Show(
                    $"Не удалось запустить тест видеокарты:{Environment.NewLine}{ex}",
                    "Тест видеокарты",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = "GpuStressUiThread";

        lock (_sync)
        {
            _uiThreads.Add(thread);
        }

        thread.Start();
    }
}
