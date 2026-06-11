using System.ComponentModel;
using TargetSchedulerManager.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TargetSchedulerManager.App.ViewModels.Rows;

/// <summary>
/// The collapsible header row for one target group: aggregates of the per-plane rows beneath it.
/// Rebuilt on every filter pass over the *visible* children, so its sums always match what expanding
/// reveals. The only mutable state is <see cref="IsExpanded"/> — the chevron re-renders via
/// INotifyPropertyChanged while the view-model edits the bound list in place (insert/remove children),
/// which is what preserves the scroll position on toggle.
/// </summary>
public sealed class TargetGroupRow : INotifyPropertyChanged
{
    private RowAggregates _sums;
    private bool _isExpanded;

    public TargetGroupRow(
        string target, IReadOnlyList<ReconciliationRow> children, bool isExpanded, bool isTargetEnabled,
        IReadOnlyList<PanelGroupRow>? panels = null)
    {
        Target = target;
        Children = children;
        Panels = panels;
        _isExpanded = isExpanded;
        IsTargetEnabled = isTargetEnabled;

        // Source and Project are per-target upstream (TargetResolver), so the first child speaks for all.
        Source = children[0].Source;
        Project = children[0].Project;

        _sums = RowAggregates.Compute(children);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Re-aggregates over the children after one was edited in place, refreshing the bound total cells
    /// (Desired/Hours) without a grid rebuild — keeps the header the literal sum of its children.</summary>
    public void Recompute()
    {
        _sums = RowAggregates.Compute(Children);
        Raise(nameof(DesiredText));
        Raise(nameof(HoursText));
        Raise(nameof(HoursBackground));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Target { get; }

    /// <summary>ALL descendant leaf rows (across every panel for a mosaic) — the header totals stay
    /// whole-target regardless of nesting. Already filtered/ordered by the view-model.</summary>
    public IReadOnlyList<ReconciliationRow> Children { get; }

    /// <summary>The collapsible panel mini-headers for a mosaic; null for a normal target.</summary>
    public IReadOnlyList<PanelGroupRow>? Panels { get; }

    public RowSource Source { get; }
    public string Project { get; }

    /// <summary>Target enable state (TS <c>target.active</c>) bound to the leftmost checkbox. The view-model
    /// passes the effective value — a pending in-session toggle if any, else the loaded state.</summary>
    public bool IsTargetEnabled { get; }

    /// <summary>Write-back key for this target's TS row; null when there is no TS target (disk-only / mosaic parent).</summary>
    public string? TsTargetKey => Children[0].TsTargetKey;

    /// <summary>Canonical target id for the detail panel — the shared id of a normal group's rows; null for a
    /// mosaic parent (a grouping node — its panels carry their own ids, so select a panel for detail).</summary>
    public Guid? TargetId => Panels is null ? Children[0].TargetId : null;

    /// <summary>True when an enable checkbox applies: a normal (non-mosaic) group backed by a TS target.</summary>
    public bool CanEnable => Panels is null && TsTargetKey is not null;

    /// <summary>Checkbox visibility — mirrors the <c>ChevronVisibility</c> pattern so XAML can x:Bind it directly.</summary>
    public Visibility CanEnableVisibility => CanEnable ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Summed over the TS children; null when no child has a plan (pure disk-only group).</summary>
    public int? Desired => _sums.Desired;
    public int? Acquired => _sums.Acquired;
    public int? Accepted => _sums.Accepted;
    public int Disk => _sums.Disk;

    /// <summary>Sum of per-cell shortfalls max(0, desired − disk) — the group-level "remaining" sort key.</summary>
    public int Remaining => _sums.Remaining;

    /// <summary>
    /// Disk hours − desired hours across all the target's rows: negative = the target still needs telescope
    /// time; ≥ 0 = the captured time met the plan's commitment. Null when no row carries hours.
    /// </summary>
    public double? HoursDelta => _sums.HoursDelta;

    /// <summary>Union of the children's badges (distinct, order of first appearance).</summary>
    public string Badge => _sums.Badge;

    /// <summary>Distinct filter names across the children (plane/sub-length splits don't inflate it).</summary>
    public int FilterCount { get; }

    public bool IsFlagged => _sums.IsFlagged;

    /// <summary>Frame-count delta (disk − desired) — kept as the "Sort: Δ" key; the column itself shows hours.</summary>
    public int? Delta => Desired is int d ? Disk - d : null;

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

    /// <summary>Segoe Fluent Icons: ChevronDown when expanded, ChevronRight when collapsed.</summary>
    public string ChevronGlyph => _isExpanded ? "\uE70D" : "\uE76C";

    public string SourceText => Source switch
    {
        RowSource.Both => "Both",
        RowSource.TsOnly => "TS",
        _ => "Disk",
    };

    public string TargetText => Panels is null
        ? Target
        : $"{Target}  ·  {Panels.Count} {(Panels.Count == 1 ? "panel" : "panels")}";
    public string DesiredText => Desired?.ToString() ?? "—";
    public string AcquiredText => Acquired?.ToString() ?? "—";
    public string AcceptedText => Accepted?.ToString() ?? "—";
    public string DiskText => Disk.ToString();

    public string HoursText => HoursDelta switch
    {
        null => "—",
        > 0 => $"+{Format.Hours(HoursDelta.Value)}",
        _ => Format.Hours(HoursDelta.Value),
    };

    /// <summary>Soft theme fill behind the header's hours: caution when time is still needed, success when
    /// the plan's committed time is met. Null (no fill) when the group has no hours at all.</summary>
    public Brush? HoursBackground => HoursDelta switch
    {
        null => null,
        < 0 => ThemeBrushes.Caution,
        _ => ThemeBrushes.Success,
    };
}
