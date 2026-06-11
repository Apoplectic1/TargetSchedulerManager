using System.Runtime.CompilerServices;

namespace TargetSchedulerManager.App.Tests;

internal static class TestEnv
{
    /// <summary>Runs before anything else in this assembly: a Debug build of the app defaults TSM_DIAG to
    /// all-on, and VM toggles would otherwise append DIAG lines to the user's real session log
    /// (%APPDATA%\TargetSchedulerManager\Logs\tsm.log — the file they read their Ctrl+N notes from).</summary>
    [ModuleInitializer]
    internal static void Init() => Environment.SetEnvironmentVariable("TSM_DIAG", "");
}
