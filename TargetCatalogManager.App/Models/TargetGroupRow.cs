using System.ComponentModel;

namespace TargetCatalogManager.App.Models;

/// <summary>
/// The collapsible header row for one target group: aggregates of the (filter, purpose) rows beneath it.
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

        bool anyPlanned = false;
        int desired = 0, acquired = 0, accepted = 0;
        foreach (ReconciliationRow r in children)
        {
            if (r.Desired is int d) { anyPlanned = true; desired += d; }
            acquired += r.Acquired ?? 0;
            accepted += r.Accepted ?? 0;
            Disk += r.Disk;
            Remaining += Math.Max(0, (r.Desired ?? 0) - r.Disk);
            if (r.IsFlagged) IsFlagged = true;
        }
        Desired = anyPlanned ? desired : null;
        Acquired = anyPlanned ? acquired : null;
        Accepted = anyPlanned ? accepted : null;
        Badge = string.Join(" · ", children.Select(r => r.Badge).Where(b => b.Length > 0).Distinct());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Target { get; }

    /// <summary>The filter rows this header aggregates — already filtered/ordered by the view-model.</summary>
    public IReadOnlyList<ReconciliationRow> Children { get; }

    public RowSource Source { get; }
    public string Project { get; }

    /// <summary>Summed over children with a plan; null when no child has one (pure disk-only group).</summary>
    public int? Desired { get; }
    public int? Acquired { get; }
    public int? Accepted { get; }
    public int Disk { get; }

    /// <summary>Sum of per-cell shortfalls max(0, desired − disk) — the group-level "remaining" sort key.</summary>
    public int Remaining { get; }

    /// <summary>Union of the children's badges (distinct, order of first appearance).</summary>
    public string Badge { get; }

    public bool IsFlagged { get; }

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

    public string TargetText => $"{Target}  ·  {Children.Count} {(Children.Count == 1 ? "filter" : "filters")}";
    public string DesiredText => Desired?.ToString() ?? "—";
    public string AcquiredText => Acquired?.ToString() ?? "—";
    public string AcceptedText => Accepted?.ToString() ?? "—";
    public string DiskText => Disk.ToString();
    public string DeltaText => Delta switch { null => "—", > 0 => $"+{Delta}", _ => Delta.Value.ToString() };
}
