using Astronomy.Catalog.TargetScheduler;
using Astronomy.Diagnostics;
using Microsoft.Data.Sqlite;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The harden-ts-pull change (2026-07-23 kill-mid-pull incident): the atomic tmp+swap pull, the torn-local
// heal gate, cancellation/progress via the chunked backup, and the pull log lines. Real temp SQLite files
// throughout — the semantics under test are file-level.
public class TsPullHardeningTests
{
    // ---- atomic pull (tasks 1.1–1.2) ----------------------------------------------------------------------

    [Fact]
    public void Pull_Succeeds_AndLeavesNoTmpFiles()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");

        sync.Pull(sync.ProbeRemote()!);

        Assert.Equal("night-1", SyncTestEnv.ReadMarker(sync.LocalPath));
        Assert.False(File.Exists(sync.LocalPath + ".pull-tmp"));
        Assert.False(File.Exists(sync.LocalPath + ".pull-tmp-journal"));
    }

    [Fact]
    public void StaleTmp_FromADeadPull_IsSweptByTheNextPull()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        File.WriteAllText(sync.LocalPath + ".pull-tmp", "half a backup");
        File.WriteAllText(sync.LocalPath + ".pull-tmp-journal", "its hot journal");

        sync.Pull(sync.ProbeRemote()!);

        Assert.Equal("night-1", SyncTestEnv.ReadMarker(sync.LocalPath));
        Assert.False(File.Exists(sync.LocalPath + ".pull-tmp"));
        Assert.False(File.Exists(sync.LocalPath + ".pull-tmp-journal"));
    }

    [Fact]
    public void Pull_SwapSucceeds_DespiteAPooledReaderHandleOnTheLocalDb()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.Pull(sync.ProbeRemote()!);

        // A pooled connection (pooling defaults ON) keeps the OS handle after Close — exactly what the
        // reader/editor leave behind between loads. The swap must clear pools or die on a sharing violation.
        using (SqliteConnection pooled = new(new SqliteConnectionStringBuilder
        {
            DataSource = sync.LocalPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString()))
        {
            pooled.Open();
            using SqliteCommand cmd = pooled.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM marker;";
            cmd.ExecuteScalar();
        }

        SyncTestEnv.CreateDb(sync.RemotePath, "night-2");
        File.SetLastWriteTimeUtc(sync.RemotePath, DateTime.UtcNow.AddMinutes(5));
        sync.Pull(sync.ProbeRemote()!);

        Assert.Equal("night-2", SyncTestEnv.ReadMarker(sync.LocalPath));
    }

    // ---- cancellation + progress (tasks 3.1 / 3.4) --------------------------------------------------------

    [Fact]
    public void CancelledPull_LeavesOldDbAndBaselineUntouched_TmpGone()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.Pull(sync.ProbeRemote()!);
        TsBaseline before = sync.Baseline!;

        SyncTestEnv.CreateDb(sync.RemotePath, "night-2");
        File.SetLastWriteTimeUtc(sync.RemotePath, DateTime.UtcNow.AddMinutes(5));
        using CancellationTokenSource cts = new();
        cts.Cancel();   // deterministic: the loop's token check fires before the first chunk

        Assert.Throws<OperationCanceledException>(() => sync.Pull(sync.ProbeRemote()!, null, cts.Token));

        Assert.Equal("night-1", SyncTestEnv.ReadMarker(sync.LocalPath));   // previous copy intact
        Assert.Same(before, sync.Baseline);                                // no baseline recorded
        Assert.False(File.Exists(sync.LocalPath + ".pull-tmp"));
    }

    [Fact]
    public void CancelledFirstEverPull_CreatesNoLocalDb()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => sync.Pull(sync.ProbeRemote()!, null, cts.Token));

        Assert.False(File.Exists(sync.LocalPath));
        Assert.Null(sync.Baseline);
    }

    [Fact]
    public void Pull_ReportsMonotonicPercents_EndingAt100()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        List<int> percents = [];

        sync.Pull(sync.ProbeRemote()!, new SyncProgress(percents.Add));

        Assert.NotEmpty(percents);
        Assert.Equal(100, percents[^1]);
        Assert.Equal(percents.OrderBy(p => p), percents);   // never goes backwards
    }

    [Fact]
    public void Push_ClosingPullCancelled_StillSuccess_NextOpenPulls()
    {
        RecordingEditor editor = new();
        string dir = SyncTestEnv.NewDir();
        TsSync sync = new(
            Path.Combine(dir, "remote.sqlite"), Path.Combine(dir, "local.sqlite"),
            _ => editor, _ => new StubWriteBackApplier());
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");
        using CancellationTokenSource cts = new();
        cts.Cancel();

        PushResult result = sync.Push(null, cts.Token);

        Assert.Equal(PushOutcome.Success, result.Outcome);   // the replay applied — only the pull stopped
        Assert.Single(editor.Writes);
        Assert.False(sync.IsDirty);
        Assert.False(result.PulledFresh);
        Assert.Null(sync.Baseline);                          // unbaselined → the next open pulls fresh
    }

    // ---- torn-local heal gate (tasks 2.1–2.2) -------------------------------------------------------------

    [Fact]
    public void Heal_HotJournal_DiscardsDbSidecarsBaseline_KeepsEditJournal()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.Pull(sync.ProbeRemote()!);
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");
        File.WriteAllText(sync.LocalPath + "-journal", "hot journal from a killed writer");

        Assert.True(sync.HealTornLocal());

        Assert.False(File.Exists(sync.LocalPath));
        Assert.False(File.Exists(sync.LocalPath + "-journal"));
        Assert.Null(sync.Baseline);                          // matching-baseline skip can't trust torn local
        Assert.True(sync.IsDirty);                           // unpushed edits survive the heal
        Assert.True(File.Exists(sync.LocalPath + ".tsm-edits.jsonl"));

        // Healed ⇒ unbaselined ⇒ the pull the caller must run is unconditional.
        Assert.True(sync.ShouldPull(sync.ProbeRemote()!));
    }

    [Fact]
    public void Heal_WalSidecar_IsAlsoTorn()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.Pull(sync.ProbeRemote()!);
        File.WriteAllText(sync.LocalPath + "-wal", "");

        Assert.True(sync.HealTornLocal());
        Assert.False(File.Exists(sync.LocalPath));
        Assert.False(File.Exists(sync.LocalPath + "-wal"));
    }

    [Fact]
    public void Heal_HealthyLocal_IsANoOp()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.Pull(sync.ProbeRemote()!);
        TsBaseline baseline = sync.Baseline!;

        Assert.False(sync.HealTornLocal());

        Assert.Equal("night-1", SyncTestEnv.ReadMarker(sync.LocalPath));
        Assert.Same(baseline, sync.Baseline);
        Assert.False(sync.ShouldPull(sync.ProbeRemote()!));   // the skip rule still applies
    }

    [Fact]
    public async Task Load_TornLocalWithRemoteOffline_FailsLoudly_WithoutReadingLocal()
    {
        TsSync sync = SyncTestEnv.NewSync(out _);   // remote path never created — unreachable
        SyncTestEnv.CreateDb(sync.LocalPath, "local-work");
        File.WriteAllText(sync.LocalPath + "-journal", "hot");
        MainViewModel vm = new(new TsEditGate(sync, _ => new RecordingEditor()));

        await vm.LoadAsync(PullPolicy.IfChanged);

        Assert.StartsWith("load failed: local TS copy was torn", vm.StatusText);
        Assert.Empty(vm.Rows);                               // nothing was read from the torn file
        Assert.False(File.Exists(sync.LocalPath));           // heal ran; the pull could not
    }

    // ---- discard hardening --------------------------------------------------------------------------------

    [Fact]
    public void Discard_RunsAfterThePull_SoNoInterruptedPullCanStrandLocalValues()
    {
        // Pull-first (openspec truthful-outcome) inverted the old guard: Discard is bookkeeping AFTER
        // the discarding pull landed, so there is no follow-up pull left to die — the swap already
        // physically replaced the discarded values. The one remaining window (crash between the pull and
        // Discard) leaves a dirty journal over the fresh copy: the next open re-prompts instead of ever
        // showing discarded values as clean, journal-less truth.
        TsSync sync = SyncTestEnv.NewSync(out _);
        SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
        sync.RecordEdit(TsTable.ExposurePlan, "ep-1", "desired", 25, "20", "A · H");

        sync.Pull(sync.ProbeRemote()!);                      // the discarding pull lands first

        TsSync crashed = new(sync.RemotePath, sync.LocalPath,
            _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException());
        Assert.True(crashed.IsDirty);                        // crash before Discard ⇒ still dirty ⇒ re-prompt

        sync.Discard();                                      // bookkeeping: journal clears, baseline stays
        Assert.False(sync.IsDirty);
        Assert.NotNull(sync.Baseline);
        Assert.False(sync.ShouldPull(sync.ProbeRemote()!));  // fresh baseline — no forced extra pull either
    }

    // ---- pull log lines (task 4.1) ------------------------------------------------------------------------

    [Fact]
    public void PullFlows_WriteTheirLogLines()
    {
        string logRoot = SyncTestEnv.NewDir();
        Log.Init(new AppLogIdentity("TsmTests", "test.log", "TSM_DIAG", DiagDefault.None, RootOverride: logRoot));
        Directory.CreateDirectory(Path.Combine(logRoot, "Logs"));
        try
        {
            TsSync sync = SyncTestEnv.NewSync(out _);
            SyncTestEnv.CreateDb(sync.RemotePath, "night-1");
            sync.Pull(sync.ProbeRemote()!);                                    // start + completion-with-duration

            using (CancellationTokenSource cts = new())
            {
                cts.Cancel();
                Assert.Throws<OperationCanceledException>(() => sync.Pull(sync.ProbeRemote()!, null, cts.Token));
            }                                                                  // cancelled line

            File.WriteAllText(sync.LocalPath + "-journal", "hot");
            Assert.True(sync.HealTornLocal());                                 // LOCAL TORN line

            string log = File.ReadAllText(Path.Combine(logRoot, "Logs", "test.log"));
            Assert.Contains("PULL starting (", log);
            Assert.Matches(@"PULL .+ in \d+\.\d s", log);
            Assert.Contains("PULL cancelled at 0% — tmp discarded", log);
            Assert.Contains("LOCAL TORN — ", log);
        }
        finally
        {
            // Point the shared static Log away from this test's temp dir for whatever runs next.
            Log.Init(new AppLogIdentity("TsmTests", "test.log", "TSM_DIAG", DiagDefault.None, Enabled: false));
        }
    }

    /// <summary>Synchronous IProgress — xunit has no UI SynchronizationContext, and Progress&lt;T&gt;'s
    /// thread-pool posting would race the assertions.</summary>
    private sealed class SyncProgress(Action<int> onReport) : IProgress<int>
    {
        public void Report(int value) => onReport(value);
    }
}
