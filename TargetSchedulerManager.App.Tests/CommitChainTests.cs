using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The commit serializer (openspec serial-commits): strict confirmation order, no overlap, per-commit
// fault isolation. Plain tasks — no WinUI; the editor wiring is compile-checked + the user pass.
public class CommitChainTests
{
    [Fact]
    public async Task SecondCommit_DoesNotStart_UntilTheFirstCompletes()
    {
        CommitChain chain = new();
        TaskCompletionSource firstGate = new();
        bool secondStarted = false;

        Task<bool> first = chain.Run(async () => { await firstGate.Task; return true; });
        Task<bool> second = chain.Run(() => { secondStarted = true; return Task.FromResult(true); });

        await Task.Delay(50);
        Assert.False(secondStarted);          // held behind the in-flight first commit

        firstGate.SetResult();
        Assert.True(await second);
        Assert.True(secondStarted);
        Assert.True(await first);
    }

    [Fact]
    public async Task Commits_ApplyInSubmissionOrder_EvenWhenTheFirstIsSlow()
    {
        CommitChain chain = new();
        TaskCompletionSource slowGate = new();
        List<int> applied = [];

        Task<bool> a = chain.Run(async () => { await slowGate.Task; applied.Add(1); return true; });
        Task<bool> b = chain.Run(() => { applied.Add(2); return Task.FromResult(true); });
        Task<bool> c = chain.Run(() => { applied.Add(3); return Task.FromResult(true); });

        slowGate.SetResult();
        await Task.WhenAll(a, b, c);
        Assert.Equal([1, 2, 3], applied);     // confirmation order, never completion-race order
    }

    [Fact]
    public async Task ThrowingCommit_FaultsOnlyItself_ChainContinues()
    {
        CommitChain chain = new();

        Task<bool> bad = chain.Run(() => throw new InvalidOperationException("boom"));
        Task<bool> after = chain.Run(() => Task.FromResult(true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => bad);
        Assert.True(await after);             // the fault did not poison the chain
    }

    [Fact]
    public async Task EachCaller_GetsItsOwnResult()
    {
        CommitChain chain = new();
        Task<bool> ok = chain.Run(() => Task.FromResult(true));
        Task<bool> failed = chain.Run(() => Task.FromResult(false));   // a refused/failed write, not a fault
        Task<bool> okAgain = chain.Run(() => Task.FromResult(true));

        Assert.True(await ok);
        Assert.False(await failed);
        Assert.True(await okAgain);
    }
}
