namespace TargetSchedulerManager.App.Shared;

/// <summary>Which TS database this session reads + edits — the user's LIVE/LOCAL choice.</summary>
public enum TsMode { Live, Local }

/// <summary>
/// Owns the TS-source policy for one app session: the LIVE (BIRDWATCHER, over SMB) and LOCAL (working-copy) paths,
/// the injected reachability <paramref name="probe"/>, and the Live/Local + sticky-disabled state. Consulted by
/// both the load (via <see cref="ResolvePathForLoad"/>) and the write gate (via <see cref="CurrentPath"/> and
/// <see cref="NotifyLiveWriteFailed"/>). UI-free: it exposes state, never raises change notifications — the
/// view-model refreshes its bindings after each (awaited) call. Machine/network policy, so it lives in the App's
/// <c>Shared\</c> folder, never the consumer-neutral library.
/// </summary>
internal sealed class TsSource
{
    private readonly string _livePath;
    private readonly string _localPath;
    private readonly Func<bool> _probe;
    private TsMode _mode = TsMode.Local;
    private bool _liveDisabled;
    private bool _probed;

    public TsSource(string livePath, string localPath, Func<bool> probe)
    {
        _livePath = livePath;
        _localPath = localPath;
        _probe = probe;
    }

    /// <summary>The real session: the live BIRDWATCHER db, the restorable local copy, and the SMB reachability probe.</summary>
    public static TsSource CreateDefault() =>
        new(DevDefaults.TsDatabaseLive, DevDefaults.TsDatabase, TsDatabaseResolver.IsLiveReachable);

    /// <summary>The db the load and the gate currently act on.</summary>
    public string CurrentPath => _mode == TsMode.Live ? _livePath : _localPath;

    /// <summary>True when this session is on the LIVE BIRDWATCHER db.</summary>
    public bool IsLive => _mode == TsMode.Live;

    /// <summary>False once a probe found BIRDWATCHER unreachable this session (the LIVE choice greys out — sticky).</summary>
    public bool LiveEnabled => !_liveDisabled;

    /// <summary>
    /// Resolves the path to scan/read this load: the first call probes (LIVE if reachable, else LOCAL + LIVE
    /// sticky-disabled); thereafter a LIVE load re-probes and sticky-falls to LOCAL if the rig dropped.
    /// </summary>
    public string ResolvePathForLoad()
    {
        if (!_probed)
        {
            _probed = true;
            bool reachable = _probe();
            _liveDisabled = !reachable;
            _mode = reachable ? TsMode.Live : TsMode.Local;
        }
        else if (_mode == TsMode.Live && !_probe())
        {
            _liveDisabled = true;
            _mode = TsMode.Local;
        }
        return CurrentPath;
    }

    /// <summary>Honours a LIVE/LOCAL radio choice; a sticky-disabled LIVE is ignored. Returns true when the mode
    /// actually changed (the caller then reloads).</summary>
    public bool TrySelectMode(TsMode mode)
    {
        if (mode == TsMode.Live && _liveDisabled) return false;
        if (mode == _mode) return false;
        _mode = mode;
        return true;
    }

    /// <summary>The gate calls this after a write fault: if we are LIVE and a re-probe now finds BIRDWATCHER
    /// unreachable, sticky-fall to LOCAL and return true (it was a live drop). Otherwise return false (some other
    /// fault — the gate reports a failure instead).</summary>
    public bool NotifyLiveWriteFailed()
    {
        if (_mode != TsMode.Live) return false;
        if (_probe()) return false;
        _liveDisabled = true;
        _mode = TsMode.Local;
        return true;
    }
}
