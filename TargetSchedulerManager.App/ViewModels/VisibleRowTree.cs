using TargetSchedulerManager.App.ViewModels.Rows;

namespace TargetSchedulerManager.App.ViewModels;

/// <summary>
/// The visible-row tree model: turns the filtered <see cref="TargetGroupRow"/> groups (each carrying mosaic
/// <see cref="PanelGroupRow"/>s and per-plane <see cref="ReconciliationRow"/> leaves) into the flat list the
/// <c>ListView</c> binds, and splices one node's children in/out on a toggle. ONE rule —
/// <see cref="ExpandedContent"/>, "the rows a node contributes when expanded" — drives the wholesale
/// <see cref="Flatten"/> AND every toggle's insert <em>and</em> remove, so the incremental splice can never
/// drift from a full rebuild. Also the single owner of node identity: the target / panel / rollup
/// expansion-key formats live here (not scattered across the loader, the view-model, and
/// <see cref="ExpansionState"/>), over a dumb string-set <see cref="ExpansionState"/> store. Pure over the row
/// objects' <c>IsExpanded</c> flags — no XAML, no view-model — so the flattening invariant is unit-testable.
/// </summary>
internal sealed class VisibleRowTree(ExpansionState expansion)
{
    private readonly ExpansionState _expansion = expansion;

    // --- node identity: the three expansion-key formats, in one place ---
    private static string TargetKey(string target) => target;
    private static string PanelKey(string target, string panelKey) => $"{target}|{panelKey}";
    private static string RollupKey(ReconciliationRow rollup) =>
        $"{rollup.Target}|{rollup.PanelKey}|{rollup.Filter}|{rollup.Purpose}";

    // --- expansion memory, keyed by node identity (callers never touch raw key strings) ---
    public bool IsTargetExpanded(string target) => _expansion.IsTargetExpanded(TargetKey(target));
    public bool IsPanelExpanded(string target, string panelKey) =>
        _expansion.IsPanelExpanded(PanelKey(target, panelKey));
    public bool IsRollupExpanded(ReconciliationRow rollup) => _expansion.IsRollupExpanded(RollupKey(rollup));
    public void ExpandAllTargets(IEnumerable<string> targets) => _expansion.ExpandTargets(targets);
    public void CollapseAllTargets() => _expansion.CollapseAllTargets();

    /// <summary>Restores every rollup leaf's expansion flag from memory (for all groups, visible or not), so a
    /// later toggle expands to the remembered nested state. Group/panel flags are set at construction in the
    /// rebuild; the rollup leaves are the per-pass survivors that need re-pinning.</summary>
    public void RestoreRollupExpansion(IReadOnlyList<TargetGroupRow> groups)
    {
        foreach (TargetGroupRow g in groups)
            foreach (ReconciliationRow child in g.Children)
                if (child.Detail is not null)
                    child.IsExpanded = IsRollupExpanded(child);
    }

    /// <summary>The flat visible list: each group header, followed by its content when expanded.</summary>
    public List<object> Flatten(IReadOnlyList<TargetGroupRow> groups)
    {
        List<object> rows = [];
        foreach (TargetGroupRow g in groups)
        {
            rows.Add(g);
            if (g.IsExpanded)
                rows.AddRange(ExpandedContent(g));
        }
        return rows;
    }

    /// <summary>The rows a node contributes when expanded — a mosaic group's panels (with each expanded panel's
    /// leaves), a plain group's leaves, a panel's leaves, or a rollup's detail lines. The single rule the
    /// rebuild and every toggle share, so a node's insert and remove can't disagree.</summary>
    public List<object> ExpandedContent(object node)
    {
        List<object> content = [];
        switch (node)
        {
            case TargetGroupRow { Panels: { } panels }:
                foreach (PanelGroupRow panel in panels)
                {
                    content.Add(panel);
                    if (panel.IsExpanded)
                        AppendLeaves(content, panel.Children);
                }
                break;
            case TargetGroupRow group:
                AppendLeaves(content, group.Children);
                break;
            case PanelGroupRow panel:
                AppendLeaves(content, panel.Children);
                break;
            case ReconciliationRow { Detail: { } detail }:
                content.AddRange(detail);
                break;
        }
        return content;
    }

    private static void AppendLeaves(IList<object> sink, IReadOnlyList<ReconciliationRow> leaves)
    {
        foreach (ReconciliationRow leaf in leaves)
        {
            sink.Add(leaf);
            if (leaf is { Detail: not null, IsExpanded: true })
                foreach (ReconciliationRow d in leaf.Detail)
                    sink.Add(d);
        }
    }

    /// <summary>Toggles one node in the bound row list <em>in place</em> (preserving scroll position): removes
    /// the node's current content when collapsing, inserts it when expanding — exactly
    /// <see cref="ExpandedContent"/> either way — then flips the node's flag and persists it. No-op when the
    /// node isn't in <paramref name="rows"/>.</summary>
    public void Toggle(IList<object> rows, object node)
    {
        int index = rows.IndexOf(node);
        if (index < 0) return;

        bool wasExpanded = IsNodeExpanded(node);
        List<object> content = ExpandedContent(node);
        if (wasExpanded)
            for (int i = 0; i < content.Count; i++) rows.RemoveAt(index + 1);
        else
            for (int i = 0; i < content.Count; i++) rows.Insert(index + 1 + i, content[i]);

        SetNodeExpanded(node, !wasExpanded);
    }

    private static bool IsNodeExpanded(object node) => node switch
    {
        TargetGroupRow g => g.IsExpanded,
        PanelGroupRow p => p.IsExpanded,
        ReconciliationRow r => r.IsExpanded,
        _ => false,
    };

    private void SetNodeExpanded(object node, bool expanded)
    {
        switch (node)
        {
            case TargetGroupRow g:
                g.IsExpanded = expanded;
                _expansion.SetTarget(TargetKey(g.Target), expanded);
                break;
            case PanelGroupRow p:
                p.IsExpanded = expanded;
                _expansion.SetPanel(PanelKey(p.Target, p.Key), expanded);
                break;
            case ReconciliationRow r:
                r.IsExpanded = expanded;
                _expansion.SetRollup(RollupKey(r), expanded);
                break;
        }
    }
}
