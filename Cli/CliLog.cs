namespace TargetCatalogManager.Cli;

/// <summary>
/// Append-only audit log at <c>%APPDATA%\TargetCatalogManager\Logs\tcm-cli.log</c>: every writeback run's
/// decisions (writes with old→new counts and flags, manual groups, unplanned buckets, verify results) in
/// grep-friendly <c>key=value</c> lines, so "what did the CLI do to the TS db, and when" is always answerable
/// after the fact. Best-effort: IO failures are swallowed — logging must never break a run. Deliberately
/// separate from the WinUI app's session-rotated <c>tcm.log</c> in the same folder.
/// </summary>
internal static class CliLog
{
    private static readonly string sPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TargetCatalogManager", "Logs", "tcm-cli.log");

    private static readonly object sGate = new();

    public static void Line(string message)
    {
        try
        {
            lock (sGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sPath)!);
                File.AppendAllText(sPath, $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Best-effort — a logging failure must never escalate.
        }
    }
}
