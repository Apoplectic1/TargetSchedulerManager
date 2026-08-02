using Microsoft.UI.Xaml;
using Astronomy.Diagnostics;
using TargetSchedulerManager.App.Services;
using static TargetSchedulerManager.App.Shared.UiTask;   // FireAndLog — the fire-and-forget seam (review N3)

namespace TargetSchedulerManager.App;

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
        // Configure the shared diagnostics log for this app, then rotate so each run's trail is self-contained.
        // Diag channels default to all in Debug / off in Release; TSM_DIAG overrides either at runtime.
#if DEBUG
        const DiagDefault diag = DiagDefault.All;
#else
        const DiagDefault diag = DiagDefault.None;
#endif
        Log.Init(new AppLogIdentity("TargetSchedulerManager", "tsm.log", "TSM_DIAG", diag));
        Log.StartNewSession();
        _window = new MainWindow();
        _window.Activate();
        // Startup-only update check (spec self-update): fire-and-forget so the load pipeline is
        // never delayed; a dev (non-installed) run makes this a no-op inside the service.
        FireAndLog(() => UpdateService.CheckOnStartupAsync(_window), "startup update check");
    }
}
