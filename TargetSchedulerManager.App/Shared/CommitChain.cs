namespace TargetSchedulerManager.App.Shared;

/// <summary>
/// Serializes async commits issued from one editing surface: each <see cref="Run"/> starts only after
/// every earlier one from the same chain has completed, so two rapid confirmations can never interleave
/// their write + read-back verify or complete out of order (the spurious-revert race, review 2026-07-24).
/// Callers await their OWN task, so per-site success/revert handling is unchanged; continuations resume
/// on the dispatcher in chain order, so last-known bookkeeping lands in confirmation order too.
/// UI-thread confined by contract — the tail swap needs no locking.
/// </summary>
internal sealed class CommitChain
{
    private Task _tail = Task.CompletedTask;

    /// <summary>Queues one commit behind everything already queued; returns that commit's own result.</summary>
    public Task<bool> Run(Func<Task<bool>> commit)
    {
        Task<bool> next = RunAfter(_tail, commit);
        _tail = next;
        return next;
    }

    private static async Task<bool> RunAfter(Task prev, Func<Task<bool>> commit)
    {
        // An earlier commit's fault must not poison later ones — each task carries only its own outcome.
        try { await prev; } catch { }
        return await commit();
    }
}
