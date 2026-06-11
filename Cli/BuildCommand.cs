using System.Diagnostics;
using Astronomy.Catalog.Build;

namespace TargetCatalogManager.Cli;

/// <summary>
/// Default verb: rebuild <c>Catalog.db</c> from the image library (ACTUAL) and the TS database (PLAN) via
/// <see cref="CatalogBuilder"/>, then print the build report, read-back, and goal-vs-actual reconciliation.
/// </summary>
internal static class BuildCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        CliOptions o = CliOptions.Parse(args);

        if (!Directory.Exists(o.Library))
        {
            Console.Error.WriteLine($"image library root not found: {o.Library}");
            return 1;
        }
        if (!File.Exists(o.TsDb))
        {
            Console.Error.WriteLine($"Target Scheduler database not found: {o.TsDb}");
            return 1;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(o.Catalog)!);

        Console.WriteLine($"Building {o.Catalog}");
        Console.WriteLine($"  library  : {o.Library}");
        Console.WriteLine($"  TS db    : {o.TsDb}");
        Console.WriteLine($"  tolerance: {o.Resolve.MatchToleranceDegrees:0.00} deg");
        Console.WriteLine();

        Stopwatch sw = Stopwatch.StartNew();
        CatalogBuildReport report = await CatalogBuilder.BuildAsync(o.Catalog, o.Library, o.TsDb, o.Resolve);
        sw.Stop();

        ConsoleRenderer.PrintReport(report, sw.Elapsed);
        ConsoleRenderer.PrintReadBack(o.Catalog);
        ConsoleRenderer.PrintReconciliation(o.Catalog);
        return 0;
    }
}
