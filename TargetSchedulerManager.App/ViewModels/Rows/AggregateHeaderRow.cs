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
        Raise(nameof(CameraText));
        Raise(nameof(GainText));
        Raise(nameof(OffsetText));
        Raise(nameof(BinText));
        Raise(nameof(RotText));
        Raise(nameof(CameraBackground));
        Raise(nameof(GainBackground));
        Raise(nameof(OffsetBackground));
        Raise(nameof(BinBackground));
        Raise(nameof(RotBackground));
    }

    /// <summary>Summed over the TS children; null when no child has a plan (pure disk-only).</summary>
    public int? Desired => _sums.Desired;
    public int? Acquired => _sums.Acquired;
    public int? Accepted => _sums.Accepted;
    public int Disk => _sums.Disk;

    /// <summary>Sum of per-cell shortfalls max(0, desired − acquired) — the "remaining" sort key, on the
    /// same acquired basis as the Hours gauge so the two never disagree.</summary>
    public int Remaining => _sums.Remaining;

    // ---- Capture configuration: the shared value beneath this header, or "mixed" where children disagree.
    // Reading these before expanding tells you WHICH dimension is inconsistent.
    public string CameraText => _sums.Camera;
    public string GainText => _sums.Gain;
    public string OffsetText => _sums.Offset;
    public string BinText => _sums.Bin;
    public string RotText => _sums.Rot;

    /// <summary>Caution fill behind a configuration cell whose children disagree — the same pill the Seconds
    /// cell uses for mixed sub lengths, so one idiom covers every "these differ" cell.</summary>
    private static Brush? MixedFill(string text) => text == Format.Mixed ? ThemeBrushes.Caution : null;

    public Brush? CameraBackground => MixedFill(_sums.Camera);
    public Brush? GainBackground => MixedFill(_sums.Gain);
    public Brush? OffsetBackground => MixedFill(_sums.Offset);
    public Brush? BinBackground => MixedFill(_sums.Bin);
    public Brush? RotBackground => MixedFill(_sums.Rot);

    /// <summary>The header's Hours gauge (obs 01b7, same rule every level): the time still owed beneath as
    /// a negative while any plan is short, else the total captured disk time; null when the subtree carries
    /// no hours at all.</summary>
    public double? Hours => _sums.RemainingHours is double r && r > 0 ? -r : _sums.DiskHours;

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

    private string _markGlyph = "";
    private string? _markTooltip;

    /// <summary>The header's rolled-up sync-direction mark \u2014 the union of its subtree's directions
    /// (\u2190 inbound / \u2192 unpushed / \u21C4 both / empty), shown in column 0.</summary>
    public string MarkGlyph => _markGlyph;

    /// <summary>Direction-count summary behind the mark; null when unmarked (no empty tooltip box).</summary>
    public string? MarkTooltip => _markTooltip;

    /// <summary>Applies a resolved mark in place (the marks sweep) \u2014 raises only on a real change.</summary>
    public void ApplyMark(string glyph, string? tooltip)
    {
        if (_markGlyph == glyph && _markTooltip == tooltip) return;
        _markGlyph = glyph;
        _markTooltip = tooltip;
        Raise(nameof(MarkGlyph));
        Raise(nameof(MarkTooltip));
    }

    public string SourceText => Source switch
    {
        RowSource.Both => "Both",
        RowSource.TsOnly => "TS",
        _ => "Disk",
    };

    public string DesiredText => Format.CountOrDash(Desired);
    public string AcquiredText => Format.CountOrDash(Acquired);
    public string AcceptedText => Format.CountOrDash(Accepted);
    public string DiskText => Disk.ToString();

    // No "+" prefix: a positive value is the captured TOTAL, not a surplus over a goal (obs 01b7).
    public string HoursText => Hours switch
    {
        null => Format.Dash,
        double h => Format.Hours(h),
    };

    /// <summary>Soft theme fill behind the header's hours gauge: caution (brown) while time is still owed
    /// beneath, success (green) once nothing is — the value is then the captured total, whether the goals
    /// were met or there were never any. Null (no fill) when the header has no hours at all.</summary>
    public Brush? HoursBackground => Hours switch
    {
        null => null,
        < 0 => ThemeBrushes.Caution,
        _ => ThemeBrushes.Success,
    };
}
