using Astronomy.Catalog.Scan;
using Astronomy.Catalog.TargetScheduler;

namespace TargetSchedulerManager.App.Models;

/// <summary>The display-convention home (openspec presentation-conventions): every house text rule —
/// the em-dash empty convention, hours precision, the when-format, the cell and identity-label naming —
/// defined once and consumed by every renderer. A format change edits here, nowhere else.</summary>
internal static class Format
{
    /// <summary>The empty-cell convention: absent renders as the em dash — never blank, never a
    /// fabricated 0 (a measured zero is a fact and renders as 0; the dash means "nothing to say").</summary>
    public const string Dash = "—";

    /// <summary>A nullable count cell: the value, or the dash.</summary>
    public static string CountOrDash(int? count) => count?.ToString() ?? Dash;

    /// <summary>F1 decimal hours, except tiny non-zero values keep two decimals — 22 short 5 s frames
    /// are 0.03 h, and rendering that as "0.0" reads as missing rather than small. Zero (including
    /// negative zero from negated empty commitments) always renders "0.0".</summary>
    public static string Hours(double h) =>
        h == 0 ? "0.0"
        : Math.Abs(h) < 0.05 ? h.ToString("F2")
        : h.ToString("F1");

    /// <summary>The badge/status when-format: "yy/MM/dd hh:mm AM|PM", always the full stamp — a relative
    /// or day-name form goes stale the moment a session spans midnight or a badge survives a week
    /// (user-set 2026-08-02, obs 0cf4). Invariant culture pins the separators and AM/PM designators.</summary>
    public static string When(DateTimeOffset at) =>
        at.LocalDateTime.ToString("yy/MM/dd hh:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"H @900s" (Light) / "B Stars @60s" — the grid/write-back cell naming convention.</summary>
    public static string Cell(string filter, FilterPurpose purpose, int seconds) =>
        purpose == FilterPurpose.Light ? $"{filter} @{seconds}s" : $"{filter} {purpose} @{seconds}s";

    /// <summary>The " · "-joined identity label ("M 81 · Ha"; "Mosaic - X · Panel 01of16"). The OUTPUT is
    /// contract: these labels persist in the edit journal and surface in the push review — the
    /// convention can gain call sites but its shape must not drift.</summary>
    public static string Label(string left, string right) => $"{left} · {right}";

    /// <summary>The marker a rollup cell shows when its children disagree — the same word the Seconds cell
    /// already uses for mixed sub lengths, so one idiom covers every "these differ" cell.</summary>
    public const string Mixed = "mixed";

    /// <summary>The filter display rank — narrowband H, S, O, then broadband L, R, G, B (user 2026-08-05,
    /// obs c73e): the order a target's expanded rows read in, replacing alphabetical. Presentation only —
    /// never a matching or reconciliation key. When the filter set changes, the user re-specifies this
    /// list; it is the single edit point (codes outside it sort after every ranked one, naturally
    /// ordered among themselves — see <see cref="FilterRankIndex"/>).</summary>
    public static readonly string[] FilterRank = ["H", "S", "O", "L", "R", "G", "B"];

    /// <summary>The sort position of a filter code: its <see cref="FilterRank"/> index, or one past the
    /// rank for codes outside it (the caller breaks unranked ties naturally). Case-insensitive to match
    /// the grid's uppercased filter grouping.</summary>
    public static int FilterRankIndex(string filter)
    {
        int i = Array.FindIndex(FilterRank, f => f.Equals(filter, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : FilterRank.Length;
    }

    /// <summary>
    /// Resolves a capture directory name to its camera alias, or <see langword="null"/> when the directory
    /// names no camera we know. Matching is on the model number the directory contains, so a directory named
    /// for the model in any style resolves the same way.
    /// <para>Presentation only: the alias never enters a key, so two directory spellings of one camera stay
    /// separate buckets. Returning null is what raises the <c>camera</c> badge — an unknown camera is
    /// reported, never shown raw as though understood.</para>
    /// </summary>
    public static string? Camera(string? captureDirectory)
    {
        if (string.IsNullOrWhiteSpace(captureDirectory)) return null;
        if (captureDirectory.Contains("183", StringComparison.Ordinal)) return "Z183";
        if (captureDirectory.Contains("533", StringComparison.Ordinal)) return "Z533";
        if (captureDirectory.Contains("178", StringComparison.Ordinal)) return "Q178";
        if (captureDirectory.Contains("144", StringComparison.Ordinal)) return "A144";
        return null;
    }

    /// <summary>A camera cell: the alias, the raw directory name when it resolves to none (so the offending
    /// name is visible beside its badge), or the dash when there is no disk side at all.</summary>
    public static string CameraCell(string? captureDirectory) =>
        string.IsNullOrWhiteSpace(captureDirectory) ? Dash : Camera(captureDirectory) ?? captureDirectory;

    /// <summary>A Gain/Offset config cell: the value, or — when it is the exposure template's sentinel
    /// (schema-driven, never a hard-coded −1) — the word <c>default</c>. The cell-width form of the
    /// general render-as-meaning rule (user 2026-07-29): a sentinel never displays raw anywhere; the full
    /// label ("camera default") lives in the flyout and the old→new displays (<c>TsValueText.ForField</c>).
    /// Disk-side values are always real numbers, so this only ever fires on plan-side cells.</summary>
    public static string TemplateNumberCell(string column, int value) =>
        TsEditableSchema.Find(TsTable.ExposureTemplate, column) is { Sentinel: double s } && value == s
            ? "default"
            : value.ToString();

    /// <summary>A rotation cell: the fold-180 angle for a sky rotation ("65.1°"), the same visibly marked
    /// mechanical ("172.3°(M)" — real rotation the plan cannot be compared against), or the dash when no
    /// rotation is expressed (frames recording neither angle, or a plan without one). Never a fabricated
    /// sky value — the mechanical marking is load-bearing (openspec rotation-framing-key).</summary>
    public static string Rotation(RotationExpression? expression, double? foldDeg) => expression switch
    {
        RotationExpression.Sky when foldDeg is double s => $"{s:0.#}°",
        RotationExpression.Mechanical when foldDeg is double m => $"{m:0.#}°(M)",
        _ => Dash,
    };
}
