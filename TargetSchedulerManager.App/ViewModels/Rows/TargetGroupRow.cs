using Microsoft.UI.Xaml;

namespace TargetSchedulerManager.App.ViewModels.Rows;

/// <summary>
/// The collapsible header row for one target group, over <see cref="AggregateHeaderRow"/>: adds the
/// target-enable checkbox, the mosaic panel mini-headers, and the target/detail identity. Source and Project
/// are per-target upstream (TargetResolver), so the first child speaks for all of them. Rebuilt on every filter
/// pass over the *visible* children, so its sums always match what expanding reveals.
/// </summary>
public sealed class TargetGroupRow : AggregateHeaderRow
{
    public TargetGroupRow(
        string target, IReadOnlyList<ReconciliationRow> children, bool isExpanded, bool isTargetEnabled,
        IReadOnlyList<PanelGroupRow>? panels = null)
        : base(target, children, children[0].Source, isExpanded)
    {
        Panels = panels;
        IsTargetEnabled = isTargetEnabled;
        Project = children[0].Project;
    }

    /// <summary>The collapsible panel mini-headers for a mosaic; null for a normal target.</summary>
    public IReadOnlyList<PanelGroupRow>? Panels { get; }

    public string Project { get; }

    /// <summary>Target enable state (TS <c>target.active</c>) bound to the leftmost checkbox. The view-model
    /// passes the effective value — a pending in-session toggle if any, else the loaded state.</summary>
    public bool IsTargetEnabled { get; private set; }

    /// <summary>In-place mirror after a verified <c>active</c> write (checkbox or edit flyout): refreshes the
    /// bound checkbox without a grid rebuild, keeping both edit paths visibly consistent.</summary>
    public void ApplyEnabled(bool enabled)
    {
        if (IsTargetEnabled == enabled) return;
        IsTargetEnabled = enabled;
        Raise(nameof(IsTargetEnabled));
    }

    /// <summary>Write-back key for this target's TS row; null when there is no TS target (disk-only / mosaic parent).</summary>
    public string? TsTargetKey => Children[0].TsTargetKey;

    /// <summary>Canonical target id for the edit flyout / sync-marks — the shared id of a normal group's rows;
    /// null for a mosaic parent (a grouping node — its panels carry their own ids, so act on a panel).</summary>
    public Guid? TargetId => Panels is null ? Children[0].TargetId : null;

    /// <summary>True when an enable checkbox applies: a normal (non-mosaic) group backed by a TS target.</summary>
    public bool CanEnable => Panels is null && TsTargetKey is not null;

    /// <summary>Checkbox visibility — mirrors the chevron-visibility pattern so XAML can x:Bind it directly.</summary>
    public Visibility CanEnableVisibility => CanEnable ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>True when this group is a mosaic parent (a grouping node over panel targets).</summary>
    public bool IsMosaic => Panels is not null;

    /// <summary>Write-back key for the group's TS project; from any child (all of a target's — or a mosaic's
    /// panels' — rows share the one project). Null when there is no TS project.</summary>
    public string? ProjectTsKey => Children[0].ProjectTsKey;

    /// <summary>Edit-glyph/menu gate: a normal group edits its TS target (checkbox predicate); a mosaic parent
    /// edits its TS *project* (master enable + project priority), so it gates on the project key instead.</summary>
    public Visibility EditGlyphVisibility =>
        (IsMosaic ? ProjectTsKey is not null : CanEnable) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The glyph tooltip — names what the flyout edits for this group kind.</summary>
    public string EditTooltip => IsMosaic ? "Edit mosaic project…" : "Edit target…";

    /// <summary>The target name, plus the panel count for a mosaic ("M101  ·  4 panels").</summary>
    public string TargetText => Panels is null
        ? Target
        : $"{Target}  ·  {Panels.Count} {(Panels.Count == 1 ? "panel" : "panels")}";
}
