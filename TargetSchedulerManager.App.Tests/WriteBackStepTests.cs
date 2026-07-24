using System.Globalization;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The post-load write-back pass: real WriteBackPlanner over a hand-built graph (so the app step demonstrably
// mirrors the planner contract), stub applier as the local db, and the sync journal as the observable output.
public class WriteBackStepTests
{
    [Fact]
    public void Drift_StampsLocally_JournalsChangedColumns_MakesDirty()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        StubWriteBackApplier applier = new();
        applier.Rows[500] = (Acquired: 40, Accepted: 40, Desired: 34);
        TsSync sync = NewSync(applier);

        WriteBackStepResult result = WriteBackStep.Run(
            Graph([Both(t, "M 81")], [Plan(t, tpl, tsId: 500, desired: 34, acquired: 40, accepted: 40)],
                  [Tpl(tpl, "Ha", "Ha")], [Inv(t, "Ha", FilterPurpose.Light, 42)]),
            Report(), sync);

        Assert.Null(result.Refusal);
        Assert.Equal(1, result.PlansStamped);
        Assert.Equal((42, 42, 42), applier.Rows[500]);          // acquired = accepted = disk; desired ratcheted
        Assert.Equal(3, result.FieldsJournaled);
        Assert.True(sync.IsDirty);

        Assert.All(sync.Journal.Entries, e => Assert.Equal(TsEditKind.WriteBack, e.Kind));
        Assert.All(sync.Journal.Entries, e => Assert.Equal("500", e.Key));
        Assert.Equal(42L, sync.Journal.Entries.Single(e => e.Column == "acquired").Value);
        Assert.Equal("40", sync.Journal.Entries.Single(e => e.Column == "acquired").Old);
        Assert.Equal(42L, sync.Journal.Entries.Single(e => e.Column == "desired").Value);
        Assert.Contains("M 81 · Ha", sync.Journal.Entries[0].Label);
    }

    [Fact]
    public void CleanSystem_NoWrites_NoJournal_StaysSkippable()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        StubWriteBackApplier applier = new();
        applier.Rows[500] = (Acquired: 42, Accepted: 42, Desired: 50);
        TsSync sync = NewSync(applier);

        WriteBackStepResult result = WriteBackStep.Run(
            Graph([Both(t, "M 81")], [Plan(t, tpl, tsId: 500, desired: 50, acquired: 42, accepted: 42)],
                  [Tpl(tpl, "Ha", "Ha")], [Inv(t, "Ha", FilterPurpose.Light, 42)]),
            Report(), sync);

        Assert.Equal(0, result.PlansStamped);
        Assert.Equal(0, applier.ApplyCalls);                    // no-ops produce no write at all
        Assert.False(sync.IsDirty);                             // …and no journal entries — relaunch stays clean
    }

    [Fact]
    public void OneSidedTargets_CleanPlansAreNoOps_DivergedCountersHealToZero()
    {
        // Disk truth covers absence: a planned-only target's plans stamp against an empty disk. A clean 0/0
        // plan diffs to a no-op (session stays clean); a diverged counter (accepted != acquired) heals to 0.
        Guid planned = Guid.NewGuid(), actual = Guid.NewGuid(), tpl = Guid.NewGuid();
        StubWriteBackApplier applier = new();
        applier.Rows[9] = (Acquired: 0, Accepted: 0, Desired: 50);        // clean not-yet-shot plan
        applier.Rows[10] = (Acquired: 0, Accepted: 64, Desired: 64);      // hand-edit slip
        TsSync sync = NewSync(applier);

        WriteBackStepResult result = WriteBackStep.Run(
            Graph(
                [Planned(planned, "Future"), Actual(actual, "Shot Only")],
                [Plan(planned, tpl, tsId: 9, desired: 50),
                 Plan(planned, tpl, tsId: 10, desired: 64, accepted: 64, seconds: 600.0)],
                [Tpl(tpl, "Ha", "Ha")],
                [Inv(actual, "Ha", FilterPurpose.Light, 50)]),
            Report(plannedOnly: 1, actualOnly: 1), sync);

        Assert.Equal(1, result.PlansStamped);                             // only the diverged plan
        Assert.Equal((0, 0, 64), applier.Rows[10]);                       // accepted re-coupled to truth (0)
        Assert.Equal((0, 0, 50), applier.Rows[9]);                        // untouched
        Assert.True(sync.IsDirty);                                        // the heal rides the journal/push
    }

    [Fact]
    public void EmptyDiskBucket_StampsZero_ReviewFlagsTheDecrease()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        StubWriteBackApplier applier = new();
        applier.Rows[7] = (Acquired: 31, Accepted: 31, Desired: 31);
        TsSync sync = NewSync(applier);

        WriteBackStep.Run(
            Graph([Both(t, "M 82")], [Plan(t, tpl, tsId: 7, desired: 31, acquired: 31, accepted: 31)],
                  [Tpl(tpl, "O3", "O3", defExp: 600.0)], []),   // no frames at the plan's duration
            Report(), sync);

        Assert.Equal((0, 0, 31), applier.Rows[7]);              // disk is truth — counts to 0, desired kept
        PushReviewCountLine line = Assert.Single(sync.PreparePush(null).WriteBack);
        Assert.True(line.IsDecrease);                           // the dangerous half, flagged for review
        Assert.Equal(0, line.NewCount);
        Assert.Equal("31", line.OldCount);
    }

    [Fact]
    public void LocalDbRefusal_IsLoud_NothingJournaled()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        StubWriteBackApplier applier = new() { Sidecar = true };
        TsSync sync = NewSync(applier);

        WriteBackStepResult result = WriteBackStep.Run(
            Graph([Both(t, "M 81")], [Plan(t, tpl, tsId: 500, desired: 34)],
                  [Tpl(tpl, "Ha", "Ha")], [Inv(t, "Ha", FilterPurpose.Light, 42)]),
            Report(), sync);

        Assert.NotNull(result.Refusal);
        Assert.Equal(0, applier.ApplyCalls);
        Assert.False(sync.IsDirty);
    }

    // ---- builders (library-test idiom, trimmed to what these cases need) ----------------------------------

    private static TsSync NewSync(StubWriteBackApplier applier)
    {
        string dir = SyncTestEnv.NewDir();
        return new TsSync(
            Path.Combine(dir, "remote.sqlite"), Path.Combine(dir, "local.sqlite"),
            _ => throw new InvalidOperationException("no field editor in write-back tests"),
            _ => applier);
    }

    private static CatalogGraph Graph(
        IReadOnlyList<Target> targets, IReadOnlyList<ExposurePlan> plans,
        IReadOnlyList<ExposureTemplate> templates, IReadOnlyList<InventoryFilter> inventory) =>
        new([], [], templates, targets, plans, inventory);

    private static Target Both(Guid id, string name) => new(
        id, TargetSource.Both, ProjectId: null, name, Enabled: true, RaHours: null, DecDegreesSigned: null,
        Epoch.J2000, RotationDeg: null, RoiPercent: null, Priority: null, DirectoryName: name, Catalog: null,
        CommonName: null, ObjectName: null, ScannedAt: 0, CreatedAt: 0, ImportedFromTsGuid: null);

    private static Target Planned(Guid id, string name) => new(
        id, TargetSource.Planned, ProjectId: null, name, Enabled: true, RaHours: null, DecDegreesSigned: null,
        Epoch.J2000, RotationDeg: null, RoiPercent: null, Priority: null, DirectoryName: null, Catalog: null,
        CommonName: null, ObjectName: null, ScannedAt: null, CreatedAt: 0, ImportedFromTsGuid: "guid");

    private static Target Actual(Guid id, string name) => new(
        id, TargetSource.Actual, ProjectId: null, name, Enabled: true, RaHours: null, DecDegreesSigned: null,
        Epoch.J2000, RotationDeg: null, RoiPercent: null, Priority: null, DirectoryName: name, Catalog: null,
        CommonName: null, ObjectName: null, ScannedAt: 0, CreatedAt: 0, ImportedFromTsGuid: null);

    private static ExposureTemplate Tpl(Guid id, string name, string filter, double? defExp = 300.0) =>
        new(id, Guid.NewGuid(), name, filter, Gain: null, OffsetAdu: null, Binning: null, ReadoutMode: null,
            DefaultExposureSeconds: defExp, ImportedFromTsGuid: null);

    private static ExposurePlan Plan(
        Guid target, Guid template, long tsId, int desired = 0, int acquired = 0, int accepted = 0,
        double? seconds = null) =>
        new(Guid.NewGuid(), target, template, ExposureSeconds: seconds, desired, acquired, accepted,
            Enabled: true, ImportedFromTsGuid: tsId.ToString(CultureInfo.InvariantCulture));

    private static InventoryFilter Inv(
        Guid target, string filter, FilterPurpose purpose, int count, double seconds = 300.0) =>
        new(target, filter, purpose, filter, count, count * seconds, FirstImagedAt: 0, LastImagedAt: 0,
            TypicalGain: 100, TypicalOffset: 50, TypicalSetTempC: -10.0, TypicalBinningX: 1, TypicalBinningY: 1,
            ExposureSeconds: seconds, Cameras: "Z533");

    private static CatalogBuildReport Report(int plannedOnly = 0, int actualOnly = 0) => new(
        DiskTargetCount: 0, TsTargetCount: 0, BothCount: 0,
        PlannedOnlyCount: plannedOnly, ActualOnlyCount: actualOnly,
        NameMismatches: [], AmbiguousMatches: [], DuplicateTsTargets: [],
        UnanchoredTsTargets: [], InvalidTsTargets: []);
}
