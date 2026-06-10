using Microsoft.UI.Xaml.Media;

namespace TargetCatalogManager.App.Models;

/// <summary>Which side(s) of the reconciliation a row's target exists on.</summary>
public enum RowSource
{
    /// <summary>On disk and in TS (resolved onto one canonical target).</summary>
    Both,

    /// <summary>In TS only — planned, nothing captured yet (or it failed to anchor to disk).</summary>
    TsOnly,

    /// <summary>On disk only — captured, but no TS plan exists.</summary>
    DiskOnly,
}

/// <summary>Which plane(s) of its cell one grid row carries.</summary>
public enum RowPlane
{
    /// <summary>A TS plan row — Desired/Acq/Acc; Hours = desired × seconds (planned commitment).</summary>
    Ts,

    /// <summary>A disk actuals row — the frame count; Hours = count × seconds (actual integration).</summary>
    Disk,

    /// <summary>A merged plan+actuals row. Hours = disk − desired hours (filled caution/green); the
    /// Seconds cell shows "disk≠plan" in a caution pill when the paired sub lengths drifted.</summary>
    Both,
}

/// <summary>
/// One grid row = one (target, filter, purpose) cell, split or merged by plane: a filter with both a plan
/// and disk frames pairs back into one <see cref="RowPlane.Both"/> row (exact-seconds pairs first, then a
/// lone plan with a lone disk bucket even across drifted sub lengths); unpaired sides stay one-plane rows.
/// Immutable — the grid reloads wholesale (fresh scan), it never mutates rows in place. Numeric properties
/// stay typed for sorting; the <c>*Text</c> properties are what the XAML binds for display ("—" where the
/// row's plane has nothing, like an empty DataGridView cell).
/// </summary>
public sealed class ReconciliationRow(
    string target,
    string project,
    string filter,
    string purpose,
    int planSeconds,
    int diskSeconds,
    RowSource source,
    RowPlane plane,
    int? desired,
    int? acquired,
    int? accepted,
    int disk,
    int planCount,
    string badge,
    bool isFlagged)
{
    public string Target { get; } = target;
    public string Project { get; } = project;
    public string Filter { get; } = filter;
    public string Purpose { get; } = purpose;

    /// <summary>The plan's whole-second sub length (effective: plan value, else template default); 0 = none/unknown.</summary>
    public int PlanSeconds { get; } = planSeconds;

    /// <summary>The disk bucket's whole-second sub length; 0 = none.</summary>
    public int DiskSeconds { get; } = diskSeconds;

    /// <summary>The target's classification — drives the source dropdown and the group header's label.</summary>
    public RowSource Source { get; } = source;

    /// <summary>Which plane(s) this row carries; leaf rows show it in the Source column.</summary>
    public RowPlane Plane { get; } = plane;

    /// <summary>Summed <c>desired</c> across the cell's plans; null on Disk rows.</summary>
    public int? Desired { get; } = desired;

    /// <summary>Summed TS <c>acquired</c> (the cached column write-back owns); null on Disk rows.</summary>
    public int? Acquired { get; } = acquired;

    /// <summary>Summed TS <c>accepted</c> (cached column); null on Disk rows.</summary>
    public int? Accepted { get; } = accepted;

    /// <summary>Frames on disk for this cell (ACTUAL — ground truth); 0 on TS rows.</summary>
    public int Disk { get; } = disk;

    /// <summary>TS plans contributing to this cell (&gt;1 = mosaic fold, alias fold, or a same-purpose multi-plan).</summary>
    public int PlanCount { get; } = planCount;

    /// <summary>Match-state badges for the row's target ("alias", "duplicate", "name≠", "mosaic", …); empty when clean.</summary>
    public string Badge { get; } = badge;

    /// <summary>True when the target needs human attention (duplicate / name-mismatch / ambiguous / multi-plan).</summary>
    public bool IsFlagged { get; } = isFlagged;

    /// <summary>Planned commitment in decimal hours (desired × plan seconds); null without a plan side.</summary>
    public double? PlanHours =>
        Desired is int d && PlanSeconds > 0 ? d * PlanSeconds / 3600.0 : null;

    /// <summary>Actual integration in decimal hours (frames × disk seconds); null without a disk side.</summary>
    public double? DiskHours =>
        Plane != RowPlane.Ts && DiskSeconds > 0 ? Disk * DiskSeconds / 3600.0 : null;

    /// <summary>
    /// What the Hours column shows: the plane's own time on one-plane rows; on a Both row, the gap
    /// (disk − desired hours) — the per-filter version of the group header's delta.
    /// </summary>
    public double? Hours => Plane switch
    {
        RowPlane.Ts => PlanHours,
        RowPlane.Disk => DiskHours,
        _ => DiskHours is double dh && PlanHours is double ph ? dh - ph : null,
    };

    public string SourceText => Plane switch
    {
        RowPlane.Ts => "TS",
        RowPlane.Disk => "Disk",
        _ => "Both",
    };

    public string SecondsText => Plane switch
    {
        RowPlane.Ts => PlanSeconds > 0 ? PlanSeconds.ToString() : "—",
        RowPlane.Disk => DiskSeconds > 0 ? DiskSeconds.ToString() : "—",
        _ => DiskSeconds == PlanSeconds ? PlanSeconds.ToString() : $"{DiskSeconds}≠{PlanSeconds}",
    };

    /// <summary>Caution pill behind the Seconds cell when a Both row's paired sub lengths drifted.</summary>
    public Brush? SecondsBackground =>
        Plane == RowPlane.Both && DiskSeconds != PlanSeconds ? ThemeBrushes.Caution : null;

    public string DesiredText => Desired?.ToString() ?? "—";
    public string AcquiredText => Acquired?.ToString() ?? "—";
    public string AcceptedText => Accepted?.ToString() ?? "—";
    public string DiskText => Plane == RowPlane.Ts ? "—" : Disk.ToString();

    public string HoursText => Hours switch
    {
        null => "—",
        double h when Plane == RowPlane.Both && h > 0 => $"+{h:F1}",
        double h => h.ToString("F1"),
    };

    /// <summary>Caution/green fill behind a Both row's hours gap (needs time vs goal met); plain on one-plane rows.</summary>
    public Brush? HoursBackground =>
        Plane == RowPlane.Both && Hours is double h
            ? (h < 0 ? ThemeBrushes.Caution : ThemeBrushes.Success)
            : null;

    public string PlanCountText => PlanCount > 1 ? $"×{PlanCount}" : string.Empty;

    /// <summary>Case-insensitive match against the searchable columns.</summary>
    public bool Matches(string search) =>
        Target.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Project.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Filter.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Badge.Contains(search, StringComparison.OrdinalIgnoreCase);
}
