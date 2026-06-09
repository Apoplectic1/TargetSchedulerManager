using System.Diagnostics;
using System.Globalization;
using Astronomy.Catalog;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Reconcile;
using Astronomy.Catalog.TargetScheduler;

namespace TargetCatalogManager;

/// <summary>
/// Headless TCM entry point (Phase 1–2): rebuilds <c>Catalog.db</c> from the image library (ACTUAL) and the
/// N.I.N.A. Target Scheduler database (PLAN) via <see cref="CatalogBuilder"/>, then prints the reconciliation
/// report. The Phase-3 WinUI maintenance app will sit on the same builder. Usage:
/// <code>tcm [--catalog PATH] [--library PATH] [--ts PATH] [--tolerance DEG]</code>
/// </summary>
internal static class Program
{
    // Dev defaults for this machine; override via args. The live TS DB lives on the imaging PC (BIRDWATCHER). The
    // default here is the restorable working copy under Processing\Catalog\TS Database\ — one location TCM + IS both
    // read, on the BIRDWATCHER-mapped imaging tree so refreshes are easy ("schedulerdb - Copy.sqlite" restores it).
    private const string DefaultCatalog = @"E:\Photography\Astro Photography\Processing\Catalog\Catalog.db";
    private const string DefaultLibrary = @"E:\Photography\Astro Photography\Processing";
    private const string DefaultTs = @"E:\Photography\Astro Photography\Processing\Catalog\TS Database\schedulerdb.sqlite";

    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("writeback", StringComparison.OrdinalIgnoreCase))
            return await WriteBack(args);
        return await BuildAndReport(args);
    }

    /// <summary>Default verb: rebuild Catalog.db and print the reconciliation + goal-vs-actual report.</summary>
    private static async Task<int> BuildAndReport(string[] args)
    {
        Dictionary<string, string> opts = ParseArgs(args);
        string catalog = opts.GetValueOrDefault("catalog", DefaultCatalog);
        string library = opts.GetValueOrDefault("library", DefaultLibrary);
        string tsDb = opts.GetValueOrDefault("ts", DefaultTs);
        ResolveOptions resolve = ParseTolerance(opts);

        if (!Directory.Exists(library))
        {
            Console.Error.WriteLine($"image library root not found: {library}");
            return 1;
        }
        if (!File.Exists(tsDb))
        {
            Console.Error.WriteLine($"Target Scheduler database not found: {tsDb}");
            return 1;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(catalog)!);

        Console.WriteLine($"Building {catalog}");
        Console.WriteLine($"  library  : {library}");
        Console.WriteLine($"  TS db    : {tsDb}");
        Console.WriteLine($"  tolerance: {resolve.MatchToleranceDegrees:0.00} deg");
        Console.WriteLine();

        Stopwatch sw = Stopwatch.StartNew();
        CatalogBuildReport report = await CatalogBuilder.BuildAsync(catalog, library, tsDb, resolve);
        sw.Stop();

        PrintReport(report, sw.Elapsed);
        PrintReadBack(catalog);
        PrintReconciliation(catalog);
        return 0;
    }

    /// <summary>
    /// <c>writeback</c> verb: fresh-rebuild the catalog, then push disk-derived counts into the <b>local</b> TS
    /// copy so its planner reflects ACTUAL. Dry-run by default; pass <c>--apply</c> to commit. Refuses a
    /// version-mismatched or apparently-open db. Never touches the live imaging-PC database.
    /// </summary>
    private static async Task<int> WriteBack(string[] args)
    {
        Dictionary<string, string> opts = ParseArgs(args);
        string catalog = opts.GetValueOrDefault("catalog", DefaultCatalog);
        string library = opts.GetValueOrDefault("library", DefaultLibrary);
        string tsDb = opts.GetValueOrDefault("ts", DefaultTs);
        bool apply = args.Any(a => a.Equals("--apply", StringComparison.OrdinalIgnoreCase));
        ResolveOptions resolve = ParseTolerance(opts);

        if (!Directory.Exists(library))
        {
            Console.Error.WriteLine($"image library root not found: {library}");
            return 1;
        }
        if (!File.Exists(tsDb))
        {
            Console.Error.WriteLine($"Target Scheduler database not found: {tsDb}");
            return 1;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(catalog)!);

        Console.WriteLine($"writeback {(apply ? "(APPLY)" : "(dry-run - pass --apply to commit)")}");
        Console.WriteLine($"  library  : {library}");
        Console.WriteLine($"  TS db    : {tsDb}  (local copy - never the live imaging-PC db)");
        Console.WriteLine($"  tolerance: {resolve.MatchToleranceDegrees:0.00} deg");
        Console.WriteLine();

        // Fresh re-scan so we never push stale numbers.
        CatalogBuildReport report = await CatalogBuilder.BuildAsync(catalog, library, tsDb, resolve);

        WriteBackPlan plan;
        using (CatalogStore store = CatalogStore.OpenReadOnly(catalog))
            plan = WriteBackPlanner.Plan(
                store.GetTargets(), store.GetExposurePlans(), store.GetExposureTemplates(),
                store.GetInventoryFilters(), report);

        using TargetSchedulerWriter writer = new(tsDb);
        if (!writer.HasRequiredColumns)
        {
            Console.Error.WriteLine(
                "refusing: TS exposureplan lacks the acquired/accepted/Id columns this writer updates (incompatible schema).");
            return 1;
        }
        if (writer.HasOpenSidecar)
        {
            Console.Error.WriteLine(
                "refusing: TS db has a -wal/-shm/-journal sidecar (may be open). Close NINA / copy a fresh snapshot.");
            return 1;
        }
        if (writer.IsReadOnly)
        {
            Console.Error.WriteLine(
                "refusing: TS db file is read-only. Clear the read-only attribute or re-copy a writable snapshot.");
            return 1;
        }

        Console.WriteLine($"TS schema user_version {writer.SchemaUserVersion} (validated by column presence)");
        Console.WriteLine();

        WriteBackResult result = writer.Execute(plan, apply);
        PrintWriteBack(plan, result, apply);
        return result.VerifyFailures.Count == 0 ? 0 : 1;
    }

    private static ResolveOptions ParseTolerance(Dictionary<string, string> opts) =>
        opts.TryGetValue("tolerance", out string? tol)
            && double.TryParse(tol, NumberStyles.Float, CultureInfo.InvariantCulture, out double deg)
            ? new ResolveOptions(deg)
            : ResolveOptions.Default;

    private static void PrintReconciliation(string catalog)
    {
        using CatalogStore store = CatalogStore.OpenReadOnly(catalog);
        List<TargetReconciliation> planned =
            [.. store.GetReconciliation().Where(r => r.TotalDesired > 0)];

        int complete = planned.Count(r => r.Status == ReconcileStatus.Complete);
        int inProgress = planned.Count(r => r.Status == ReconcileStatus.InProgress);
        int notStarted = planned.Count(r => r.Status == ReconcileStatus.NotStarted);
        int desired = planned.Sum(r => r.TotalDesired);
        int remaining = planned.Sum(r => r.TotalRemaining);

        Console.WriteLine();
        Console.WriteLine("goal vs actual (Combined: Light + Stars vs desired):");
        Console.WriteLine($"  planned targets : {planned.Count}  (complete {complete} / in-progress {inProgress} / not-started {notStarted})");
        Console.WriteLine($"  frames          : {desired - remaining}/{desired} done, {remaining} remaining");
        Console.WriteLine("  in-progress targets (most frames remaining):");
        foreach (TargetReconciliation r in planned
            .Where(r => r.Status == ReconcileStatus.InProgress)
            .OrderByDescending(r => r.TotalRemaining)
            .Take(10))
        {
            string perFilter = string.Join("  ", r.Filters
                .Where(f => f.DesiredCount > 0)
                .Select(f => $"{f.Filter} {f.AcquiredCount}/{f.DesiredCount}{(f.Status == ReconcileStatus.Complete ? "*" : "")}"));
            string name = r.Name.Length <= 26 ? r.Name : r.Name[..26];
            Console.WriteLine($"    {name,-26} {r.FractionComplete * 100,3:0}%  rem {r.TotalRemaining,4}  [{perFilter}]");
        }
    }

    private static void PrintWriteBack(WriteBackPlan plan, WriteBackResult result, bool apply)
    {
        List<WriteBackChange> effective = [.. result.Changes.Where(c => !c.IsNoOp)];
        int decreases = effective.Count(c => c.IsDecrease);
        int raised = effective.Count(c => c.RaisesDesired);
        int noOps = result.Changes.Count - effective.Count;

        foreach (WriteBackChange c in effective
            .OrderByDescending(c => c.IsDecrease)
            .ThenBy(c => c.TargetName, StringComparer.OrdinalIgnoreCase))
        {
            string name = c.TargetName.Length <= 26 ? c.TargetName : c.TargetName[..26];
            string flag = c.IsDecrease ? "  <- DECREASE" : "";
            string desiredNote = c.RaisesDesired ? $"  desired {c.OldDesired}->{c.NewDesired}" : "";
            Console.WriteLine($"  {name,-26} {c.Filter,-2} {c.Purpose,-5}  acq/acc {c.OldAcquired}/{c.OldAccepted} -> {c.NewCount}{desiredNote}{flag}");
        }

        Console.WriteLine();
        Console.WriteLine($"writes: {effective.Count}  (decreases {decreases}, goals-raised {raised}, no-ops {noOps})   " +
            $"manual: {plan.Manual.Count}   needs-reconciliation: {plan.NeedsReconciliation.Count}   " +
            $"ignored-missing: {plan.IgnoredMissing}");

        if (plan.Manual.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("manual reconciliation (NOT written - resolve by hand in TS):");
            foreach (ManualGroup g in plan.Manual)
            {
                Console.WriteLine($"  {g.TargetName}  {g.Filter} {g.Purpose}  disk={g.DiskCount}  ({g.Reason})");
                foreach (ManualPlan p in g.Plans)
                    Console.WriteLine($"      ts#{p.TsExposurePlanId}  acq/acc {p.CatalogAcquired}/{p.CatalogAccepted}  desired {p.Desired}");
            }
        }

        if (plan.NeedsReconciliation.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("needs reconciliation (TS target issues surfaced):");
            foreach (ReconcileNote n in plan.NeedsReconciliation)
                Console.WriteLine($"  {n.Kind,-12} '{n.TargetName}'  {n.Detail}");
        }

        Console.WriteLine();
        if (!apply)
            Console.WriteLine($"dry-run - nothing written. Re-run with --apply to commit {effective.Count} change(s).");
        else if (result.VerifyFailures.Count == 0)
            Console.WriteLine($"applied {plan.Writes.Count} plan(s) ({effective.Count} changed, {decreases} decreased, {raised} goals raised); read-back verify OK.");
        else
        {
            Console.WriteLine($"applied with {result.VerifyFailures.Count} VERIFY FAILURE(S):");
            foreach (WriteBackVerifyFailure f in result.VerifyFailures)
                Console.WriteLine($"  ts#{f.TsExposurePlanId}: expected {f.Expected}, got acq/acc {f.ActualAcquired}/{f.ActualAccepted}");
        }
    }

    private static void PrintReport(CatalogBuildReport r, TimeSpan elapsed)
    {
        Console.WriteLine($"built in {elapsed.TotalSeconds:0.0}s");
        Console.WriteLine($"  disk targets : {r.DiskTargetCount}");
        Console.WriteLine($"  TS targets   : {r.TsTargetCount}");
        Console.WriteLine($"    Both        : {r.BothCount}");
        Console.WriteLine($"    PlannedOnly : {r.PlannedOnlyCount}");
        Console.WriteLine($"    ActualOnly  : {r.ActualOnlyCount}");
        if (r.MosaicsResolved > 0)
            Console.WriteLine($"    Mosaics     : {r.MosaicsResolved}  ({r.PanelsFolded} panels folded)");
        Console.WriteLine();
        Console.WriteLine("reconciliation (TS 'problems and errors' surfaced, not dropped):");
        Console.WriteLine($"  name mismatches : {r.NameMismatches.Count}");
        Console.WriteLine($"  ambiguous       : {r.AmbiguousMatches.Count}");
        Console.WriteLine($"  TS duplicates   : {r.DuplicateTsTargets.Count}");
        Console.WriteLine($"  unanchored      : {r.UnanchoredTsTargets.Count}");
        Console.WriteLine($"  invalid/coerced : {r.InvalidTsTargets.Count}");

        foreach (NameMismatch m in r.NameMismatches)
            Console.WriteLine($"    name != : TS '{m.TsName}' -> disk '{m.DiskDirectory}' (OBJECT {m.DiskObjectName}) sep {m.SeparationDegrees:0.000} deg");
        foreach (AmbiguousMatch a in r.AmbiguousMatches)
            Console.WriteLine($"    ambig   : TS '{a.TsName}' -> [{string.Join(" | ", a.CandidateDirectories)}] nearest {a.NearestSeparationDegrees:0.000} deg");
        foreach (DuplicateTsTarget d in r.DuplicateTsTargets)
            Console.WriteLine($"    dup     : disk '{d.DiskDirectory}' <- TS [{string.Join(" | ", d.TsTargetNames)}]");
        foreach (UnanchoredTsTarget u in r.UnanchoredTsTargets)
            Console.WriteLine($"    no-coord: TS '{u.TsName}'");
        foreach (InvalidTsTarget i in r.InvalidTsTargets)
            Console.WriteLine($"    invalid : TS '{i.TsName}' -- {i.Reason}");
    }

    private static void PrintReadBack(string catalog)
    {
        using CatalogStore store = CatalogStore.OpenReadOnly(catalog);
        Console.WriteLine();
        Console.WriteLine($"read-back: {store.GetTargets().Count} targets, {store.GetShotTargets().Count} shot, " +
            $"{store.GetInventoryFilters().Count} inventory rows, {store.GetExposurePlans().Count} plans");
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        Dictionary<string, string> opts = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                opts[args[i][2..]] = args[++i];
        }
        return opts;
    }
}
