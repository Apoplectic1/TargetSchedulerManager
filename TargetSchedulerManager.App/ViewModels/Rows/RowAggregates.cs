using TargetSchedulerManager.App.Models;

namespace TargetSchedulerManager.App.ViewModels.Rows;

/// <summary>
/// The column sums a collapsible header row shows for its leaves, computed one way for every level of the
/// tree (target groups and panel groups alike) so the additive rules can't drift between them.
/// </summary>
internal sealed record RowAggregates(
    int? Desired,
    int? Acquired,
    int? Accepted,
    int Disk,
    int Remaining,
    double? HoursDelta,
    string Badge,
    bool IsFlagged)
{
    public static RowAggregates Compute(IReadOnlyList<ReconciliationRow> children)
    {
        bool anyPlanned = false, anyHours = false, flagged = false;
        int desired = 0, acquired = 0, accepted = 0, disk = 0, remaining = 0;
        double diskHours = 0, desiredHours = 0;
        foreach (ReconciliationRow r in children)
        {
            if (r.Desired is int d) { anyPlanned = true; desired += d; }
            acquired += r.Acquired ?? 0;
            accepted += r.Accepted ?? 0;
            disk += r.Disk;
            if (r.IsFlagged) flagged = true;
            // A Both row carries both components; one-plane rows carry one. Summing the components (not
            // the displayed Hours, which is a gap on Both rows) keeps the header delta exact.
            if (r.DiskHours is double dh) { anyHours = true; diskHours += dh; }
            if (r.PlanHours is double ph) { anyHours = true; desiredHours += ph; }
            // Both rows pair desired against their own disk; leftover TS rows are wholly unshot; leftover
            // Disk rows have no goal — per-row shortfalls are already per-cell.
            remaining += Math.Max(0, (r.Desired ?? 0) - r.Disk);
        }

        return new RowAggregates(
            anyPlanned ? desired : null,
            anyPlanned ? acquired : null,
            anyPlanned ? accepted : null,
            disk,
            remaining,
            anyHours ? diskHours - desiredHours : null,
            // Distinct over TOKENS, not whole child badge strings: children of one mosaic read "mosaic" and
            // "mosaic · multi-plan" (target-scope vs. filter-scope flags), which deduped as strings would
            // render "mosaic · mosaic · multi-plan". First-appearance order preserved.
            Badges.Join(children.SelectMany(r => Badges.Split(r.Badge)).Select(t => t.Token).Distinct()),
            flagged);
    }
}
