namespace TargetSchedulerManager.App.ViewModels;

/// <summary>
/// The grid's expansion memory: which targets, mosaic panels, and mixed-seconds rollups are open. Keyed by
/// stable identity strings (target name; <c>target|panel</c>; <c>target|panel|filter|purpose</c>) so expansion
/// survives filter changes and reloads — collapsed is the default for anything never touched. Extracted from
/// <see cref="MainViewModel"/> (§7.5) so the survives-a-reload behaviour is a unit in its own right; the
/// view-model owns the key construction, this owns only the membership.
/// </summary>
internal sealed class ExpansionState
{
    private readonly HashSet<string> _targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _panels = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _rollups = new(StringComparer.OrdinalIgnoreCase);

    public bool IsTargetExpanded(string target) => _targets.Contains(target);
    public void SetTarget(string target, bool expanded) => Set(_targets, target, expanded);

    /// <summary>Marks every given target expanded (the toolbar's Expand-all).</summary>
    public void ExpandTargets(IEnumerable<string> targets)
    {
        foreach (string target in targets)
            _targets.Add(target);
    }

    /// <summary>Collapses every target (the toolbar's Collapse-all); panel/rollup memory is left intact so a
    /// later re-expand restores the nested state.</summary>
    public void CollapseAllTargets() => _targets.Clear();

    public bool IsPanelExpanded(string key) => _panels.Contains(key);
    public void SetPanel(string key, bool expanded) => Set(_panels, key, expanded);

    public bool IsRollupExpanded(string key) => _rollups.Contains(key);
    public void SetRollup(string key, bool expanded) => Set(_rollups, key, expanded);

    private static void Set(HashSet<string> set, string key, bool expanded)
    {
        if (expanded) set.Add(key);
        else set.Remove(key);
    }
}
