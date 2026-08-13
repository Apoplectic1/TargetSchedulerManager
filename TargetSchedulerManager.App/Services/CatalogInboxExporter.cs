using System.Globalization;
using System.Text;
using System.Text.Json;
using Astronomy.Catalog.TargetScheduler;
using Astronomy.Diagnostics;
using Microsoft.Data.Sqlite;
using TargetSchedulerManager.App.Shared;

namespace TargetSchedulerManager.App.Services;

/// <summary>A local <c>project</c> row snapshot, the columns the inbox contract's ops project.
/// Altitude bounds keep TS's raw 0.0 no-constraint sentinels — the mapping turns them into the
/// contract's nulls (the importer's translation, mirrored).</summary>
internal sealed record InboxProjectRow(
    long Id, string? Guid, string Name, long State, long Priority,
    long MinimumTimeMinutes, double? MinimumAltitudeDeg, double? MaximumAltitudeDeg,
    bool UseCustomHorizon, double HorizonOffsetDeg, long? MeridianWindowMinutes,
    long? FilterSwitchFrequency, long? DitherEvery, bool SmartExposureOrder, bool IsMosaic);

/// <summary>A local <c>target</c> row snapshot. <c>RaHours</c>/<c>DecDegrees</c> are TS's own units
/// (decimal hours / signed degrees — the contract's units, no conversion).</summary>
internal sealed record InboxTargetRow(
    long Id, string? Guid, long? ProjectId, string Name, bool Enabled,
    double? RaHours, double? DecDegrees, long EpochCode, double? RotationDeg);

/// <summary>A local <c>exposureplan</c> row snapshot. <c>ExposureSeconds</c> keeps TS's raw −1
/// inherit-template sentinel — the mapping turns it into the contract's null.</summary>
internal sealed record InboxPlanRow(
    long Id, string? Guid, long TargetId, long TemplateId, double ExposureSeconds, long DesiredCount, bool Enabled);

/// <summary>A local <c>exposuretemplate</c> row snapshot. Gain/offset/readout keep TS's raw −1
/// camera-default sentinels — the mapping turns them into the contract's nulls.</summary>
internal sealed record InboxTemplateRow(
    long Id, string? Guid, string Name, string FilterName, long Gain, long Offset, long Bin, long ReadoutMode,
    double DefaultExposureSeconds, long TwilightLevel, bool MoonAvoidanceEnabled,
    double? MoonAvoidanceSeparationDeg, double? MoonAvoidanceWidthDays,
    double? MoonRelaxScale, double? MoonRelaxMaxAltitudeDeg, double? MoonRelaxMinAltitudeDeg);

/// <summary>An observed-emission batch's row identities: the distinct target and project guids whose
/// fields a pull observed arriving changed (existing rows only — the scope filter already dropped
/// <c>(new)</c> entries and plan/template rows).</summary>
internal sealed record ObservedGuids(IReadOnlyCollection<string> TargetGuids, IReadOnlyCollection<string> ProjectGuids)
{
    public bool IsEmpty => TargetGuids.Count == 0 && ProjectGuids.Count == 0;
}

/// <summary>The four-table snapshot the mapping resolves rows from — read from the local working copy
/// after the push (journal says <em>which</em> rows, this says <em>what</em> values), or built in-memory
/// by mapping tests.</summary>
internal sealed record CatalogExportRows(
    IReadOnlyList<InboxProjectRow> Projects,
    IReadOnlyList<InboxTargetRow> Targets,
    IReadOnlyList<InboxPlanRow> Plans,
    IReadOnlyList<InboxTemplateRow> Templates);

/// <summary>
/// The catalog-export duty (openspec <c>catalog-export</c>): after a push commits, project the applied
/// user-authored journal entries into ISM's catalog inbox as contract-v2 JSONL upserts
/// (<c>..\IntervalSchedulerManager\docs\design\catalog-inbox-contract.md</c>). TSM's one ISM-era duty;
/// the writer side only — TSM never opens <c>Catalog.db</c>.
/// <para>Structure mirrors the testing seams: <see cref="BuildLines"/> is the pure mapping
/// (entries + row snapshot → ordered JSON lines: origin filter, one full-value op per row, template
/// mirror with every plan upsert, authored-desired sourcing, references-first order);
/// <see cref="ReadRows"/> is the thin local-db read; <see cref="WriteInbox"/> the atomic
/// <c>.partial</c>→<c>.jsonl</c> publisher. Any contract violation met while mapping (missing guid,
/// unknown enum code, unresolvable row) throws — the caller surfaces it loudly (rule #16); the TS push
/// is already committed and idempotent upserts make a re-push after the fix harmless.</para>
/// </summary>
internal static class CatalogInboxExporter
{
    /// <summary>The whole duty: map the applied entries against the local db and publish one inbox file.
    /// Returns the record count (0 = nothing intent-shaped applied — e.g. a write-back-only push — and no
    /// file was written).</summary>
    public static int Export(string localDbPath, IReadOnlyList<TsJournalEntry> applied, DateTimeOffset committedAt, string inboxDir)
    {
        IReadOnlyList<string> lines = BuildLines(applied, ReadRows(localDbPath), committedAt);
        if (lines.Count == 0)
            return 0;
        string file = WriteInbox(lines, inboxDir, committedAt);
        Log.Info($"CATALOG EXPORT {lines.Count} record(s) -> {file}");
        return lines.Count;
    }

    /// <summary>The observed-emission half of the duty (openspec <c>add-target-rename</c>, scope widened to
    /// project rows by <c>add-inbox-v2-emission</c>): project pull-observed target- and project-table changes
    /// into the inbox — one full-value upsert per existing row whose fields arrived changed from TS's side,
    /// values read from the fresh post-pull local copy. Same records, same transport;
    /// <paramref name="observedAt"/> is the observing pull's completion time (TS's own commit time is
    /// unknowable). Returns the record count (0 = no guids, no file).</summary>
    public static int ExportObserved(
        string localDbPath, ObservedGuids observed, DateTimeOffset observedAt, string inboxDir)
    {
        if (observed.IsEmpty)
            return 0;
        IReadOnlyList<string> lines = BuildObservedLines(observed, ReadRows(localDbPath), observedAt);
        string file = WriteInbox(lines, inboxDir, observedAt);
        Log.Info($"CATALOG EXPORT {lines.Count} observed record(s) -> {file}");
        return lines.Count;
    }

    /// <summary>The observed-emission scope filter (spec: target and project rows, existing rows only): the
    /// distinct guids among a pull's inbound field changes — never <c>(new)</c> row entries (a remotely-added
    /// row without its family is half a family, an accepted residual), never plan/template rows (plan columns
    /// include actuals; the plan-push mirror keeps templates current).</summary>
    public static ObservedGuids ObservedInboundGuids(IReadOnlyList<TsInboundChange> arrived) =>
        new(GuidsOf(arrived, TsTable.Target), GuidsOf(arrived, TsTable.Project));

    private static string[] GuidsOf(IReadOnlyList<TsInboundChange> arrived, TsTable table) =>
        [.. arrived
            .Where(c => c.Table == table && c.Column != TsInboundDiff.NewRowColumn)
            .Select(c => c.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Maps observed guids to full-value upsert lines against <paramref name="rows"/> —
    /// references first (project upserts before target upserts). A guid the fresh local copy cannot resolve
    /// is a contract violation (the pull's diff just observed the row) — thrown loudly, never skipped
    /// (rule #16).</summary>
    public static IReadOnlyList<string> BuildObservedLines(
        ObservedGuids observed, CatalogExportRows rows, DateTimeOffset observedAt)
    {
        Dictionary<string, InboxProjectRow> projectsByGuid = ByGuid(rows.Projects, p => p.Guid);
        Dictionary<string, InboxTargetRow> targetsByGuid = ByGuid(rows.Targets, t => t.Guid);
        Dictionary<long, InboxProjectRow> projectsById = rows.Projects.ToDictionary(p => p.Id);
        long at = observedAt.ToUnixTimeSeconds();
        List<string> lines = [];
        foreach (string guid in observed.ProjectGuids)
            lines.Add(ProjectUpsertLine(
                projectsByGuid.TryGetValue(guid, out InboxProjectRow? p) ? p
                    : throw new InvalidOperationException(
                        $"catalog export: observed project guid '{guid}' not found in the local db after pull"),
                at));
        foreach (string guid in observed.TargetGuids)
            lines.Add(TargetUpsertLine(
                targetsByGuid.TryGetValue(guid, out InboxTargetRow? t) ? t
                    : throw new InvalidOperationException(
                        $"catalog export: observed target guid '{guid}' not found in the local db after pull"),
                projectsById, at));
        return lines;
    }

    // ---- the pure mapping ---------------------------------------------------------------------------------

    /// <summary>Maps the push's applied entries to contract records, resolving full row values from
    /// <paramref name="rows"/>. Origin filter: <see cref="TsEditKind.Manual"/>/<see cref="TsEditKind.Insert"/>
    /// entries make a row emit; <see cref="TsEditKind.WriteBack"/> entries never do — their only read is the
    /// desired entry's Old, the authored-value source when a same-push ratchet moved the committed desired.</summary>
    public static IReadOnlyList<string> BuildLines(
        IReadOnlyList<TsJournalEntry> applied, CatalogExportRows rows, DateTimeOffset committedAt)
    {
        Dictionary<string, InboxProjectRow> projectsByGuid = ByGuid(rows.Projects, p => p.Guid);
        Dictionary<long, InboxProjectRow> projectsById = rows.Projects.ToDictionary(p => p.Id);
        Dictionary<string, InboxTargetRow> targetsByGuid = ByGuid(rows.Targets, t => t.Guid);
        Dictionary<long, InboxTargetRow> targetsById = rows.Targets.ToDictionary(t => t.Id);
        Dictionary<string, InboxPlanRow> plansByGuid = ByGuid(rows.Plans, p => p.Guid);
        Dictionary<long, InboxPlanRow> plansById = rows.Plans.ToDictionary(p => p.Id);
        Dictionary<string, InboxTemplateRow> templatesByGuid = ByGuid(rows.Templates, t => t.Guid);
        Dictionary<long, InboxTemplateRow> templatesById = rows.Templates.ToDictionary(t => t.Id);

        // Affected rows in first-seen order, deduped by row identity. A plan accumulates its desired
        // sourcing facts alongside (Manual desired value / WriteBack desired Old).
        List<InboxProjectRow> projects = [];
        List<InboxTargetRow> targets = [];
        List<InboxTemplateRow> templates = [];
        List<PlanEmit> plans = [];
        Dictionary<long, PlanEmit> planEmits = [];

        foreach (TsJournalEntry e in applied)
        {
            switch (e.Table)
            {
                case TsTable.Project when e.Kind is TsEditKind.Manual or TsEditKind.Insert:
                    AddOnce(projects, Resolve(projectsByGuid, projectsById, RowRef(e), e));
                    break;

                case TsTable.Target when e.Kind is TsEditKind.Manual or TsEditKind.Insert:
                    AddOnce(targets, Resolve(targetsByGuid, targetsById, RowRef(e), e));
                    break;

                case TsTable.ExposureTemplate when e.Kind is TsEditKind.Manual or TsEditKind.Insert:
                    // A pushed template edit refreshes the mirror (mirror semantics, never authoring —
                    // the values were committed to TS first; this only keeps ISM's copy resolvable+fresh).
                    AddOnce(templates, Resolve(templatesByGuid, templatesById, RowRef(e), e));
                    break;

                case TsTable.ExposurePlan:
                    PlanEmit emit = PlanFor(Resolve(plansByGuid, plansById, RowRef(e), e));
                    if (e.Kind is TsEditKind.Manual or TsEditKind.Insert)
                    {
                        emit.Affected = true;
                        if (e.Kind == TsEditKind.Manual && e.Column.Equals("desired", StringComparison.OrdinalIgnoreCase))
                            emit.ManualDesired = Convert.ToInt64(e.Value, CultureInfo.InvariantCulture);
                    }
                    else if (e.Column.Equals("desired", StringComparison.OrdinalIgnoreCase))
                    {
                        emit.RatchetOld = e.Old
                            ?? throw Violation(e, "write-back desired entry has no Old (pre-push value)");
                    }
                    // WriteBack acquired/accepted: actuals — never read, never emit.
                    break;
            }
        }

        // Every emitted plan's referenced template rides along as a mirror (spec: the mirror accompanies
        // every exposure-plan upsert, so a TS-authored post-import template resolves at ingest).
        foreach (PlanEmit p in plans.Where(p => p.Affected))
            AddOnce(templates, templatesById.TryGetValue(p.Row.TemplateId, out InboxTemplateRow? t)
                ? t
                : throw new InvalidOperationException(
                    $"catalog export: plan id {p.Row.Id} references exposure template id {p.Row.TemplateId}, not found in the local db"));

        long at = committedAt.ToUnixTimeSeconds();
        List<string> lines = [];
        // References first: project → template → target → plan.
        foreach (InboxProjectRow p in projects)
            lines.Add(ProjectUpsertLine(p, at));
        foreach (InboxTemplateRow t in templates)
            lines.Add(Serialize("exposure-template-upsert", at, new()
            {
                ["ts_guid"] = RequireGuid(t.Guid, "exposure template", t.Name),
                ["name"] = t.Name,
                ["filter_name"] = t.FilterName,
                ["gain"] = Unsentinel(t.Gain),
                ["offset"] = Unsentinel(t.Offset),
                ["bin"] = t.Bin,
                ["readout_mode"] = Unsentinel(t.ReadoutMode),
                ["default_exposure_seconds"] = t.DefaultExposureSeconds,
                ["twilight_level"] = TwilightLevelName(t),
                ["moon_avoidance_enabled"] = t.MoonAvoidanceEnabled,
                ["moon_avoidance_separation_deg"] = t.MoonAvoidanceSeparationDeg,
                ["moon_avoidance_width_days"] = t.MoonAvoidanceWidthDays,
                ["moon_relax_scale"] = t.MoonRelaxScale,
                ["moon_relax_max_altitude_deg"] = t.MoonRelaxMaxAltitudeDeg,
                ["moon_relax_min_altitude_deg"] = t.MoonRelaxMinAltitudeDeg,
            }));
        foreach (InboxTargetRow t in targets)
            lines.Add(TargetUpsertLine(t, projectsById, at));
        foreach (PlanEmit p in plans.Where(p => p.Affected))
            lines.Add(Serialize("exposure-plan-upsert", at, new()
            {
                ["ts_guid"] = RequireGuid(p.Row.Guid, "exposure plan", $"id {p.Row.Id}"),
                ["target_ts_guid"] = RequireGuid(
                    targetsById.TryGetValue(p.Row.TargetId, out InboxTargetRow? owner) ? owner.Guid : null,
                    "plan's target", $"plan id {p.Row.Id}"),
                ["exposure_template_ts_guid"] = RequireGuid(
                    templatesById[p.Row.TemplateId].Guid, "plan's template", $"plan id {p.Row.Id}"),
                ["exposure_seconds"] = p.Row.ExposureSeconds < 0 ? null : p.Row.ExposureSeconds,
                ["desired_count"] = p.AuthoredDesired(),
                ["enabled"] = p.Row.Enabled,
            }));
        return lines;

        PlanEmit PlanFor(InboxPlanRow row)
        {
            if (!planEmits.TryGetValue(row.Id, out PlanEmit? emit))
                plans.Add(planEmits[row.Id] = emit = new PlanEmit(row));
            return emit;
        }
    }

    /// <summary>One plan row's emission state: whether a user-authored entry touched it, and the
    /// authored-desired sourcing facts (design D2: an explicit desired edit outranks the ratchet's
    /// pre-push Old; with neither, the committed row value IS the authored value).</summary>
    private sealed class PlanEmit(InboxPlanRow row)
    {
        public InboxPlanRow Row { get; } = row;
        public bool Affected { get; set; }
        public long? ManualDesired { get; set; }
        public string? RatchetOld { get; set; }

        public long AuthoredDesired() =>
            ManualDesired
            ?? (RatchetOld is { } old
                ? long.TryParse(old, NumberStyles.Integer, CultureInfo.InvariantCulture, out long o)
                    ? o
                    : throw new InvalidOperationException(
                        $"catalog export: write-back desired Old '{old}' on plan id {Row.Id} is not an integer")
                : Row.DesiredCount);
    }

    // The one project-upsert serialization — the push path and the observed-emission path emit the same
    // full-value v2 record. Altitude 0.0 is TS's no-constraint sentinel on both bounds -> null, the
    // importer's translation mirrored (contract v2 sentinel table).
    private static string ProjectUpsertLine(InboxProjectRow p, long at) =>
        Serialize("project-upsert", at, new()
        {
            ["ts_guid"] = RequireGuid(p.Guid, "project", p.Name),
            ["name"] = p.Name,
            ["state"] = ProjectStateName(p),
            ["priority"] = ProjectPriorityName(p),
            ["minimum_time_minutes"] = p.MinimumTimeMinutes,
            ["minimum_altitude_deg"] = ZeroUnsentinel(p.MinimumAltitudeDeg),
            ["maximum_altitude_deg"] = ZeroUnsentinel(p.MaximumAltitudeDeg),
            ["use_custom_horizon"] = p.UseCustomHorizon,
            ["horizon_offset_deg"] = p.HorizonOffsetDeg,
            ["meridian_window_minutes"] = p.MeridianWindowMinutes,
            ["filter_switch_frequency"] = p.FilterSwitchFrequency,
            ["dither_every"] = p.DitherEvery,
            ["smart_exposure_order"] = p.SmartExposureOrder,
            ["is_mosaic"] = p.IsMosaic,
        });

    // The one target-upsert serialization — the push path and the observed-emission path emit the same
    // full-value record.
    private static string TargetUpsertLine(InboxTargetRow t, Dictionary<long, InboxProjectRow> projectsById, long at) =>
        Serialize("target-upsert", at, new()
        {
            ["ts_guid"] = RequireGuid(t.Guid, "target", t.Name),
            ["project_ts_guid"] = RequireGuid(
                t.ProjectId is { } pid && projectsById.TryGetValue(pid, out InboxProjectRow? parent) ? parent.Guid : null,
                "target's project", t.Name),
            ["name"] = t.Name,
            ["enabled"] = t.Enabled,
            ["ra_hours"] = t.RaHours ?? throw MissingField("target", t.Name, "ra"),
            ["dec_degrees_signed"] = t.DecDegrees ?? throw MissingField("target", t.Name, "dec"),
            ["epoch"] = EpochName(t),
            ["rotation_deg"] = t.RotationDeg,
        });

    private static Dictionary<string, T> ByGuid<T>(IReadOnlyList<T> rows, Func<T, string?> guid) =>
        rows.Where(r => !string.IsNullOrWhiteSpace(guid(r)))
            .ToDictionary(r => guid(r)!, StringComparer.OrdinalIgnoreCase);

    /// <summary>An entry's row reference: an Insert's minted <see cref="TsJournalEntry.RowGuid"/> (the
    /// journaled local id may have been renumbered by the push round-trip; the guid is the stable name),
    /// else the journal key (Target/Project = guid, plan/template = integer id — the per-table key spaces).</summary>
    private static string RowRef(TsJournalEntry e) => e.RowGuid ?? e.Key;

    /// <summary>Resolves a row reference guid-first, then as an integer id — covering both key spaces and
    /// the Provenance id-string fallback. Unresolvable = the journal and the local db disagree about a row
    /// that just replayed: a contract violation, thrown loudly.</summary>
    private static T Resolve<T>(
        Dictionary<string, T> byGuid, Dictionary<long, T> byId, string reference, TsJournalEntry e) =>
        byGuid.TryGetValue(reference, out T? row) ? row
        : long.TryParse(reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)
            && byId.TryGetValue(id, out row) ? row
        : throw Violation(e, $"row '{reference}' not found in the local db after push");

    private static void AddOnce<T>(List<T> list, T row) where T : class
    {
        if (!list.Contains(row))
            list.Add(row);
    }

    private static string RequireGuid(string? guid, string what, string identity) =>
        string.IsNullOrWhiteSpace(guid)
            ? throw new InvalidOperationException($"catalog export: {what} \"{identity}\" has no TS guid — cannot express contract identity")
            : guid;

    private static InvalidOperationException MissingField(string what, string identity, string field) =>
        new($"catalog export: {what} \"{identity}\" has no {field} — required by the inbox contract");

    private static InvalidOperationException Violation(TsJournalEntry e, string detail) =>
        new($"catalog export: {e.Table} entry \"{e.Label}\" (key {e.Key}): {detail}");

    /// <summary>TS's −1 defer-to-default sentinel → the contract's null.</summary>
    private static object? Unsentinel(long value) => value < 0 ? null : value;

    /// <summary>TS's 0.0 no-constraint sentinel (altitude bounds) → the contract's null.</summary>
    private static object? ZeroUnsentinel(double? value) => value is null or 0.0 ? null : value;

    // The contract's string vocabularies, from TS's own enum codes (TsEditableSchema's maps). An unknown
    // code cannot be projected — throw (rule #16), never guess a nearest value.
    private static string ProjectStateName(InboxProjectRow p) => p.State switch
    {
        0 => "draft", 1 => "active", 2 => "inactive", 3 => "closed",
        _ => throw new InvalidOperationException($"catalog export: project \"{p.Name}\" has unknown state code {p.State}"),
    };

    private static string ProjectPriorityName(InboxProjectRow p) => p.Priority switch
    {
        0 => "low", 1 => "normal", 2 => "high",
        _ => throw new InvalidOperationException($"catalog export: project \"{p.Name}\" has unknown priority code {p.Priority}"),
    };

    private static string EpochName(InboxTargetRow t) => t.EpochCode switch
    {
        0 => "JNOW", 1 => "B1950", 2 => "J2000", 3 => "J2050",
        _ => throw new InvalidOperationException($"catalog export: target \"{t.Name}\" has unknown epoch code {t.EpochCode}"),
    };

    private static string TwilightLevelName(InboxTemplateRow t) => t.TwilightLevel switch
    {
        0 => "nighttime", 1 => "astronomical", 2 => "nautical", 3 => "civil",
        _ => throw new InvalidOperationException($"catalog export: template \"{t.Name}\" has unknown twilight level code {t.TwilightLevel}"),
    };

    private static string Serialize(string op, long at, Dictionary<string, object?> fields)
    {
        Dictionary<string, object?> record = new() { ["v"] = 2, ["at"] = at, ["source"] = "TSM", ["op"] = op };
        foreach ((string key, object? value) in fields)
            record[key] = value;
        return JsonSerializer.Serialize(record);
    }

    // ---- the thin local-db read ---------------------------------------------------------------------------

    /// <summary>Reads the four-table snapshot from the local working copy (read-only, unpooled, explicit
    /// column lists). After a push this holds every committed value — regardless of whether the closing
    /// pull landed, since edits were local-first.</summary>
    public static CatalogExportRows ReadRows(string localDbPath)
    {
        using SqliteConnection c = new(new SqliteConnectionStringBuilder
        {
            DataSource = localDbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        c.Open();

        List<InboxProjectRow> projects = [];
        foreach (object?[] r in Rows(c,
            "SELECT Id, guid, name, state, priority, minimumtime, minimumaltitude, maximumAltitude, " +
            "usecustomhorizon, horizonoffset, meridianwindow, filterswitchfrequency, ditherevery, " +
            "smartexposureorder, isMosaic FROM project"))
            projects.Add(new((long)r[0]!, (string?)r[1], (string)r[2]!, (long)r[3]!, (long)r[4]!,
                (long)r[5]!, AsDouble(r[6]), AsDouble(r[7]), (long)r[8]! != 0, AsDouble(r[9]) ?? 0,
                (long?)r[10], (long?)r[11], (long?)r[12], (long)r[13]! != 0, (long)r[14]! != 0));

        List<InboxTargetRow> targets = [];
        foreach (object?[] r in Rows(c, "SELECT Id, guid, projectid, name, active, ra, dec, epochcode, rotation FROM target"))
            targets.Add(new((long)r[0]!, (string?)r[1], (long?)r[2], (string)r[3]!, (long)r[4]! != 0,
                AsDouble(r[5]), AsDouble(r[6]), (long)r[7]!, AsDouble(r[8])));

        List<InboxPlanRow> plans = [];
        foreach (object?[] r in Rows(c, "SELECT Id, guid, targetid, exposureTemplateId, exposure, desired, enabled FROM exposureplan"))
            plans.Add(new((long)r[0]!, (string?)r[1], (long)r[2]!, (long)r[3]!,
                AsDouble(r[4]) ?? -1, (long)r[5]!, (long)r[6]! != 0));

        List<InboxTemplateRow> templates = [];
        foreach (object?[] r in Rows(c,
            "SELECT Id, guid, name, filtername, gain, offset, bin, readoutmode, defaultexposure, " +
            "twilightlevel, moonavoidanceenabled, moonavoidanceseparation, moonavoidancewidth, " +
            "moonrelaxscale, moonrelaxmaxaltitude, moonrelaxminaltitude FROM exposuretemplate"))
            templates.Add(new((long)r[0]!, (string?)r[1], (string)r[2]!, (string)r[3]!,
                (long)r[4]!, (long)r[5]!, (long)r[6]!, (long)r[7]!, AsDouble(r[8]) ?? 0,
                (long)r[9]!, (long)r[10]! != 0, AsDouble(r[11]), AsDouble(r[12]),
                AsDouble(r[13]), AsDouble(r[14]), AsDouble(r[15])));

        return new CatalogExportRows(projects, targets, plans, templates);
    }

    private static IEnumerable<object?[]> Rows(SqliteConnection c, string sql)
    {
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            object?[] row = new object?[reader.FieldCount];
            for (int i = 0; i < row.Length; i++)
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            yield return row;
        }
    }

    // SQLite column affinity can hand back INTEGER for whole REAL values — box both to double.
    private static double? AsDouble(object? value) =>
        value is null ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);

    // ---- the atomic file publish --------------------------------------------------------------------------

    /// <summary>Publishes one emission's records: creates the inbox directory if missing, writes every line
    /// to <c>tsm-&lt;yyyyMMdd-HHmmss&gt;.jsonl.partial</c> (UTF-8 no BOM, <c>\n</c> endings), flushes to disk,
    /// then renames to <c>.jsonl</c> — ISM's <c>*.jsonl</c> glob never observes an incomplete file, and a
    /// crashed <c>.partial</c> is inert. A taken stamp (a push and its closing pull's observed emission
    /// landing in the same second) advances to the next free second — never an overwrite, never a name
    /// outside the contract's pattern. Never touches any other file (including <c>*.processing</c>).</summary>
    public static string WriteInbox(IReadOnlyList<string> lines, string inboxDir, DateTimeOffset stamp)
    {
        Directory.CreateDirectory(inboxDir);
        string final, partial;
        while (true)
        {
            final = Path.Combine(inboxDir, $"tsm-{stamp.ToLocalTime():yyyyMMdd-HHmmss}.jsonl");
            partial = final + ".partial";
            // A crashed .partial also blocks its stamp: it stays on disk for diagnosis, never reclaimed.
            if (!File.Exists(final) && !File.Exists(partial))
                break;
            stamp = stamp.AddSeconds(1);
        }
        using (FileStream fs = new(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            foreach (string line in lines)
                fs.Write(Encoding.UTF8.GetBytes(line + "\n"));
            fs.Flush(flushToDisk: true);
        }
        File.Move(partial, final, overwrite: false);
        return final;
    }
}
