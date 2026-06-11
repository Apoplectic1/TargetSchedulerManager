using Astronomy.Catalog;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.TargetScheduler;

namespace TargetCatalogManager.Cli;

/// <summary>
/// <c>writeback</c> verb: push disk-derived counts into the <b>local</b> TS copy so its planner reflects
/// ACTUAL. The bulk form fresh-rebuilds the catalog and writes every resolvable target; <c>--target
/// "&lt;dir&gt;"</c> is the surgical form — scans just one directory (incl. per-panel for a mosaic) and
/// writes only its cells, no catalog rebuild. The two forms differ only in how the
/// <see cref="WriteBackPlan"/> is produced; both finish through the same <see cref="ExecutePlan"/> tail
/// (guards → execute → render → audit). Dry-run by default; <c>--apply</c> commits. Refuses an
/// incompatible or apparently-open db. Never touches the live imaging-PC database.
/// </summary>
internal static class WriteBackCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        CliOptions o = CliOptions.Parse(args);

        if (!File.Exists(o.TsDb))
        {
            Console.Error.WriteLine($"Target Scheduler database not found: {o.TsDb}");
            return 1;
        }

        return o.Target is not null ? await RunSingleTargetAsync(o) : await RunBulkAsync(o);
    }

    private static async Task<int> RunBulkAsync(CliOptions o)
    {
        if (!Directory.Exists(o.Library))
        {
            Console.Error.WriteLine($"image library root not found: {o.Library}");
            return 1;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(o.Catalog)!);

        Console.WriteLine($"writeback {(o.Apply ? "(APPLY)" : "(dry-run - pass --apply to commit)")}");
        Console.WriteLine($"  library  : {o.Library}");
        Console.WriteLine($"  TS db    : {o.TsDb}  (local copy - never the live imaging-PC db)");
        Console.WriteLine($"  tolerance: {o.Resolve.MatchToleranceDegrees:0.00} deg");
        Console.WriteLine();
        WriteBackAuditLog.StartBulk(o.Apply, o.TsDb, o.Catalog, o.Library, o.Resolve);

        // Fresh re-scan so we never push stale numbers.
        CatalogBuildReport report = await CatalogBuilder.BuildAsync(o.Catalog, o.Library, o.TsDb, o.Resolve);

        WriteBackPlan plan;
        using (CatalogStore store = CatalogStore.OpenReadOnly(o.Catalog))
            plan = WriteBackPlanner.Plan(
                store.GetTargets(), store.GetExposurePlans(), store.GetExposureTemplates(),
                store.GetInventoryFilters(), report);

        return ExecutePlan(plan, o.TsDb, o.Apply, listNoOps: false, unitsLine: null);
    }

    /// <summary>
    /// Surgical form: scan one disk target only and push its per-(filter,purpose,binning,seconds) counts to
    /// the matching TS plans. For a mosaic, writes each panel's counts to that panel's own plan.
    /// </summary>
    private static async Task<int> RunSingleTargetAsync(CliOptions o)
    {
        // Resolve the directory: an absolute path or one containing separators is used as-is; a bare name is
        // taken relative to the library root.
        string target = o.Target!;
        bool hasSeparators =
            target.Contains(Path.DirectorySeparatorChar) || target.Contains(Path.AltDirectorySeparatorChar);
        string dir = Path.IsPathRooted(target) || hasSeparators ? target : Path.Combine(o.Library, target);
        dir = Path.TrimEndingDirectorySeparator(dir);

        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"target directory not found: {dir}");
            return 1;
        }

        string dirName = Path.GetFileName(dir);
        bool isMosaic = MosaicConvention.IsMosaicDirectory(dirName);

        Console.WriteLine($"writeback --target {(o.Apply ? "(APPLY)" : "(dry-run - pass --apply to commit)")}");
        Console.WriteLine($"  target   : {dirName}{(isMosaic ? "  (mosaic - per panel)" : "")}");
        Console.WriteLine($"  dir      : {dir}");
        Console.WriteLine($"  TS db    : {o.TsDb}  (local copy - never the live imaging-PC db)");
        Console.WriteLine($"  tolerance: {o.Resolve.MatchToleranceDegrees:0.00} deg");
        Console.WriteLine();
        WriteBackAuditLog.StartTarget(dirName, dir, o.Apply, o.TsDb, o.Resolve);

        // Read the TS plan plane directly (no catalog rebuild), closing the reader before the writer opens.
        TsPlanData ts;
        using (TargetSchedulerReader reader = new(o.TsDb))
            ts = reader.ReadPlanData();

        IReadOnlyList<TargetReport> units = await ImageLibraryScanner.ScanUnitsAsync(dir);
        if (units.Count == 0)
        {
            Console.Error.WriteLine($"no imaging frames found under {Path.Combine(dir, "Captures")} - nothing to write.");
            return 1;
        }

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic, dirName, ts, o.Resolve);

        // Surgical runs answer "did it touch X?" inline, so they list no-ops too.
        return ExecutePlan(plan, o.TsDb, o.Apply, listNoOps: true,
            unitsLine: $"units scanned: {units.Count}{(isMosaic ? " panel(s)" : "")}");
    }

    // The shared tail both forms finish through: open writer → guards → execute → render → audit → rc.
    private static int ExecutePlan(WriteBackPlan plan, string tsDb, bool apply, bool listNoOps, string? unitsLine)
    {
        using TargetSchedulerWriter writer = new(tsDb);
        if (WriterGuardsFail(writer))
        {
            WriteBackAuditLog.End(rc: 1);
            return 1;
        }

        Console.WriteLine($"TS schema user_version {writer.SchemaUserVersion} (validated by column presence)");
        if (unitsLine is not null) Console.WriteLine(unitsLine);
        Console.WriteLine();

        WriteBackResult result = writer.Execute(plan, apply);
        ConsoleRenderer.PrintWriteBack(plan, result, apply, listNoOps);
        WriteBackAuditLog.Outcome(plan, result);
        int rc = result.VerifyFailures.Count == 0 ? 0 : 1;
        WriteBackAuditLog.End(rc);
        return rc;
    }

    // Shared refusal guards for the writer; prints (and logs) a clear reason and returns true when the db
    // must not be written.
    private static bool WriterGuardsFail(TargetSchedulerWriter writer)
    {
        string? reason =
            !writer.HasRequiredColumns
                ? "TS exposureplan lacks the acquired/accepted/Id columns this writer updates (incompatible schema)."
            : writer.HasOpenSidecar
                ? "TS db has a -wal/-shm/-journal sidecar (may be open). Close NINA / copy a fresh snapshot."
            : writer.IsReadOnly
                ? "TS db file is read-only. Clear the read-only attribute or re-copy a writable snapshot."
            : null;

        if (reason is null) return false;
        Console.Error.WriteLine($"refusing: {reason}");
        WriteBackAuditLog.Abort(reason);
        return true;
    }
}
