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
    bool IsFlagged,
    string Camera,
    string Gain,
    string Offset,
    string Bin,
    string Rot)
{
    /// <summary>What a rollup cell shows for one capture-configuration column: the shared value when every
    /// child agrees, or the mixed marker when they do not. Never blank on disagreement — silence would read
    /// as "nothing to say" exactly when the fact to convey is "these differ". A child showing the em dash
    /// expresses nothing for that dimension (a TS row's camera, an unexpressed rotation) and therefore never
    /// counts as disagreement (user obs 2026-07-29): only two *expressed* values can differ. All-dash
    /// children roll up to the dash.</summary>
    private static string Uniform(IReadOnlyList<ReconciliationRow> children, Func<ReconciliationRow, string> cell)
    {
        if (children.Count == 0) return string.Empty;
        string expressed = string.Empty;
        foreach (ReconciliationRow r in children)
        {
            string v = cell(r);
            if (v == Format.Dash) continue;
            if (expressed.Length == 0) expressed = v;
            else if (!string.Equals(v, expressed, StringComparison.Ordinal)) return Format.Mixed;
        }
        return expressed.Length == 0 ? Format.Dash : expressed;
    }

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
            flagged,
            // The capture configuration a header can report before it is expanded: which dimensions are
            // consistent beneath it, and which are the reason its numbers do not add up.
            Uniform(children, r => r.CameraText),
            Uniform(children, r => r.GainText),
            Uniform(children, r => r.OffsetText),
            Uniform(children, r => r.BinText),
            Uniform(children, r => r.RotText));
    }
}
