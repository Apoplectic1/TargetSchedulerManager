using System.Diagnostics;
using System.Globalization;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetCatalogManager.App.Models;
using TargetCatalogManager.App.Support;

namespace TargetCatalogManager.App.Services;

/// <summary>Result of one load: the grid rows plus the build report the summary/badges came from.</summary>
public sealed record LoadResult(
    IReadOnlyList<ReconciliationRow> Rows,
    CatalogBuildReport Report,
    TimeSpan Elapsed);

/// <summary>
/// The app's data layer: fresh disk scan + TS snapshot read + <see cref="TargetResolver"/>, all in memory —
/// the same pipeline the <c>tcm</c> console host runs, minus the Catalog.db write. The grid therefore can
/// never show stale ACTUAL, and the app doesn't need (or touch) Catalog.db at all in M1.
/// </summary>
public static class ReconciliationLoader
{
    public static async Task<LoadResult> LoadAsync(
        string libraryRoot, string tsDbPath, double toleranceDegrees, CancellationToken ct = default)
    {
        Stopwatch sw = Stopwatch.StartNew();

        ImageLibraryReport scan = await ImageLibraryScanner.ScanAsync(libraryRoot, ct).ConfigureAwait(false);
        TimeSpan tScan = sw.Elapsed;

        TsPlanData ts;
        using (TargetSchedulerReader reader = new(tsDbPath))
            ts = reader.ReadPlanData();
        TimeSpan tTsRead = sw.Elapsed;

        (CatalogGraph graph, CatalogBuildReport report) = TargetResolver.Resolve(
            scan.Targets, ts, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), new ResolveOptions(toleranceDegrees));
        TimeSpan tResolve = sw.Elapsed;

        List<ReconciliationRow> rows = BuildRows(graph, report);

        Log.Diag("Load",
            $"scan={Sec(tScan)}s diskTargets={scan.Targets.Count}" +
            $" tsRead={Sec(tTsRead - tScan)}s tsTargets={ts.Targets.Count} tsPlans={ts.Plans.Count}" +
            $" resolve={Sec(tResolve - tTsRead)}s rows={rows.Count} total={Sec(sw.Elapsed)}s");
        Log.Diag("Load",
            $"report: both={report.BothCount} tsOnly={report.PlannedOnlyCount} diskOnly={report.ActualOnlyCount}" +
            $" aliases={report.AliasTsTargets.Count} dups={report.DuplicateTsTargets.Count}" +
            $" mismatches={report.NameMismatches.Count} ambiguous={report.AmbiguousMatches.Count}" +
            $" unanchored={report.UnanchoredTsTargets.Count} mosaics={report.MosaicsResolved}/{report.PanelsFolded}");

        return new LoadResult(rows, report, sw.Elapsed);
    }

    /// <summary>
    /// Projects the resolved graph into flat per-plane rows: each (target, filter, purpose, seconds) cell
    /// yields a TS row (plan commitment) and/or a Disk row (actual integration). Write-back's coarser
    /// (filter, purpose) key folds these — what the grid shows is still what <c>tcm writeback</c> acts on.
    /// </summary>
    private static List<ReconciliationRow> BuildRows(CatalogGraph graph, CatalogBuildReport report)
    {
        Dictionary<Guid, Project> projects = graph.Projects.ToDictionary(p => p.Id);
        Dictionary<Guid, ExposureTemplate> templates = graph.Templates.ToDictionary(t => t.Id);
        ILookup<Guid, ExposurePlan> plansByTarget = graph.Plans.ToLookup(p => p.TargetId);
        ILookup<Guid, InventoryFilter> invByTarget = graph.InventoryFilters.ToLookup(i => i.TargetId);

        HashSet<string> aliasDirs = new(report.AliasTsTargets.Select(a => a.DiskDirectory), StringComparer.OrdinalIgnoreCase);
        HashSet<string> dupDirs = new(report.DuplicateTsTargets.Select(d => d.DiskDirectory), StringComparer.OrdinalIgnoreCase);
        HashSet<string> mismatchDirs = new(report.NameMismatches.Select(m => m.DiskDirectory), StringComparer.OrdinalIgnoreCase);
        HashSet<string> ambiguousDirs = new(StringComparer.OrdinalIgnoreCase);
        foreach (AmbiguousMatch a in report.AmbiguousMatches)
            foreach (string d in a.CandidateDirectories)
                ambiguousDirs.Add(d);
        HashSet<string> unanchoredNames = new(report.UnanchoredTsTargets.Select(u => u.TsName), StringComparer.OrdinalIgnoreCase);

        List<ReconciliationRow> rows = [];
        foreach (Target t in graph.Targets)
        {
            string project = t.ProjectId is Guid pid && projects.TryGetValue(pid, out Project? proj) ? proj.Name : "—";
            string? dir = t.DirectoryName;
            bool isMosaic = dir is not null && MosaicConvention.IsMosaicDirectory(dir);
            bool isAlias = dir is not null && aliasDirs.Contains(dir);
            bool isDup = dir is not null && dupDirs.Contains(dir);
            bool isMismatch = dir is not null && mismatchDirs.Contains(dir);
            bool isAmbiguous = dir is not null && ambiguousDirs.Contains(dir);
            bool isUnanchored = t.Source == TargetSource.Planned && unanchoredNames.Contains(t.Name);

            RowSource source = t.Source switch
            {
                TargetSource.Both => RowSource.Both,
                TargetSource.Planned => RowSource.TsOnly,
                _ => RowSource.DiskOnly,
            };

            // Aggregate plans and inventory per (filter, purpose, exposure seconds), filter case-insensitive;
            // the pairing pass below decides which cells merge into Both rows and which stay one-plane.
            Dictionary<(string Filter, FilterPurpose Purpose, int Seconds), Cell> cells = [];
            foreach (ExposurePlan p in plansByTarget[t.Id])
            {
                if (!templates.TryGetValue(p.ExposureTemplateId, out ExposureTemplate? tpl)) continue;
                // Effective planned sub length: the plan's own value, else its template default
                // (the resolver already normalized TS's -1 sentinel to null). 0 = unknown.
                int seconds = (int)Math.Round(p.ExposureSeconds ?? tpl.DefaultExposureSeconds ?? 0.0);
                Cell c = GetCell(cells, tpl.FilterName, FilterPurposeClassifier.Classify(tpl.Name), seconds);
                c.Desired += p.DesiredCount;
                c.Acquired += p.AcquiredCount;
                c.Accepted += p.AcceptedCount;
                c.PlanCount++;
            }
            foreach (InventoryFilter f in invByTarget[t.Id])
            {
                // The scanner already buckets aggregates to whole seconds (ExposureSeconds is identity).
                Cell c = GetCell(cells, f.FilterName, f.Purpose, (int)Math.Round(f.ExposureSeconds));
                c.Disk += f.ExposureCount;
            }

            // Pair the planes back up per (filter, purpose): a cell carrying both planes is a Both row
            // outright (sub lengths agree); after that, a lone leftover plan and a lone leftover disk
            // bucket also merge — the Seconds cell shows the "disk≠plan" drift. Several leftovers on a
            // side would make that pairing a guess, so those stay one-plane rows.
            foreach (IGrouping<(string Filter, FilterPurpose Purpose), Cell> fp in cells.Values
                .GroupBy(c => (c.Filter.ToUpperInvariant(), c.Purpose)))
            {
                // A multi-plan group is explained (mosaic panels fold, alias members fold) or it's the
                // same-purpose multiplicity that routes write-back to manual — only the latter is a flag.
                bool multiPlan = fp.Sum(c => c.PlanCount) > 1 && !isMosaic && !isAlias;
                List<string> badges = [];
                if (isMosaic) badges.Add("mosaic");
                if (isAlias) badges.Add("alias");
                if (isDup) badges.Add("duplicate");
                if (isMismatch) badges.Add("name≠");
                if (isAmbiguous) badges.Add("ambiguous");
                if (isUnanchored) badges.Add("no-coords");
                if (multiPlan) badges.Add("multi-plan");

                string badge = string.Join(" · ", badges);
                bool flagged = isDup || isMismatch || isAmbiguous || multiPlan;

                List<Cell> tsLeft = [], diskLeft = [];
                foreach (Cell c in fp.OrderBy(c => c.Seconds))
                {
                    if (c.PlanCount > 0 && c.Disk > 0)
                    {
                        rows.Add(new ReconciliationRow(
                            t.Name, project, c.Filter, c.Purpose.ToString(),
                            planSeconds: c.Seconds, diskSeconds: c.Seconds, source, RowPlane.Both,
                            c.Desired, c.Acquired, c.Accepted, c.Disk, c.PlanCount, badge, flagged));
                    }
                    else if (c.PlanCount > 0) tsLeft.Add(c);
                    else diskLeft.Add(c);
                }

                if (tsLeft.Count == 1 && diskLeft.Count == 1)
                {
                    Cell ts = tsLeft[0], dk = diskLeft[0];
                    rows.Add(new ReconciliationRow(
                        t.Name, project, ts.Filter, ts.Purpose.ToString(),
                        planSeconds: ts.Seconds, diskSeconds: dk.Seconds, source, RowPlane.Both,
                        ts.Desired, ts.Acquired, ts.Accepted, dk.Disk, ts.PlanCount, badge, flagged));
                }
                else
                {
                    foreach (Cell c in tsLeft)
                    {
                        rows.Add(new ReconciliationRow(
                            t.Name, project, c.Filter, c.Purpose.ToString(),
                            planSeconds: c.Seconds, diskSeconds: 0, source, RowPlane.Ts,
                            c.Desired, c.Acquired, c.Accepted, disk: 0, c.PlanCount, badge, flagged));
                    }
                    foreach (Cell c in diskLeft)
                    {
                        rows.Add(new ReconciliationRow(
                            t.Name, project, c.Filter, c.Purpose.ToString(),
                            planSeconds: 0, diskSeconds: c.Seconds, source, RowPlane.Disk,
                            desired: null, acquired: null, accepted: null, c.Disk, planCount: 0, badge, flagged));
                    }
                }
            }

            // A target with no plans and no scanned LIGHT frames would otherwise vanish from the grid.
            if (cells.Count == 0)
            {
                rows.Add(new ReconciliationRow(
                    t.Name, project, "—", "—", planSeconds: 0, diskSeconds: 0, source,
                    plane: source == RowSource.TsOnly ? RowPlane.Ts : RowPlane.Disk,
                    desired: null, acquired: null, accepted: null, disk: 0, planCount: 0,
                    badge: isUnanchored ? "no-coords" : "no data",
                    isFlagged: false));
            }
        }

        rows.Sort((a, b) =>
        {
            int byTarget = string.Compare(a.Target, b.Target, StringComparison.OrdinalIgnoreCase);
            if (byTarget != 0) return byTarget;
            int byFilter = string.Compare(a.Filter, b.Filter, StringComparison.OrdinalIgnoreCase);
            if (byFilter != 0) return byFilter;
            int byPurpose = string.Compare(a.Purpose, b.Purpose, StringComparison.Ordinal);
            if (byPurpose != 0) return byPurpose;
            int bySeconds = SortSeconds(a).CompareTo(SortSeconds(b));
            return bySeconds != 0 ? bySeconds : a.Plane.CompareTo(b.Plane);
        });
        return rows;

        // Plan seconds when the row has a plan side, the disk bucket otherwise — keeps a filter's rows
        // in sub-length order with merged rows sitting where their plan does.
        static int SortSeconds(ReconciliationRow r) => r.PlanSeconds > 0 ? r.PlanSeconds : r.DiskSeconds;
    }

    private static string Sec(TimeSpan t) => t.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture);

    private static Cell GetCell(
        Dictionary<(string, FilterPurpose, int), Cell> cells, string filter, FilterPurpose purpose, int seconds)
    {
        (string, FilterPurpose, int) key = (filter.ToUpperInvariant(), purpose, seconds);
        if (!cells.TryGetValue(key, out Cell? cell))
            cells[key] = cell = new Cell { Filter = filter, Purpose = purpose, Seconds = seconds };
        return cell;
    }

    private sealed class Cell
    {
        public required string Filter { get; init; }
        public required FilterPurpose Purpose { get; init; }

        /// <summary>Whole-second sub length — part of the cell key; 0 = unknown.</summary>
        public required int Seconds { get; init; }

        public int Desired;
        public int Acquired;
        public int Accepted;
        public int Disk;
        public int PlanCount;
    }
}
