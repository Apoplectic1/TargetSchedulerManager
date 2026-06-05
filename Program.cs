using System.Diagnostics;
using System.Globalization;
using Astronomy.Catalog;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Reconcile;
using Astronomy.Catalog.Schema;

namespace TargetCatalogManager;

/// <summary>
/// Headless TCM entry point (Phase 1–2): rebuilds <c>Catalog.db</c> from the image library (ACTUAL) and the
/// N.I.N.A. Target Scheduler database (PLAN) via <see cref="CatalogBuilder"/>, then prints the reconciliation
/// report. The Phase-3 WinUI maintenance app will sit on the same builder. Usage:
/// <code>tcm [--catalog PATH] [--library PATH] [--ts PATH] [--tolerance DEG]</code>
/// </summary>
internal static class Program
{
    // Dev defaults for this machine; override via args. The live TS DB usually lives on the imaging PC, so the
    // default here is the pinned snapshot used for development.
    private const string DefaultCatalog = @"E:\Photography\Astro Photography\Processing\Catalog\Catalog.db";
    private const string DefaultLibrary = @"E:\Photography\Astro Photography\Processing";
    private const string DefaultTs = @"E:\Projects\VisualStudio\Astronomy\IntervalScheduler\TS DataBase Example\schedulerdb.sqlite";

    private static async Task<int> Main(string[] args)
    {
        Dictionary<string, string> opts = ParseArgs(args);
        string catalog = opts.GetValueOrDefault("catalog", DefaultCatalog);
        string library = opts.GetValueOrDefault("library", DefaultLibrary);
        string tsDb = opts.GetValueOrDefault("ts", DefaultTs);
        ResolveOptions resolve = opts.TryGetValue("tolerance", out string? tol)
            && double.TryParse(tol, NumberStyles.Float, CultureInfo.InvariantCulture, out double deg)
            ? new ResolveOptions(deg)
            : ResolveOptions.Default;

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

    private static void PrintReport(CatalogBuildReport r, TimeSpan elapsed)
    {
        Console.WriteLine($"built in {elapsed.TotalSeconds:0.0}s");
        Console.WriteLine($"  disk targets : {r.DiskTargetCount}");
        Console.WriteLine($"  TS targets   : {r.TsTargetCount}");
        Console.WriteLine($"    Both        : {r.BothCount}");
        Console.WriteLine($"    PlannedOnly : {r.PlannedOnlyCount}");
        Console.WriteLine($"    ActualOnly  : {r.ActualOnlyCount}");
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
