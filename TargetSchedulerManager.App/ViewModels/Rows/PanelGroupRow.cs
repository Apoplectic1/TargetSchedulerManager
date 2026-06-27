using TargetSchedulerManager.App.Models;

namespace TargetSchedulerManager.App.ViewModels.Rows;

/// <summary>
/// The collapsible mini-header for one mosaic panel inside its target group, over
/// <see cref="AggregateHeaderRow"/>: the same column aggregates a target header carries, computed over the
/// panel's leaves only, plus the panel's key + label. Exactly like <see cref="TargetGroupRow"/> minus the
/// target-level commands.
/// </summary>
public sealed class PanelGroupRow : AggregateHeaderRow
{
    public PanelGroupRow(
        string target, string key, string label, RowSource source,
        IReadOnlyList<ReconciliationRow> children, bool isExpanded)
        : base(target, children, source, isExpanded)
    {
        Key = key;
        Label = label;
    }

    /// <summary>The expansion-state key (<c>target|panelKey</c> uses this panel part).</summary>
    public string Key { get; }

    /// <summary>"Panel 01of16 · CygnusLoop P1" — one name when the panel is one-sided.</summary>
    public string Label { get; }

    /// <summary>This panel's canonical target id — the detail panel's key into the retained graph.</summary>
    public Guid TargetId => Children[0].TargetId;
}
