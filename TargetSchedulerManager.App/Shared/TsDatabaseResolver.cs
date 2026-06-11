namespace TargetSchedulerManager;

/// <summary>
/// Probes whether the live BIRDWATCHER TS db is reachable over SMB. The UI uses this to decide whether the LIVE
/// source is selectable (and to sticky-fall to LOCAL when the rig drops); the actual path choice is the user's
/// LIVE/LOCAL radio, not this class. The probe runs under a hard timeout because <see cref="File.Exists"/> on a
/// UNC for a <b>down</b> host blocks ~20 s on SMB name resolution — callers must fall back fast, not hang.
/// Machine-path policy, so it lives in the App's <c>Shared\</c> folder — never the consumer-neutral library.
/// </summary>
internal static class TsDatabaseResolver
{
    /// <summary>Probe budget: long enough for a live SMB stat, short enough not to stall startup when the host is off.</summary>
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>True when the live BIRDWATCHER db (<see cref="DevDefaults.TsDatabaseLive"/>) answers within the
    /// default probe budget. Re-checked on load and after a failed live write.</summary>
    public static bool IsLiveReachable() => IsReachable(DevDefaults.TsDatabaseLive, DefaultProbeTimeout);

    /// <summary>
    /// Probes <paramref name="networkPath"/> under a hard <paramref name="timeout"/>. The (possibly-blocking)
    /// <c>File.Exists</c> runs on a worker we abandon if it overruns — a leaked probe completes harmlessly later.
    /// Never throws (any fault ⇒ not reachable).
    /// </summary>
    public static bool IsReachable(string networkPath, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(networkPath)) return false;
        try
        {
            Task<bool> probe = Task.Run(() => File.Exists(networkPath));
            return probe.Wait(timeout) && probe.Result;
        }
        catch
        {
            return false;
        }
    }
}
