using Microsoft.UI.Xaml;

namespace TargetCatalogManager.App;

/// <summary>
/// Application entry (WinUI's generated <c>Main</c> calls this) — the analogue of
/// <c>Application.Run(new MainForm())</c> in WinForms, split into XAML init + window activation.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
