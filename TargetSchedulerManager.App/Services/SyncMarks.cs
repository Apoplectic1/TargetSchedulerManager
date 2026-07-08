using System.Globalization;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Shared;

namespace TargetSchedulerManager.App.Services;

/// <summary>
/// One refresh's worth of direction-mark resolution: the journal (outbound, →) and the inbound store (←)
/// snapshotted into keyed lookups, plus the graph's target→plan-key map so a plan folded into a multi-plan
/// rollup row (which carries no row-level plan key) still rolls its mark up to the header. Built fresh per
/// marks sweep — both sources are cheap to snapshot and the sweep is the only consumer, so the marks can
/// never disagree with the journal/inbound facts they mirror. UI-free: returns glyph + tooltip strings.
/// </summary>
internal sealed class SyncMarks
{
    public const string In = "←";        // ← inbound: BIRDWATCHER arrived different
    public const string Out = "→";       // → outbound: unpushed local writes
    public const string BothWays = "⇄";  // ⇄ both on one row

    private sealed record Line(string Column, string? Old, string? New);

    private readonly Dictionary<TsTable, Dictionary<string, List<Line>>> _inbound;
    private readonly Dictionary<TsTable, Dictionary<string, List<Line>>> _outbound;
    private readonly Dictionary<Guid, List<string>> _planKeysByTarget;

    private SyncMarks(
        Dictionary<TsTable, Dictionary<string, List<Line>>> inbound,
        Dictionary<TsTable, Dictionary<string, List<Line>>> outbound,
        Dictionary<Guid, List<string>> planKeysByTarget)
    {
        _inbound = inbound;
        _outbound = outbound;
        _planKeysByTarget = planKeysByTarget;
    }

    /// <summary>Resolves nothing — the pre-first-load state (every mark blank).</summary>
    public static SyncMarks Empty { get; } = new([], [], []);

    /// <summary>Snapshots the two fact sources and the graph's plan-key map into one resolver.
    /// <paramref name="plans"/> may be empty (no load yet) — headers then cover only the plan keys their
    /// visible child rows carry.</summary>
    public static SyncMarks Build(TsJournal journal, TsInboundStore inbound, IReadOnlyList<ExposurePlan> plans)
    {
        Dictionary<TsTable, Dictionary<string, List<Line>>> inb = [];
        foreach (TsInboundChange c in inbound.Snapshot())
            Add(inb, c.Table, c.Key, new Line(c.Column, c.Old, c.New));

        // Collapse() already carries the tooltip shape per field: the FIRST write's Old → the LAST value.
        Dictionary<TsTable, Dictionary<string, List<Line>>> outb = [];
        foreach (TsJournalEntry e in journal.Collapse())
            Add(outb, e.Table, e.Key, new Line(e.Column, e.Old, FormatValue(e.Value)));

        Dictionary<Guid, List<string>> planKeys = [];
        foreach (ExposurePlan plan in plans)
        {
            if (plan.ImportedFromTsGuid is not string key)
                continue;
            if (!planKeys.TryGetValue(plan.TargetId, out List<string>? list))
                planKeys[plan.TargetId] = list = [];
            list.Add(key);
        }
        return new SyncMarks(inb, outb, planKeys);
    }

    /// <summary>
    /// A filter/detail row's mark: keyed on its 1:1 plan key only — disk-plane rows (no plan key) are
    /// structurally blank, and target/project-level changes mark the header, not leaves. Tooltip lists one
    /// old→new line per pending field per direction.
    /// </summary>
    public (string Glyph, string? Tooltip) ForPlan(string? planTsKey)
    {
        if (planTsKey is null)
            return ("", null);
        List<Line>? inb = Get(_inbound, TsTable.ExposurePlan, planTsKey);
        List<Line>? outb = Get(_outbound, TsTable.ExposurePlan, planTsKey);
        if (inb is null && outb is null)
            return ("", null);

        List<string> lines = [];
        if (inb is not null)
            lines.AddRange(inb.Select(l => l.Column == TsInboundDiff.NewRowColumn
                ? $"{In} BIRDWATCHER: new row"
                : $"{In} BIRDWATCHER: {l.Column} {l.Old ?? "—"} → {l.New ?? "—"}"));
        if (outb is not null)
            lines.AddRange(outb.Select(l => $"{Out} unpushed: {l.Column} {l.Old ?? "—"} → {l.New ?? "—"}"));
        return (Glyph(inb is not null, outb is not null), string.Join("\n", lines));
    }

    /// <summary>
    /// A header's mark: the union of directions over its target key(s), its project key (the group header /
    /// mosaic parent only — panels pass null so a project edit never lights them), and every plan key of its
    /// target id(s) (graph map) plus any row-carried plan keys the caller collected (covers 1:1 cells when no
    /// graph is loaded). Tooltip summarizes field counts per direction.
    /// </summary>
    public (string Glyph, string? Tooltip) ForKeys(
        IEnumerable<string> targetKeys, string? projectKey, IEnumerable<Guid> targetIds,
        IEnumerable<string>? planKeys = null)
    {
        int inFields = 0, outFields = 0;
        void Count(TsTable table, string key)
        {
            inFields += Get(_inbound, table, key)?.Count ?? 0;
            outFields += Get(_outbound, table, key)?.Count ?? 0;
        }

        foreach (string key in targetKeys)
            Count(TsTable.Target, key);
        if (projectKey is not null)
            Count(TsTable.Project, projectKey);
        HashSet<string> seenPlans = new(StringComparer.OrdinalIgnoreCase);
        foreach (Guid id in targetIds)
        {
            if (!_planKeysByTarget.TryGetValue(id, out List<string>? keys))
                continue;
            foreach (string key in keys)
                if (seenPlans.Add(key))
                    Count(TsTable.ExposurePlan, key);
        }
        foreach (string key in planKeys ?? [])
            if (seenPlans.Add(key))
                Count(TsTable.ExposurePlan, key);

        if (inFields == 0 && outFields == 0)
            return ("", null);
        List<string> parts = [];
        if (inFields > 0)
            parts.Add($"{In} {inFields} field(s) arrived changed from BIRDWATCHER");
        if (outFields > 0)
            parts.Add($"{Out} {outFields} field(s) unpushed");
        return (Glyph(inFields > 0, outFields > 0), string.Join("\n", parts));
    }

    private static string Glyph(bool inbound, bool outbound) =>
        inbound && outbound ? BothWays : inbound ? In : Out;

    private static List<Line>? Get(
        Dictionary<TsTable, Dictionary<string, List<Line>>> source, TsTable table, string key) =>
        source.TryGetValue(table, out Dictionary<string, List<Line>>? keys)
        && keys.TryGetValue(key, out List<Line>? lines) ? lines : null;

    private static void Add(
        Dictionary<TsTable, Dictionary<string, List<Line>>> target, TsTable table, string key, Line line)
    {
        if (!target.TryGetValue(table, out Dictionary<string, List<Line>>? keys))
            target[table] = keys = new(StringComparer.OrdinalIgnoreCase);
        if (!keys.TryGetValue(key, out List<Line>? lines))
            keys[key] = lines = [];
        lines.Add(line);
    }

    private static string? FormatValue(object? value) =>
        value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
}
