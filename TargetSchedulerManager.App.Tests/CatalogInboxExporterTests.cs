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

    private static InboxProjectRow Project() => new(1, "p-g", "Orion Group", State: 1, Priority: 2);

    private static InboxTargetRow Target() => new(
        10, "t-g", ProjectId: 1, "M 42", Enabled: true,
        RaHours: 5.588, DecDegrees: -5.39, EpochCode: 2, RotationDeg: null);

    private static InboxPlanRow Plan() => new(
        30, "pl-g", TargetId: 10, TemplateId: 20, ExposureSeconds: 300, DesiredCount: 30, Enabled: true);

    private static InboxTemplateRow Template() => new(
        20, "tpl-g", "Ha 300", "Ha", Gain: 100, Offset: 30, Bin: 1, ReadoutMode: 0,
        DefaultExposureSeconds: 300, TwilightLevel: 1, MoonAvoidanceEnabled: true,
        MoonAvoidanceSeparationDeg: 60, MoonAvoidanceWidthDays: 7);

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
            CREATE TABLE project (Id INTEGER PRIMARY KEY, guid TEXT, name TEXT, state INTEGER, priority INTEGER);
            CREATE TABLE target (Id INTEGER PRIMARY KEY, guid TEXT, projectid INTEGER, name TEXT, active INTEGER,
                ra REAL, dec REAL, epochcode INTEGER, rotation REAL);
            CREATE TABLE exposureplan (Id INTEGER PRIMARY KEY, guid TEXT, targetid INTEGER,
                exposureTemplateId INTEGER, exposure REAL, desired INTEGER, enabled INTEGER);
            CREATE TABLE exposuretemplate (Id INTEGER PRIMARY KEY, guid TEXT, name TEXT, filtername TEXT,
                gain INTEGER, offset INTEGER, bin INTEGER, readoutmode INTEGER, defaultexposure REAL,
                twilightlevel INTEGER, moonavoidanceenabled INTEGER, moonavoidanceseparation REAL,
                moonavoidancewidth INTEGER);
            INSERT INTO project VALUES (1, 'p-g', 'Orion Group', 1, 2);
            INSERT INTO target VALUES (10, 't-g', 1, 'M 42', 1, 5.588, -5.39, 2, NULL);
            INSERT INTO exposureplan VALUES (30, 'pl-g', 10, 20, 300, 30, 1);
            INSERT INTO exposuretemplate VALUES (20, 'tpl-g', 'Ha 300', 'Ha', 100, 30, 1, -1, 300, 1, 1, 60, 7);
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
            Assert.Equal(1, record.GetProperty("v").GetInt32());
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
    public void ProjectEdit_EmitsProjectUpsert_WithContractVocabulary()
    {
        IReadOnlyList<string> lines = CatalogInboxExporter.BuildLines(
            [Entry(TsEditKind.Manual, TsTable.Project, "p-g", "state", 1L, "0")], Rows(), CommitStamp);

        JsonElement project = Parse(Assert.Single(lines));
        Assert.Equal("project-upsert", project.GetProperty("op").GetString());
        Assert.Equal("p-g", project.GetProperty("ts_guid").GetString());
        Assert.Equal("active", project.GetProperty("state").GetString());
        Assert.Equal("high", project.GetProperty("priority").GetString());
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
    public async Task PushAsync_RefusedPush_EmitsNothing()
    {
        (MainViewModel vm, TsSync sync) = PushVm();
        File.Delete(sync.RemotePath);                                // unreachable — push refuses whole
        sync.RecordEdit(TsTable.ExposurePlan, "30", "desired", 30L, "20", "M 42 · Ha");

        await vm.PushAsync();

        Assert.Contains("unreachable", vm.StatusText);
        Assert.False(Directory.Exists(vm.CatalogInboxDir));
    }
}
