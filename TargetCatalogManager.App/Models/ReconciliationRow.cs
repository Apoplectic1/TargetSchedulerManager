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

/// <summary>
/// One grid row = one (target, filter, purpose) cell: the TS plan numbers next to the disk-ACTUAL count.
/// Immutable — the grid reloads wholesale (fresh scan), it never mutates rows in place. Numeric properties
/// stay <see cref="int"/>-typed for sorting; the <c>*Text</c> properties are what the XAML binds for display
/// ("—" where a side has nothing, like an empty DataGridView cell).
/// </summary>
public sealed class ReconciliationRow(
    string target,
    string project,
    string filter,
    string purpose,
    RowSource source,
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
    public RowSource Source { get; } = source;

    /// <summary>Summed <c>desired</c> across the cell's plans; null when the target has no TS plan.</summary>
    public int? Desired { get; } = desired;

    /// <summary>Summed TS <c>acquired</c> (the cached column write-back owns); null when no TS plan.</summary>
    public int? Acquired { get; } = acquired;

    /// <summary>Summed TS <c>accepted</c> (cached column); null when no TS plan.</summary>
    public int? Accepted { get; } = accepted;

    /// <summary>Frames on disk for this cell (ACTUAL — ground truth).</summary>
    public int Disk { get; } = disk;

    /// <summary>TS plans contributing to this cell (&gt;1 = mosaic fold, alias fold, or a same-purpose multi-plan).</summary>
    public int PlanCount { get; } = planCount;

    /// <summary>Match-state badges for the row's target ("alias", "duplicate", "name≠", "mosaic", …); empty when clean.</summary>
    public string Badge { get; } = badge;

    /// <summary>True when the target needs human attention (duplicate / name-mismatch / ambiguous / multi-plan).</summary>
    public bool IsFlagged { get; } = isFlagged;

    /// <summary>Disk − Desired; null when there is no goal to compare against.</summary>
    public int? Delta => Desired is int d ? Disk - d : null;

    public string SourceText => Source switch
    {
        RowSource.Both => "Both",
        RowSource.TsOnly => "TS",
        _ => "Disk",
    };

    public string DesiredText => Desired?.ToString() ?? "—";
    public string AcquiredText => Acquired?.ToString() ?? "—";
    public string AcceptedText => Accepted?.ToString() ?? "—";
    public string DiskText => Disk.ToString();
    public string DeltaText => Delta switch { null => "—", > 0 => $"+{Delta}", _ => Delta.Value.ToString() };
    public string PlanCountText => PlanCount > 1 ? $"×{PlanCount}" : string.Empty;

    /// <summary>Case-insensitive match against the searchable columns.</summary>
    public bool Matches(string search) =>
        Target.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Project.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Filter.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Badge.Contains(search, StringComparison.OrdinalIgnoreCase);
}
