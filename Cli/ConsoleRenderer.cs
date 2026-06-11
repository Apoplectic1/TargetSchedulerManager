using Astronomy.Catalog;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Reconcile;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;

namespace TargetCatalogManager.Cli;

/// <summary>All console presentation for the CLI verbs — commands orchestrate, this class prints.</summary>
internal static class ConsoleRenderer
{
    public static void PrintReport(CatalogBuildReport r, TimeSpan elapsed)
    {
        Console.WriteLine($"built in {elapsed.TotalSeconds:0.0}s");
        Console.WriteLine($"  disk targets : {r.DiskTargetCount}");
        Console.WriteLine($"  TS targets   : {r.TsTargetCount}");
        Console.WriteLine($"    Both        : {r.BothCount}");
        Console.WriteLine($"    PlannedOnly : {r.PlannedOnlyCount}");
        Console.WriteLine($"    ActualOnly  : {r.ActualOnlyCount}");
        if (r.MosaicsResolved > 0 || r.PanelsActualOnly > 0)
            Console.WriteLine($"    Mosaics     : {r.MosaicsResolved}  (panels: {r.PanelsMatched} matched / " +
                $"{r.PanelsPlannedOnly} planned-only / {r.PanelsActualOnly} disk-only)");
        Console.WriteLine();
        Console.WriteLine("reconciliation (TS 'problems and errors' surfaced, not dropped):");
        Console.WriteLine($"  name mismatches : {r.NameMismatches.Count}");
        Console.WriteLine($"  ambiguous       : {r.AmbiguousMatches.Count}");
        Console.WriteLine($"  TS duplicates   : {r.DuplicateTsTargets.Count}");
        Console.WriteLine($"  TS aliases      : {r.AliasTsTargets.Count}");
        Console.WriteLine($"  unanchored      : {r.UnanchoredTsTargets.Count}");
        Console.WriteLine($"  invalid/coerced : {r.InvalidTsTargets.Count}");

        foreach (NameMismatch m in r.NameMismatches)
            Console.WriteLine($"    name != : TS '{m.TsName}' -> disk '{m.DiskDirectory}' (OBJECT {m.DiskObjectName}) sep {m.SeparationDegrees:0.000} deg");
        foreach (AmbiguousMatch a in r.AmbiguousMatches)
            Console.WriteLine($"    ambig   : TS '{a.TsName}' -> [{string.Join(" | ", a.CandidateDirectories)}] nearest {a.NearestSeparationDegrees:0.000} deg");
        foreach (DuplicateTsTarget d in r.DuplicateTsTargets)
            Console.WriteLine($"    dup     : disk '{d.DiskDirectory}' <- TS [{string.Join(" | ", d.TsTargetNames)}]");
        foreach (AliasTsTarget al in r.AliasTsTargets)
            Console.WriteLine($"    alias   : disk '{al.DiskDirectory}' <- TS [{string.Join(" | ", al.TsTargetNames)}] (same object; counts write to all)");
        foreach (UnanchoredTsTarget u in r.UnanchoredTsTargets)
            Console.WriteLine($"    no-coord: TS '{u.TsName}'");
        foreach (InvalidTsTarget i in r.InvalidTsTargets)
            Console.WriteLine($"    invalid : TS '{i.TsName}' -- {i.Reason}");
    }

    public static void PrintReadBack(string catalog)
    {
        using CatalogStore store = CatalogStore.OpenReadOnly(catalog);
        IReadOnlyList<Target> all = store.GetTargets();
        int panels = all.Count(t => t.ParentTargetId is not null);
        Console.WriteLine();
        Console.WriteLine($"read-back: {all.Count - panels} targets (+{panels} panels), {store.GetShotTargets().Count} shot, " +
            $"{store.GetInventoryFilters().Count} inventory rows, {store.GetExposurePlans().Count} plans");
    }

    public static void PrintReconciliation(string catalog)
    {
        using CatalogStore store = CatalogStore.OpenReadOnly(catalog);

        // Mosaic panels reconcile individually; the family fold keeps a mosaic one display line under its
        // parent's name (the same filter-level sums the panel-folded model used to produce).
        IReadOnlyList<TargetReconciliation> display =
            Reconciler.MergeFamilies(store.GetTargets(), store.GetReconciliation());

        List<TargetReconciliation> planned = [.. display.Where(r => r.TotalDesired > 0)];

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

    public static void PrintWriteBack(WriteBackPlan plan, WriteBackResult result, bool apply, bool listNoOps)
    {
        List<WriteBackChange> effective = [.. result.Changes.Where(c => !c.IsNoOp)];
        int decreases = effective.Count(c => c.IsDecrease);
        int raised = effective.Count(c => c.RaisesDesired);
        int noOps = result.Changes.Count - effective.Count;
        List<ReconcileNote> unplanned =
            [.. plan.NeedsReconciliation.Where(n => n.Kind == ReconcileNote.UnplannedFramesKind)];
        List<ReconcileNote> otherNotes =
            [.. plan.NeedsReconciliation.Where(n => n.Kind != ReconcileNote.UnplannedFramesKind)];

        // The surgical --target path lists no-ops too, so "did it touch X?" is answered by the output itself.
        foreach (WriteBackChange c in (listNoOps ? result.Changes : effective)
            .OrderByDescending(c => c.IsDecrease)
            .ThenBy(c => c.TargetName, StringComparer.OrdinalIgnoreCase))
        {
            string name = c.TargetName.Length <= 26 ? c.TargetName : c.TargetName[..26];
            string secs = $"@{c.PlanSeconds}s";
            string body = c.IsNoOp
                ? $"acq/acc {c.OldAcquired}/{c.OldAccepted} == disk {c.NewCount}, no-op"
                : $"acq/acc {c.OldAcquired}/{c.OldAccepted} -> {c.NewCount}"
                  + (c.RaisesDesired ? $"  desired {c.OldDesired}->{c.NewDesired}" : "")
                  + (c.IsDecrease ? "  <- DECREASE" : "");
            Console.WriteLine($"  {name,-26} {c.Filter,-2} {c.Purpose,-5} {secs,-6}  {body}");
        }

        Console.WriteLine();
        Console.WriteLine($"writes: {effective.Count}  (decreases {decreases}, goals-raised {raised}, no-ops {noOps})   " +
            $"manual: {plan.Manual.Count}   unplanned: {unplanned.Count}   needs-reconciliation: {otherNotes.Count}   " +
            $"ignored-missing: {plan.IgnoredMissing}");

        if (plan.Manual.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("manual reconciliation (NOT written - resolve by hand in TS):");
            foreach (ManualGroup g in plan.Manual)
            {
                Console.WriteLine($"  {g.TargetName}  {g.Filter} {g.Purpose} @{g.Seconds}s  disk={g.DiskCount}  ({g.Reason})");
                foreach (ManualPlan p in g.Plans)
                    Console.WriteLine($"      ts#{p.TsExposurePlanId} @{p.PlanSeconds}s  acq/acc {p.CatalogAcquired}/{p.CatalogAccepted}  desired {p.Desired}");
            }
        }

        if (unplanned.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("unplanned frames (no TS plan at this exposure - not written; plan creation is a later milestone):");
            foreach (ReconcileNote n in unplanned)
                Console.WriteLine($"  '{n.TargetName}'  {n.Detail}");
        }

        if (otherNotes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("needs reconciliation (TS target issues surfaced):");
            foreach (ReconcileNote n in otherNotes)
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
}
