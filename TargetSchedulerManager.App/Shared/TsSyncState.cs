using System.Text.Json;
using Astronomy.Diagnostics;

namespace TargetSchedulerManager.App.Shared;

/// <summary>
/// The remote TS db's identity (main-file size + last-write time, both BIRDWATCHER's own values) at the moment
/// the local copy last mirrored it — recorded after every successful pull (a push ends in a pull, so it covers
/// both). The skip rule compares a fresh remote stat against this: equal, with no remote sidecar, proves the
/// local copy is current and the open can skip the copy.
/// </summary>
internal sealed record TsBaseline(long RemoteLength, DateTime RemoteLastWriteUtc, DateTimeOffset RecordedAt);

/// <summary>
/// Persistence for the sync baseline: one small JSON sidecar beside the local db (guards carry facts — the
/// baseline must survive relaunches and crashes, never live in anyone's memory). Saves are crash-safe
/// (temp + atomic move). A missing or unreadable file loads as unbaselined, which forces a full pull —
/// the safe direction (a stale baseline could only ever cause an extra copy, never a false skip).
/// Dirty state is deliberately NOT stored here: dirty is defined as journal-non-empty and is derived from the
/// journal file itself, so the two can never disagree after a crash.
/// </summary>
internal sealed class TsSyncState
{
    private readonly string _path;

    private TsSyncState(string path, TsBaseline? baseline)
    {
        _path = path;
        Baseline = baseline;
    }

    /// <summary>The recorded baseline, or null when never pulled (or the sidecar was unreadable).</summary>
    public TsBaseline? Baseline { get; private set; }

    public static TsSyncState Load(string path)
    {
        if (!File.Exists(path))
            return new TsSyncState(path, null);
        try
        {
            TsBaseline? baseline = JsonSerializer.Deserialize<TsBaseline>(File.ReadAllText(path));
            return new TsSyncState(path, baseline);
        }
        catch (Exception ex)
        {
            // Crash artifact, not an input contract: treat as unbaselined (forces one full pull) but say so.
            Log.Warn($"sync-state sidecar unreadable — treating as never-pulled (next open pulls): {path} ({ex.Message})");
            return new TsSyncState(path, null);
        }
    }

    /// <summary>Drops the baseline (memory + sidecar): the local copy no longer mirrors the remote (a
    /// discard abandoned it, or the torn-local heal deleted it), so the safe direction is unbaselined —
    /// the next open always pulls.</summary>
    public void Clear()
    {
        Baseline = null;
        File.Delete(_path);
    }

    /// <summary>Records a new baseline and persists it (temp + atomic move).</summary>
    public void Record(TsBaseline baseline)
    {
        Baseline = baseline;
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(baseline));
        File.Move(tmp, _path, overwrite: true);
    }
}
