using Astronomy.Catalog.Build;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.Shared;

namespace TargetSchedulerManager.App.Services;

/// <summary>
/// One refresh's worth of direction-mark resolution: the journal (outbound, →) and the inbound store (←)
/// snapshotted into keyed lookups, plus maps derived from the retained graph — target→plan-keys (a plan
/// folded into a multi-plan rollup row carries no row-level key but still rolls its mark up to the header),
/// plan→template key and target→template keys (a template change marks every plan using it), and key→display
/// names for tooltip attribution. Built fresh per marks sweep — all sources are cheap to snapshot and the
/// sweep is the only consumer, so the marks can never disagree with the journal/inbound facts they mirror.
/// UI-free: returns glyph + tooltip strings.
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
    private readonly Dictionary<Guid, List<string>> _templateKeysByTarget;
    private readonly Dictionary<string, string> _templateKeyByPlan;
    private readonly Dictionary<string, string> _templateNames;
    private readonly Dictionary<string, string> _targetNames;
    private readonly Dictionary<string, string> _projectNames;

    private SyncMarks(
        Dictionary<TsTable, Dictionary<string, List<Line>>> inbound,
        Dictionary<TsTable, Dictionary<string, List<Line>>> outbound,
        Dictionary<Guid, List<string>> planKeysByTarget,
        Dictionary<Guid, List<string>> templateKeysByTarget,
        Dictionary<string, string> templateKeyByPlan,
        Dictionary<string, string> templateNames,
        Dictionary<string, string> targetNames,
        Dictionary<string, string> projectNames)
    {
        _inbound = inbound;
        _outbound = outbound;
        _planKeysByTarget = planKeysByTarget;
        _templateKeysByTarget = templateKeysByTarget;
        _templateKeyByPlan = templateKeyByPlan;
        _templateNames = templateNames;
        _targetNames = targetNames;
        _projectNames = projectNames;
    }

    private static Dictionary<string, string> NewKeyMap() => new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves nothing — the pre-first-load state (every mark blank).</summary>
    public static SyncMarks Empty { get; } =
        new([], [], [], [], NewKeyMap(), NewKeyMap(), NewKeyMap(), NewKeyMap());

    /// <summary>Snapshots the two fact sources and derives the graph maps into one resolver.
    /// <paramref name="graph"/> may be null (no load yet) — headers then cover only the plan keys their
    /// visible child rows carry, and template changes resolve onto no row.</summary>
    public static SyncMarks Build(TsJournal journal, TsInboundStore inbound, CatalogGraph? graph)
    {
        Dictionary<TsTable, Dictionary<string, List<Line>>> inb = [];
        foreach (TsInboundChange c in inbound.Snapshot())
            Add(inb, c.Table, c.Key, new Line(c.Column, c.Old, c.New));

        // Collapse() already carries the tooltip shape per field: the FIRST write's Old → the LAST value.
        Dictionary<TsTable, Dictionary<string, List<Line>>> outb = [];
        foreach (TsJournalEntry e in journal.Collapse())
            Add(outb, e.Table, e.Key, new Line(e.Column, e.Old, FormatValue(e.Value)));

        Dictionary<Guid, List<string>> planKeys = [];
        Dictionary<Guid, List<string>> templateKeysByTarget = [];
        Dictionary<string, string> templateKeyByPlan = NewKeyMap();
        Dictionary<string, string> templateNames = NewKeyMap();
        Dictionary<string, string> targetNames = NewKeyMap();
        Dictionary<string, string> projectNames = NewKeyMap();
        if (graph is not null)
        {
            Dictionary<Guid, string> templateKeyById = [];
            foreach (ExposureTemplate template in graph.Templates)
            {
                if (template.ImportedFromTsGuid is not string key)
                    continue;
                templateKeyById[template.Id] = key;
                templateNames[key] = template.Name;
            }
            foreach (ExposurePlan plan in graph.Plans)
            {
                string? templateKey = templateKeyById.GetValueOrDefault(plan.ExposureTemplateId);
                if (plan.ImportedFromTsGuid is string planKey)
                {
                    GetList(planKeys, plan.TargetId).Add(planKey);
                    if (templateKey is not null)
                        templateKeyByPlan[planKey] = templateKey;
                }
                if (templateKey is not null)
                {
                    List<string> list = GetList(templateKeysByTarget, plan.TargetId);
                    if (!list.Contains(templateKey))
                        list.Add(templateKey);
                }
            }
            foreach (Target target in graph.Targets)
                if (target.ImportedFromTsGuid is string key)
                    targetNames[key] = target.Name;
            foreach (Project project in graph.Projects)
                if (project.ImportedFromTsGuid is string key)
                    projectNames[key] = project.Name;
        }
        return new SyncMarks(inb, outb, planKeys, templateKeysByTarget, templateKeyByPlan,
            templateNames, targetNames, projectNames);
    }

    /// <summary>
    /// A filter/detail row's mark: keyed on its 1:1 plan key plus that plan's template key — disk-plane rows
    /// (no plan key) are structurally blank, and target/project-level changes mark the header, not leaves.
    /// Tooltip lists one old→new line per pending field per direction; template-derived lines are attributed
    /// ("— template '<name>'") so an inherited change is never mistaken for a row-level edit.
    /// </summary>
    public (string Glyph, string? Tooltip) ForPlan(string? planTsKey)
    {
        if (planTsKey is null)
            return ("", null);
        List<Line>? inb = Get(_inbound, TsTable.ExposurePlan, planTsKey);
        List<Line>? outb = Get(_outbound, TsTable.ExposurePlan, planTsKey);
        string? templateKey = _templateKeyByPlan.GetValueOrDefault(planTsKey);
        List<Line>? tplIn = templateKey is null ? null : Get(_inbound, TsTable.ExposureTemplate, templateKey);
        List<Line>? tplOut = templateKey is null ? null : Get(_outbound, TsTable.ExposureTemplate, templateKey);
        if (inb is null && outb is null && tplIn is null && tplOut is null)
            return ("", null);

        List<string> lines = [];
        AddLines(lines, inb, In, "BIRDWATCHER", attribution: null);
        AddLines(lines, outb, Out, "unpushed", attribution: null);
        string? attribution = templateKey is null ? null : Attribution("template", templateKey, _templateNames);
        AddLines(lines, tplIn, In, "BIRDWATCHER", attribution);
        AddLines(lines, tplOut, Out, "unpushed", attribution);
        return (Glyph(inb is not null || tplIn is not null, outb is not null || tplOut is not null),
            string.Join("\n", lines));
    }

    /// <summary>
    /// A header's mark, resolved over: its target key(s) and project key (the group header / mosaic parent
    /// only — panels pass null so a project edit never lights them) as own-scope entries, plus every plan key
    /// and template key of its target id(s) (graph map) and any row-carried plan keys the caller collected
    /// (covers 1:1 cells when no graph is loaded) as rolled-up entries. Own-scope fields — which mark the
    /// header only, no child row carries their detail — list attributed old→new lines; rolled-up plan/template
    /// fields are summarized as direction counts (their detail lives on the leaf rows). A pending template
    /// field counts once per header regardless of how many of its plans share the template.
    /// </summary>
    public (string Glyph, string? Tooltip) ForKeys(
        IEnumerable<string> targetKeys, string? projectKey, IEnumerable<Guid> targetIds,
        IEnumerable<string>? planKeys = null)
    {
        List<string> lines = [];
        bool anyIn = false, anyOut = false;
        void OwnScope(TsTable table, string key, string kind, Dictionary<string, string> names)
        {
            List<Line>? inb = Get(_inbound, table, key);
            List<Line>? outb = Get(_outbound, table, key);
            string attribution = Attribution(kind, key, names);
            AddLines(lines, inb, In, "BIRDWATCHER", attribution);
            AddLines(lines, outb, Out, "unpushed", attribution);
            anyIn |= inb is not null;
            anyOut |= outb is not null;
        }

        foreach (string key in targetKeys)
            OwnScope(TsTable.Target, key, "target", _targetNames);
        if (projectKey is not null)
            OwnScope(TsTable.Project, projectKey, "project", _projectNames);

        int inFields = 0, outFields = 0;
        void Count(TsTable table, string key)
        {
            inFields += Get(_inbound, table, key)?.Count ?? 0;
            outFields += Get(_outbound, table, key)?.Count ?? 0;
        }

        HashSet<string> seenPlans = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenTemplates = new(StringComparer.OrdinalIgnoreCase);
        foreach (Guid id in targetIds)
        {
            foreach (string key in _planKeysByTarget.GetValueOrDefault(id) ?? [])
                if (seenPlans.Add(key))
                    Count(TsTable.ExposurePlan, key);
            foreach (string key in _templateKeysByTarget.GetValueOrDefault(id) ?? [])
                if (seenTemplates.Add(key))
                    Count(TsTable.ExposureTemplate, key);
        }
        foreach (string key in planKeys ?? [])
        {
            if (seenPlans.Add(key))
                Count(TsTable.ExposurePlan, key);
            if (_templateKeyByPlan.GetValueOrDefault(key) is string templateKey && seenTemplates.Add(templateKey))
                Count(TsTable.ExposureTemplate, templateKey);
        }

        anyIn |= inFields > 0;
        anyOut |= outFields > 0;
        if (!anyIn && !anyOut)
            return ("", null);
        if (inFields > 0)
            lines.Add($"{In} {inFields} field(s) arrived changed from BIRDWATCHER");
        if (outFields > 0)
            lines.Add($"{Out} {outFields} field(s) unpushed");
        return (Glyph(anyIn, anyOut), string.Join("\n", lines));
    }

    /// <summary>A single template's mark for the Templates… picker: its own entries, unattributed — the
    /// picker row already names the template.</summary>
    public (string Glyph, string? Tooltip) ForTemplate(string tsKey)
    {
        List<Line>? inb = Get(_inbound, TsTable.ExposureTemplate, tsKey);
        List<Line>? outb = Get(_outbound, TsTable.ExposureTemplate, tsKey);
        if (inb is null && outb is null)
            return ("", null);
        List<string> lines = [];
        AddLines(lines, inb, In, "BIRDWATCHER", attribution: null);
        AddLines(lines, outb, Out, "unpushed", attribution: null);
        return (Glyph(inb is not null, outb is not null), string.Join("\n", lines));
    }

    // "template 'H900'" / "project 'Nebulae - Above 45'"; the raw key is a display fallback only, not a
    // contract guard — the graph normally resolves every marked key.
    private static string Attribution(string kind, string key, Dictionary<string, string> names) =>
        $"{kind} '{names.GetValueOrDefault(key, key)}'";

    private static void AddLines(
        List<string> lines, List<Line>? source, string glyph, string origin, string? attribution)
    {
        if (source is null)
            return;
        string prefix = attribution is null ? $"{glyph} {origin}" : $"{glyph} {origin} — {attribution}";
        lines.AddRange(source.Select(l => l.Column == TsInboundDiff.NewRowColumn
            ? $"{prefix}: new row"
            : $"{prefix}: {l.Column} {l.Old ?? Format.Dash} → {l.New ?? Format.Dash}"));
    }

    private static string Glyph(bool inbound, bool outbound) =>
        inbound && outbound ? BothWays : inbound ? In : Out;

    private static List<string> GetList(Dictionary<Guid, List<string>> map, Guid id)
    {
        if (!map.TryGetValue(id, out List<string>? list))
            map[id] = list = [];
        return list;
    }

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

    // A tooltip line's null is "no old value" — the shared rule's null passes straight through.
    private static string? FormatValue(object? value) => TsValueText.From(value);
}
