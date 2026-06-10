using System.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace TargetCatalogManager.App.Models;

/// <summary>
/// The collapsible header row for one target group: aggregates of the per-plane rows beneath it.
/// Rebuilt on every filter pass over the *visible* children, so its sums always match what expanding
/// reveals. The only mutable state is <see cref="IsExpanded"/> — the chevron re-renders via
/// INotifyPropertyChanged while the view-model edits the bound list in place (insert/remove children),
/// which is what preserves the scroll position on toggle.
/// </summary>
public sealed class TargetGroupRow : INotifyPropertyChanged
{
    private bool _isExpanded;

    public TargetGroupRow(string target, IReadOnlyList<ReconciliationRow> children, bool isExpanded)
    {
        Target = target;
        Children = children;
        _isExpanded = isExpanded;

        // Source and Project are per-target upstream (TargetResolver), so the first child speaks for all.
        Source = children[0].Source;
        Project = children[0].Project;

        bool anyPlanned = false, anyHours = false;
        int desired = 0, acquired = 0, accepted = 0;
        double diskHours = 0, desiredHours = 0;
        foreach (ReconciliationRow r in children)
        {
            if (r.Desired is int d) { anyPlanned = true; desired += d; }
            acquired += r.Acquired ?? 0;
            accepted += r.Accepted ?? 0;
            Disk += r.Disk;
            if (r.IsFlagged) IsFlagged = true;
            // A Both row carries both components; one-plane rows carry one. Summing the components (not
            // the displayed Hours, which is a gap on Both rows) keeps the header delta exact.
            if (r.DiskHours is double dh) { anyHours = true; diskHours += dh; }
            if (r.PlanHours is double ph) { anyHours = true; desiredHours += ph; }
            // Both rows pair desired against their own disk; leftover TS rows are wholly unshot; leftover
            // Disk rows have no goal — so per-row shortfalls are already per-cell (over-shot filters
            // can't mask another filter's gap).
            Remaining += Math.Max(0, (r.Desired ?? 0) - r.Disk);
        }
        Desired = anyPlanned ? desired : null;
        Acquired = anyPlanned ? acquired : null;
        Accepted = anyPlanned ? accepted : null;
        HoursDelta = anyHours ? diskHours - desiredHours : null;

        Badge = string.Join(" · ", children.Select(r => r.Badge).Where(b => b.Length > 0).Distinct());
        // Distinct filters, not child rows — a filter split by plane or sub length is still one filter.
        FilterCount = children.Select(r => r.Filter).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Target { get; }

    /// <summary>The per-plane rows this header aggregates — already filtered/ordered by the view-model.</summary>
    public IReadOnlyList<ReconciliationRow> Children { get; }

    public RowSource Source { get; }
    public string Project { get; }

    /// <summary>Summed over the TS children; null when no child has a plan (pure disk-only group).</summary>
    public int? Desired { get; }
    public int? Acquired { get; }
    public int? Accepted { get; }
    public int Disk { get; }

    /// <summary>Sum of per-cell shortfalls max(0, desired − disk) — the group-level "remaining" sort key.</summary>
    public int Remaining { get; }

    /// <summary>
    /// Disk hours − desired hours across all the target's rows: negative = the target still needs telescope
    /// time; ≥ 0 = the captured time met the plan's commitment. Null when no row carries hours.
    /// </summary>
    public double? HoursDelta { get; }

    /// <summary>Union of the children's badges (distinct, order of first appearance).</summary>
    public string Badge { get; }

    /// <summary>Distinct filter names across the children (plane/sub-length splits don't inflate it).</summary>
    public int FilterCount { get; }

    public bool IsFlagged { get; }

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

    public string TargetText => $"{Target}  ·  {FilterCount} {(FilterCount == 1 ? "filter" : "filters")}";
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
