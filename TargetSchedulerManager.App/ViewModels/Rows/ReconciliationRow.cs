using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using TargetSchedulerManager.App.Models;

namespace TargetSchedulerManager.App.ViewModels.Rows;

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
    IReadOnlyList<ReconciliationRow>? detail = null,
    string? panelKey = null,
    string? panelLabel = null,
    RowSource? panelSource = null,
    bool enabled = true,
    string? tsTargetKey = null,
    Guid targetId = default,
    string? planTsKey = null,
    string? projectTsKey = null,
    bool? planEnabled = null) : INotifyPropertyChanged
{
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Target { get; } = target;
    public string Project { get; } = project;
    public string Filter { get; } = filter;
    public string Purpose { get; } = purpose;

    /// <summary>The plan side's whole-second sub length (representative when mixed); 0 = none/unknown. Settable
    /// for an in-place inline exposure edit (see <see cref="ApplyPlanSeconds"/>).</summary>
    public int PlanSeconds { get; private set; } = planSeconds;

    /// <summary>The disk side's whole-second sub length (representative when mixed); 0 = none.</summary>
    public int DiskSeconds { get; } = diskSeconds;

    /// <summary>The target's classification — drives the source dropdown and the group header's label.</summary>
    public RowSource Source { get; } = source;

    /// <summary>Which plane(s) this row carries; leaf rows show it in the Source column.</summary>
    public RowPlane Plane { get; } = plane;

    /// <summary>Summed <c>desired</c> across the row's plans; null on Disk rows. Settable for an in-place
    /// inline edit (see <see cref="ApplyDesired"/>) so committing one doesn't rebuild the grid.</summary>
    public int? Desired { get; private set; } = desired;

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

    /// <summary>Planned commitment in decimal hours, summed per sub length by the loader; null without a plan
    /// side. Recomputed in place when <see cref="ApplyDesired"/> edits the count.</summary>
    public double? PlanHours { get; private set; } = planHours;

    /// <summary>Actual integration in decimal hours, summed per sub length by the loader; null without a disk side.</summary>
    public double? DiskHours { get; } = diskHours;

    /// <summary>True when the rollup's sub lengths aren't all one identical value (2+ distinct times
    /// across the plan and disk sides) — the Seconds cell reads "mixed" and the row is expandable.</summary>
    public bool SecondsMixed { get; } = secondsMixed;

    /// <summary>True for a one-plane source line living under a rollup's disclosure (extra indent).</summary>
    public bool IsDetail { get; } = isDetail;

    /// <summary>The rollup's one-plane source lines; null when the row has nothing to disclose.</summary>
    public IReadOnlyList<ReconciliationRow>? Detail { get; } = detail;

    /// <summary>Stable key of the mosaic panel this row belongs to; null on a normal target's rows.</summary>
    public string? PanelKey { get; } = panelKey;

    /// <summary>The panel's display label ("Panel 01of16 · CygnusLoop P1"; one name when one-sided).</summary>
    public string? PanelLabel { get; } = panelLabel;

    /// <summary>The panel's own classification (the row's <see cref="Source"/> stays the parent's).</summary>
    public RowSource? PanelSource { get; } = panelSource;

    /// <summary>The target's TS-enable state (<c>target.active</c>); true by default for a target with no TS row.</summary>
    public bool Enabled { get; } = enabled;

    /// <summary>Write-back key for the target's TS row (guid, or integer Id as a string); null when there is no
    /// TS target behind this row (disk-only target, mosaic parent) — the enable checkbox is then hidden.</summary>
    public string? TsTargetKey { get; } = tsTargetKey;

    /// <summary>Canonical catalog target id — a reusable key into the retained graph.</summary>
    public Guid TargetId { get; } = targetId;

    /// <summary>Write-back key for this row's single TS exposure plan — set only on a one-plan cell, so a value
    /// here marks the row's <c>desired</c> as 1:1 editable; null on multi-plan rollups, disk rows, and headers.</summary>
    public string? PlanTsKey { get; } = planTsKey;

    /// <summary>Write-back key for the target's TS project (project-scope edits: priority, constraints);
    /// null when the target has no TS project.</summary>
    public string? ProjectTsKey { get; } = projectTsKey;

    /// <summary>The single TS plan's <c>enabled</c> flag — set only on a one-plan cell (like <see cref="PlanTsKey"/>);
    /// null hides the plan-enable checkbox (multi-plan rollups, disk rows).</summary>
    public bool? PlanEnabled { get; private set; } = planEnabled;

    /// <summary>The plan-enable checkbox binding (null-safe for x:Bind).</summary>
    public bool IsPlanEnabled => PlanEnabled == true;

    public Visibility PlanEnableVisibility =>
        PlanTsKey is not null && PlanEnabled is not null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Applies a committed plan-enable toggle (after the local TS write verified) in place.</summary>
    public void ApplyPlanEnabled(bool enabled)
    {
        if (PlanEnabled == enabled) return;
        PlanEnabled = enabled;
        Raise(nameof(IsPlanEnabled));
    }

    /// <summary>True when Desired is directly editable: exactly one TS plan behind this row, with a plan side present.</summary>
    public bool CanEditDesired => PlanTsKey is not null && Desired is not null;

    /// <summary>Desired as a NumberBox value (the real count on editable rows; a 0 stand-in elsewhere).</summary>
    public double DesiredValue => Desired ?? 0;

    /// <summary>Edit-glyph/menu gate for the plan flyout: only a 1:1 TS plan row is editable (same key the
    /// write path needs; rollups, disk-only rows, and headers have no plan key).</summary>
    public Visibility EditGlyphVisibility => PlanTsKey is not null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>NumberBox visibility for an editable Desired cell (read-only text shows otherwise).</summary>
    public Visibility DesiredEditVisibility => CanEditDesired ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Read-only Desired text visibility — the inverse of <see cref="DesiredEditVisibility"/>.</summary>
    public Visibility DesiredTextVisibility => CanEditDesired ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Applies a committed inline desired edit (after the TS write verified): updates the count and the
    /// derived plan hours and refreshes the bound Hours cell — in place, no grid rebuild, so the scroll position
    /// and any in-progress edit survive. The caller re-aggregates the owning group/panel.</summary>
    public void ApplyDesired(int newDesired)
    {
        if (Desired == newDesired) return;
        Desired = newDesired;
        PlanHours = PlanSeconds > 0 ? newDesired * (double)PlanSeconds / 3600.0 : null;
        Raise(nameof(HoursText));
        Raise(nameof(HoursBackground));
    }

    /// <summary>Applies a committed inline exposure edit (after the TS write verified): updates the plan-side
    /// seconds + derived plan hours and refreshes the bound Seconds/Hours cells in place. Display-only mirror —
    /// the reconciliation keys cell identity on seconds at load time, so the row may split from (or rejoin) its
    /// disk frames on the next reload; that split is correct reconciliation, not drift. The caller re-aggregates
    /// the owning group/panel.</summary>
    public void ApplyPlanSeconds(int newSeconds)
    {
        if (PlanSeconds == newSeconds) return;
        PlanSeconds = newSeconds;
        PlanHours = newSeconds > 0 && Desired is int d ? d * (double)newSeconds / 3600.0 : null;
        Raise(nameof(SecondsText));
        Raise(nameof(HoursText));
        Raise(nameof(HoursBackground));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
    /// leaf text aligns just past it, and detail source lines step in once more. Rows under a mosaic panel
    /// shift one extra step so they read as the panel's children.</summary>
    public Thickness SourceMargin
    {
        get
        {
            int extra = PanelKey is null ? 0 : 14;
            return Detail is not null
                ? new Thickness(18 + extra, 0, 0, 0)
                : IsDetail ? new Thickness(50 + extra, 0, 0, 0) : new Thickness(36 + extra, 0, 0, 0);
        }
    }

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
        || Badge.Contains(search, StringComparison.OrdinalIgnoreCase)
        || (PanelLabel?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
}
