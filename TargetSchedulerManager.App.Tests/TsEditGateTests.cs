using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The guarded write in isolation: a stub ITsEditor (no SQLite) + a TsSource with an injected probe.
public class TsEditGateTests
{
    private sealed class StubEditor : ITsEditor
    {
        public (FieldEditResult? Result, RefusalReason Refusal) Next = (null, RefusalReason.None);
        public bool Throw;
        public (FieldEditResult? Result, RefusalReason Refusal) TrySetField(
            TsTable table, string tsKey, string column, object? value) =>
            Throw ? throw new InvalidOperationException("boom") : Next;
        public void Dispose() { }
    }

    private static TsSource Live() { TsSource s = new("LIVE", "LOCAL", () => true); s.ResolvePathForLoad(); return s; }

    [Fact]
    public async Task CleanWrite_ReturnsApplied()
    {
        StubEditor ed = new() { Next = (new FieldEditResult(RowFound: true, OldValue: "5", Verified: true), RefusalReason.None) };
        TsEditGate gate = new(Live(), _ => ed);
        EditOutcome o = await gate.ApplyAsync(TsTable.ExposurePlan, "ep-1", "desired", 10, "A · H");
        EditOutcome.Applied a = Assert.IsType<EditOutcome.Applied>(o);
        Assert.Equal("5", a.Old);
        Assert.Equal(10, a.New);
    }

    [Fact]
    public async Task RefusedWrite_PassesTheReasonThrough()
    {
        StubEditor ed = new() { Next = (null, RefusalReason.OpenSidecar) };
        TsEditGate gate = new(Live(), _ => ed);
        EditOutcome o = await gate.ApplyAsync(TsTable.ExposurePlan, "ep-1", "desired", 10, "A · H");
        Assert.Equal(RefusalReason.OpenSidecar, Assert.IsType<EditOutcome.Refused>(o).Reason);
    }

    [Fact]
    public async Task VerifyFails_ReturnsFailed()
    {
        StubEditor ed = new() { Next = (new FieldEditResult(RowFound: true, OldValue: "5", Verified: false), RefusalReason.None) };
        TsEditGate gate = new(Live(), _ => ed);
        EditOutcome.Failed f = Assert.IsType<EditOutcome.Failed>(
            await gate.ApplyAsync(TsTable.ExposurePlan, "ep-1", "desired", 10, "A · H"));
        Assert.True(f.Found);
        Assert.False(f.Verified);
    }

    [Fact]
    public async Task EditorThrows_LiveNowUnreachable_ReturnsLiveDropped_AndSourceFalls()
    {
        bool reachable = true;
        TsSource src = new("LIVE", "LOCAL", () => reachable);
        src.ResolvePathForLoad();                       // → Live
        reachable = false;                              // BIRDWATCHER drops mid-write
        StubEditor ed = new() { Throw = true };
        TsEditGate gate = new(src, _ => ed);
        EditOutcome o = await gate.ApplyAsync(TsTable.Target, "g-1", "active", 1, "A");
        Assert.IsType<EditOutcome.LiveDropped>(o);
        Assert.False(src.IsLive);                       // sticky-fell to LOCAL
    }

    [Fact]
    public async Task EditorThrows_NotLive_ReturnsFailed()
    {
        TsSource src = new("LIVE", "LOCAL", () => false);
        src.ResolvePathForLoad();                       // → Local
        StubEditor ed = new() { Throw = true };
        TsEditGate gate = new(src, _ => ed);
        Assert.IsType<EditOutcome.Failed>(await gate.ApplyAsync(TsTable.Target, "g-1", "active", 1, "A"));
    }
}
