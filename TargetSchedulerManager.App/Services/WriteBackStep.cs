using System.Globalization;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.TargetScheduler;
using Astronomy.Diagnostics;
using TargetSchedulerManager.App.Shared;

namespace TargetSchedulerManager.App.Services;

/// <summary>What one post-load write-back pass did: how many plans were stamped locally (and how many journal
/// fields that produced), what the planner routed to manual, and a structural refusal when the local db
/// couldn't be written. A clean system is all zeros — no writes, no journal entries, nothing to push.</summary>
internal sealed record WriteBackStepResult(
    int PlansStamped,
    int FieldsJournaled,
    int ManualCount,
    int VerifyFailures,
    string? Refusal)
{
    public static readonly WriteBackStepResult Clean = new(0, 0, 0, 0, null);

    /// <summary>A short status-line fragment; empty when the pass changed nothing and had nothing to say.</summary>
    public string Describe() =>
        Refusal is not null ? $"  ·  WRITE-BACK REFUSED: {Refusal}"
        : VerifyFailures > 0 ? $"  ·  write-back stamped {PlansStamped}, {VerifyFailures} FAILED verify (see tsm.log)"
        : PlansStamped > 0 ? $"  ·  write-back stamped {PlansStamped} plan(s)"
        : "";
}

/// <summary>
/// The automatic disk→TS reconciliation inside the sync model: after every load, plan write-back from the
/// fresh scan + local TS read (<see cref="WriteBackPlanner"/> — every existing plan stamps to its disk bucket,
/// 0 when the target has no disk match at all: disk truth covers absence, so stray counters on not-yet-shot
/// targets heal; identity-flagged cells go to manual) and stamp every non-no-op change into the
/// <b>local</b> db through the library writer, journaling each changed column so it rides the reviewed push
/// like any edit. No-op changes produce no write and no journal entry, so an unchanged system leaves the
/// session clean and the next open skippable. BIRDWATCHER is never touched here.
/// </summary>
internal static class WriteBackStep
{
    public static WriteBackStepResult Run(CatalogGraph graph, CatalogBuildReport report, TsSync sync)
    {
        WriteBackPlan plan = WriteBackPlanner.Plan(
            graph.Targets, graph.Plans, graph.Templates, graph.InventoryFilters, report);
        if (plan.Manual.Count > 0)
            Log.Warn($"write-back: {plan.Manual.Count} cell(s) need manual reconciliation (multi-plan/duplicate/identity) — not auto-written");
        if (plan.Writes.Count == 0)
            return WriteBackStepResult.Clean with { ManualCount = plan.Manual.Count };

        using ITsWriteBackApplier applier = sync.CreateLocalWriteBackApplier();
        if (!applier.HasRequiredColumns)
            return Refuse("local TS copy schema incompatible (exposureplan columns missing)", plan);
        if (applier.IsReadOnly)
            return Refuse("local TS copy is read-only", plan);
        if (applier.HasOpenSidecar)
            return Refuse("local TS copy has an open sidecar (another tool holds it?)", plan);

        // Diff first so no-ops produce no write at all, then apply only the changed plans in one transaction.
        WriteBackResult diff = applier.Execute(plan, apply: false);
        HashSet<long> changedIds = [.. diff.Changes.Where(c => !c.IsNoOp).Select(c => c.TsExposurePlanId)];
        if (changedIds.Count == 0)
        {
            Log.Diag("Load", $"write-back: all {plan.Writes.Count} auto cells already stamped — nothing to do");
            return WriteBackStepResult.Clean with { ManualCount = plan.Manual.Count };
        }

        WriteBackPlan filtered = new(
            [.. plan.Writes.Where(w => changedIds.Contains(w.TsExposurePlanId))],
            plan.Manual, plan.NeedsReconciliation, plan.IgnoredMissing);
        WriteBackResult applied = applier.Execute(filtered, apply: true);
        HashSet<long> failedIds = [.. applied.VerifyFailures.Select(f => f.TsExposurePlanId)];
        foreach (WriteBackVerifyFailure f in applied.VerifyFailures)
            Log.Error($"write-back verify FAILED locally for TS plan {f.TsExposurePlanId}: expected {f.Expected}, read {f.ActualAcquired}/{f.ActualAccepted}");

        int fields = 0, stamped = 0;
        foreach (WriteBackChange c in applied.Changes.Where(c => !c.IsNoOp && !failedIds.Contains(c.TsExposurePlanId)))
        {
            stamped++;
            string key = c.TsExposurePlanId.ToString(CultureInfo.InvariantCulture);
            string label = c.Purpose == FilterPurpose.Light
                ? $"{c.TargetName} · {c.Filter} @{c.PlanSeconds}s"
                : $"{c.TargetName} · {c.Filter} {c.Purpose} @{c.PlanSeconds}s";
            if (c.NewCount != c.OldAcquired)
            {
                sync.RecordWriteBack(key, "acquired", c.NewCount, Text(c.OldAcquired), label);
                fields++;
            }
            if (c.NewCount != c.OldAccepted)
            {
                sync.RecordWriteBack(key, "accepted", c.NewCount, Text(c.OldAccepted), label);
                fields++;
            }
            if (c.RaisesDesired)
            {
                sync.RecordWriteBack(key, "desired", c.NewDesired, Text(c.OldDesired), label);
                fields++;
            }
        }

        Log.Info($"WRITE-BACK stamped {stamped} plan(s) on local ({fields} fields journaled; " +
            $"{plan.Manual.Count} manual, {plan.IgnoredMissing} disk-only ignored)");
        return new WriteBackStepResult(stamped, fields, plan.Manual.Count, applied.VerifyFailures.Count, null);
    }

    private static WriteBackStepResult Refuse(string reason, WriteBackPlan plan)
    {
        Log.Error($"write-back refused — {reason}");
        return new WriteBackStepResult(0, 0, plan.Manual.Count, 0, reason);
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
