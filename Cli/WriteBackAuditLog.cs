using System.Globalization;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.TargetScheduler;

namespace TargetCatalogManager.Cli;

/// <summary>
/// Writeback's audit trail into <see cref="CliLog"/> (<c>tcm-cli.log</c>): run banners, refusals, every
/// write/manual/unplanned decision with old→new counts, and the verified outcome — so "what did the CLI do
/// to the TS db, and when" is always answerable after the fact.
/// </summary>
internal static class WriteBackAuditLog
{
    public static void StartBulk(bool apply, bool live, string tsDb, string catalog, string library, ResolveOptions resolve) =>
        CliLog.Line($"writeback start mode=bulk apply={apply} live={live} ts=\"{tsDb}\" catalog=\"{catalog}\" " +
            $"library=\"{library}\" tolerance={resolve.MatchToleranceDegrees.ToString("0.00", CultureInfo.InvariantCulture)}");

    public static void StartTarget(string dirName, string dir, bool apply, bool live, string tsDb, ResolveOptions resolve) =>
        CliLog.Line($"writeback start mode=target target=\"{dirName}\" dir=\"{dir}\" apply={apply} live={live} " +
            $"ts=\"{tsDb}\" tolerance={resolve.MatchToleranceDegrees.ToString("0.00", CultureInfo.InvariantCulture)}");

    public static void Abort(string reason) => CliLog.Line($"abort reason=\"{reason}\"");

    public static void End(int rc) => CliLog.Line($"writeback end rc={rc}");

    /// <summary>Mirrors the printed outcome so every TS write decision is auditable after the fact.</summary>
    public static void Outcome(WriteBackPlan plan, WriteBackResult result)
    {
        foreach (WriteBackChange c in result.Changes)
        {
            string flags = c.IsNoOp
                ? "noop"
                : string.Join(",", new[] { c.IsDecrease ? "decrease" : null, c.RaisesDesired ? "ratchet" : null }
                    .Where(f => f is not null));
            if (flags.Length == 0) flags = "-";
            CliLog.Line($"write ts#{c.TsExposurePlanId} target=\"{c.TargetName}\" filter={c.Filter} " +
                $"purpose={c.Purpose} sec={c.PlanSeconds} acq {c.OldAcquired}->{c.NewCount} " +
                $"acc {c.OldAccepted}->{c.NewCount} desired {c.OldDesired}->{c.NewDesired} flags={flags}");
        }

        foreach (ManualGroup g in plan.Manual)
        {
            CliLog.Line($"manual target=\"{g.TargetName}\" filter={g.Filter} purpose={g.Purpose} sec={g.Seconds} " +
                $"disk={g.DiskCount} reason={g.Reason} plans={g.Plans.Count}");
            foreach (ManualPlan p in g.Plans)
                CliLog.Line($"manual-plan ts#{p.TsExposurePlanId} sec={p.PlanSeconds} " +
                    $"acq/acc {p.CatalogAcquired}/{p.CatalogAccepted} desired {p.Desired}");
        }

        foreach (ReconcileNote n in plan.NeedsReconciliation.Where(n => n.Kind == ReconcileNote.UnplannedFramesKind))
            CliLog.Line($"unplanned target=\"{n.TargetName}\" detail=\"{n.Detail}\"");

        int effective = result.Changes.Count(c => !c.IsNoOp);
        CliLog.Line($"result applied={result.Applied} writes={result.Changes.Count} changed={effective} " +
            $"decreases={result.Changes.Count(c => !c.IsNoOp && c.IsDecrease)} " +
            $"ratchets={result.Changes.Count(c => !c.IsNoOp && c.RaisesDesired)} " +
            $"noops={result.Changes.Count - effective} manual={plan.Manual.Count} " +
            $"unplanned={plan.NeedsReconciliation.Count(n => n.Kind == ReconcileNote.UnplannedFramesKind)} " +
            $"verify-failures={result.VerifyFailures.Count}");
        foreach (WriteBackVerifyFailure f in result.VerifyFailures)
            CliLog.Line($"verify-fail ts#{f.TsExposurePlanId} expected={f.Expected} got={f.ActualAcquired}/{f.ActualAccepted}");
    }
}
