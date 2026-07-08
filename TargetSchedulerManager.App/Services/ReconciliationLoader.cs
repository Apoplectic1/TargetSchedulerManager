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

/// <summary>Result of one load: the grid rows, the build report the summary/badges came from, and the resolved
/// <see cref="CatalogGraph"/> retained so the detail panel can pull a target's full disk + TS dossier.</summary>
public sealed record LoadResult(
    IReadOnlyList<ReconciliationRow> Rows,
    CatalogBuildReport Report,
    CatalogGraph Graph,
    TimeSpan Elapsed);

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
            ts = reader.ReadPlanData();
        TimeSpan tTsRead = sw.Elapsed;

        (CatalogGraph graph, CatalogBuildReport report) = TargetResolver.Resolve(
            scan.Targets, ts, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), new ResolveOptions(toleranceDegrees));
        TimeSpan tResolve = sw.Elapsed;

        List<ReconciliationRow> rows = BuildRows(graph, report);

        Log.Diag("Load",
            $"tsRead={Sec(tTsRead)}s tsTargets={ts.Targets.Count} tsPlans={ts.Plans.Count}" +
            $" resolve={Sec(tResolve - tTsRead)}s rows={rows.Count} total={Sec(sw.Elapsed)}s");
        Log.Diag("Load",
            $"report: both={report.BothCount} tsOnly={report.PlannedOnlyCount} diskOnly={report.ActualOnlyCount}" +
            $" aliases={report.AliasTsTargets.Count} dups={report.DuplicateTsTargets.Count}" +
            $" mismatches={report.NameMismatches.Count} ambiguous={report.AmbiguousMatches.Count}" +
            $" unanchored={report.UnanchoredTsTargets.Count} mosaics={report.MosaicsResolved}" +
            $" panels={report.PanelsMatched}/{report.PanelsPlannedOnly}/{report.PanelsActualOnly}");

        return new LoadResult(rows, report, graph, sw.Elapsed);
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
            bool isMosaic = tc.IsMosaicDirectory;
            bool isAlias = tc.Issues.HasFlag(TargetMatchIssues.Alias);
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
                // A multi-plan group is explained (alias members fold) or it's the same-purpose
                // multiplicity that routes write-back to manual — only the latter is a flag.
                bool multiPlan = fp.Sum(c => c.PlanCount) > 1 && !isAlias;
                // TS records out of sync: a plan whose accepted ≠ acquired. With the grader off TS increments
                // both together (accepted == acquired) and write-back re-sets them equal, so any divergence is the
                // in-session TS drift the user reconciles — surface it as a flag rather than re-showing the hidden
                // Accepted column.
                bool accNeAcq = fp.Any(c => c.PlanCount > 0 && c.Accepted != c.Acquired);
                List<string> badges = [];
                if (isMosaic) badges.Add("mosaic");
                if (isAlias) badges.Add("alias");
                if (isDup) badges.Add("duplicate");
                if (isMismatch) badges.Add("name≠");
                if (isAmbiguous) badges.Add("ambiguous");
                if (isUnanchored) badges.Add("no-coords");
                if (multiPlan) badges.Add("multi-plan");
                if (accNeAcq) badges.Add("acc≠acq");

                string badge = string.Join(" · ", badges);
                bool flagged = isDup || isMismatch || isAmbiguous || multiPlan || accNeAcq;

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

                    bool mixed = planCells.Count > 1 || diskCells.Count > 1
                        || planCells[0].Seconds != diskCells[0].Seconds;

                    rows.Add(new ReconciliationRow(
                        groupName, project, planCells[0].Filter, fp.Key.Purpose.ToString(),
                        planSeconds: planCells[0].Seconds, diskSeconds: diskCells[0].Seconds,
                        source, RowPlane.Both,
                        desired: planCells.Sum(c => c.Desired),
                        acquired: planCells.Sum(c => c.Acquired),
                        accepted: planCells.Sum(c => c.Accepted),
                        disk: diskCells.Sum(c => c.Disk),
                        planCount: planCells.Sum(c => c.PlanCount),
                        badge, flagged,
                        planHours: planCells.Sum(c => c.Desired * (double)c.Seconds) / 3600.0,
                        diskHours: diskCells.Sum(c => c.Disk * (double)c.Seconds) / 3600.0,
                        secondsMixed: mixed,
                        detail: mixed ? detail : null,
                        panelKey: panelKey, panelLabel: panelLabel, panelSource: panelSource,
                        enabled: tc.Enabled, tsTargetKey: tc.TsTargetKey, projectTsKey: tc.ProjectTsKey, targetId: tc.TargetId,
                        // 1:1 only: a single plan sub-length is editable; a mixed rollup's sum is not.
                        planTsKey: planCells.Count == 1 ? planCells[0].PlanTsKey : null,
                        planEnabled: planCells.Count == 1 ? planCells[0].PlanEnabled : null));
                }
                else
                {
                    foreach (ReconciliationCell c in planCells) rows.Add(TsRow(c, isDetail: false));
                    foreach (ReconciliationCell c in diskCells) rows.Add(DiskRow(c, isDetail: false));
                }

                ReconciliationRow BothRow(ReconciliationCell c) => new(
                    groupName, project, c.Filter, c.Purpose.ToString(),
                    planSeconds: c.Seconds, diskSeconds: c.Seconds, source, RowPlane.Both,
                    c.Desired, c.Acquired, c.Accepted, c.Disk, c.PlanCount, badge, flagged,
                    planHours: c.Desired * (double)c.Seconds / 3600.0,
                    diskHours: c.Disk * (double)c.Seconds / 3600.0,
                    isDetail: true,
                    panelKey: panelKey, panelLabel: panelLabel, panelSource: panelSource,
                    enabled: tc.Enabled, tsTargetKey: tc.TsTargetKey, projectTsKey: tc.ProjectTsKey, targetId: tc.TargetId,
                    planTsKey: c.PlanTsKey, planEnabled: c.PlanEnabled);

                ReconciliationRow TsRow(ReconciliationCell c, bool isDetail) => new(
                    groupName, project, c.Filter, c.Purpose.ToString(),
                    planSeconds: c.Seconds, diskSeconds: 0, source, RowPlane.Ts,
                    c.Desired, c.Acquired, c.Accepted, disk: 0, c.PlanCount, badge, flagged,
                    planHours: c.Seconds > 0 ? c.Desired * c.Seconds / 3600.0 : null, diskHours: null,
                    isDetail: isDetail,
                    panelKey: panelKey, panelLabel: panelLabel, panelSource: panelSource,
                    enabled: tc.Enabled, tsTargetKey: tc.TsTargetKey, projectTsKey: tc.ProjectTsKey, targetId: tc.TargetId,
                    planTsKey: c.PlanTsKey, planEnabled: c.PlanEnabled);

                ReconciliationRow DiskRow(ReconciliationCell c, bool isDetail) => new(
                    groupName, project, c.Filter, c.Purpose.ToString(),
                    planSeconds: 0, diskSeconds: c.Seconds, source, RowPlane.Disk,
                    desired: null, acquired: null, accepted: null, c.Disk, planCount: 0, badge, flagged,
                    planHours: null, diskHours: c.Disk * (double)c.Seconds / 3600.0,
                    isDetail: isDetail,
                    panelKey: panelKey, panelLabel: panelLabel, panelSource: panelSource,
                    enabled: tc.Enabled, tsTargetKey: tc.TsTargetKey, projectTsKey: tc.ProjectTsKey, targetId: tc.TargetId);
            }

            // A target with no plans and no scanned LIGHT frames would otherwise vanish from the grid.
            if (tc.Cells.Count == 0)
            {
                rows.Add(new ReconciliationRow(
                    groupName, project, "—", "—", planSeconds: 0, diskSeconds: 0, source,
                    plane: source == RowSource.TsOnly ? RowPlane.Ts : RowPlane.Disk,
                    desired: null, acquired: null, accepted: null, disk: 0, planCount: 0,
                    badge: isUnanchored ? "no-coords" : "no data",
                    isFlagged: false, planHours: null, diskHours: null,
                    panelKey: panelKey, panelLabel: panelLabel, panelSource: panelSource,
                    enabled: tc.Enabled, tsTargetKey: tc.TsTargetKey, projectTsKey: tc.ProjectTsKey, targetId: tc.TargetId));
            }
        }

        rows.Sort((a, b) =>
        {
            // Sort precedence follows the columns left-to-right: Target → Project → Filter → Purpose → Seconds,
            // natural order on the text columns ("IC 405" before "IC 1318"). Project only separates same-named
            // targets in different projects; Panel keeps a mosaic's panels under their parent; Plane (TS above
            // Disk) is the final tiebreak within a cell.
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
            return bySeconds != 0 ? bySeconds : a.Plane.CompareTo(b.Plane);
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