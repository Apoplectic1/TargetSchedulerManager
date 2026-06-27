using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using TargetSchedulerManager.App.Models;

namespace TargetSchedulerManager.App.ViewModels.Rows;

/// <summary>
/// The shared shape of a collapsible header row — a target group (<see cref="TargetGroupRow"/>) or a mosaic
/// panel (<see cref="PanelGroupRow"/>): the column aggregates over its leaves (recomputed each filter pass so
/// the sums always match what expanding reveals), the disclosure chevron, and the in-place
/// <see cref="Recompute"/> after an inline edit. Every display rule lives here once, so a fill/format change
/// can't drift between the two header levels; the concretes add only their own identity + commands. A base
/// class (not an interface) because WinUI <c>x:Bind</c> resolves members on the template's concrete
/// <c>x:DataType</c>, which inherits these — an interface would force casts in the bindings.
/// </summary>
public abstract class AggregateHeaderRow : INotifyPropertyChanged
{
    private RowAggregates _sums;
    private bool _isExpanded;

    private protected AggregateHeaderRow(
        string target, IReadOnlyList<ReconciliationRow> children, RowSource source, bool isExpanded)
    {
        Target = target;
        Children = children;
        Source = source;
        _isExpanded = isExpanded;
        _sums = RowAggregates.Compute(children);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Target { get; }

    /// <summary>The leaf rows this header aggregates — already filtered/ordered by the view-model. For a target
    /// group that is ALL descendant leaves (across every panel of a mosaic); for a panel, the panel's leaves.</summary>
    public IReadOnlyList<ReconciliationRow> Children { get; }

    public RowSource Source { get; }

    /// <summary>Re-aggregates over the children after one was edited in place, refreshing the bound total cells
    /// (Desired/Hours) without a grid rebuild — keeps the header the literal sum of its children.</summary>
    public void Recompute()
    {
        _sums = RowAggregates.Compute(Children);
        Raise(nameof(DesiredText));
        Raise(nameof(HoursText));
        Raise(nameof(HoursBackground));
    }

    /// <summary>Summed over the TS children; null when no child has a plan (pure disk-only).</summary>
    public int? Desired => _sums.Desired;
    public int? Acquired => _sums.Acquired;
    public int? Accepted => _sums.Accepted;
    public int Disk => _sums.Disk;

    /// <summary>Sum of per-cell shortfalls max(0, desired − disk) — the "remaining" sort key.</summary>
    public int Remaining => _sums.Remaining;

    /// <summary>Disk hours − desired hours: negative = still needs telescope time; ≥ 0 = the plan's committed
    /// time was met. Null when no row carries hours.</summary>
    public double? HoursDelta => _sums.HoursDelta;

    /// <summary>Union of the children's badges (distinct, first-appearance order).</summary>
    public string Badge => _sums.Badge;

    public bool IsFlagged => _sums.IsFlagged;

    /// <summary>Frame-count delta (disk − desired) — kept as the "Sort: Δ" key; the column itself shows hours.</summary>
    public int? Delta => Desired is int d ? Disk - d : null;

    /// <summary>The only mutable state — the chevron re-renders via INPC while the view-model edits the bound
    /// list in place (insert/remove children), which is what preserves the scroll position on toggle.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            Raise(nameof(IsExpanded));
            Raise(nameof(ChevronGlyph));
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

    /// <summary>Soft theme fill behind the header's hours: caution when time is still needed, success when the
    /// plan's committed time is met. Null (no fill) when the header has no hours at all.</summary>
    public Brush? HoursBackground => HoursDelta switch
    {
        null => null,
        < 0 => ThemeBrushes.Caution,
        _ => ThemeBrushes.Success,
    };
}
