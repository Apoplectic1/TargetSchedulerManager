using System.Runtime.CompilerServices;

namespace TargetCatalogManager.App.Tests;

internal static class TestEnv
{
    /// <summary>Runs before anything else in this assembly: a Debug build of the app defaults TCM_DIAG to
    /// all-on, and VM toggles would otherwise append DIAG lines to the user's real session log
    /// (%APPDATA%\TargetCatalogManager\Logs\tcm.log — the file they read their Ctrl+N notes from).</summary>
    [ModuleInitializer]
    internal static void Init() => Environment.SetEnvironmentVariable("TCM_DIAG", "");
}
