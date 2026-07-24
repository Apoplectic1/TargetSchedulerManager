namespace TargetSchedulerManager;

/// <summary>The remote TS db's identity at one probe: main-file size + last-write time (both the remote
/// machine's own values) and whether a <c>-wal</c>/<c>-shm</c>/<c>-journal</c> sidecar exists beside it
/// (content ambiguous under WAL — a checkpoint can change pages without touching the main file's mtime).</summary>
public sealed record TsDbStat(long Length, DateTime LastWriteUtc, bool HasSidecar);

/// <summary>
/// Probes the live BIRDWATCHER TS db over SMB: one stat (size + mtime + sidecar presence) under a hard timeout,
/// because file-system calls on a UNC for a <b>down</b> host block ~20 s on SMB name resolution — callers must
/// fall back fast, not hang. The sync layer uses the stat to decide pull-vs-skip against its recorded baseline;
/// <c>null</c> means unreachable (offline session). Machine/network policy, so it lives in the App's
/// <c>Shared\</c> folder — never the consumer-neutral library.
/// </summary>
internal static class TsDatabaseResolver
{
    /// <summary>Probe budget: long enough for a live SMB stat, short enough not to stall startup when the host is off.</summary>
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Stats <paramref name="dbPath"/> under a hard <paramref name="timeout"/>, blocking the caller for at
    /// most that long. The (possibly-blocking) file-system calls run on a worker we abandon if it overruns —
    /// a leaked probe completes harmlessly later. Never throws; <c>null</c> when the file is missing, the
    /// host is down, or the probe timed out. For async callers use <see cref="StatAsync"/> — this sync form
    /// serves the push replay, which runs wholly on a worker already.
    /// </summary>
    public static TsDbStat? Stat(string dbPath, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(dbPath)) return null;
        try
        {
            Task<TsDbStat?> probe = StartProbe(dbPath);
            return probe.Wait(timeout) ? probe.Result : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The await-friendly probe (review N8): same stat, same abandoned-worker timeout semantics,
    /// but no thread parks for the wait — <see cref="Task.WaitAsync(TimeSpan)"/>'s timeout surfaces as
    /// <c>null</c> like every other unreachable case. Never throws.</summary>
    public static async Task<TsDbStat?> StatAsync(string dbPath, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(dbPath)) return null;
        try
        {
            return await StartProbe(dbPath).WaitAsync(timeout);
        }
        catch
        {
            return null;   // timeout (worker abandoned, completes harmlessly later) or a stat fault
        }
    }

    private static Task<TsDbStat?> StartProbe(string dbPath) => Task.Run(() =>
    {
        FileInfo fi = new(dbPath);
        if (!fi.Exists) return null;
        bool sidecar = File.Exists(dbPath + "-wal")
            || File.Exists(dbPath + "-shm")
            || File.Exists(dbPath + "-journal");
        return (TsDbStat?)new TsDbStat(fi.Length, fi.LastWriteTimeUtc, sidecar);
    });
}
