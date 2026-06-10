using System.ComponentModel;
using Microsoft.UI.Xaml;
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
    /// <summary>A TS plan row — Desired/Acq/Acc; Hours = −(desired × seconds): the commitment shown as the
    /// deficit it contributes to its parent's total.</summary>
    Ts,

    /// <summary>A disk actuals row — the frame count; Hours = +(count × seconds) (actual integration).</summary>
    Disk,

    /// <summary>A merged plan+actuals rollup. Hours = disk − desired hours (filled caution/green); when
    /// the sub lengths aren't all one value the Seconds cell reads "mixed" (caution pill) and the row
    /// expands into its one-plane <see cref="ReconciliationRow.Detail"/> source lines.</summary>
    Both,
}

/// <summary>
/// One grid row = one (target, filter, purpose) cell or one plane of it. A filter carrying both a plan and
/// disk frames is a single <see cref="RowPlane.Both"/> rollup of every sub length; when those sub lengths
/// don't all agree the rollup gets a disclosure chevron and <see cref="Detail"/> holds one source line per
/// sub length, seconds ascending (a bucket with both planes is a nested Both line). Rows are immutable except
/// <see cref="IsExpanded"/>, which the view-model flips while editing the bound list in place (keeping the
/// scroll position). The <c>*Text</c> properties are what the XAML binds for display ("—" where the row's
/// plane has nothing, like an empty DataGridView cell).
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
    bool isFlagged,
    double? planHours,
    double? diskHours,
    bool secondsMixed = false,
    bool isDetail = false,
    IReadOnlyList<ReconciliationRow>? detail = null) : INotifyPropertyChanged
{
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Target { get; } = target;
    public string Project { get; } = project;
    public string Filter { get; } = filter;
    public string Purpose { get; } = purpose;

    /// <summary>The plan side's whole-second sub length (representative when mixed); 0 = none/unknown.</summary>
    public int PlanSeconds { get; } = planSeconds;

    /// <summary>The disk side's whole-second sub length (representative when mixed); 0 = none.</summary>
    public int DiskSeconds { get; } = diskSeconds;

    /// <summary>The target's classification — drives the source dropdown and the group header's label.</summary>
    public RowSource Source { get; } = source;

    /// <summary>Which plane(s) this row carries; leaf rows show it in the Source column.</summary>
    public RowPlane Plane { get; } = plane;

    /// <summary>Summed <c>desired</c> across the row's plans; null on Disk rows.</summary>
    public int? Desired { get; } = desired;

    /// <summary>Summed TS <c>acquired</c> (the cached column write-back owns); null on Disk rows.</summary>
    public int? Acquired { get; } = acquired;

    /// <summary>Summed TS <c>accepted</c> (cached column); null on Disk rows.</summary>
    public int? Accepted { get; } = accepted;

    /// <summary>Frames on disk (ACTUAL — ground truth); 0 on TS rows.</summary>
    public int Disk { get; } = disk;

    /// <summary>TS plans contributing (&gt;1 = mosaic fold, alias fold, or a same-purpose multi-plan).</summary>
    public int PlanCount { get; } = planCount;

    /// <summary>Match-state badges for the row's target ("alias", "duplicate", "name≠", "mosaic", …); empty when clean.</summary>
    public string Badge { get; } = badge;

    /// <summary>True when the target needs human attention (duplicate / name-mismatch / ambiguous / multi-plan).</summary>
    public bool IsFlagged { get; } = isFlagged;

    /// <summary>Planned commitment in decimal hours, summed per sub length by the loader; null without a plan side.</summary>
    public double? PlanHours { get; } = planHours;

    /// <summary>Actual integration in decimal hours, summed per sub length by the loader; null without a disk side.</summary>
    public double? DiskHours { get; } = diskHours;

    /// <summary>True when the rollup's sub lengths aren't all one identical value (2+ distinct times
    /// across the plan and disk sides) — the Seconds cell reads "mixed" and the row is expandable.</summary>
    public bool SecondsMixed { get; } = secondsMixed;

    /// <summary>True for a one-plane source line living under a rollup's disclosure (extra indent).</summary>
    public bool IsDetail { get; } = isDetail;

    /// <summary>The rollup's one-plane source lines; null when the row has nothing to disclose.</summary>
    public IReadOnlyList<ReconciliationRow>? Detail { get; } = detail;

    /// <summary>Expansion state of a rollup's disclosure; owned by the view-model (set restored per pass).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChevronGlyph)));
        }
    }

    /// <summary>
    /// What the Hours column shows — every row's SIGNED contribution to its parent's total, so parents are
    /// the literal sum of their children: a TS row is the unmet commitment (−desired × seconds), a Disk row
    /// the captured time (+frames × seconds), a Both rollup their gap (disk − desired hours).
    /// </summary>
    public double? Hours => Plane switch
    {
        RowPlane.Ts => PlanHours is double ph ? -ph : null,
        RowPlane.Disk => DiskHours,
        _ => DiskHours is double dh && PlanHours is double ph ? dh - ph : null,
    };

    /// <summary>Segoe Fluent Icons chevron for expandable rollups; empty otherwise.</summary>
    public string ChevronGlyph => Detail is null ? "" : _isExpanded ? "\uE70D" : "\uE76C";

    public Visibility ChevronVisibility => Detail is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Indent ladder for the Source column: rollups carry their chevron at the leaf level, plain
    /// leaf text aligns just past it, and detail source lines step in once more.</summary>
    public Thickness SourceMargin => Detail is not null
        ? new Thickness(18, 0, 0, 0)
        : IsDetail ? new Thickness(50, 0, 0, 0) : new Thickness(36, 0, 0, 0);

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
        _ when SecondsMixed => "mixed",
        _ => PlanSeconds.ToString(),
    };

    /// <summary>Caution pill behind the Seconds cell when a rollup's sub lengths are mixed.</summary>
    public Brush? SecondsBackground =>
        Plane == RowPlane.Both && SecondsMixed ? ThemeBrushes.Caution : null;

    public string DesiredText => Desired?.ToString() ?? "—";
    public string AcquiredText => Acquired?.ToString() ?? "—";
    public string AcceptedText => Accepted?.ToString() ?? "—";
    public string DiskText => Plane == RowPlane.Ts ? "—" : Disk.ToString();

    public string HoursText => Hours switch
    {
        null => "—",
        double h when Plane == RowPlane.Both && h > 0 => $"+{Format.Hours(h)}",
        double h => Format.Hours(h),
    };

    /// <summary>Fill behind the Hours cell: gap rows follow the sign rule (caution = needs time, green =
    /// goal met); TS rows are always commitments, so caution when outstanding and the error fill for a
    /// desired-0 plan (data that shouldn't exist). Disk rows stay plain — quiet positive facts.</summary>
    public Brush? HoursBackground => Plane switch
    {
        RowPlane.Both when Hours is double h => h < 0 ? ThemeBrushes.Caution : ThemeBrushes.Success,
        RowPlane.Ts when Hours is double h => h < 0 ? ThemeBrushes.Caution : ThemeBrushes.Critical,
        _ => null,
    };

    public string PlanCountText => PlanCount > 1 ? $"×{PlanCount}" : string.Empty;

    /// <summary>Case-insensitive match against the searchable columns.</summary>
    public bool Matches(string search) =>
        Target.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Project.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Filter.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Badge.Contains(search, StringComparison.OrdinalIgnoreCase);
}
