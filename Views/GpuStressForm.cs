using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PcStressTester.Models;
using PcStressTester.Services;

namespace PcStressTester.Views;

public sealed class GpuStressForm : Form
{
    private readonly System.Windows.Forms.Timer _renderTimer;
    private readonly System.Windows.Forms.Timer _statsTimer;
    private readonly Panel _renderHost;
    private readonly Panel _infoPanel;
    private readonly HardwareMonitorService _hardwareMonitor;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _loadLabel;
    private readonly Label _gpuTempLabel;
    private readonly Label _gpuTempSourceLabel;
    private readonly Label _gpuLoadLabel;
    private readonly Label _gpuLoadSourceLabel;
    private readonly Label _cpuTempLabel;
    private readonly Label _elapsedLabel;
    private readonly Label _resolutionLabel;
    private readonly Label _renderAreaLabel;
    private readonly Label _frameLabel;
    private readonly Label _hintLabel;
    private readonly Label _stressModeLabel;
    private readonly Button _maxLoadButton;
    private readonly Button _skipButton;
    private readonly Button _stopButton;

    private IntPtr _deviceContext;
    private IntPtr _glContext;
    private NativeMethods.SwapIntervalDelegate? _swapInterval;
    private ShaderApi? _shaderApi;
    private uint _shaderProgram;
    private int _shaderTimeUniform = -1;
    private int _shaderResolutionUniform = -1;
    private int _shaderIntensityUniform = -1;
    private bool _shaderStressAvailable;
    private int _frame;
    private int _intensityPercent = 10;
    private bool _initialized;
    private bool _renderFailed;
    private bool _finishRequested;
    private DateTime _testStartedAt;
    private float? _maxGpuTemp;
    private float? _maxCpuTemp;
    private float? _maxGpuLoad;
    private double _gpuLoadSum;
    private int _gpuLoadSamples;
    private bool _hasReportedFinish;
    private bool _maximumLoadMode;

    private const int MaxQuadGrid = 520;
    private const int InfoPanelWidth = 340;
    private static readonly TimeSpan TestDuration = TimeSpan.FromMinutes(3);
    private const float GpuWarnTemp = 80f;
    private const float GpuDangerTemp = 85f;
    private const float CpuWarnTemp = 85f;
    private const float CpuDangerTemp = 90f;

    public event EventHandler<GpuStressFinishedEventArgs>? TestFinished;

    public GpuStressForm()
    {
        Text = "Тест видеокарты";
        BackColor = Color.Black;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        DoubleBuffered = false;
        TopMost = true;

        _hardwareMonitor = new HardwareMonitorService();

        _renderHost = new Panel
        {
            BackColor = Color.Black,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        Controls.Add(_renderHost);

        _titleLabel = CreateInfoLabel("Тест видеокарты", 18, FontStyle.Bold);
        _statusLabel = CreateInfoLabel("Запуск...", 10, FontStyle.Bold);
        _loadLabel = CreateInfoLabel("Целевая нагрузка: 10%", 10, FontStyle.Regular);
        _gpuTempLabel = CreateInfoLabel("Температура GPU: -", 11, FontStyle.Bold);
        _gpuTempSourceLabel = CreateInfoLabel("Датчик GPU: -", 9, FontStyle.Italic);
        _gpuLoadLabel = CreateInfoLabel("Загрузка GPU: -", 11, FontStyle.Bold);
        _gpuLoadSourceLabel = CreateInfoLabel("Источник загрузки: -", 9, FontStyle.Italic);
        _cpuTempLabel = CreateInfoLabel("Температура CPU: -", 10, FontStyle.Bold);
        _elapsedLabel = CreateInfoLabel("Время теста: 00:00 / 03:00", 11, FontStyle.Bold);
        _resolutionLabel = CreateInfoLabel("Разрешение окна: -", 10, FontStyle.Regular);
        _renderAreaLabel = CreateInfoLabel("Область рендера: -", 10, FontStyle.Regular);
        _frameLabel = CreateInfoLabel("Кадр: 0", 10, FontStyle.Regular);
        _hintLabel = CreateInfoLabel("Esc - остановить тест", 10, FontStyle.Bold);
        _hintLabel.ForeColor = Color.FromArgb(255, 220, 120);
        _stressModeLabel = CreateInfoLabel("Режим: обычная нагрузка", 10, FontStyle.Bold);
        _stressModeLabel.ForeColor = Color.FromArgb(170, 255, 190);
        _maxLoadButton = CreateMaxLoadButton();
        _skipButton = CreateSkipButton();
        _stopButton = CreateStopButton();

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(18),
            BackColor = Color.FromArgb(8, 12, 20),
            AutoScroll = true
        };
        layout.Controls.Add(CreateHeaderPanel(_titleLabel, _statusLabel, _loadLabel));
        layout.Controls.Add(CreateSectionPanel("Видеокарта", Color.FromArgb(39, 194, 160), _gpuTempLabel, _gpuTempSourceLabel, _gpuLoadLabel, _gpuLoadSourceLabel));
        layout.Controls.Add(CreateSectionPanel("Система", Color.FromArgb(120, 210, 255), _cpuTempLabel, _elapsedLabel, _resolutionLabel, _renderAreaLabel, _frameLabel));
        layout.Controls.Add(CreateSectionPanel("Управление", Color.FromArgb(255, 220, 120), _stressModeLabel, _maxLoadButton, _hintLabel, _skipButton, _stopButton));

        _infoPanel = new Panel
        {
            BackColor = Color.FromArgb(8, 12, 20),
            Padding = new Padding(0),
            BorderStyle = BorderStyle.None
        };
        _infoPanel.Controls.Add(layout);
        Controls.Add(_infoPanel);
        _infoPanel.BringToFront();

        Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        PerformLayoutManually();

        _renderTimer = new System.Windows.Forms.Timer
        {
            Interval = 16
        };
        _renderTimer.Tick += (_, _) => RenderFrame();

        _statsTimer = new System.Windows.Forms.Timer
        {
            Interval = 700
        };
        _statsTimer.Tick += (_, _) => UpdateSensorInfo();

        Shown += (_, _) =>
        {
            try
            {
                InitializeOpenGl();
            }
            catch (Exception ex)
            {
                HandleRenderFailure($"Ошибка инициализации OpenGL: {ex.Message}");
            }
        };
        FormClosed += (_, _) => CleanupOpenGl();
        FormClosing += (_, _) =>
        {
            if (!_finishRequested)
                ReportFinish(false, "Тест видеокарты закрыт пользователем.");
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
                FinishTest(false, "Тест видеокарты остановлен пользователем.");
        };
    }

    public void SetIntensity(int intensityPercent)
    {
        _intensityPercent = Math.Clamp(intensityPercent, 10, 100);
        Text = $"Тест видеокарты - {_intensityPercent}%";
        _loadLabel.Text = $"Целевая нагрузка: {_intensityPercent}%";
    }

    public void SetMaximumLoadMode(bool enabled)
    {
        _maximumLoadMode = enabled;
        _stressModeLabel.Text = enabled ? "Режим: максимальная нагрузка" : "Режим: обычная нагрузка";
        _stressModeLabel.ForeColor = enabled
            ? Color.FromArgb(255, 210, 90)
            : Color.FromArgb(170, 255, 190);
        _maxLoadButton.Text = enabled ? "Обычная нагрузка" : "Максимальная нагрузка";
        _maxLoadButton.BackColor = enabled
            ? Color.FromArgb(255, 210, 90)
            : Color.FromArgb(39, 194, 160);

        if (enabled && _shaderStressAvailable)
            _stressModeLabel.Text = "Режим: шейдерная нагрузка";
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (_initialized && _renderHost.ClientSize.Width > 0 && _renderHost.ClientSize.Height > 0)
            Gl.glViewport(0, 0, Math.Max(1, _renderHost.ClientSize.Width), Math.Max(1, _renderHost.ClientSize.Height));

        PerformLayoutManually();
        UpdateInfoPanel();
    }

    private void InitializeOpenGl()
    {
        if (_initialized)
            return;

        _deviceContext = NativeMethods.GetDC(_renderHost.Handle);
        if (_deviceContext == IntPtr.Zero)
            throw new InvalidOperationException("Не удалось получить device context для GPU-теста.");

        var pixelFormat = new NativeMethods.PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf<NativeMethods.PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = NativeMethods.PFD_DRAW_TO_WINDOW | NativeMethods.PFD_SUPPORT_OPENGL | NativeMethods.PFD_DOUBLEBUFFER,
            iPixelType = NativeMethods.PFD_TYPE_RGBA,
            cColorBits = 32,
            cDepthBits = 24,
            cStencilBits = 8,
            iLayerType = NativeMethods.PFD_MAIN_PLANE
        };

        int format = NativeMethods.ChoosePixelFormat(_deviceContext, ref pixelFormat);
        if (format == 0 || !NativeMethods.SetPixelFormat(_deviceContext, format, ref pixelFormat))
            throw new InvalidOperationException("Не удалось настроить pixel format для OpenGL.");

        _glContext = NativeMethods.wglCreateContext(_deviceContext);
        if (_glContext == IntPtr.Zero || !NativeMethods.wglMakeCurrent(_deviceContext, _glContext))
            throw new InvalidOperationException("Не удалось создать OpenGL-контекст для GPU-теста.");

        _initialized = true;
        Gl.glDisable(Gl.GL_DEPTH_TEST);
        Gl.glEnable(Gl.GL_BLEND);
        Gl.glBlendFunc(Gl.GL_SRC_ALPHA, Gl.GL_ONE_MINUS_SRC_ALPHA);
        Gl.glViewport(0, 0, Math.Max(1, _renderHost.ClientSize.Width), Math.Max(1, _renderHost.ClientSize.Height));
        InitializeSwapInterval();
        InitializeShaderStress();

        _statusLabel.Text = "Активен";
        _statusLabel.ForeColor = Color.FromArgb(170, 255, 190);
        _testStartedAt = DateTime.UtcNow;
        UpdateInfoPanel();
        UpdateSensorInfo();
        _statsTimer.Start();
        _renderTimer.Start();
    }

    private void RenderFrame()
    {
        if (_renderFailed ||
            !_initialized ||
            _deviceContext == IntPtr.Zero ||
            _glContext == IntPtr.Zero ||
            WindowState == FormWindowState.Minimized ||
            _finishRequested)
            return;

        try
        {
            _frame++;
            if ((_frame & 15) == 0)
                UpdateInfoPanel();

            int width = Math.Max(1, _renderHost.ClientSize.Width);
            int height = Math.Max(1, _renderHost.ClientSize.Height);

            if (_maximumLoadMode && RenderShaderStress(width, height))
                return;

            float intensity = _intensityPercent / 100f;
            int modeMultiplier = _maximumLoadMode ? 3 : 1;
            int passes = (45 + _intensityPercent) * modeMultiplier;
            int layers = (260 + _intensityPercent * 8) * modeMultiplier;
            int bursts = (60 + _intensityPercent * 4) * modeMultiplier;
            int gridSize = Math.Min(MaxQuadGrid, (_maximumLoadMode ? 130 : 70) + _intensityPercent);

            Gl.glViewport(0, 0, width, height);
            Gl.glClearColor(0.015f, 0.015f, 0.03f, 1f);
            Gl.glClear(Gl.GL_COLOR_BUFFER_BIT);

            for (int pass = 0; pass < passes; pass++)
            {
                float passShift = _frame * 0.0035f * (pass + 1);
                float phase = pass * 0.11f;
                float alpha = 0.0035f + (_intensityPercent / 100f) * 0.009f;

                Gl.glBegin(Gl.GL_QUADS);
                for (int tile = 0; tile < 6; tile++)
                {
                    float t = passShift + tile * 0.37f;
                    float inset = MathF.Abs(MathF.Sin(t * 0.6f + phase)) * 0.35f;
                    float left = -1f + inset;
                    float right = 1f - inset;
                    float top = 1f - MathF.Abs(MathF.Cos(t * 0.8f + phase)) * 0.35f;
                    float bottom = -1f + MathF.Abs(MathF.Sin(t * 0.7f + phase)) * 0.35f;
                    float r = 0.18f + 0.42f * MathF.Abs(MathF.Sin(t));
                    float g = 0.16f + 0.38f * MathF.Abs(MathF.Cos(t * 1.3f));
                    float b = 0.22f + 0.48f * MathF.Abs(MathF.Sin(t * 1.7f));

                    Gl.glColor4f(r, g, b, alpha);
                    Gl.glVertex2f(left, bottom);
                    Gl.glVertex2f(right, bottom);
                    Gl.glVertex2f(right, top);
                    Gl.glVertex2f(left, top);
                }
                Gl.glEnd();
            }

            Gl.glBegin(Gl.GL_TRIANGLES);
            for (int ring = 0; ring < layers; ring++)
            {
                float t = _frame * 0.006f + ring * 0.017f;
                float radius = 0.02f + (ring % 160) * 0.0038f;
                float angle = t * 2.6f;
                float cx = MathF.Cos(angle * 0.7f) * 0.55f;
                float cy = MathF.Sin(angle * 0.9f) * 0.55f;

                EmitTriangleFanCore(
                    cx,
                    cy,
                    radius,
                    angle,
                    0.25f + 0.45f * MathF.Abs(MathF.Sin(t)),
                    0.22f + 0.40f * MathF.Abs(MathF.Cos(t * 1.2f)),
                    0.28f + 0.44f * MathF.Abs(MathF.Sin(t * 0.8f)),
                    0.035f);
            }
            Gl.glEnd();

            for (int burst = 0; burst < bursts; burst++)
            {
                float t = _frame * 0.01f + burst * 0.17f;
                float x = MathF.Sin(t * 1.9f) * 0.92f;
                float y = MathF.Cos(t * 1.4f) * 0.92f;
                float size = 0.02f + (burst % 7) * 0.005f + intensity * 0.03f;

                Gl.glBegin(Gl.GL_QUADS);
                Gl.glColor4f(0.55f, 0.22f + 0.32f * MathF.Abs(MathF.Sin(t)), 0.18f + 0.36f * MathF.Abs(MathF.Cos(t)), 0.03f);
                Gl.glVertex2f(x - size, y - size);
                Gl.glVertex2f(x + size, y - size);
                Gl.glVertex2f(x + size, y + size);
                Gl.glVertex2f(x - size, y + size);
                Gl.glEnd();
            }

            float cellWidth = 2f / gridSize;
            float cellHeight = 2f / gridSize;
            for (int y = 0; y < gridSize; y++)
            {
                float py = -1f + y * cellHeight;

                Gl.glBegin(Gl.GL_QUADS);
                for (int x = 0; x < gridSize; x++)
                {
                    float px = -1f + x * cellWidth;
                    float wave = MathF.Sin((_frame * 0.03f) + x * 0.11f + y * 0.07f);
                    float mix = MathF.Abs(wave);

                    Gl.glColor4f(0.08f + mix * 0.28f, 0.06f + (1f - mix) * 0.20f, 0.12f + mix * 0.30f, 0.012f);
                    Gl.glVertex2f(px, py);
                    Gl.glVertex2f(px + cellWidth, py);
                    Gl.glVertex2f(px + cellWidth, py + cellHeight);
                    Gl.glVertex2f(px, py + cellHeight);
                }
                Gl.glEnd();
            }

            Gl.glBegin(Gl.GL_LINES);
            for (int i = 0; i < (260 + _intensityPercent * 5) * modeMultiplier; i++)
            {
                float t = _frame * 0.008f + i * 0.031f;
                float x1 = MathF.Sin(t * 1.7f) * 0.98f;
                float y1 = MathF.Cos(t * 1.3f) * 0.98f;
                float x2 = MathF.Sin(t * 2.1f + 1.4f) * 0.98f;
                float y2 = MathF.Cos(t * 1.9f + 0.8f) * 0.98f;

                Gl.glColor4f(
                    0.18f + 0.32f * MathF.Abs(MathF.Sin(t)),
                    0.18f + 0.30f * MathF.Abs(MathF.Cos(t * 0.7f)),
                    0.22f + 0.34f * MathF.Abs(MathF.Sin(t * 1.3f)),
                    0.014f);
                Gl.glVertex2f(x1, y1);
                Gl.glVertex2f(x2, y2);
            }
            Gl.glEnd();

            Gl.glFlush();
            NativeMethods.SwapBuffers(_deviceContext);
        }
        catch (Exception ex)
        {
            HandleRenderFailure($"Ошибка рендера: {ex.Message}");
        }
    }

    private void InitializeSwapInterval()
    {
        IntPtr proc = NativeMethods.wglGetProcAddress("wglSwapIntervalEXT");
        if (proc == IntPtr.Zero)
            return;

        _swapInterval = Marshal.GetDelegateForFunctionPointer<NativeMethods.SwapIntervalDelegate>(proc);
        _swapInterval(0);
    }

    private void InitializeShaderStress()
    {
        try
        {
            _shaderApi = ShaderApi.TryCreate();
            if (_shaderApi is null)
                return;

            uint shader = _shaderApi.CreateShader(Gl.GL_FRAGMENT_SHADER);
            string source = """
                #version 120
                uniform float u_time;
                uniform vec2 u_resolution;
                uniform float u_intensity;

                void main()
                {
                    vec2 uv = gl_FragCoord.xy / max(u_resolution, vec2(1.0));
                    vec2 p = uv * 2.0 - 1.0;
                    vec2 z = p * (2.0 + u_intensity * 1.2);
                    float acc = 0.0;

                    for (int i = 0; i < 980; i++)
                    {
                        float fi = float(i);
                        z = vec2(
                            sin(z.x * z.y + u_time * 0.0015 + fi * 0.011),
                            cos(z.x - z.y + u_time * 0.0012 - fi * 0.013)
                        ) + p * (1.0 + u_intensity * 0.35);

                        acc += sin(z.x * 37.0 + fi) * cos(z.y * 29.0 - fi);
                    }

                    float glow = smoothstep(1.05, 0.05, length(p));
                    float band = 0.5 + 0.5 * sin((uv.y * 5.0) + acc * 0.00018 + u_time * 0.003);
                    float pulse = 0.72 + 0.08 * sin(u_time * 0.015);
                    vec3 baseColor = vec3(0.025, 0.055, 0.085);
                    vec3 coolColor = vec3(0.03, 0.42, 0.36);
                    vec3 warmColor = vec3(0.85, 0.38, 0.18);
                    vec3 color = mix(baseColor, coolColor, glow * pulse);
                    color = mix(color, warmColor, band * glow * 0.18);
                    gl_FragColor = vec4(color, 1.0);
                }
                """;

            _shaderApi.ShaderSource(shader, source);
            _shaderApi.CompileShader(shader);

            if (!_shaderApi.GetShaderStatus(shader, Gl.GL_COMPILE_STATUS))
            {
                _shaderApi.DeleteShader(shader);
                return;
            }

            uint program = _shaderApi.CreateProgram();
            _shaderApi.AttachShader(program, shader);
            _shaderApi.LinkProgram(program);
            _shaderApi.DeleteShader(shader);

            if (!_shaderApi.GetProgramStatus(program, Gl.GL_LINK_STATUS))
            {
                _shaderApi.DeleteProgram(program);
                return;
            }

            _shaderProgram = program;
            _shaderTimeUniform = _shaderApi.GetUniformLocation(program, "u_time");
            _shaderResolutionUniform = _shaderApi.GetUniformLocation(program, "u_resolution");
            _shaderIntensityUniform = _shaderApi.GetUniformLocation(program, "u_intensity");
            _shaderStressAvailable = true;
        }
        catch
        {
            _shaderStressAvailable = false;
            _shaderProgram = 0;
        }
    }

    private bool RenderShaderStress(int width, int height)
    {
        if (!_shaderStressAvailable || _shaderApi is null || _shaderProgram == 0)
            return false;

        _shaderApi.UseProgram(_shaderProgram);
        if (_shaderTimeUniform >= 0)
            _shaderApi.Uniform1f(_shaderTimeUniform, _frame);
        if (_shaderResolutionUniform >= 0)
            _shaderApi.Uniform2f(_shaderResolutionUniform, width, height);
        if (_shaderIntensityUniform >= 0)
            _shaderApi.Uniform1f(_shaderIntensityUniform, _intensityPercent / 100f);

        Gl.glViewport(0, 0, width, height);
        Gl.glClearColor(0.01f, 0.01f, 0.02f, 1f);
        Gl.glClear(Gl.GL_COLOR_BUFFER_BIT);

        int passes = Math.Max(2, _intensityPercent / 35);
        for (int i = 0; i < passes; i++)
        {
            Gl.glBegin(Gl.GL_QUADS);
            Gl.glVertex2f(-1f, -1f);
            Gl.glVertex2f(1f, -1f);
            Gl.glVertex2f(1f, 1f);
            Gl.glVertex2f(-1f, 1f);
            Gl.glEnd();
        }

        _shaderApi.UseProgram(0);
        Gl.glFlush();
        NativeMethods.SwapBuffers(_deviceContext);
        return true;
    }

    private static void EmitTriangleFanCore(float x, float y, float radius, float angle, float r, float g, float b, float alpha)
    {
        float x1 = x + MathF.Cos(angle) * radius;
        float y1 = y + MathF.Sin(angle) * radius;
        float x2 = x + MathF.Cos(angle + 2.094f) * radius;
        float y2 = y + MathF.Sin(angle + 2.094f) * radius;
        float x3 = x + MathF.Cos(angle + 4.188f) * radius;
        float y3 = y + MathF.Sin(angle + 4.188f) * radius;

        Gl.glColor4f(r, g, b, alpha);
        Gl.glVertex2f(x1, y1);
        Gl.glColor4f(g, b, r, alpha);
        Gl.glVertex2f(x2, y2);
        Gl.glColor4f(b, r, g, alpha);
        Gl.glVertex2f(x3, y3);
    }

    private void CleanupOpenGl()
    {
        _renderTimer.Stop();
        _statsTimer.Stop();

        if (_glContext != IntPtr.Zero)
        {
            if (_shaderProgram != 0 && _shaderApi is not null)
            {
                _shaderApi.DeleteProgram(_shaderProgram);
                _shaderProgram = 0;
            }

            NativeMethods.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            NativeMethods.wglDeleteContext(_glContext);
            _glContext = IntPtr.Zero;
        }

        if (_deviceContext != IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(_renderHost.Handle, _deviceContext);
            _deviceContext = IntPtr.Zero;
        }

        _initialized = false;
        _hardwareMonitor.Dispose();
    }

    private void HandleRenderFailure(string message)
    {
        if (_renderFailed)
            return;

        _renderFailed = true;
        _statusLabel.Text = "Ошибка";
        _statusLabel.ForeColor = Color.FromArgb(255, 96, 96);
        _hintLabel.Text = "Esc - закрыть окно";

        try
        {
            _renderTimer.Stop();
            _statsTimer.Stop();
        }
        catch
        {
        }

        BeginInvoke(new Action(() =>
        {
            MessageBox.Show(
                null,
                message,
                "Тест видеокарты",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            Close();
        }));
    }

    private void UpdateInfoPanel()
    {
        int windowWidth = Math.Max(1, ClientSize.Width);
        int windowHeight = Math.Max(1, ClientSize.Height);
        _resolutionLabel.Text = $"Разрешение окна: {windowWidth} x {windowHeight}";
        _renderAreaLabel.Text = $"Область рендера: {Math.Max(1, _renderHost.ClientSize.Width)} x {Math.Max(1, _renderHost.ClientSize.Height)}";
        _frameLabel.Text = $"Кадр: {_frame}";
    }

    private void PerformLayoutManually()
    {
        int panelWidth = Math.Min(InfoPanelWidth, Math.Max(280, ClientSize.Width / 4));
        int renderWidth = Math.Max(1, ClientSize.Width - panelWidth);

        _renderHost.Bounds = new Rectangle(0, 0, renderWidth, Math.Max(1, ClientSize.Height));
        _infoPanel.Bounds = new Rectangle(renderWidth, 0, panelWidth, Math.Max(1, ClientSize.Height));
        _infoPanel.BringToFront();
    }

    private void UpdateSensorInfo()
    {
        try
        {
            var sensors = _hardwareMonitor.ReadSensors();
            var gpuTemp = FindGpuTemperature(sensors);
            var gpuLoad = FindGpuLoad(sensors);
            var cpuTemp = FindCpuTemperature(sensors);

            _gpuTempLabel.Text = gpuTemp.Value.HasValue
                ? $"Температура GPU: {gpuTemp.Value.Value:F1} °C"
                : "Температура GPU: -";
            _gpuTempSourceLabel.Text = $"Датчик GPU: {gpuTemp.Source}";

            _gpuLoadLabel.Text = gpuLoad.Value.HasValue
                ? $"Загрузка GPU: {gpuLoad.Value.Value:F1} %"
                : "Загрузка GPU: -";
            _gpuLoadSourceLabel.Text = $"Источник загрузки: {gpuLoad.Source}";

            _cpuTempLabel.Text = cpuTemp.Value.HasValue
                ? $"Температура CPU: {cpuTemp.Value.Value:F1} °C"
                : "Температура CPU: -";

            TrackResults(gpuTemp.Value, gpuLoad.Value, cpuTemp.Value);
            UpdateElapsedInfo();

            ApplyTemperatureWarningStyles(gpuTemp.Value, gpuLoad.Value, cpuTemp.Value);

            if (!_finishRequested && DateTime.UtcNow - _testStartedAt >= TestDuration)
                FinishTest(true, "Тест успешно завершен.");
        }
        catch
        {
            _gpuTempLabel.Text = "Температура GPU: ошибка чтения";
            _gpuTempSourceLabel.Text = "Датчик GPU: недоступен";
            _gpuLoadLabel.Text = "Загрузка GPU: ошибка чтения";
            _gpuLoadSourceLabel.Text = "Источник загрузки: недоступен";
            _cpuTempLabel.Text = "Температура CPU: ошибка чтения";
            UpdateElapsedInfo();
            ApplyTemperatureWarningStyles(null, null, null);
        }
    }

    private void TrackResults(float? gpuTemp, float? gpuLoad, float? cpuTemp)
    {
        if (gpuTemp.HasValue)
            _maxGpuTemp = !_maxGpuTemp.HasValue ? gpuTemp.Value : Math.Max(_maxGpuTemp.Value, gpuTemp.Value);

        if (cpuTemp.HasValue)
            _maxCpuTemp = !_maxCpuTemp.HasValue ? cpuTemp.Value : Math.Max(_maxCpuTemp.Value, cpuTemp.Value);

        if (gpuLoad.HasValue)
        {
            _maxGpuLoad = !_maxGpuLoad.HasValue ? gpuLoad.Value : Math.Max(_maxGpuLoad.Value, gpuLoad.Value);
            _gpuLoadSum += gpuLoad.Value;
            _gpuLoadSamples++;
        }
    }

    private void UpdateElapsedInfo()
    {
        var elapsed = DateTime.UtcNow - _testStartedAt;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        if (elapsed > TestDuration)
            elapsed = TestDuration;

        _elapsedLabel.Text = $"Время теста: {elapsed:mm\\:ss} / {TestDuration:mm\\:ss}";
    }

    private void FinishTest(bool showResults, string reason)
    {
        if (_finishRequested)
            return;

        _finishRequested = true;
        ReportFinish(showResults, reason);

        try
        {
            _renderTimer.Stop();
            _statsTimer.Stop();
        }
        catch
        {
        }

        if (showResults)
        {
            double averageGpuLoad = _gpuLoadSamples > 0 ? _gpuLoadSum / _gpuLoadSamples : 0;
            var elapsed = DateTime.UtcNow - _testStartedAt;
            if (elapsed > TestDuration)
                elapsed = TestDuration;

            BeginInvoke(new Action(() =>
            {
                TopMost = false;
                Hide();
                TestResultForm.ShowResult(
                    null,
                    "Результаты теста видеокарты",
                    reason,
                    new List<(string Label, string Value)>
                    {
                        ("Длительность", elapsed.ToString(@"mm\:ss")),
                        ("Макс. температура GPU", FormatMetric(_maxGpuTemp, "°C")),
                        ("Макс. температура CPU", FormatMetric(_maxCpuTemp, "°C")),
                        ("Пиковая загрузка GPU", FormatMetric(_maxGpuLoad, "%")),
                        ("Средняя загрузка GPU", $"{averageGpuLoad:F1} %")
                    },
                    Color.FromArgb(120, 210, 255));
                Close();
            }));
        }
        else
        {
            BeginInvoke(new Action(Close));
        }
    }

    private void ReportFinish(bool completed, string reason)
    {
        if (_hasReportedFinish)
            return;

        _hasReportedFinish = true;
        TestFinished?.Invoke(this, new GpuStressFinishedEventArgs(completed, reason));
    }

    private static string FormatMetric(float? value, string unit)
    {
        return value.HasValue ? $"{value.Value:F1} {unit}" : "-";
    }

    private void ApplyTemperatureWarningStyles(float? gpuTemp, float? gpuLoad, float? cpuTemp)
    {
        _gpuTempLabel.ForeColor = GetTemperatureColor(gpuTemp, GpuWarnTemp, GpuDangerTemp);
        _cpuTempLabel.ForeColor = GetTemperatureColor(cpuTemp, CpuWarnTemp, CpuDangerTemp);
        _gpuLoadLabel.ForeColor = gpuLoad.HasValue && gpuLoad.Value >= 85f
            ? Color.FromArgb(140, 255, 198)
            : Color.White;

        bool gpuDanger = gpuTemp.HasValue && gpuTemp.Value >= GpuDangerTemp;
        bool cpuDanger = cpuTemp.HasValue && cpuTemp.Value >= CpuDangerTemp;
        bool gpuWarn = gpuTemp.HasValue && gpuTemp.Value >= GpuWarnTemp;
        bool cpuWarn = cpuTemp.HasValue && cpuTemp.Value >= CpuWarnTemp;

        if (_renderFailed)
            return;

        if (gpuDanger || cpuDanger)
        {
            _statusLabel.Text = "Перегрев";
            _statusLabel.ForeColor = Color.FromArgb(255, 96, 96);
            _infoPanel.BackColor = Color.FromArgb(52, 18, 18);
        }
        else if (gpuWarn || cpuWarn)
        {
            _statusLabel.Text = "Высокая температура";
            _statusLabel.ForeColor = Color.FromArgb(255, 210, 90);
            _infoPanel.BackColor = Color.FromArgb(51, 40, 15);
        }
        else
        {
            _statusLabel.Text = "Активен";
            _statusLabel.ForeColor = Color.FromArgb(170, 255, 190);
            _infoPanel.BackColor = Color.FromArgb(8, 12, 20);
        }
    }

    private static Color GetTemperatureColor(float? temperature, float warn, float danger)
    {
        if (!temperature.HasValue)
            return Color.White;

        if (temperature.Value >= danger)
            return Color.FromArgb(255, 96, 96);

        if (temperature.Value >= warn)
            return Color.FromArgb(255, 210, 90);

        return Color.FromArgb(170, 255, 190);
    }

    private static SensorReading FindCpuTemperature(List<SensorInfo> sensors)
    {
        var cpuTemps = sensors
            .Where(s => s.Type.Equals("Temperature", StringComparison.OrdinalIgnoreCase))
            .Where(s => ContainsAny(s.Hardware, "cpu", "intel", "amd", "ryzen") ||
                        ContainsAny(s.Name, "cpu", "package", "core", "ccd", "tctl", "tdie"))
            .Where(s => s.Value.HasValue && s.Value.Value > 0.1f)
            .ToList();

        var preferred = cpuTemps.FirstOrDefault(s =>
            ContainsAny(s.Name, "CPU Package", "Package", "Tctl/Tdie", "Core Average", "CCD"));

        var selected = preferred ?? cpuTemps.FirstOrDefault();
        return new SensorReading(selected?.Value, selected is null ? "-" : $"{selected.Hardware} / {selected.Name}");
    }

    private static SensorReading FindGpuTemperature(List<SensorInfo> sensors)
    {
        var gpuTemps = sensors
            .Where(s => s.Type.Equals("Temperature", StringComparison.OrdinalIgnoreCase))
            .Where(s => ContainsAny(s.Hardware, "gpu", "graphics", "nvidia", "geforce", "radeon", "intel arc") ||
                        ContainsAny(s.Name, "gpu", "graphics", "hot spot", "hotspot", "memory", "edge"))
            .Where(s => s.Value.HasValue && s.Value.Value > 0.1f)
            .ToList();

        var preferred = gpuTemps.FirstOrDefault(s =>
            ContainsAny(s.Name, "GPU Core", "GPU Temperature", "Core", "Edge"));

        var selected = preferred ?? gpuTemps.FirstOrDefault();
        return new SensorReading(selected?.Value, selected is null ? "-" : $"{selected.Hardware} / {selected.Name}");
    }

    private static SensorReading FindGpuLoad(List<SensorInfo> sensors)
    {
        var gpuLoads = sensors
            .Where(s => s.Type.Equals("Load", StringComparison.OrdinalIgnoreCase))
            .Where(s => ContainsAny(s.Hardware, "gpu", "graphics", "nvidia", "geforce", "radeon", "intel arc") ||
                        ContainsAny(s.Name, "gpu", "graphics", "3D", "core"))
            .Where(s => s.Value.HasValue && s.Value.Value > 0.1f)
            .ToList();

        var preferred = gpuLoads.FirstOrDefault(s =>
            ContainsAny(s.Name, "GPU Core", "D3D 3D", "3D", "GPU"));

        var selected = preferred ?? gpuLoads.FirstOrDefault();
        return new SensorReading(selected?.Value, selected is null ? "-" : $"{selected.Hardware} / {selected.Name}");
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

    private static Label CreateInfoLabel(string text, float size, FontStyle style)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            Font = new Font("Segoe UI", size, style),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            MaximumSize = new Size(280, 0),
            Margin = new Padding(0, 0, 0, 8)
        };
    }

    private Button CreateStopButton()
    {
        var button = new Button
        {
            Text = "Остановить тест",
            Width = 286,
            Height = 42,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(241, 110, 91),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 8, 0, 0)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => FinishTest(false, "Тест видеокарты остановлен пользователем.");
        return button;
    }

    private Button CreateMaxLoadButton()
    {
        var button = new Button
        {
            Text = "Максимальная нагрузка",
            Width = 286,
            Height = 42,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(39, 194, 160),
            ForeColor = Color.FromArgb(8, 12, 18),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 8, 0, 0)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => SetMaximumLoadMode(!_maximumLoadMode);
        return button;
    }

    private Button CreateSkipButton()
    {
        var button = new Button
        {
            Text = "Завершить сейчас",
            Width = 286,
            Height = 42,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(39, 194, 160),
            ForeColor = Color.FromArgb(8, 12, 18),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 8, 0, 0)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => FinishTest(true, "Тест завершен досрочно для проверки.");
        return button;
    }

    private static Panel CreateHeaderPanel(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            Width = 286,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(16, 16, 16, 12),
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.FromArgb(17, 28, 43)
        };

        foreach (var control in controls)
            panel.Controls.Add(control);

        return panel;
    }

    private static Panel CreateSectionPanel(string title, Color accentColor, params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            Width = 286,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(14, 12, 14, 12),
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.FromArgb(14, 21, 34)
        };

        var titleRow = new Panel
        {
            Width = 258,
            Height = 28,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.Transparent
        };
        titleRow.Controls.Add(new Panel
        {
            Width = 5,
            Height = 20,
            Location = new Point(0, 4),
            BackColor = accentColor
        });
        titleRow.Controls.Add(new Label
        {
            AutoSize = true,
            Text = title,
            Location = new Point(14, 3),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(226, 238, 250),
            BackColor = Color.Transparent
        });
        panel.Controls.Add(titleRow);

        foreach (var control in controls)
            panel.Controls.Add(control);

        return panel;
    }

    private static Control CreateSpacer(int height)
    {
        return new Panel
        {
            Width = 1,
            Height = height,
            Margin = new Padding(0)
        };
    }

    private readonly record struct SensorReading(float? Value, string Source);

    private static class Gl
    {
        public const uint GL_COLOR_BUFFER_BIT = 0x00004000;
        public const uint GL_BLEND = 0x0BE2;
        public const uint GL_DEPTH_TEST = 0x0B71;
        public const uint GL_TRIANGLES = 0x0004;
        public const uint GL_LINES = 0x0001;
        public const uint GL_QUADS = 0x0007;
        public const uint GL_SRC_ALPHA = 0x0302;
        public const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;
        public const uint GL_FRAGMENT_SHADER = 0x8B30;
        public const uint GL_COMPILE_STATUS = 0x8B81;
        public const uint GL_LINK_STATUS = 0x8B82;

        [DllImport("opengl32.dll")]
        public static extern void glBegin(uint mode);

        [DllImport("opengl32.dll")]
        public static extern void glEnd();

        [DllImport("opengl32.dll")]
        public static extern void glVertex2f(float x, float y);

        [DllImport("opengl32.dll")]
        public static extern void glColor4f(float red, float green, float blue, float alpha);

        [DllImport("opengl32.dll")]
        public static extern void glClear(uint mask);

        [DllImport("opengl32.dll")]
        public static extern void glClearColor(float red, float green, float blue, float alpha);

        [DllImport("opengl32.dll")]
        public static extern void glViewport(int x, int y, int width, int height);

        [DllImport("opengl32.dll")]
        public static extern void glDisable(uint cap);

        [DllImport("opengl32.dll")]
        public static extern void glEnable(uint cap);

        [DllImport("opengl32.dll")]
        public static extern void glBlendFunc(uint sfactor, uint dfactor);

        [DllImport("opengl32.dll")]
        public static extern void glFinish();

        [DllImport("opengl32.dll")]
        public static extern void glFlush();
    }

    private sealed class ShaderApi
    {
        private readonly GlCreateShaderDelegate _createShader;
        private readonly GlShaderSourceDelegate _shaderSource;
        private readonly GlCompileShaderDelegate _compileShader;
        private readonly GlGetShaderivDelegate _getShaderiv;
        private readonly GlCreateProgramDelegate _createProgram;
        private readonly GlAttachShaderDelegate _attachShader;
        private readonly GlLinkProgramDelegate _linkProgram;
        private readonly GlGetProgramivDelegate _getProgramiv;
        private readonly GlUseProgramDelegate _useProgram;
        private readonly GlGetUniformLocationDelegate _getUniformLocation;
        private readonly GlUniform1fDelegate _uniform1f;
        private readonly GlUniform2fDelegate _uniform2f;
        private readonly GlDeleteShaderDelegate _deleteShader;
        private readonly GlDeleteProgramDelegate _deleteProgram;

        private ShaderApi(
            GlCreateShaderDelegate createShader,
            GlShaderSourceDelegate shaderSource,
            GlCompileShaderDelegate compileShader,
            GlGetShaderivDelegate getShaderiv,
            GlCreateProgramDelegate createProgram,
            GlAttachShaderDelegate attachShader,
            GlLinkProgramDelegate linkProgram,
            GlGetProgramivDelegate getProgramiv,
            GlUseProgramDelegate useProgram,
            GlGetUniformLocationDelegate getUniformLocation,
            GlUniform1fDelegate uniform1f,
            GlUniform2fDelegate uniform2f,
            GlDeleteShaderDelegate deleteShader,
            GlDeleteProgramDelegate deleteProgram)
        {
            _createShader = createShader;
            _shaderSource = shaderSource;
            _compileShader = compileShader;
            _getShaderiv = getShaderiv;
            _createProgram = createProgram;
            _attachShader = attachShader;
            _linkProgram = linkProgram;
            _getProgramiv = getProgramiv;
            _useProgram = useProgram;
            _getUniformLocation = getUniformLocation;
            _uniform1f = uniform1f;
            _uniform2f = uniform2f;
            _deleteShader = deleteShader;
            _deleteProgram = deleteProgram;
        }

        public static ShaderApi? TryCreate()
        {
            try
            {
                return new ShaderApi(
                    Load<GlCreateShaderDelegate>("glCreateShader"),
                    Load<GlShaderSourceDelegate>("glShaderSource"),
                    Load<GlCompileShaderDelegate>("glCompileShader"),
                    Load<GlGetShaderivDelegate>("glGetShaderiv"),
                    Load<GlCreateProgramDelegate>("glCreateProgram"),
                    Load<GlAttachShaderDelegate>("glAttachShader"),
                    Load<GlLinkProgramDelegate>("glLinkProgram"),
                    Load<GlGetProgramivDelegate>("glGetProgramiv"),
                    Load<GlUseProgramDelegate>("glUseProgram"),
                    Load<GlGetUniformLocationDelegate>("glGetUniformLocation"),
                    Load<GlUniform1fDelegate>("glUniform1f"),
                    Load<GlUniform2fDelegate>("glUniform2f"),
                    Load<GlDeleteShaderDelegate>("glDeleteShader"),
                    Load<GlDeleteProgramDelegate>("glDeleteProgram"));
            }
            catch
            {
                return null;
            }
        }

        public uint CreateShader(uint type) => _createShader(type);

        public void ShaderSource(uint shader, string source)
        {
            string[] sources = [source];
            int[] lengths = [source.Length];
            _shaderSource(shader, 1, sources, lengths);
        }

        public void CompileShader(uint shader) => _compileShader(shader);

        public bool GetShaderStatus(uint shader, uint parameter)
        {
            _getShaderiv(shader, parameter, out int status);
            return status != 0;
        }

        public uint CreateProgram() => _createProgram();

        public void AttachShader(uint program, uint shader) => _attachShader(program, shader);

        public void LinkProgram(uint program) => _linkProgram(program);

        public bool GetProgramStatus(uint program, uint parameter)
        {
            _getProgramiv(program, parameter, out int status);
            return status != 0;
        }

        public void UseProgram(uint program) => _useProgram(program);

        public int GetUniformLocation(uint program, string name) => _getUniformLocation(program, name);

        public void Uniform1f(int location, float value) => _uniform1f(location, value);

        public void Uniform2f(int location, float x, float y) => _uniform2f(location, x, y);

        public void DeleteShader(uint shader) => _deleteShader(shader);

        public void DeleteProgram(uint program) => _deleteProgram(program);

        private static T Load<T>(string name) where T : Delegate
        {
            IntPtr proc = NativeMethods.wglGetProcAddress(name);
            if (proc == IntPtr.Zero)
                throw new InvalidOperationException($"OpenGL function is unavailable: {name}");

            return Marshal.GetDelegateForFunctionPointer<T>(proc);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint GlCreateShaderDelegate(uint shaderType);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlShaderSourceDelegate(uint shader, int count, string[] source, int[] length);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlCompileShaderDelegate(uint shader);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlGetShaderivDelegate(uint shader, uint pname, out int parameters);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint GlCreateProgramDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlAttachShaderDelegate(uint program, uint shader);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlLinkProgramDelegate(uint program);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlGetProgramivDelegate(uint program, uint pname, out int parameters);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlUseProgramDelegate(uint program);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate int GlGetUniformLocationDelegate(uint program, string name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlUniform1fDelegate(int location, float value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlUniform2fDelegate(int location, float x, float y);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlDeleteShaderDelegate(uint shader);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GlDeleteProgramDelegate(uint program);
    }

    private static class NativeMethods
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int SwapIntervalDelegate(int interval);

        public const uint PFD_DRAW_TO_WINDOW = 0x00000004;
        public const uint PFD_SUPPORT_OPENGL = 0x00000020;
        public const uint PFD_DOUBLEBUFFER = 0x00000001;
        public const byte PFD_TYPE_RGBA = 0;
        public const sbyte PFD_MAIN_PLANE = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct PIXELFORMATDESCRIPTOR
        {
            public ushort nSize;
            public ushort nVersion;
            public uint dwFlags;
            public byte iPixelType;
            public byte cColorBits;
            public byte cRedBits;
            public byte cRedShift;
            public byte cGreenBits;
            public byte cGreenShift;
            public byte cBlueBits;
            public byte cBlueShift;
            public byte cAlphaBits;
            public byte cAlphaShift;
            public byte cAccumBits;
            public byte cAccumRedBits;
            public byte cAccumGreenBits;
            public byte cAccumBlueBits;
            public byte cAccumAlphaBits;
            public byte cDepthBits;
            public byte cStencilBits;
            public byte cAuxBuffers;
            public sbyte iLayerType;
            public byte bReserved;
            public uint dwLayerMask;
            public uint dwVisibleMask;
            public uint dwDamageMask;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

        [DllImport("gdi32.dll")]
        public static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR ppfd);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SwapBuffers(IntPtr hdc);

        [DllImport("opengl32.dll")]
        public static extern IntPtr wglCreateContext(IntPtr hdc);

        [DllImport("opengl32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool wglDeleteContext(IntPtr hglrc);

        [DllImport("opengl32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

        [DllImport("opengl32.dll", CharSet = CharSet.Ansi)]
        public static extern IntPtr wglGetProcAddress(string name);
    }
}

