using System.Text.Json;
using Astronomy.Catalog.TargetScheduler;
using Microsoft.Data.Sqlite;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

/// <summary>
/// The catalog-export duty (openspec <c>catalog-export</c>) — file-level contract fixtures only: the pure
/// mapping (origin filter, full-value ops, authored-desired sourcing, mirror accompaniment, ordering,
/// envelope), the local-db row read, the atomic <c>.partial</c>→<c>.jsonl</c> publisher, and the PushAsync
/// hook. No test here (or anywhere) opens <c>Catalog.db</c> — the inbox file IS the boundary.
/// </summary>
public class CatalogInboxExporterTests
{
    private static readonly DateTimeOffset CommitStamp = DateTimeOffset.FromUnixTimeSeconds(1_755_043_200);

    // ---- fixtures -----------------------------------------------------------------------------------------

    private static CatalogExportRows Rows(
        InboxProjectRow? project = null, InboxTargetRow? target = null,
        InboxPlanRow? plan = null, InboxTemplateRow? template = null) => new(
        [project ?? Project()], [target ?? Target()], [plan ?? Plan()], [template ?? Template()]);

    private static InboxProjectRow Project() => new(1, "p-g", "Orion Group", State: 1, Priority: 2,
        MinimumTimeMinutes: 90, MinimumAltitudeDeg: 30, MaximumAltitudeDeg: 0,   // max keeps TS's raw 0.0 sentinel
        UseCustomHorizon: true, HorizonOffsetDeg: 0, MeridianWindowMinutes: null,
        FilterSwitchFrequency: null, DitherEvery: 3, SmartExposureOrder: false, IsMosaic: false);

    private static InboxTargetRow Target() => new(
        10, "t-g", ProjectId: 1, "M 42", Enabled: true,
        RaHours: 5.588, DecDegrees: -5.39, EpochCode: 2, RotationDeg: null);

    private static InboxPlanRow Plan() => new(
        30, "pl-g", TargetId: 10, TemplateId: 20, ExposureSeconds: 300, DesiredCount: 30, Enabled: true);

    private static InboxTemplateRow Template() => new(
        20, "tpl-g", "Ha 300", "Ha", Gain: 100, Offset: 30, Bin: 1, ReadoutMode: 0,
        DefaultExposureSeconds: 300, TwilightLevel: 1, MoonAvoidanceEnabled: true,
        MoonAvoidanceSeparationDeg: 60, MoonAvoidanceWidthDays: 7,
        MoonRelaxScale: 0, MoonRelaxMaxAltitudeDeg: 5, MoonRelaxMinAltitudeDeg: -15);

    private static TsJournalEntry Entry(
        TsEditKind kind, TsTable table, string key, string column, object? value,
        string? old = null, long seq = 1, string? rowGuid = null) =>
        new(seq, kind, table, key, column, value, old, "label", DateTimeOffset.UnixEpoch) { RowGuid = rowGuid };

    private static JsonElement Parse(string line) => JsonSerializer.Deserialize<JsonElement>(line);

    /// <summary>A real local-db fixture with the four TS tables (the columns the exporter reads) and the
    /// standard row set above.</summary>
    internal static void CreateTsDb(string path)
    {
        using SqliteConnection c = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE project (Id INTEGER PRIMARY KEY, guid TEXT, name TEXT, state INTEGER, priority INTEGER,
                minimumtime INTEGER, minimumaltitude REAL, maximumAltitude REAL, usecustomhorizon INTEGER,
                horizonoffset REAL, meridianwindow INTEGER, filterswitchfrequency INTEGER, ditherevery INTEGER,
                smartexposureorder INTEGER, isMosaic INTEGER);
            CREATE TABLE target (Id INTEGER PRIMARY KEY, guid TEXT, projectid INTEGER, name TEXT, active INTEGER,
                ra REAL, dec REAL, epochcode INTEGER, rotation REAL);
            CREATE TABLE exposureplan (Id INTEGER PRIMARY KEY, guid TEXT, targetid INTEGER,
                exposureTemplateId INTEGER, exposure REAL, desired INTEGER, enabled INTEGER);
            CREATE TABLE exposuretemplate (Id INTEGER PRIMARY KEY, guid TEXT, name TEXT, filtername TEXT,
                gain INTEGER, offset INTEGER, bin INTEGER, readoutmode INTEGER, defaultexposure REAL,
                twilightlevel INTEGER, moonavoidanceenabled INTEGER, moonavoidanceseparation REAL,
                moonavoidancewidth INTEGER, moonrelaxscale REAL, moonrelaxmaxaltitude REAL,
                moonrelaxminaltitude REAL);
            INSERT INTO project VALUES (1, 'p-g', 'Orion Group', 1, 2, 90, 30, 0, 1, 0, NULL, NULL, 3, 0, 0);
            INSERT INTO target VALUES (10, 't-g', 1, 'M 42', 1, 5.588, -5.39, 2, NULL);
            INSERT INTO exposureplan VALUES (30, 'pl-g', 10, 20, 300, 30, 1);
            INSERT INTO exposuretemplate VALUES (20, 'tpl-g', 'Ha 300', 'Ha', 100, 30, 1, -1, 300, 1, 1, 60, 7,
                0, 5, -15);
            """;
        cmd.ExecuteNonQuery();
    }

    // ---- the pure mapping (spec: sole emitter · full-value ops · mirror · actuals never emit) --------------

    [Fact]
    public void DesiredEdit_EmitsFullPlanRow_WithTemplateMirror_AndEnvelope()
    {
        IReadOnlyList<string> lines = CatalogInboxExporter.BuildLines(
            [Entry(TsEditKind.Manual, TsTable.ExposurePlan, "30", "desired", 30L, "20")], Rows(), CommitStamp);

        Assert.Equal(2, lines.Count);                           // mirror rides every plan upsert
        JsonElement mirror = Parse(lines[0]);
        JsonElement plan = Parse(lines[1]);                     // references first: template before plan

        Assert.Equal("exposure-template-upsert", mirror.GetProperty("op").GetString());
        Assert.Equal("exposure-plan-upsert", plan.GetProperty("op").GetString());
        foreach (JsonElement record in new[] { mirror, plan })
        {
            Assert.Equal(2, record.GetProperty("v").GetInt32());
            Assert.Equal(1_755_043_200, record.GetProperty("at").GetInt64());
            Assert.Equal("TSM", record.GetProperty("source").GetString());
        }

        Assert.Equal("pl-g", plan.GetProperty("ts_guid").GetString());
        Assert.Equal("t-g", plan.GetProperty("target_ts_guid").GetString());
        Assert.Equal("tpl-g", plan.GetProperty("exposure_template_ts_guid").GetString());
        Assert.Equal(300, plan.GetProperty("exposure_seconds").GetDouble());
        Assert.Equal(30, plan.GetProperty("desired_count").GetInt64());
        Assert.True(plan.GetProperty("enabled").GetBoolean());

        Assert.Equal("tpl-g", mirror.GetProperty("ts_guid").GetString());
        Assert.Equal("Ha", mirror.GetProperty("filter_name").GetString());
        Assert.Equal("astronomical", mirror.GetProperty("twilight_level").GetString());
        Assert.Equal(60, mirror.GetProperty("moon_avoidance_separation_deg").GetDouble());
        Assert.Equal(0, mirror.GetProperty("moon_relax_scale").GetDouble());       // v2 relax triplet
        Assert.Equal(5, mirror.GetProperty("moon_relax_max_altitude_deg").GetDouble());
        Assert.Equal(-15, mirror.GetProperty("moon_relax_min_altitude_deg").GetDouble());
    }

    [Fact]
    public void WriteBackOnlyPush_EmitsNothing()
    {
        // The pushed-ratchet-silent scenario: acquired/accepted stamps AND the desired ratchet replay,
        // but nothing user-authored touched the plan — actuals never emit.
        IReadOnlyList<string> lines = CatalogInboxExporter.BuildLines(
            [
                Entry(TsEditKind.WriteBack, TsTable.ExposurePlan, "30", "acquired", 40L, "35", seq: 1),
                Entry(TsEditKind.WriteBack, TsTable.ExposurePlan, "30", "accepted", 40L, "35", seq: 2),
                Entry(TsEditKind.WriteBack, TsTable.ExposurePlan, "30", "desired", 40L, "30", seq: 3),
            ],
            Rows(plan: Plan() with { DesiredCount = 40 }), CommitStamp);

        Assert.Empty(lines);
    }

    [Fact]
    public void CoEditedRow_SourcesAuthoredDesired_FromRatchetPrePushOld()
    {
        // Same-push ratchet (30→40) + a manual enable edit on the same plan: the emitted row carries the
        // authored 30, never the ratcheted 40 the committed row holds (design D2).
        IReadOnlyList<string> lines = CatalogInboxExporter.BuildLines(
            [
                Entry(TsEditKind.WriteBack, TsTable.ExposurePlan, "30", "desired", 40L, "30", seq: 1),
                Entry(TsEditKind.Manual, TsTable.ExposurePlan, "30", "enabled", 0L, "1", seq: 2),
            ],
            Rows(plan: Plan() with { DesiredCount = 40, Enabled = false }), CommitStamp);

        JsonElement plan = Parse(lines[^1]);
        Assert.Equal(30, plan.GetProperty("desired_count").GetInt64());
        Assert.False(plan.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void CoEditedRow_ExplicitDesiredEdit_OutranksTheRatchetOld()
    {
        IReadOnlyList<string> lines = CatalogInboxExporter.BuildLines(
            [
                Entry(TsEditKind.WriteBack, TsTable.ExposurePlan, "30", "desired", 40L, "30", seq: 1),
                Entry(TsEditKind.Manual, TsTable.ExposurePlan, "30", "desired", 35L, "30", seq: 2),
            ],
            Rows(plan: Plan() with { DesiredCount = 35 }), CommitStamp);

        Assert.Equal(35, Parse(lines[^1]).GetProperty("desired_count").GetInt64());
    }

    [Fact]
    public void AdoptionInserts_EmitTargetPlanAndMirror_ResolvedByGuid_ReferencesFirst()
    {
        // The inserted plan journaled under its LOCAL id (5); the push round-trip renumbered it (30 in the
        // snapshot) — resolution rides the minted RowGuid, the copy-stable name.
        IReadOnlyList<string> lines = CatalogInboxExporter.BuildLines(
            [
                Entry(TsEditKind.Insert, TsTable.Target, "t-g", TsJournal.InsertColumn, "{}", seq: 1, rowGuid: "t-g"),
                Entry(TsEditKind.Insert, TsTable.ExposurePlan, "5", TsJournal.InsertColumn, "{}", seq: 2, rowGuid: "pl-g"),
            ],
            Rows(), CommitStamp);

        Assert.Equal(
            "exposure-template-upsert, target-upsert, exposure-plan-upsert",
            string.Join(", ", lines.Select(l => Parse(l).GetProperty("op").GetString())));

        JsonElement target = Parse(lines[1]);
        Assert.Equal("t-g", target.GetProperty("ts_guid").GetString());
        Assert.Equal("p-g", target.GetProperty("project_ts_guid").GetString());
        Assert.Equal("J2000", target.GetProperty("epoch").GetString());
        Assert.Equal(5.588, target.GetProperty("ra_hours").GetDouble());
        Assert.Equal(JsonValueKind.Null, target.GetProperty("rotation_deg").ValueKind);
    }

    [Fact]
    public void ProjectEdit_EmitsProjectUpsert_WithContractVocabulary_AndV2SettingsBlock()
    {
        IReadOnlyList<string> lines = CatalogInboxExporter.BuildLines(
            [Entry(TsEditKind.Manual, TsTable.Project, "p-g", "state", 1L, "0")], Rows(), CommitStamp);

        JsonElement project = Parse(Assert.Single(lines));
        Assert.Equal("project-upsert", project.GetProperty("op").GetString());
        Assert.Equal("p-g", project.GetProperty("ts_guid").GetString());
        Assert.Equal("active", project.GetProperty("state").GetString());
        Assert.Equal("high", project.GetProperty("priority").GetString());
        // The v2 settings block — full committed values, altitude 0.0 sentinels as nulls.
        Assert.Equal(90, project.GetProperty("minimum_time_minutes").GetInt64());
        Assert.Equal(30, project.GetProperty("minimum_altitude_deg").GetDouble());
        Assert.Equal(JsonValueKind.Null, project.GetProperty("maximum_altitude_deg").ValueKind);
        Assert.True(project.GetProperty("use_custom_horizon").GetBoolean());
        Assert.Equal(0, project.GetProperty("horizon_offset_deg").GetDouble());
        Assert.Equal(JsonValueKind.Null, project.GetProperty("meridian_window_minutes").ValueKind);
        Assert.Equal(JsonValueKind.Null, project.GetProperty("filter_switch_frequency").ValueKind);
        Assert.Equal(3, project.GetProperty("dither_every").GetInt64());
        Assert.False(project.GetProperty("smart_exposure_order").GetBoolean());
        Assert.False(project.GetProperty("is_mosaic").GetBoolean());
    }

    [Fact]
    public void TemplateEdit_RefreshesTheMirror_MirrorSemanticsOnly()
    {
        // TSM's template manager edits replay to TS; the export refreshes ISM's mirror of the committed
        // values — one template-upsert, nothing else (mirror, never authoring).
        IReadOnlyList<string> lines = CatalogInboxExporter.BuildLines(
            [Entry(TsEditKind.Manual, TsTable.ExposureTemplate, "20", "gain", 139L, "100")], Rows(), CommitStamp);

        Assert.Equal("exposure-template-upsert", Parse(Assert.Single(lines)).GetProperty("op").GetString());
    }

    [Fact]
    public void Sentinels_BecomeContractNulls()
    {
        IReadOnlyList<string> lines = CatalogInboxExporter.BuildLines(
            [Entry(TsEditKind.Manual, TsTable.ExposurePlan, "30", "enabled", 1L, "0")],
            Rows(
                plan: Plan() with { ExposureSeconds = -1 },     // inherit-template sentinel
                template: Template() with { Gain = -1, Offset = -1, ReadoutMode = -1 }),
            CommitStamp);

        JsonElement mirror = Parse(lines[0]);
        JsonElement plan = Parse(lines[1]);
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("exposure_seconds").ValueKind);
        Assert.Equal(JsonValueKind.Null, mirror.GetProperty("gain").ValueKind);
        Assert.Equal(JsonValueKind.Null, mirror.GetProperty("offset").ValueKind);
        Assert.Equal(JsonValueKind.Null, mirror.GetProperty("readout_mode").ValueKind);
    }

    [Fact]
    public void MultipleEntriesOnOneRow_CollapseToOneFullValueOp()
    {
        IReadOnlyList<string> lines = CatalogInboxExporter.BuildLines(
            [
                Entry(TsEditKind.Manual, TsTable.ExposurePlan, "30", "desired", 30L, "20", seq: 1),
                Entry(TsEditKind.Manual, TsTable.ExposurePlan, "30", "enabled", 1L, "0", seq: 2),
            ],
            Rows(), CommitStamp);

        Assert.Equal(2, lines.Count);                           // one mirror + ONE plan op, not two
        Assert.Single(lines, l => Parse(l).GetProperty("op").GetString() == "exposure-plan-upsert");
    }

    [Fact]
    public void MissingGuid_OrUnknownEnumCode_ThrowLoudly()
    {
        // Rule #16: identity or vocabulary the contract can't express aborts the export — never a guess.
        Assert.Throws<InvalidOperationException>(() => CatalogInboxExporter.BuildLines(
            [Entry(TsEditKind.Manual, TsTable.ExposurePlan, "30", "desired", 30L, "20")],
            Rows(plan: Plan() with { Guid = null }), CommitStamp));

        Assert.Throws<InvalidOperationException>(() => CatalogInboxExporter.BuildLines(
            [Entry(TsEditKind.Manual, TsTable.Project, "p-g", "state", 9L, "0")],
            Rows(project: Project() with { State = 9 }), CommitStamp));
    }

    // ---- the local-db read --------------------------------------------------------------------------------

    [Fact]
    public void ReadRows_ReadsAllFourTables_RawSentinelsAndNullsIntact()
    {
        string db = Path.Combine(SyncTestEnv.NewDir(), "local.sqlite");
        CreateTsDb(db);

        CatalogExportRows rows = CatalogInboxExporter.ReadRows(db);

        Assert.Equal(Project(), Assert.Single(rows.Projects));
        Assert.Equal(Target(), Assert.Single(rows.Targets));    // rotation NULL survives as null
        Assert.Equal(Plan(), Assert.Single(rows.Plans));
        InboxTemplateRow template = Assert.Single(rows.Templates);
        Assert.Equal(Template() with { ReadoutMode = -1 }, template);   // fixture stores the raw sentinel
    }

    // ---- the atomic file publish --------------------------------------------------------------------------

    [Fact]
    public void WriteInbox_CreatesDirectory_NoBom_LfEndings_ContractName_NoPartialLeftBehind()
    {
        string inbox = Path.Combine(SyncTestEnv.NewDir(), "inbox");   // does not exist yet

        string file = CatalogInboxExporter.WriteInbox(["{\"a\":1}", "{\"b\":2}"], inbox, CommitStamp);

        Assert.Equal($"tsm-{CommitStamp.ToLocalTime():yyyyMMdd-HHmmss}.jsonl", Path.GetFileName(file));
        byte[] bytes = File.ReadAllBytes(file);
        Assert.Equal("{\"a\":1}\n{\"b\":2}\n"u8.ToArray(), bytes);    // UTF-8, no BOM, \n endings
        Assert.Equal([file], Directory.GetFiles(inbox));              // the .partial renamed away
    }

    [Fact]
    public void WriteInbox_NeverTouchesOtherFiles_IncludingProcessing()
    {
        string inbox = SyncTestEnv.NewDir();
        string older = Path.Combine(inbox, "tsm-20260101-000000.jsonl");
        string claimed = Path.Combine(inbox, "tsm-20260102-000000.processing");
        File.WriteAllText(older, "older\n");
        File.WriteAllText(claimed, "claimed\n");

        CatalogInboxExporter.WriteInbox(["{\"a\":1}"], inbox, CommitStamp);

        Assert.Equal("older\n", File.ReadAllText(older));
        Assert.Equal("claimed\n", File.ReadAllText(claimed));
        Assert.Equal(3, Directory.GetFiles(inbox).Length);
    }

    // ---- the PushAsync hook -------------------------------------------------------------------------------

    private static (MainViewModel Vm, TsSync Sync) PushVm()
    {
        // The closing-pull-failure shape (see PushAsync_ClosingPullFailure_ReportsHonestSuccess): the
        // replay lands via the stubs, the closing pull chokes on a non-SQLite remote — so PushAsync never
        // enters the full reload, and the export reads the LOCAL db, which local-first edits already hold.
        string dir = SyncTestEnv.NewDir();
        TsSync sync = new(
            Path.Combine(dir, "remote.sqlite"), Path.Combine(dir, "local.sqlite"),
            _ => new RecordingEditor(), _ => new StubWriteBackApplier());
        File.WriteAllText(sync.RemotePath, "not a sqlite database");
        MainViewModel vm = new(new TsEditGate(sync, _ => throw new InvalidOperationException("no local edits")))
        {
            CatalogInboxDir = Path.Combine(dir, "inbox"),
        };
        return (vm, sync);
    }

    [Fact]
    public async Task PushAsync_CommittedPush_PublishesInboxRecords_AndSaysSo()
    {
        (MainViewModel vm, TsSync sync) = PushVm();
        CreateTsDb(sync.LocalPath);
        sync.RecordEdit(TsTable.ExposurePlan, "30", "desired", 30L, "20", "M 42 · Ha");

        await vm.PushAsync();

        Assert.Contains("pushed 1 field(s)", vm.StatusText);
        Assert.Contains("catalog inbox 2 record(s)", vm.StatusText);
        string file = Assert.Single(Directory.GetFiles(vm.CatalogInboxDir, "*.jsonl"));
        string[] lines = File.ReadAllLines(file);
        Assert.Equal(2, lines.Length);
        Assert.Equal("exposure-plan-upsert", Parse(lines[1]).GetProperty("op").GetString());
    }

    [Fact]
    public async Task PushAsync_ExportFault_SurfacesLoudly_PushOutcomeAndJournalUntouched()
    {
        (MainViewModel vm, TsSync sync) = PushVm();
        SyncTestEnv.CreateDb(sync.LocalPath, "no TS tables here");   // export's row read will throw
        sync.RecordEdit(TsTable.ExposurePlan, "30", "desired", 30L, "20", "M 42 · Ha");

        await vm.PushAsync();

        Assert.Contains("pushed 1 field(s)", vm.StatusText);         // the push outcome is preserved…
        Assert.Contains("CATALOG EXPORT FAILED", vm.StatusText);     // …and the fault is loud, not silent
        Assert.DoesNotContain("PUSH FAILED", vm.StatusText);
        Assert.False(sync.IsDirty);                                  // journal never retained for export faults
        Assert.False(Directory.Exists(vm.CatalogInboxDir));          // faulted before any inbox write
    }

    [Fact]
    public async Task OpenTimeDirtyPush_EmitsToo_ThePushFunnelIsShared()
    {
        // Regression, CB-1 (2026-08-16 maintain sweep): the open-with-dirty prompt's Push is a second
        // push-as-replay commit. It used to replay to BIRDWATCHER without exporting, and since the push
        // consumes the journal, the authored intent could never reach Catalog.db by any later push —
        // silently, because the export's own loud-failure path was never entered. Both surfaces now go
        // through PushAndExportAsync.
        (MainViewModel vm, TsSync sync) = PushVm();
        CreateTsDb(sync.LocalPath);
        sync.RecordEdit(TsTable.ExposurePlan, "30", "desired", 30L, "20", "M 42 · Ha");
        vm.OpenWithDirtyPrompt = _ => Task.FromResult(OpenDirtyDecision.Push);

        string note = await vm.PrepareTsForLoadAsync(PullPolicy.IfChanged);

        Assert.Contains("pushed", note);
        Assert.Contains("catalog inbox 2 record(s)", note);
        string file = Assert.Single(Directory.GetFiles(vm.CatalogInboxDir, "*.jsonl"));
        string[] lines = File.ReadAllLines(file);
        Assert.Equal(2, lines.Length);
        Assert.Equal("exposure-plan-upsert", Parse(lines[1]).GetProperty("op").GetString());
        Assert.False(sync.IsDirty);                                  // the journal was consumed by the push
    }

    [Fact]
    public async Task PushAsync_RefusedPush_EmitsNothing()
    {
        (MainViewModel vm, TsSync sync) = PushVm();
        File.Delete(sync.RemotePath);                                // unreachable — push refuses whole
        sync.RecordEdit(TsTable.ExposurePlan, "30", "desired", 30L, "20", "M 42 · Ha");

        await vm.PushAsync();

        Assert.Contains("unreachable", vm.StatusText);
        Assert.False(Directory.Exists(vm.CatalogInboxDir));
    }

    // ---- the observed-emission path (add-target-rename; project rows added by add-inbox-v2-emission) -------

    [Fact]
    public void ObservedInboundGuids_TargetsAndProjects_ExistingRowsOnly_Deduped()
    {
        // The scope filter: plan changes (actuals among them), template changes, and remotely-ADDED
        // rows all stay silent; target AND project field changes collect (v2); two field changes on one
        // row collapse to its one guid.
        TsInboundChange[] arrived =
        [
            new(TsTable.Target, "t-g", "name", "Cygnus Loop P9", "CygnusLoop P9"),
            new(TsTable.Target, "t-g", "active", "1", "0"),
            new(TsTable.Target, "t-new", TsInboundDiff.NewRowColumn, null, "row"),
            new(TsTable.ExposurePlan, "30", "acquired", "35", "40"),
            new(TsTable.Project, "p-g", "minimumaltitude", "0", "30"),
            new(TsTable.Project, "p-new", TsInboundDiff.NewRowColumn, null, "row"),
            new(TsTable.ExposureTemplate, "20", "gain", "100", "139"),
        ];

        ObservedGuids observed = CatalogInboxExporter.ObservedInboundGuids(arrived);
        Assert.Equal(["t-g"], observed.TargetGuids);
        Assert.Equal(["p-g"], observed.ProjectGuids);
    }

    [Fact]
    public void BuildObservedLines_EmitsFullValueUpserts_ProjectBeforeTarget_AtObservationTime()
    {
        IReadOnlyList<string> lines = CatalogInboxExporter.BuildObservedLines(
            new ObservedGuids(["t-g"], ["p-g"]), Rows(), CommitStamp);

        Assert.Equal(2, lines.Count);
        JsonElement p = Parse(lines[0]);                               // references first: project → target
        JsonElement t = Parse(lines[1]);

        Assert.Equal("project-upsert", p.GetProperty("op").GetString());
        Assert.Equal("p-g", p.GetProperty("ts_guid").GetString());
        Assert.Equal(30, p.GetProperty("minimum_altitude_deg").GetDouble());   // the v2 block travels

        Assert.Equal("target-upsert", t.GetProperty("op").GetString());
        Assert.Equal(2, t.GetProperty("v").GetInt32());
        Assert.Equal(1_755_043_200, t.GetProperty("at").GetInt64());   // the observing pull's time
        Assert.Equal("TSM", t.GetProperty("source").GetString());
        Assert.Equal("t-g", t.GetProperty("ts_guid").GetString());
        Assert.Equal("p-g", t.GetProperty("project_ts_guid").GetString());
        Assert.Equal("M 42", t.GetProperty("name").GetString());       // the full committed row, not a delta
        Assert.True(t.GetProperty("enabled").GetBoolean());
        Assert.Equal("J2000", t.GetProperty("epoch").GetString());
    }

    [Theory]
    [InlineData("UPDATE project SET horizonoffset = NULL", "horizonoffset")]
    [InlineData("UPDATE exposureplan SET exposure = NULL", "exposure")]
    [InlineData("UPDATE exposuretemplate SET defaultexposure = NULL", "defaultexposure")]
    public void ReadRows_NullInARequiredColumn_AbortsNamingIt_NeverFabricates(string nullOut, string column)
    {
        // CB-2 (2026-08-16 maintain sweep): these three columns are non-nullable in TS's own schema
        // (`exposure` is [Required]), so the old `?? 0` / `?? -1` defaults could only ever fire on a broken
        // local copy — and would have shipped a fabricated authored value to ISM indistinguishable from a
        // real one (a 0° horizon offset; a 0-second exposure, which is LITERAL in this domain; an
        // inherit-the-template exposure plan). Rule #16: abort naming the row and column.
        string dir = SyncTestEnv.NewDir();
        string db = Path.Combine(dir, "local.sqlite");
        CreateTsDb(db);
        Exec(db, nullOut);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CatalogInboxExporter.ReadRows(db));

        Assert.Contains(column, ex.Message);
        Assert.Contains("refusing to fabricate", ex.Message);
    }

    private static void Exec(string dbPath, string sql)
    {
        using SqliteConnection c = new(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void BuildObservedLines_GuidMissingFromLocalDb_ThrowsLoudly()
    {
        // The pull's diff just observed the row, so the fresh local copy MUST resolve it (rule #16).
        Assert.Throws<InvalidOperationException>(() =>
            CatalogInboxExporter.BuildObservedLines(new ObservedGuids(["no-such"], []), Rows(), CommitStamp));
        Assert.Throws<InvalidOperationException>(() =>
            CatalogInboxExporter.BuildObservedLines(new ObservedGuids([], ["no-such"]), Rows(), CommitStamp));
    }

    [Fact]
    public void ExportObserved_PublishesUpserts_AndNoGuidsWritesNoFile()
    {
        string dir = SyncTestEnv.NewDir();
        string db = Path.Combine(dir, "local.sqlite");
        string inbox = Path.Combine(dir, "inbox");
        CreateTsDb(db);

        Assert.Equal(0, CatalogInboxExporter.ExportObserved(db, new ObservedGuids([], []), CommitStamp, inbox));
        Assert.False(Directory.Exists(inbox));                       // a quiet pull writes nothing at all

        Assert.Equal(2, CatalogInboxExporter.ExportObserved(db, new ObservedGuids(["t-g"], ["p-g"]), CommitStamp, inbox));
        string file = Assert.Single(Directory.GetFiles(inbox, "*.jsonl"));
        Assert.Equal("project-upsert, target-upsert",
            string.Join(", ", File.ReadAllLines(file).Select(l => Parse(l).GetProperty("op").GetString())));
    }

    [Fact]
    public void WriteInbox_TakenStamp_AdvancesToTheNextFreeSecond()
    {
        // A push and its closing pull's observed emission can land in the same second — distinct files,
        // never an overwrite; a crashed .partial blocks its stamp too (kept for diagnosis, never reclaimed).
        string inbox = SyncTestEnv.NewDir();

        string first = CatalogInboxExporter.WriteInbox(["{\"a\":1}"], inbox, CommitStamp);
        string second = CatalogInboxExporter.WriteInbox(["{\"b\":2}"], inbox, CommitStamp);

        Assert.Equal($"tsm-{CommitStamp.AddSeconds(1).ToLocalTime():yyyyMMdd-HHmmss}.jsonl", Path.GetFileName(second));
        Assert.Equal("{\"a\":1}\n", File.ReadAllText(first));
        Assert.Equal("{\"b\":2}\n", File.ReadAllText(second));

        File.WriteAllText(Path.Combine(inbox, $"tsm-{CommitStamp.AddSeconds(2).ToLocalTime():yyyyMMdd-HHmmss}.jsonl.partial"), "x");
        string third = CatalogInboxExporter.WriteInbox(["{\"c\":3}"], inbox, CommitStamp);
        Assert.Equal($"tsm-{CommitStamp.AddSeconds(3).ToLocalTime():yyyyMMdd-HHmmss}.jsonl", Path.GetFileName(third));
    }

    [Fact]
    public void Pull_RecordsObservedInbound_TakeClears_RequeueRestores()
    {
        // The BIRDWATCHER-rename flow at the sync layer: the pull's fresh diff lands in the untaken
        // buffer (target name change observed), take-and-clear consumes it exactly once, and a requeue
        // (emission fault) restores it for the next take. The push's own changes can never appear here —
        // edits are local-first, so a closing pull returns values already identical on both sides.
        string dir = SyncTestEnv.NewDir();
        string remote = Path.Combine(dir, "remote.sqlite");
        string local = Path.Combine(dir, "local.sqlite");
        CreateTsDb(local);                                           // what the user last saw: 'M 42'
        CreateTsDb(remote);
        using (SqliteConnection c = new(new SqliteConnectionStringBuilder
        { DataSource = remote, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            c.Open();
            using SqliteCommand cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE target SET name = 'M 42 LL' WHERE guid = 't-g';";
            cmd.ExecuteNonQuery();
        }
        TsSync sync = new(remote, local,
            _ => throw new InvalidOperationException("no editor expected"),
            _ => throw new InvalidOperationException("no applier expected"));

        sync.Pull(sync.ProbeRemote()!);

        IReadOnlyList<TsInboundChange> taken = sync.TakeUntakenPullInbound();
        TsInboundChange rename = Assert.Single(taken);
        Assert.Equal((TsTable.Target, "t-g", "name", "M 42", "M 42 LL"),
            (rename.Table, rename.Key, rename.Column, rename.Old, rename.New));
        Assert.Equal(["t-g"], CatalogInboxExporter.ObservedInboundGuids(taken).TargetGuids);

        Assert.Empty(sync.TakeUntakenPullInbound());                 // take-and-clear: consumed exactly once

        sync.RequeuePullInbound(taken);                              // emission fault → retry at next take
        Assert.Single(sync.TakeUntakenPullInbound());
    }
}
