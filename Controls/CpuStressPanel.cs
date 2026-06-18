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

public sealed class CpuStressPanel : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<CpuStressPanel, bool>(nameof(IsActive));

    public static readonly StyledProperty<int> IntensityPercentProperty =
        AvaloniaProperty.Register<CpuStressPanel, int>(nameof(IntensityPercent), 10);

    private readonly DispatcherTimer _renderTimer;
    private readonly Pen _gridPen = new(new SolidColorBrush(Color.FromArgb(80, 127, 217, 200)), 1);
    private readonly IBrush _idleBrush = new SolidColorBrush(Color.FromArgb(120, 35, 49, 72));
    private readonly IBrush _activeBrush = new SolidColorBrush(Color.FromArgb(220, 39, 194, 160));
    private readonly IBrush _hotBrush = new SolidColorBrush(Color.FromArgb(230, 255, 210, 90));
    private int _frame;

    public CpuStressPanel()
    {
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(24)
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

        double width = Math.Max(1, Bounds.Width);
        double height = Math.Max(1, Bounds.Height);
        var rect = new Rect(Bounds.Size);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 16, 24)), null, rect);

        int columns = 18;
        int rows = 7;
        double gap = 8;
        double cellWidth = (width - gap * (columns + 1)) / columns;
        double cellHeight = (height - gap * (rows + 1)) / rows;
        int intensity = Math.Clamp(IntensityPercent, 0, 100);
        int activeCells = IsActive ? (int)Math.Round(columns * rows * intensity / 100d) : 0;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int index = y * columns + x;
                double px = gap + x * (cellWidth + gap);
                double py = gap + y * (cellHeight + gap);
                bool isActiveCell = index < activeCells;
                bool pulse = ((index + _frame) % 11) < 3;
                var brush = isActiveCell
                    ? pulse && intensity > 70 ? _hotBrush : _activeBrush
                    : _idleBrush;

                context.DrawRectangle(
                    brush,
                    _gridPen,
                    new RoundedRect(new Rect(px, py, cellWidth, cellHeight), 7));
            }
        }

        if (!IsActive)
            return;

        double waveY = height * (0.5 + Math.Sin(_frame * 0.12) * 0.22);
        var wavePen = new Pen(new SolidColorBrush(Color.FromArgb(210, 140, 255, 198)), 3);
        for (int i = 0; i < 5; i++)
        {
            double offset = i * 28;
            context.DrawLine(
                wavePen,
                new Point(0, (waveY + offset) % height),
                new Point(width, (waveY + offset + Math.Sin(_frame * 0.08 + i) * 22) % height));
        }
    }
}
