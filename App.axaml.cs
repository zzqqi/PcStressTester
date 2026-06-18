using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PcStressTester.ViewModels;
using PcStressTester.Views;
using System;
using System.Threading.Tasks;

namespace PcStressTester;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splashWindow = new SplashWindow();
            desktop.MainWindow = splashWindow;
            _ = ShowMainWindowAfterSplashAsync(desktop, splashWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ShowMainWindowAfterSplashAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splashWindow)
    {
        await Task.Delay(350);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            splashWindow.SetStatus("Загрузка датчиков и базы данных...");
        });

        await Task.Delay(150);

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var mainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel()
                };

                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                splashWindow.Close();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                splashWindow.SetStatus($"Ошибка запуска: {ex.Message}");
            });
        }
    }
}
