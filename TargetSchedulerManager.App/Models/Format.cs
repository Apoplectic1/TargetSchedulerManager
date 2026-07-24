using Astronomy.Catalog.Scan;

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

    /// <summary>The badge/status when-format: bare HH:mm today, "ddd HH:mm" otherwise.</summary>
    public static string When(DateTimeOffset at) =>
        at.LocalDateTime.Date == DateTime.Today ? at.LocalDateTime.ToString("HH:mm") : at.LocalDateTime.ToString("ddd HH:mm");

    /// <summary>"H @900s" (Light) / "B Stars @60s" — the grid/write-back cell naming convention.</summary>
    public static string Cell(string filter, FilterPurpose purpose, int seconds) =>
        purpose == FilterPurpose.Light ? $"{filter} @{seconds}s" : $"{filter} {purpose} @{seconds}s";

    /// <summary>The " · "-joined identity label ("M 81 · Ha"; "Mosaic - X · Panel 01of16"). The OUTPUT is
    /// contract: these labels persist in the edit journal and surface in the push review — the
    /// convention can gain call sites but its shape must not drift.</summary>
    public static string Label(string left, string right) => $"{left} · {right}";
}
