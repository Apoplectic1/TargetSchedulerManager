using System.Diagnostics;
using System.Globalization;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Reconcile;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels.Rows;
using Astronomy.Diagnostics;

namespace TargetSchedulerManager.App.Services;

/// <summary>Result of one load: the grid rows, the build report the summary/badges came from, the resolved
/// <see cref="CatalogGraph"/> retained so app actions (the edit flyout, sync-direction marks, write-back
/// re-resolve) can pull a target's full disk + TS context, the
/// raw <see cref="TsPlanData"/> snapshot the build read — retained so app actions (e.g. the Visible-tonight
/// pass) consume the load's single TS read instead of re-opening the db — and the scan's
/// <see cref="SkippedFiles"/> (path → reason), carried so unreadable frames surface on the status line and
/// in the ambiguity report instead of silently lowering the Actual counts (openspec framing-overlap-column,
/// group 4a).</summary>
public sealed record LoadResult(
    IReadOnlyList<ReconciliationRow> Rows,
    CatalogBuildReport Report,
    CatalogGraph Graph,
    TsPlanData Ts,
    TimeSpan Elapsed,
    IReadOnlyDictionary<string, string> SkippedFiles);

/// <summary>
/// The app's data layer: fresh disk scan + TS snapshot read + <see cref="TargetResolver"/>, all in memory —
/// the same pipeline the retired CLI host ran, minus the Catalog.db write. The grid therefore can
/// never show stale ACTUAL, and the app doesn't need (or touch) Catalog.db at all in M1.
/// </summary>
public static class ReconciliationLoader
{
    /// <summary>The disk half of a load, separated so a caller can reuse one scan across two resolves —
    /// the post-load write-back stamps the local TS db, and the grid must then re-read it without paying
    /// for a second disk walk.</summary>
    public static async Task<ImageLibraryReport> ScanLibraryAsync(string libraryRoot, CancellationToken ct = default)
    {
        Stopwatch sw = Stopwatch.StartNew();
        ImageLibraryReport scan = await ImageLibraryScanner.ScanAsync(libraryRoot, ct).ConfigureAwait(false);
        Log.Diag("Load", $"scan={Sec(sw.Elapsed)}s diskTargets={scan.Targets.Count}");
        return scan;
    }

    /// <summary>The TS half: read the TS db, resolve against <paramref name="scan"/>, shape the grid rows.</summary>
    public static Task<LoadResult> ResolveAsync(
        ImageLibraryReport scan, string tsDbPath, double toleranceDegrees, CancellationToken ct = default)
        => Task.Run(() =>
    {
        Stopwatch sw = Stopwatch.StartNew();

        TsPlanData ts;
        using (TargetSchedulerReader reader = new(tsDbPath))
            ts = reader.ReadPlanData(ct);
        TimeSpan tTsRead = sw.Elapsed;

        (CatalogGraph graph, CatalogBuildReport report) = TargetResolver.Resolve(
            scan.Targets, ts, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), new ResolveOptions(toleranceDegrees), ct);
        TimeSpan tResolve = sw.Elapsed;

        List<ReconciliationRow> rows = BuildRows(graph, report);

        Log.Diag("Load",
            $"tsRead={Sec(tTsRead)}s tsTargets={ts.Targets.Count} tsPlans={ts.Plans.Count}" +
            $" resolve={Sec(tResolve - tTsRead)}s rows={rows.Count} total={Sec(sw.Elapsed)}s");
        Log.Diag("Load",
            $"report: both={report.BothCount} tsOnly={report.PlannedOnlyCount} diskOnly={report.ActualOnlyCount}" +
            $" dups={report.DuplicateTsTargets.Count}" +
            $" mismatches={report.NameMismatches.Count} ambiguous={report.AmbiguousMatches.Count}" +
            $" unanchored={report.UnanchoredTsTargets.Count} mosaics={report.MosaicsResolved}" +
            $" panels={report.PanelsMatched}/{report.PanelsPlannedOnly}/{report.PanelsActualOnly}");

        return new LoadResult(rows, report, graph, ts, sw.Elapsed, scan.SkippedFiles);
    }, ct);

    /// <summary>
    /// Shapes the library's per-target reconciliation cells (<see cref="ReconciliationProjection"/>) into flat
    /// per-plane rows: each (target, filter, purpose, seconds) cell yields a TS row (plan commitment) and/or a
    /// Disk row (actual integration), with mosaic panels nested under their parent. Write-back's coarser
    /// (filter, purpose) key folds these — what the grid shows is still what write-back (the retired CLI
    /// verb, returning as an app action) acts on.
    /// The cell join lives in the library; everything here (planes, rollups, hours, badges, panels, sort) is
    /// TSM's grid presentation.
    /// </summary>
    internal static List<ReconciliationRow> BuildRows(CatalogGraph graph, CatalogBuildReport report)
    {
        IReadOnlyList<TargetCells> projected = ReconciliationProjection.Project(graph, report);

        ILookup<Guid, TargetCells> childrenByParent = projected
            .Where(t => t.ParentTargetId is not null)
            .ToLookup(t => t.ParentTargetId!.Value);

        List<ReconciliationRow> rows = [];
        Dictionary<string, int> panelOrdinal = new(StringComparer.OrdinalIgnoreCase);

        foreach (TargetCells top in projected)
        {
            if (top.ParentTargetId is not null) continue;   // panels emit under their parent below

            List<TargetCells> children = [.. childrenByParent[top.TargetId]];
            if (children.Count == 0)
            {
                EmitRows(top, top.Name, MapSource(top.Source), panelKey: null, panelLabel: null, panelSource: null);
                continue;
            }

            // A mosaic family: the parent is a grouping node with no rows of its own; each panel's rows
            // carry the parent's name (the grid groups by it) plus the panel identity for the nested level.
            RowSource parentSource = MapSource(top.Source);
            foreach (TargetCells child in children
                .OrderBy(c => c.DirectoryName is null ? 1 : 0)   // disk-backed panels first, then planned
                .ThenBy(c => c.DirectoryName ?? c.Name, StringComparer.OrdinalIgnoreCase))
            {
                string? diskLabel = child.DirectoryName is string d ? MosaicConvention.PanelLabel(d) : null;
                RowSource childSource = MapSource(child.Source);
                string panelLabel = childSource switch
                {
                    RowSource.Both => $"{diskLabel} · {child.Name}",
                    RowSource.TsOnly => child.Name,
                    _ => diskLabel ?? child.Name,
                };
                // A planned-only panel has no directory; key it by its TS guid — TsTargetKey, the target's
                // imported_from_ts_guid the projection already carries — so the shaping needs only TargetCells.
                string panelKey = child.DirectoryName ?? $"ts:{child.TsTargetKey ?? child.Name}";
                panelOrdinal[$"{top.Name}|{panelKey}"] = panelOrdinal.Count;
                EmitRows(child, top.Name, parentSource, panelKey, panelLabel, childSource);
            }
        }

        static RowSource MapSource(TargetSource s) => s switch
        {
            TargetSource.Both => RowSource.Both,
            TargetSource.Planned => RowSource.TsOnly,
            _ => RowSource.DiskOnly,
        };

        void EmitRows(TargetCells tc, string groupName, RowSource source,
            string? panelKey, string? panelLabel, RowSource? panelSource)
        {
            string project = tc.ProjectName;
            // The identity every row of this emit shares — built once, never re-threaded (see RowIdentity).
            RowIdentity id = new(groupName, project, source, panelKey, panelLabel, panelSource,
                tc.Enabled, tc.TsTargetKey, tc.TargetId, tc.ProjectTsKey);
            bool isMosaic = tc.IsMosaicDirectory;
            bool isDup = tc.Issues.HasFlag(TargetMatchIssues.Duplicate);
            bool isMismatch = tc.Issues.HasFlag(TargetMatchIssues.NameMismatch);
            bool isAmbiguous = tc.Issues.HasFlag(TargetMatchIssues.AmbiguousMatch);
            bool isUnanchored = tc.IsUnanchored;

            // One rollup per (filter, purpose) that has BOTH a plan side and a disk side, aggregating every
            // sub length. A rollup whose times all agree is a plain merged row; 2+ distinct times reads
            // "mixed" (caution pill) and expands into one source line per sub length — a nested Both line
            // where a bucket has both planes, TS/Disk where one-sided. One-plane filters emit plain lines.
            foreach (IGrouping<(string Filter, FilterPurpose Purpose), ReconciliationCell> fp in tc.Cells
                .GroupBy(c => (c.Filter.ToUpperInvariant(), c.Purpose)))
            {
                // Same-purpose multiplicity routes write-back to manual — always a flag (the alias
                // suppression died with the fold mechanism, 2026-07-08 doctrine).
                bool multiPlan = fp.Sum(c => c.PlanCount) > 1;
                // TS records out of sync: a plan whose accepted ≠ acquired. With the grader off TS increments
                // both together (accepted == acquired) and write-back re-sets them equal, so any divergence is the
                // in-session TS drift the user reconciles — surface it as a flag rather than re-showing the hidden
                // Accepted column.
                bool accNeAcq = fp.Any(c => c.PlanCount > 0 && c.Accepted != c.Acquired);
                List<string> badges = [];
                if (isMosaic) badges.Add(Badges.Mosaic);
                if (isDup) badges.Add(Badges.Duplicate);
                if (isMismatch) badges.Add(Badges.NameMismatch);
                if (isAmbiguous) badges.Add(Badges.Ambiguous);
                if (isUnanchored) badges.Add(Badges.NoCoords);
                if (multiPlan) badges.Add(Badges.MultiPlan);
                if (accNeAcq) badges.Add(Badges.AccNeAcq);

                // Target-scope badges: every row of this (filter, purpose) carries them. The camera-provenance
                // tokens are row-scoped and are appended per row below, so one bad capture directory marks the
                // rows drawing on it rather than all of a target's rows.
                string badge = Badges.Join(badges);
                // The flagged set IS the warning-badge set (Badges.IsWarning) — colour and the flagged-only
                // filter must agree, or an amber row gets hidden by the filter that should surface it. An
                // unanchored target counts: TS can't schedule it and it can never accrue disk credit.
                bool flagged = isDup || isMismatch || isAmbiguous || multiPlan || accNeAcq || isUnanchored;

                List<ReconciliationCell> planCells = [], diskCells = [];
                foreach (ReconciliationCell c in fp.OrderBy(c => c.Seconds))
                {
                    if (c.PlanCount > 0) planCells.Add(c);
                    if (c.Disk > 0) diskCells.Add(c);   // a cell carrying both planes sits in both lists
                }

                if (planCells.Count > 0 && diskCells.Count > 0)
                {
                    // One detail line per sub length, seconds ascending: a bucket carrying both planes is
                    // a nested Both line (plan + disk together, gap hours); one-sided buckets stay TS/Disk.
                    List<ReconciliationRow> detail = [];
                    foreach (ReconciliationCell c in fp.OrderBy(c => c.Seconds))
                    {
                        if (c.PlanCount > 0 && c.Disk > 0) detail.Add(BothRow(c));
                        else if (c.PlanCount > 0) detail.Add(TsRow(c, isDetail: true));
                        else detail.Add(DiskRow(c, isDetail: true));
                    }

                    // The rollup collapses to a single plain row ONLY when the group is one cell carrying both
                    // planes — i.e. the plan and the frames agree on the whole capture configuration, so the
                    // same cell sits in both lists. Anything else (several sub lengths, or a plan whose gain /
                    // offset / binning the frames do not match) keeps its disclosure, and the source lines
                    // below tell the reader which plane is which. Merging an unmatched pair into one Both row
                    // would assert a match that does not exist.
                    bool matchedSingle = planCells.Count == 1 && diskCells.Count == 1
                        && ReferenceEquals(planCells[0], diskCells[0]);
                    bool mixed = !matchedSingle;

                    // The rollup's own camera/framing provenance is the union over its disk-side cells, so a
                    // bad capture directory or a stray framing anywhere beneath it is visible without
                    // expanding. Display refinement (user obs 6b72 + 8be0, 2026-07-29): `Badge` carries the
                    // full union, but an EXPANDED rollup renders `BadgeText` without the framing token — the
                    // visible source line beneath it carries the badge then, and repeating it between the
                    // header and that line was noise. Collapsed, the rollup shows it (the line is hidden).
                    ReconciliationCell configCell = diskCells[0];
                    string rollupBadge = badge;
                    bool rollupFlagged = flagged;
                    foreach (ReconciliationCell c in diskCells)
                    {
                        rollupBadge = RowBadge(rollupBadge, c, true);
                        rollupFlagged = RowFlagged(rollupFlagged, c, true);
                    }

                    rows.Add(new ReconciliationRow(
                        id, planCells[0].Filter, fp.Key.Purpose.ToString(), RowPlane.Both,
                        new RowNumbers(
                            PlanSeconds: planCells[0].Seconds, DiskSeconds: diskCells[0].Seconds,
                            Desired: planCells.Sum(c => c.Desired),
                            Acquired: planCells.Sum(c => c.Acquired),
                            Accepted: planCells.Sum(c => c.Accepted),
                            Disk: diskCells.Sum(c => c.Disk),
                            PlanCount: planCells.Sum(c => c.PlanCount),
                            PlanHours: planCells.Sum(c => c.Desired * (double)c.Seconds) / 3600.0,
                            DiskHours: diskCells.Sum(c => c.Disk * (double)c.Seconds) / 3600.0,
                            RemainingHours: planCells.Sum(CellRemainingHours)),
                        Badges.Join(Badges.Split(rollupBadge).Select(t => t.Token).Distinct()), rollupFlagged,
                        secondsMixed: mixed,
                        detail: mixed ? detail : null,
                        // One plan behind the group ⇒ the rollup keeps its flyout anchor, even when it is
                        // mixed. The INLINE Desired box is gated separately by CanEditDesired (!SecondsMixed),
                        // so a mixed rollup opens the flyout but edits its count at the plan's own detail line.
                        planTsKey: planCells.Count == 1 ? planCells[0].PlanTsKey : null,
                        planEnabled: planCells.Count == 1 ? planCells[0].PlanEnabled : null,
                        config: Cfg(configCell, true, tc.TargetRotationDeg)));
                }
                else
                {
                    foreach (ReconciliationCell c in planCells) rows.Add(TsRow(c, isDetail: false));
                    foreach (ReconciliationCell c in diskCells) rows.Add(DiskRow(c, isDetail: false));
                }

                // The capture configuration a cell describes. Camera rides only on rows with a disk side — a
                // plan cannot name one — so a TS row's camera cell shows the dash. Rotation likewise splits
                // by plane: a disk-backed row shows its framing cluster's rotation; a plan-only row shows the
                // target's own rotation folded (as Sky), so an agreeing pair reads identically.
                static RowConfig Cfg(ReconciliationCell c, bool withCamera, double? targetRotationDeg) => new(
                    c.Gain, c.Offset, c.BinningX, c.BinningY,
                    withCamera ? c.Camera : null,
                    withCamera && c.CameraDisagrees,
                    Rotation: withCamera
                        ? c.DiskRotation
                        : targetRotationDeg is null ? null : RotationExpression.Sky,
                    RotationFoldDeg: withCamera
                        ? c.DiskRotationFoldDeg
                        : targetRotationDeg is double rot ? FramingCluster.Fold180(rot) : null,
                    FramingDisagrees: withCamera && c.FramingDisagrees,
                    FramingOverlapFraction: withCamera ? c.FramingOverlapFraction : null);

                // The time a cell's plan side still owes — the Hours gauge's debt component (obs 01b7):
                // clamped per cell so one cell's overshoot never masks another's shortfall, and
                // acquired-based (write-back stamps acquired from serving frames only) so the gauge reads
                // what TS actually schedules on, not the raw disk count.
                static double CellRemainingHours(ReconciliationCell c) =>
                    Math.Max(0, c.Desired - c.Acquired) * (double)c.Seconds / 3600.0;

                // Row-scoped camera/framing provenance, appended to the target-scope tokens for THIS row only.
                static string RowBadge(string targetBadge, ReconciliationCell c, bool withCamera)
                {
                    if (!withCamera) return targetBadge;
                    List<string> extra = [];
                    if (c.Camera is not null && Format.Camera(c.Camera) is null) extra.Add(Badges.UnknownCamera);
                    if (c.CameraDisagrees) extra.Add(Badges.CameraMismatch);
                    if (c.FramingDisagrees) extra.Add(Badges.Framing);
                    return extra.Count == 0 ? targetBadge : Badges.Join([targetBadge, .. extra]);
                }

                static bool RowFlagged(bool targetFlagged, ReconciliationCell c, bool withCamera) =>
                    targetFlagged
                    || (withCamera && ((c.Camera is not null && Format.Camera(c.Camera) is null)
                        || c.CameraDisagrees || c.FramingDisagrees));

                ReconciliationRow BothRow(ReconciliationCell c) => new(
                    id, c.Filter, c.Purpose.ToString(), RowPlane.Both,
                    new RowNumbers(
                        PlanSeconds: c.Seconds, DiskSeconds: c.Seconds,
                        Desired: c.Desired, Acquired: c.Acquired, Accepted: c.Accepted,
                        Disk: c.Disk, PlanCount: c.PlanCount,
                        PlanHours: c.Desired * (double)c.Seconds / 3600.0,
                        DiskHours: c.Disk * (double)c.Seconds / 3600.0,
                        RemainingHours: CellRemainingHours(c)),
                    RowBadge(badge, c, true), RowFlagged(flagged, c, true), isDetail: true,
                    planTsKey: c.PlanTsKey, planEnabled: c.PlanEnabled, config: Cfg(c, true, tc.TargetRotationDeg));

                ReconciliationRow TsRow(ReconciliationCell c, bool isDetail) => new(
                    id, c.Filter, c.Purpose.ToString(), RowPlane.Ts,
                    new RowNumbers(
                        PlanSeconds: c.Seconds, DiskSeconds: 0,
                        Desired: c.Desired, Acquired: c.Acquired, Accepted: c.Accepted,
                        Disk: 0, PlanCount: c.PlanCount,
                        PlanHours: c.Seconds > 0 ? c.Desired * c.Seconds / 3600.0 : null,
                        DiskHours: null,
                        RemainingHours: c.Seconds > 0 ? CellRemainingHours(c) : null),
                    badge, flagged, isDetail: isDetail,
                    planTsKey: c.PlanTsKey, planEnabled: c.PlanEnabled, config: Cfg(c, false, tc.TargetRotationDeg));

                ReconciliationRow DiskRow(ReconciliationCell c, bool isDetail) => new(
                    id, c.Filter, c.Purpose.ToString(), RowPlane.Disk,
                    new RowNumbers(
                        PlanSeconds: 0, DiskSeconds: c.Seconds,
                        Desired: null, Acquired: null, Accepted: null,
                        Disk: c.Disk, PlanCount: 0,
                        PlanHours: null,
                        DiskHours: c.Disk * (double)c.Seconds / 3600.0),
                    RowBadge(badge, c, true), RowFlagged(flagged, c, true), isDetail: isDetail,
                    config: Cfg(c, true, tc.TargetRotationDeg));
            }

            // A target with no plans and no scanned LIGHT frames would otherwise vanish from the grid. Its one
            // badge splits the two cases: no-coords is broken authoring (flagged), "no data" is queued work.
            if (tc.Cells.Count == 0)
            {
                rows.Add(new ReconciliationRow(
                    id, Format.Dash, Format.Dash, plane: source == RowSource.TsOnly ? RowPlane.Ts : RowPlane.Disk,
                    new RowNumbers(
                        PlanSeconds: 0, DiskSeconds: 0,
                        Desired: null, Acquired: null, Accepted: null,
                        Disk: 0, PlanCount: 0, PlanHours: null, DiskHours: null),
                    badge: isUnanchored ? Badges.NoCoords : Badges.NoData, isFlagged: isUnanchored));
            }
        }

        rows.Sort((a, b) =>
        {
            // Sort precedence: Target → Project → Panel → Filter → Purpose → Seconds → capture configuration
            // → Plane, natural order on the text columns ("IC 405" before "IC 1318").
            //
            // DELIBERATE EXCEPTION to "sort follows column order" (openspec capture-config-keys): Camera, Gain,
            // Offset and Bin sit LEFT of Filter but sort AFTER Seconds. Sorting them in column position would
            // group every gain-53 row across all filters ahead of every gain-0 row, splitting one filter's rows
            // apart — precisely when the reader has expanded a target to follow that filter's story. Keeping
            // them late leaves each filter's rows contiguous, with configuration breaking ties within a filter.
            int byTarget = NaturalComparer.Instance.Compare(a.Target, b.Target);
            if (byTarget != 0) return byTarget;
            int byProject = NaturalComparer.Instance.Compare(a.Project, b.Project);
            if (byProject != 0) return byProject;
            int byPanel = PanelOrd(a).CompareTo(PanelOrd(b));
            if (byPanel != 0) return byPanel;
            int byFilter = NaturalComparer.Instance.Compare(a.Filter, b.Filter);
            if (byFilter != 0) return byFilter;
            int byPurpose = string.Compare(a.Purpose, b.Purpose, StringComparison.Ordinal);
            if (byPurpose != 0) return byPurpose;
            int bySeconds = SortSeconds(a).CompareTo(SortSeconds(b));
            if (bySeconds != 0) return bySeconds;
            int byGain = a.Config.Gain.CompareTo(b.Config.Gain);
            if (byGain != 0) return byGain;
            int byOffset = a.Config.Offset.CompareTo(b.Config.Offset);
            if (byOffset != 0) return byOffset;
            int byBin = a.Config.BinningX.CompareTo(b.Config.BinningX);
            if (byBin != 0) return byBin;
            int byCamera = string.Compare(a.Config.Camera, b.Config.Camera, StringComparison.OrdinalIgnoreCase);
            return byCamera != 0 ? byCamera : a.Plane.CompareTo(b.Plane);
        });
        return rows;

        // Panels keep their emit order (disk-backed by label, then planned); normal rows have no panel.
        int PanelOrd(ReconciliationRow r) =>
            r.PanelKey is null ? -1 : panelOrdinal[$"{r.Target}|{r.PanelKey}"];

        // Plan seconds when the row has a plan side, the disk bucket otherwise — keeps a filter's rows
        // in sub-length order with merged rows sitting where their plan does.
        static int SortSeconds(ReconciliationRow r) => r.PlanSeconds > 0 ? r.PlanSeconds : r.DiskSeconds;
    }

    private static string Sec(TimeSpan t) => t.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture);
}