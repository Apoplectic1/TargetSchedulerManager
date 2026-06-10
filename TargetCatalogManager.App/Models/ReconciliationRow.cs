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

/// <summary>Which plane one grid row carries: the TS plan numbers or the disk actuals.</summary>
public enum RowPlane
{
    /// <summary>A TS plan row — Desired/Acq/Acc; Hours = desired × seconds (planned commitment).</summary>
    Ts,

    /// <summary>A disk actuals row — the frame count; Hours = count × seconds (actual integration).</summary>
    Disk,
}

/// <summary>
/// One grid row = one plane of one (target, filter, purpose, exposure seconds) cell. A cell with both a TS
/// plan and disk frames yields two adjacent rows (TS above Disk), so every row carries exactly one
/// <see cref="Hours"/> semantics — planned commitment or actual integration — instead of a horizontal Δ.
/// Immutable — the grid reloads wholesale (fresh scan), it never mutates rows in place. Numeric properties
/// stay typed for sorting; the <c>*Text</c> properties are what the XAML binds for display ("—" where the
/// row's plane has nothing, like an empty DataGridView cell).
/// </summary>
public sealed class ReconciliationRow(
    string target,
    string project,
    string filter,
    string purpose,
    int seconds,
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

    /// <summary>Whole-second sub length (plan-effective or disk bucket); 0 = unknown.</summary>
    public int Seconds { get; } = seconds;

    /// <summary>The target's classification — drives the source dropdown and the group header's label.</summary>
    public RowSource Source { get; } = source;

    /// <summary>Which plane this row carries; leaf rows show it in the Source column ("TS"/"Disk").</summary>
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

    /// <summary>
    /// The row's time total in decimal hours — desired × seconds on a TS row (planned commitment),
    /// frames × seconds on a Disk row (actual integration). Null when the sub length is unknown.
    /// </summary>
    public double? Hours => Seconds <= 0
        ? null
        : (Plane == RowPlane.Ts ? Desired ?? 0 : Disk) * Seconds / 3600.0;

    public string SourceText => Plane == RowPlane.Ts ? "TS" : "Disk";

    public string SecondsText => Seconds > 0 ? Seconds.ToString() : "—";
    public string DesiredText => Desired?.ToString() ?? "—";
    public string AcquiredText => Acquired?.ToString() ?? "—";
    public string AcceptedText => Accepted?.ToString() ?? "—";
    public string DiskText => Plane == RowPlane.Disk ? Disk.ToString() : "—";
    public string HoursText => Hours is double h ? h.ToString("F1") : "—";
    public string PlanCountText => PlanCount > 1 ? $"×{PlanCount}" : string.Empty;

    /// <summary>Case-insensitive match against the searchable columns.</summary>
    public bool Matches(string search) =>
        Target.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Project.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Filter.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Badge.Contains(search, StringComparison.OrdinalIgnoreCase);
}
