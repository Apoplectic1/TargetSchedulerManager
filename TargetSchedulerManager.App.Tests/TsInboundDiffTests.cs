using Astronomy.Catalog.TargetScheduler;
using Microsoft.Data.Sqlite;
using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The pull-time inbound differ behind the ← marks: real TS-schema SQLite dbs pulled through TsSync (the
// online-backup path), plus direct Snapshot/Diff units for the schema-drift cases. The store's union, the
// first-pull skip, and the write-back actuals mask are the spec's lifecycle scenarios.
public class TsInboundDiffTests
{
    // ---- pull-integrated scenarios ------------------------------------------------------------------------

    [Fact]
    public void FirstPull_NoLocal_RecordsNothing()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        CreateTsDb(sync.RemotePath);
        SetPlan(sync.RemotePath, id: 42, desired: 20);

        sync.Pull(sync.ProbeRemote()!);

        Assert.True(sync.Inbound.IsEmpty);   // nothing was previously seen to diff against
    }

    [Fact]
    public void PullDiff_FieldChange_RecordsOldAndNew()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        CreateTsDb(sync.RemotePath);
        SetPlan(sync.RemotePath, id: 42, desired: 20);
        sync.Pull(sync.ProbeRemote()!);                       // baseline the local copy

        Exec(sync.RemotePath, "UPDATE exposureplan SET desired = 30 WHERE Id = 42;");
        sync.Pull(sync.ProbeRemote()!);

        TsInboundChange change = Assert.Single(sync.Inbound.Snapshot());
        Assert.Equal(TsTable.ExposurePlan, change.Table);
        Assert.Equal("42", change.Key);
        Assert.Equal("desired", change.Column);
        Assert.Equal("20", change.Old);
        Assert.Equal("30", change.New);
    }

    [Fact]
    public void PullDiff_ProjectChange_IsKeyedByGuid_NotId()
    {
        // The key space must match TargetResolver.Provenance (the TS guid), which is what the flyout and the
        // journal look marks up by. Keying projects by Id here made project ← marks silently never resolve.
        TsSync sync = SyncTestEnv.NewSync(out _);
        CreateTsDb(sync.RemotePath);
        SetProject(sync.RemotePath, id: 7, guid: "guid-P", minimumAltitude: 30);
        sync.Pull(sync.ProbeRemote()!);

        Exec(sync.RemotePath, "UPDATE project SET minimumaltitude = 45 WHERE Id = 7;");
        sync.Pull(sync.ProbeRemote()!);

        TsInboundChange change = Assert.Single(sync.Inbound.Snapshot());
        Assert.Equal(TsTable.Project, change.Table);
        Assert.Equal("guid-P", change.Key);          // the guid — never "7"
        Assert.Equal("minimumaltitude", change.Column);
    }

    [Fact]
    public void IdenticalPull_RecordsNothing()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        CreateTsDb(sync.RemotePath);
        SetPlan(sync.RemotePath, id: 42, desired: 20);
        sync.Pull(sync.ProbeRemote()!);

        sync.Pull(sync.ProbeRemote()!);                       // remote unchanged — a forced re-pull

        Assert.True(sync.Inbound.IsEmpty);
    }

    [Fact]
    public void UnionAcrossPulls_KeepsEarlierFields_LatestObservationWinsPerField()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        CreateTsDb(sync.RemotePath);
        SetPlan(sync.RemotePath, id: 42, desired: 20);
        SetTarget(sync.RemotePath, guid: "t-1", name: "M 81", active: 1);
        sync.Pull(sync.ProbeRemote()!);

        Exec(sync.RemotePath, "UPDATE exposureplan SET desired = 30 WHERE Id = 42;");
        Exec(sync.RemotePath, "UPDATE target SET name = 'M 81 wide' WHERE guid = 't-1';");
        sync.Pull(sync.ProbeRemote()!);                       // the open's diff: 2 fields

        Exec(sync.RemotePath, "UPDATE exposureplan SET desired = 40 WHERE Id = 42;");
        sync.Pull(sync.ProbeRemote()!);                       // a later Pull-now: desired moved again

        IReadOnlyList<TsInboundChange> all = sync.Inbound.Snapshot();
        Assert.Equal(2, all.Count);                           // earlier name entry still standing (union)
        TsInboundChange name = Assert.Single(all, c => c.Column == "name");
        Assert.Equal("M 81", name.Old);
        Assert.Equal("M 81 wide", name.New);
        TsInboundChange desired = Assert.Single(all, c => c.Column == "desired");
        Assert.Equal("30", desired.Old);                      // latest observation wins per field
        Assert.Equal("40", desired.New);
    }

    [Fact]
    public void AddedRow_RecordsOneNewRowEntry_DeletedRow_RecordsNothing()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        CreateTsDb(sync.RemotePath);
        SetPlan(sync.RemotePath, id: 42, desired: 20);
        sync.Pull(sync.ProbeRemote()!);

        Exec(sync.RemotePath, "DELETE FROM exposureplan WHERE Id = 42;");
        SetPlan(sync.RemotePath, id: 43, desired: 5);
        sync.Pull(sync.ProbeRemote()!);

        TsInboundChange change = Assert.Single(sync.Inbound.Snapshot());   // the deletion reported nothing
        Assert.Equal("43", change.Key);
        Assert.Equal(TsInboundDiff.NewRowColumn, change.Column);
    }

    [Fact]
    public void PullDiff_TemplateFieldChange_RecordsOldAndNew()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        CreateTsDb(sync.RemotePath);
        SetTemplate(sync.RemotePath, id: 5, name: "H900", moonAvoidance: 0);
        sync.Pull(sync.ProbeRemote()!);                       // baseline the local copy

        Exec(sync.RemotePath, "UPDATE exposuretemplate SET moonavoidanceenabled = 1 WHERE Id = 5;");
        sync.Pull(sync.ProbeRemote()!);

        TsInboundChange change = Assert.Single(sync.Inbound.Snapshot());
        Assert.Equal(TsTable.ExposureTemplate, change.Table);
        Assert.Equal("5", change.Key);                        // Id-string key space — matches the journal's
        Assert.Equal("moonavoidanceenabled", change.Column);
        Assert.Equal("0", change.Old);
        Assert.Equal("1", change.New);
    }

    [Fact]
    public void UntouchedTemplate_RecordsNothing()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        CreateTsDb(sync.RemotePath);
        SetTemplate(sync.RemotePath, id: 5, name: "H900", moonAvoidance: 1);
        sync.Pull(sync.ProbeRemote()!);

        sync.Pull(sync.ProbeRemote()!);                       // remote unchanged — a forced re-pull

        Assert.True(sync.Inbound.IsEmpty);
    }

    // ---- direct Snapshot/Diff units -----------------------------------------------------------------------

    [Fact]
    public void MissingColumn_IsSkipped_OtherColumnsStillDiff()
    {
        string dir = SyncTestEnv.NewDir();
        string oldDb = Path.Combine(dir, "old.sqlite");
        string newDb = Path.Combine(dir, "new.sqlite");
        // The old local predates the rotation column (TS schema drift); name changed remotely.
        Exec(oldDb, "CREATE TABLE target (Id INTEGER PRIMARY KEY, name TEXT, active INTEGER, ra REAL, dec REAL, priority INTEGER, guid TEXT);"
            + "INSERT INTO target (Id, name, active, guid) VALUES (1, 'M 81', 1, 't-1');");
        CreateTsDb(newDb);
        SetTarget(newDb, guid: "t-1", name: "M 82", active: 1, rotation: 45.0);

        List<TsInboundChange> changes = TsInboundDiff.Diff(
            TsInboundDiff.Snapshot(oldDb), TsInboundDiff.Snapshot(newDb));

        Assert.DoesNotContain(changes, c => c.Column == "rotation");   // absent before → skipped, no error
        TsInboundChange name = Assert.Single(changes, c => c.Column == "name");
        Assert.Equal("M 81", name.Old);
        Assert.Equal("M 82", name.New);
    }

    [Fact]
    public void MarkerDb_WithoutTsTables_SnapshotsEmpty()
    {
        string path = Path.Combine(SyncTestEnv.NewDir(), "marker.sqlite");
        SyncTestEnv.CreateDb(path, "not-a-ts-db");

        Assert.Empty(TsInboundDiff.Snapshot(path));   // observation only — no tables, no throw
    }

    // ---- the write-back actuals mask ----------------------------------------------------------------------

    [Fact]
    public void WriteBackStamp_MasksAcquiredAndAccepted_KeepsDesired()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        sync.Inbound.Apply(
        [
            new TsInboundChange(TsTable.ExposurePlan, "42", "acquired", "10", "14"),
            new TsInboundChange(TsTable.ExposurePlan, "42", "accepted", "10", "14"),
            new TsInboundChange(TsTable.ExposurePlan, "42", "desired", "20", "30"),
            new TsInboundChange(TsTable.ExposurePlan, "77", "acquired", "3", "4"),
        ]);

        sync.RecordWriteBack("42", "acquired", 13, "14", "A · H @300s");

        IReadOnlyList<TsInboundChange> all = sync.Inbound.Snapshot();
        Assert.DoesNotContain(all, c => c.Key == "42" && c.Column is "acquired" or "accepted");
        Assert.Contains(all, c => c.Key == "42" && c.Column == "desired");    // a rig-side goal change survives
        Assert.Contains(all, c => c.Key == "77" && c.Column == "acquired");   // other plans untouched
    }

    [Fact]
    public void WriteBackDesiredRatchet_DoesNotMask()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        sync.Inbound.Apply([new TsInboundChange(TsTable.ExposurePlan, "42", "acquired", "10", "14")]);

        sync.RecordWriteBack("42", "desired", 14, "10", "A · H @300s");   // desired-only raise

        Assert.Contains(sync.Inbound.Snapshot(), c => c.Key == "42" && c.Column == "acquired");
    }

    // ---- helpers ------------------------------------------------------------------------------------------

    /// <summary>A real db carrying the four diffed TS tables (empty). The template table is a representative
    /// subset of the editable columns — absent ones exercise the drift-skip path.</summary>
    private static void CreateTsDb(string path) => Exec(path,
        "CREATE TABLE IF NOT EXISTS target (Id INTEGER PRIMARY KEY, name TEXT, active INTEGER, ra REAL, dec REAL, rotation REAL, priority INTEGER, guid TEXT);"
        + "CREATE TABLE IF NOT EXISTS exposureplan (Id INTEGER PRIMARY KEY, exposure REAL, desired INTEGER, acquired INTEGER, accepted INTEGER, exposureTemplateId INTEGER, enabled INTEGER);"
        + "CREATE TABLE IF NOT EXISTS project (Id INTEGER PRIMARY KEY, state INTEGER, priority INTEGER, minimumtime INTEGER, minimumaltitude REAL, maximumaltitude REAL,"
        + " usecustomhorizon INTEGER, horizonoffset REAL, meridianwindow INTEGER, ditherevery INTEGER, enablegrader INTEGER, smartexposureorder INTEGER,"
        + " flatshandling INTEGER, filterswitchfrequency INTEGER, guid TEXT);"
        + "CREATE TABLE IF NOT EXISTS exposuretemplate (Id INTEGER PRIMARY KEY, name TEXT, filtername TEXT, gain INTEGER, offset INTEGER, bin INTEGER,"
        + " defaultexposure REAL, moonavoidanceenabled INTEGER, moonavoidanceseparation REAL);");

    private static void SetTemplate(string path, long id, string name, int moonAvoidance) => Exec(path,
        $"INSERT INTO exposuretemplate (Id, name, filtername, gain, offset, bin, defaultexposure, moonavoidanceenabled, moonavoidanceseparation)"
        + $" VALUES ({id}, '{name}', 'H', 111, 10, 1, 900.0, {moonAvoidance}, 60.0)"
        + $" ON CONFLICT(Id) DO UPDATE SET moonavoidanceenabled = {moonAvoidance};");

    private static void SetPlan(string path, long id, int desired) => Exec(path,
        $"INSERT INTO exposureplan (Id, exposure, desired, acquired, accepted, exposureTemplateId, enabled)"
        + $" VALUES ({id}, 300.0, {desired}, 0, 0, 1, 1)"
        + $" ON CONFLICT(Id) DO UPDATE SET desired = {desired};");

    private static void SetProject(string path, long id, string guid, int minimumAltitude) => Exec(path,
        $"INSERT INTO project (Id, state, priority, minimumtime, minimumaltitude, maximumaltitude, usecustomhorizon,"
        + $" horizonoffset, meridianwindow, ditherevery, enablegrader, smartexposureorder, flatshandling, filterswitchfrequency, guid)"
        + $" VALUES ({id}, 1, 1, 30, {minimumAltitude}, 90, 0, 0, 0, 0, 0, 0, 0, 1, '{guid}')"
        + $" ON CONFLICT(Id) DO UPDATE SET minimumaltitude = {minimumAltitude};");

    private static void SetTarget(string path, string guid, string name, int active, double? rotation = null) => Exec(path,
        $"INSERT INTO target (Id, name, active, ra, dec, rotation, priority, guid)"
        + $" VALUES ((SELECT IFNULL(MAX(Id), 0) + 1 FROM target), '{name}', {active}, 9.9, 69.1, {(rotation is double r ? r.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL")}, 0, '{guid}')"
        + $" ON CONFLICT DO NOTHING;");

    private static void Exec(string path, string sql)
    {
        using SqliteConnection c = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
