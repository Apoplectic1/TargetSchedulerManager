using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using TargetSchedulerManager.App.Models;

namespace TargetSchedulerManager.App.ViewModels.Rows;

/// <summary>The target/panel/TS-key identity shared by every row of one loader emit — built once per
/// target (or panel) and handed to each of its rows, instead of re-threading ten arguments through every
/// construction. Scope: one emit; rows of different targets/panels never share an instance.</summary>
public sealed record RowIdentity(
    string Target,
    string Project,
    RowSource Source,
    string? PanelKey,
    string? PanelLabel,
    RowSource? PanelSource,
    bool Enabled,
    string? TsTargetKey,
    Guid TargetId,
    string? ProjectTsKey);

/// <summary>One row's plan/disk numbers — the numeric columns, in column order. Construct with NAMED
/// arguments at every site: the same-typed run is exactly the transposition hazard this record exists to
/// contain. <see cref="RemainingHours"/> is the time the plan side still owes — Σ per-plan-cell
/// max(0, desired − acquired) × seconds — clamped per cell BEFORE summing so one cell's overshoot never
/// masks another's shortfall; null when the row has no plan side. Acquired-based (not disk-frame-based)
/// deliberately: write-back stamps acquired from serving frames only, so this is the framing-aware number
/// TS actually schedules on (user decision, obs 01b7 2026-07-29).</summary>
public sealed record RowNumbers(
    int PlanSeconds,
    int DiskSeconds,
    int? Desired,
    int? Acquired,
    int? Accepted,
    int Disk,
    int PlanCount,
    double? PlanHours,
    double? DiskHours,
    double? RemainingHours = null);

/// <summary>The capture configuration a row describes (openspec capture-config-keys +
/// rotation-framing-key). Gain/Offset/Bin are reconciliation keys — a row exists separately from its
/// siblings BECAUSE one of them differs — so they are present on both planes. <see cref="Camera"/> is
/// disk-side only, because a TS plan cannot name a camera; it is null on a plan-only row and holds the raw
/// capture directory name otherwise (the alias is applied at render time, so an unrecognised directory
/// stays visible beside its badge). <see cref="Rotation"/>/<see cref="RotationFoldDeg"/> carry the row's
/// framing rotation — the disk cluster's expression + fold-180 angle on disk-backed rows, the target's own
/// rotation (as Sky) on plan-only rows, null where neither expresses one. <see cref="FramingDisagrees"/>
/// marks a disk row whose sky rotation fails the plan's — the `framing` badge's source.
/// <see cref="FramingOverlapFraction"/> prices being off the plan's footprint (openspec
/// framing-overlap-column): present exactly when the library computed one — every badged stray, plus a
/// serving framing displaced below the on-footprint threshold — and rendered into the badge on the deepest
/// visible line (<c>BadgeText</c>), never a column of its own.</summary>
public sealed record RowConfig(
    int Gain,
    int Offset,
    int BinningX,
    int BinningY,
    string? Camera,
    bool CameraDisagrees,
    Astronomy.Catalog.Scan.RotationExpression? Rotation = null,
    double? RotationFoldDeg = null,
    bool FramingDisagrees = false,
    double? FramingOverlapFraction = null)
{
    /// <summary>The configuration of a row with no disk side and no plan template behind it (a bare
    /// no-data row).</summary>
    public static readonly RowConfig None = new(0, 0, 1, 1, null, false);
}

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
    RowIdentity id,
    string filter,
    string purpose,
    RowPlane plane,
    RowNumbers numbers,
    string badge,
    bool isFlagged,
    bool secondsMixed = false,
    bool isDetail = false,
    IReadOnlyList<ReconciliationRow>? detail = null,
    string? planTsKey = null,
    bool? planEnabled = null,
    RowConfig? config = null) : INotifyPropertyChanged
{
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Target { get; } = id.Target;
    public string Project { get; } = id.Project;
    public string Filter { get; } = filter;
    public string Purpose { get; } = purpose;

    /// <summary>The plan side's whole-second sub length (representative when mixed); 0 = none/unknown. Settable
    /// for an in-place inline exposure edit (see <see cref="ApplyPlanSeconds"/>).</summary>
    public int PlanSeconds { get; private set; } = numbers.PlanSeconds;

    /// <summary>The disk side's whole-second sub length (representative when mixed); 0 = none.</summary>
    public int DiskSeconds { get; } = numbers.DiskSeconds;

    /// <summary>The target's classification — drives the source dropdown and the group header's label.</summary>
    public RowSource Source { get; } = id.Source;

    /// <summary>Which plane(s) this row carries; leaf rows show it in the Source column.</summary>
    public RowPlane Plane { get; } = plane;

    /// <summary>Summed <c>desired</c> across the row's plans; null on Disk rows. Settable for an in-place
    /// inline edit (see <see cref="ApplyDesired"/>) so committing one doesn't rebuild the grid.</summary>
    public int? Desired { get; private set; } = numbers.Desired;

    /// <summary>Summed TS <c>acquired</c> (the cached column write-back owns); null on Disk rows.</summary>
    public int? Acquired { get; } = numbers.Acquired;

    /// <summary>Summed TS <c>accepted</c> (cached column); null on Disk rows.</summary>
    public int? Accepted { get; } = numbers.Accepted;

    /// <summary>Frames on disk (ACTUAL — ground truth); 0 on TS rows.</summary>
    public int Disk { get; } = numbers.Disk;

    /// <summary>TS plans contributing (&gt;1 = mosaic fold, duplicate fold, or a same-purpose multi-plan).</summary>
    public int PlanCount { get; } = numbers.PlanCount;

    /// <summary>Match-state badges for the row's target ("duplicate", "name≠", "mosaic", …); empty when clean.
    /// The vocabulary and its per-token severity colour live in <see cref="Badges"/>. This is the FULL
    /// union (what headers aggregate and the flagged filter reasons over); the cell renders
    /// <see cref="BadgeText"/>.</summary>
    public string Badge { get; } = badge;

    /// <summary>What the Badges cell renders: <see cref="Badge"/>, except an EXPANDED rollup drops every
    /// <b>row-scoped</b> token (<see cref="Badges.IsRowScoped"/> — camera provenance and framing alike) —
    /// a badge belongs at the deepest VISIBLE level (user rule 2026-07-29). Collapsed, the triggering
    /// source line is hidden, so the rollup shows the token; expanded, that line shows it and repeating it
    /// on the rollup between header and line is noise. Target-scope tokens are untouched: their trigger is
    /// the whole target, so every level is genuinely their subject.
    /// <para>On a leaf — its own deepest level — the framing token carries its overlap price
    /// ("framing 57%", openspec framing-overlap-column): how much of these frames' footprint lies where the
    /// plan currently points. Leaf-only for the same reason the badge rule exists: a rollup spans clusters
    /// with different fractions (M81 holds three), so no single number could sit honestly above the lines.
    /// <see cref="Badge"/> itself never carries the number — search, flagging and header aggregation reason
    /// over the bare vocabulary.</para></summary>
    public string BadgeText
    {
        get
        {
            if (Detail is not null)
                return _isExpanded
                    ? Badges.Join(Badges.Split(Badge).Select(t => t.Token).Where(t => !Badges.IsRowScoped(t)))
                    : Badge;
            if (Config.FramingOverlapFraction is not double f)
                return Badge;
            return Badges.Join(Badges.Split(Badge).Select(t =>
                t.Token == Badges.Framing ? Badges.FramingWithOverlap(f) : t.Token));
        }
    }

    /// <summary>True when the target needs human attention — duplicate / name-mismatch / ambiguous /
    /// multi-plan / accepted≠acquired / no-coords. Exactly the warning-severity badge set
    /// (<see cref="Badges.IsWarning"/>), so the flagged-only filter can never hide a row the Badges column
    /// painted as a warning.</summary>
    public bool IsFlagged { get; } = isFlagged;

    /// <summary>Planned commitment in decimal hours, summed per sub length by the loader; null without a plan
    /// side. Recomputed in place when <see cref="ApplyDesired"/> edits the count.</summary>
    public double? PlanHours { get; private set; } = numbers.PlanHours;

    /// <summary>Time the plan side still owes (see <see cref="RowNumbers.RemainingHours"/>); null without a
    /// plan side. Recomputed in place by the inline edits.</summary>
    public double? PlanRemainingHours { get; private set; } = numbers.RemainingHours;

    /// <summary>Actual integration in decimal hours, summed per sub length by the loader; null without a disk side.</summary>
    public double? DiskHours { get; } = numbers.DiskHours;

    /// <summary>True when the rollup's sub lengths aren't all one identical value (2+ distinct times
    /// across the plan and disk sides) — the Seconds cell reads "mixed" and the row is expandable.</summary>
    public bool SecondsMixed { get; } = secondsMixed;

    /// <summary>True for a one-plane source line living under a rollup's disclosure (extra indent).</summary>
    public bool IsDetail { get; } = isDetail;

    /// <summary>The rollup's one-plane source lines; null when the row has nothing to disclose.</summary>
    public IReadOnlyList<ReconciliationRow>? Detail { get; } = detail;

    /// <summary>Stable key of the mosaic panel this row belongs to; null on a normal target's rows.</summary>
    public string? PanelKey { get; } = id.PanelKey;

    /// <summary>The panel's display label ("Panel 01of16 · CygnusLoop P1"; one name when one-sided).</summary>
    public string? PanelLabel { get; } = id.PanelLabel;

    /// <summary>The panel's own classification (the row's <see cref="Source"/> stays the parent's).</summary>
    public RowSource? PanelSource { get; } = id.PanelSource;

    /// <summary>The target's TS-enable state (<c>target.active</c>); true by default for a target with no TS row.</summary>
    public bool Enabled { get; } = id.Enabled;

    /// <summary>Write-back key for the target's TS row (guid, or integer Id as a string); null when there is no
    /// TS target behind this row (disk-only target, mosaic parent) — the enable checkbox is then hidden.</summary>
    public string? TsTargetKey { get; } = id.TsTargetKey;

    /// <summary>Canonical catalog target id — a reusable key into the retained graph.</summary>
    public Guid TargetId { get; } = id.TargetId;

    /// <summary>Write-back key for this row's single TS exposure plan — set only on a one-plan cell, so a value
    /// here marks the row's <c>desired</c> as 1:1 editable; null on multi-plan rollups, disk rows, and headers.</summary>
    public string? PlanTsKey { get; } = planTsKey;

    /// <summary>Write-back key for the target's TS project (project-scope edits: priority, constraints);
    /// null when the target has no TS project.</summary>
    public string? ProjectTsKey { get; } = id.ProjectTsKey;

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

    /// <summary>True when Desired is directly editable: exactly one TS plan behind this row, with a plan side
    /// present, at the row showing that plan's own exposure time — a mixed rollup aggregates sub lengths, so
    /// its box moves down to the plan's detail line (each plan is inline-editable in exactly one place; the
    /// rollup keeps its flyout gesture via <see cref="PlanTsKey"/>).</summary>
    public bool CanEditDesired => PlanTsKey is not null && Desired is not null && !SecondsMixed;

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
        PlanRemainingHours = PlanSeconds > 0
            ? Math.Max(0, newDesired - (Acquired ?? 0)) * (double)PlanSeconds / 3600.0
            : null;
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
        PlanRemainingHours = newSeconds > 0 && Desired is int d2
            ? Math.Max(0, d2 - (Acquired ?? 0)) * (double)newSeconds / 3600.0
            : null;
        Raise(nameof(SecondsText));
        Raise(nameof(HoursText));
        Raise(nameof(HoursBackground));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private string _markGlyph = "";
    private string? _markTooltip;

    /// <summary>The sync-direction mark (← inbound / → unpushed / ⇄ both / empty) shown in column 0.</summary>
    public string MarkGlyph => _markGlyph;

    /// <summary>Old→new lines behind the mark; null when unmarked (no empty tooltip box).</summary>
    public string? MarkTooltip => _markTooltip;

    /// <summary>Applies a resolved mark in place (the marks sweep) — raises only on a real change, so an
    /// unchanged grid repaints nothing.</summary>
    public void ApplyMark(string glyph, string? tooltip)
    {
        if (_markGlyph == glyph && _markTooltip == tooltip) return;
        _markGlyph = glyph;
        _markTooltip = tooltip;
        Raise(nameof(MarkGlyph));
        Raise(nameof(MarkTooltip));
    }

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BadgeText)));
        }
    }

    /// <summary>
    /// What the Hours column shows — a PROGRESS GAUGE, not a signed sum (user decision, obs 01b7
    /// 2026-07-29, replacing the additive parents-are-the-literal-sum model): while the plan side still owes
    /// images, the time remaining as a negative (brown); once nothing is owed, the total captured disk time
    /// (green). A Disk row states its plain total; a TS row states what it still owes — and shows the dash
    /// once complete (nothing owed, and its frames live on the disk sibling). A desired-0 plan keeps its
    /// 0.0-with-critical-fill tripwire (data that shouldn't exist). Debt survives a disable — Visible-Tonight
    /// flips <c>target.active</c> nightly, and progress must not churn with the sky.
    /// </summary>
    public double? Hours => Plane switch
    {
        RowPlane.Ts when Desired == 0 && PlanCount > 0 => 0.0,
        RowPlane.Ts => PlanRemainingHours is double r && r > 0 ? -r : null,
        RowPlane.Disk => DiskHours,
        _ => PlanRemainingHours is double r && r > 0 ? -r : DiskHours,
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
        RowPlane.Ts => PlanSeconds > 0 ? PlanSeconds.ToString() : Format.Dash,
        RowPlane.Disk => DiskSeconds > 0 ? DiskSeconds.ToString() : Format.Dash,
        _ when SecondsMixed => "mixed",
        _ => PlanSeconds.ToString(),
    };

    /// <summary>Caution pill behind the Seconds cell when a rollup's sub lengths are mixed.</summary>
    public Brush? SecondsBackground =>
        Plane == RowPlane.Both && SecondsMixed ? ThemeBrushes.Caution : null;

    /// <summary>The capture configuration this row describes — the reason it stands apart from its siblings.</summary>
    public RowConfig Config { get; } = config ?? RowConfig.None;

    /// <summary>A configuration cell's text: this row's own value, or — on a rollup — the value its source
    /// lines share, or the mixed marker when they disagree. A rollup that reads "mixed" names the dimension
    /// responsible before it is expanded. A line showing the em dash expresses nothing for that dimension
    /// (a TS row's camera, an unexpressed rotation) and never counts as disagreement (user obs 2026-07-29);
    /// all-dash lines roll up to the dash.</summary>
    private string Cfg(Func<ReconciliationRow, string> cell, string own)
    {
        if (Detail is not { Count: > 0 } lines) return own;
        string expressed = string.Empty;
        foreach (ReconciliationRow line in lines)
        {
            string v = cell(line);
            if (v == Format.Dash) continue;
            if (expressed.Length == 0) expressed = v;
            else if (!string.Equals(v, expressed, StringComparison.Ordinal)) return Format.Mixed;
        }
        return expressed.Length == 0 ? Format.Dash : expressed;
    }

    /// <summary>Camera cell: the alias, or the raw directory name when it names no known camera (so the
    /// offending name is readable beside its badge), or the dash on a plan-only row.</summary>
    public string CameraText => Cfg(r => r.CameraText, Format.CameraCell(Config.Camera));

    public string GainText => Cfg(r => r.GainText, Format.TemplateNumberCell("gain", Config.Gain));
    public string OffsetText => Cfg(r => r.OffsetText, Format.TemplateNumberCell("offset", Config.Offset));

    /// <summary>Binning as the single figure it always is in practice ("1"), or "XxY" if ever asymmetric.</summary>
    public string BinText => Cfg(r => r.BinText,
        Config.BinningX == Config.BinningY
            ? Config.BinningX.ToString()
            : $"{Config.BinningX}x{Config.BinningY}");

    /// <summary>Rotation cell: the framing's fold-180 angle (sky plain, mechanical marked "°(M)"), the dash
    /// where no rotation is expressed; a rollup shows the shared value or the mixed marker.</summary>
    public string RotText => Cfg(r => r.RotText, Format.Rotation(Config.Rotation, Config.RotationFoldDeg));

    private static Brush? MixedFill(string text) => text == Format.Mixed ? ThemeBrushes.Caution : null;

    public Brush? CameraBackground => MixedFill(CameraText);
    public Brush? GainBackground => MixedFill(GainText);
    public Brush? OffsetBackground => MixedFill(OffsetText);
    public Brush? BinBackground => MixedFill(BinText);
    public Brush? RotBackground => MixedFill(RotText);

    public string DesiredText => Format.CountOrDash(Desired);
    public string AcquiredText => Format.CountOrDash(Acquired);
    public string AcceptedText => Format.CountOrDash(Accepted);
    // Actual is measured over the whole disk, so a TS row's absence of frames is a real 0 — unlike the
    // authored plan-side cells, whose absence stays "—" (no plan ≠ a goal of zero).
    public string DiskText => Disk.ToString();

    // No "+" prefix: a positive value is a TOTAL (captured time), not a surplus over a goal — the surplus
    // reading died with the signed-sum model (obs 01b7).
    public string HoursText => Hours switch
    {
        null => Format.Dash,
        double h => Format.Hours(h),
    };

    /// <summary>Fill behind the Hours cell: the gauge's colors — caution (brown) while time is still owed,
    /// success (green) on a Both row whose debt is cleared (the value is then the captured total), the error
    /// fill for a desired-0 plan (data that shouldn't exist). Disk rows stay plain — quiet positive facts;
    /// the green belongs to levels that HAVE a goal and met it.</summary>
    public Brush? HoursBackground => Plane switch
    {
        RowPlane.Both when Hours is double h => h < 0 ? ThemeBrushes.Caution : ThemeBrushes.Success,
        RowPlane.Ts when Hours is double h => h < 0 ? ThemeBrushes.Caution : ThemeBrushes.Critical,
        _ => null,
    };

    public string PlanCountText => PlanCount > 1 ? $"×{PlanCount}" : string.Empty;

    /// <summary>Case-insensitive match against the searchable columns. Camera is searchable ("Z533"); the
    /// numeric configuration columns deliberately are not — a bare number would collide with the counts.</summary>
    public bool Matches(string search) =>
        Target.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Project.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Filter.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Badge.Contains(search, StringComparison.OrdinalIgnoreCase)
        || CameraText.Contains(search, StringComparison.OrdinalIgnoreCase)
        || (PanelLabel?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
}
