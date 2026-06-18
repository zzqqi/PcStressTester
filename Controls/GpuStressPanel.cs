using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Color = Avalonia.Media.Color;
using Control = Avalonia.Controls.Control;
using Pen = Avalonia.Media.Pen;
using Point = Avalonia.Point;

namespace PcStressTester.Controls;

public sealed class GpuStressPanel : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<GpuStressPanel, bool>(nameof(IsActive));

    public static readonly StyledProperty<int> IntensityPercentProperty =
        AvaloniaProperty.Register<GpuStressPanel, int>(nameof(IntensityPercent), 10);

    private readonly DispatcherTimer _renderTimer;
    private readonly IBrush[] _brushes;
    private readonly Pen[] _pens;
    private int _frame;

    public GpuStressPanel()
    {
        _brushes =
        [
            new SolidColorBrush(Color.FromArgb(190, 0, 195, 255)),
            new SolidColorBrush(Color.FromArgb(180, 255, 80, 120)),
            new SolidColorBrush(Color.FromArgb(170, 80, 235, 130)),
            new SolidColorBrush(Color.FromArgb(160, 255, 220, 80)),
            new SolidColorBrush(Color.FromArgb(150, 170, 120, 255))
        ];

        _pens =
        [
            new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), 1),
            new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), 1),
            new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 195, 255)), 2)
        ];

        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(8)
        };
        _renderTimer.Tick += (_, _) =>
        {
            _frame++;
            InvalidateVisual();
        };
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public int IntensityPercent
    {
        get => GetValue(IntensityPercentProperty);
        set => SetValue(IntensityPercentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsActiveProperty)
        {
            if (IsActive)
                _renderTimer.Start();
            else
                _renderTimer.Stop();

            InvalidateVisual();
        }

        if (change.Property == IntensityPercentProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var rect = new Rect(Bounds.Size);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 18, 22)), null, rect);

        if (!IsActive)
            return;

        double width = Math.Max(1, Bounds.Width);
        double height = Math.Max(1, Bounds.Height);
        int intensity = Math.Clamp(IntensityPercent, 10, 100);
        double areaFactor = Math.Clamp((width * height) / 250_000d, 1d, 12d);
        int shapeCount = (int)(700 * areaFactor) + intensity * 90;
        int lineCount = (int)(160 * areaFactor) + intensity * 20;
        int glowCount = (int)(50 * areaFactor) + intensity * 8;
        double time = _frame * 0.07;

        for (int i = 0; i < glowCount; i++)
        {
            double angle = time * (0.4 + i * 0.005) + i * 0.21;
            double radiusX = width * (0.18 + PositiveWave(i, 0.13) * 0.32);
            double radiusY = height * (0.15 + PositiveWave(i, 0.17) * 0.30);
            double centerX = width / 2 + Math.Cos(angle) * radiusX * 0.65;
            double centerY = height / 2 + Math.Sin(angle * 1.3) * radiusY * 0.65;
            double size = 60 + PositiveWave(i, 0.91) * (40 + intensity * 1.4);
            var glowBrush = _brushes[(i + _frame / 2) % _brushes.Length];

            context.DrawEllipse(glowBrush, null, new Point(centerX, centerY), size, size * 0.65);
        }

        for (int i = 0; i < shapeCount; i++)
        {
            double seed = i * 12.9898 + _frame * 0.21;
            double x = PositiveWave(seed, 78.233) * width;
            double y = PositiveWave(seed, 37.719) * height;
            double size = 4 + PositiveWave(seed, 19.137) * 28;
            double wobble = Math.Sin(time + i * 0.13) * 14;
            var brush = _brushes[(i + _frame) % _brushes.Length];

            context.DrawRectangle(
                brush,
                _pens[i % _pens.Length],
                new Rect((x + wobble) % width, y, size, size));

            if (i % 4 == 0)
            {
                var p1 = new Point(x, y);
                var p2 = new Point((x + size * 3 + wobble) % width, (y + size * 2) % height);
                context.DrawLine(_pens[(i / 4) % _pens.Length], p1, p2);
            }
        }

        for (int i = 0; i < lineCount; i++)
        {
            double t = time + i * 0.035;
            double x1 = PositiveWave(t, 1.91) * width;
            double y1 = PositiveWave(t + 3.4, 1.37) * height;
            double x2 = PositiveWave(t + 5.2, 0.87) * width;
            double y2 = PositiveWave(t + 7.1, 1.19) * height;
            context.DrawLine(_pens[(i + _frame) % _pens.Length], new Point(x1, y1), new Point(x2, y2));
        }

        int gridStep = Math.Max(14, 38 - intensity / 4);
        for (double x = 0; x < width; x += gridStep)
        {
            double wobble = Math.Sin(time + x * 0.02) * (10 + intensity * 0.4);
            context.DrawLine(_pens[2], new Point(x + wobble, 0), new Point(x - wobble, height));
        }

        for (double y = 0; y < height; y += gridStep)
        {
            double wobble = Math.Cos(time * 0.9 + y * 0.018) * (10 + intensity * 0.4);
            context.DrawLine(_pens[1], new Point(0, y + wobble), new Point(width, y - wobble));
        }
    }

    private static double PositiveWave(double value, double multiplier)
    {
        return Math.Abs(Math.Sin(value * multiplier));
    }
}
