using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace TargetSchedulerManager.App;

/// <summary>
/// The owned entry point (<c>DISABLE_XAML_GENERATED_MAIN</c>): identical to the XAML-generated
/// <c>Main</c> except that Velopack runs first. <see cref="VelopackApp"/> services the
/// install/uninstall/update hook invocations Setup.exe and Update.exe relaunch the app with —
/// those must be handled (and exited) before any XAML, or even <c>Log.Init</c>, exists.
/// </summary>
public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }
}
