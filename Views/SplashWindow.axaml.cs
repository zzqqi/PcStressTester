using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PcStressTester.Views;

public partial class SplashWindow : Window
{
    private TextBlock? _statusText;

    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _statusText = this.FindControl<TextBlock>("StatusText");
    }

    public void SetStatus(string status)
    {
        if (_statusText is not null)
            _statusText.Text = status;
    }
}
