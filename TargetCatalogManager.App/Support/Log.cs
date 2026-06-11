using System.Reflection;

namespace TargetCatalogManager.App.Support;

/// <summary>
/// Append-only diagnostic log at <c>%APPDATA%\TargetCatalogManager\Logs\tcm.log</c>, ported from
/// TargetPlanner's proven design. Best-effort: any exception writing the log is swallowed so logging
/// failures can never cascade into hard errors in the caller.
///
/// Diagnostic channels (<see cref="Diag"/>) ride the same file with category prefixes so consumers can
/// grep/filter. Categories toggle via the <c>TCM_DIAG</c> environment variable: comma-separated list, or
/// <c>"*"</c> for all. Debug builds default to all; Release defaults to none (no diag overhead).
/// Planned categories: "Load" (scan + TS read + resolve), "UI" (filter/sort), "Write" (M2 TS edits).
///
/// USER_OBS_* lines come from the Ctrl+N observation window (<see cref="ObservationWindow"/>): START/END
/// (or CANCEL) markers share a short id, chronologically bracketing whatever the user did while it was
/// open — <c>grep id=&lt;short&gt; tcm.log</c> surfaces the full investigation window.
/// </summary>
internal static class Log
{
    private static readonly string sPath = ComputePath();
    private static readonly object sGate = new();
    // null sentinel = "all categories enabled"; empty set = "none enabled".
    private static readonly HashSet<string>? sEnabledCategories = ResolveEnabledCategories();

    public static string FilePath => sPath;

    /// <summary>The user-visible folder holding tcm.log, tcm.log.prev, screenshots/, screenshots.prev/ —
    /// one delete clears every captured artifact.</summary>
    public static string NotesFolderPath => Path.GetDirectoryName(sPath)!;

    /// <summary>Rotate tcm.log → tcm.log.prev and screenshots/ → screenshots.prev/ (each overwriting any
    /// previous rotation), then start a fresh log. Called once at app startup so each run's trail is
    /// self-contained and the disk footprint stays bounded at one session back; .prev screenshot paths
    /// referenced in tcm.log.prev still resolve.</summary>
    public static void StartNewSession()
    {
        try
        {
            lock (sGate)
            {
                string dir = NotesFolderPath;
                Directory.CreateDirectory(dir);
                string prevPath = sPath + ".prev";
                if (File.Exists(sPath))
                {
                    if (File.Exists(prevPath)) File.Delete(prevPath);
                    File.Move(sPath, prevPath);
                }
                File.WriteAllText(sPath, $"{DateTimeOffset.Now:o} INFO new session build={TryGetBuildVersion()}{Environment.NewLine}");

                string shotsDir = Path.Combine(dir, "screenshots");
                string shotsPrev = Path.Combine(dir, "screenshots.prev");
                if (Directory.Exists(shotsDir))
                {
                    if (Directory.Exists(shotsPrev)) Directory.Delete(shotsPrev, recursive: true);
                    Directory.Move(shotsDir, shotsPrev);
                }
            }
        }
        catch
        {
            // Best-effort — a logging failure must never escalate.
        }
    }

    /// <summary>Always-on audit/info line (not <c>TCM_DIAG</c>-gated) — e.g. the standing M2 rule that every TS
    /// write is recorded, so the trail survives in Release where diag is off.</summary>
    public static void Info(string message) => Append("INFO", message, null);

    public static void Warn(string message) => Append("WARN", message, null);

    public static void Warn(string message, Exception ex) => Append("WARN", message, ex);

    public static void Error(string message) => Append("ERROR", message, null);

    public static void Error(string message, Exception ex) => Append("ERROR", message, ex);

    /// <summary>True when <paramref name="category"/> is enabled. Cheap; check before building expensive messages.</summary>
    public static bool IsDiagEnabled(string category) =>
        sEnabledCategories is null || sEnabledCategories.Contains(category);

    /// <summary>Append a diag line tagged with <paramref name="category"/>; no-op when disabled.
    /// Keep messages short and structured (key=value pairs) so grep filtering stays useful.</summary>
    public static void Diag(string category, string message)
    {
        if (!IsDiagEnabled(category)) return;
        Append("DIAG/" + category, message, null);
    }

    /// <summary>Mark the moment the observation window opened; the matching END/CANCEL carries the same id.</summary>
    public static void UserObservationStart(string id) =>
        Append("USER_OBS_START", $"id={id} build={TryGetBuildVersion()}", null);

    /// <summary>Close an observation window: app-state snapshot (<paramref name="ctx"/>), screenshot path
    /// (empty when capture failed), and the user's notes. Newlines/quotes in notes are escaped so one
    /// observation stays one grep-friendly line; blank notes log as "(checkpoint)" — the all-okay gesture.</summary>
    public static void UserObservationEnd(string id, string ctx, string notes, string screenshotPath)
    {
        string bodyNotes = string.IsNullOrWhiteSpace(notes)
            ? "(checkpoint)"
            : notes
                .Replace("\\", "\\\\")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\n")
                .Replace("\"", "\\\"");
        Append("USER_OBS_END", $"id={id} ctx=({ctx}) screenshot={screenshotPath} notes=\"{bodyNotes}\"", null);
    }

    /// <summary>Observation window abandoned (Cancel or close-X); every START gets a terminator.</summary>
    public static void UserObservationCancel(string id) => Append("USER_OBS_CANCEL", "id=" + id, null);

    private static void Append(string level, string message, Exception? ex)
    {
        try
        {
            lock (sGate)
            {
                Directory.CreateDirectory(NotesFolderPath);
                // Local time with offset ("o" keeps it sortable + unambiguous): the user reads these
                // alongside their own notes, and UTC rolls a day ahead during evening sessions.
                string body = ex is null
                    ? $"{DateTimeOffset.Now:o} {level} {message}{Environment.NewLine}"
                    : $"{DateTimeOffset.Now:o} {level} {message}: {ex}{Environment.NewLine}";
                File.AppendAllText(sPath, body);
            }
        }
        catch
        {
            // Best-effort — a logging failure must never escalate.
        }
    }

    private static string ComputePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "TargetCatalogManager", "Logs", "tcm.log");
    }

    private static string TryGetBuildVersion()
    {
        try
        {
            Assembly? asm = Assembly.GetEntryAssembly();
            string? infoVer = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVer)) return infoVer;
            return asm?.GetName().Version?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static HashSet<string>? ResolveEnabledCategories()
    {
        string? env = Environment.GetEnvironmentVariable("TCM_DIAG");
        if (env is not null)
        {
            string trimmed = env.Trim();
            if (trimmed == "*") return null;  // null sentinel = all
            return new HashSet<string>(
                trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
        }
#if DEBUG
        return null;  // default in Debug: all categories enabled
#else
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);  // default in Release: off
#endif
    }
}
