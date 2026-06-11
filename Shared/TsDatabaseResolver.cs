namespace TargetCatalogManager;

/// <summary>Which TS database a run targets: the live BIRDWATCHER db (over SMB) when reachable, else the local
/// working copy. <see cref="IsLive"/> drives the loud LIVE/LOCAL indicator so an edit's blast radius is never a
/// surprise; <see cref="Reason"/> is a short human note for logs/status.</summary>
internal sealed record TsDatabaseChoice(string Path, bool IsLive, string Reason);

/// <summary>
/// Resolves the TS database path: the live imaging-PC db (BIRDWATCHER) when it is network-reachable, otherwise the
/// local working copy. The reachability probe runs under a hard timeout because <see cref="File.Exists"/> on a UNC
/// for a <b>down</b> host blocks ~20 s on SMB name resolution — startup must fall back fast, not hang. Machine-path
/// policy, so it lives in <c>Shared\</c> (compiled into both heads) — never the consumer-neutral library.
/// </summary>
internal static class TsDatabaseResolver
{
    /// <summary>Probe budget: long enough for a live SMB stat, short enough not to stall startup when the host is off.</summary>
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>Resolves using the dev defaults — live = the BIRDWATCHER UNC, local = the working copy.</summary>
    public static TsDatabaseChoice Resolve() =>
        Resolve(DevDefaults.TsDatabaseLive, DevDefaults.TsDatabase, DefaultProbeTimeout);

    /// <summary>
    /// Returns <paramref name="networkPath"/> (live) when it is reachable within <paramref name="timeout"/>,
    /// otherwise <paramref name="localPath"/>. "Reachable" = <see cref="File.Exists"/> returns true before the
    /// timeout; a down host (whose <c>File.Exists</c> would block far longer) is treated as unreachable. Never
    /// throws — any probe fault resolves to local.
    /// </summary>
    public static TsDatabaseChoice Resolve(string networkPath, string localPath, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(networkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

        try
        {
            // Run the (possibly-blocking) UNC probe on a worker and abandon it if it overruns the budget. A
            // leaked File.Exists on a thread-pool thread completes harmlessly later; we just don't wait for it.
            Task<bool> probe = Task.Run(() => File.Exists(networkPath));
            if (probe.Wait(timeout) && probe.Result)
                return new TsDatabaseChoice(networkPath, IsLive: true, "live db reachable");

            return new TsDatabaseChoice(localPath, IsLive: false,
                probe.IsCompleted ? "live db not found — using local copy" : "live db probe timed out — using local copy");
        }
        catch
        {
            return new TsDatabaseChoice(localPath, IsLive: false, "live db probe failed — using local copy");
        }
    }
}
