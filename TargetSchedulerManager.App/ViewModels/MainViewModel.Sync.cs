using System.ComponentModel;
using System.Diagnostics;
using Astronomy.Catalog.Scan;
using Astronomy.Diagnostics;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;

namespace TargetSchedulerManager.App.ViewModels;

// The sync-orchestration surface (review M4 split): load/pull/push commands, the pull UI (percent +
// cancel), the open-with-dirty routing, and the badge/tooltip the toolbar binds. The busy exclusion
// these commands acquire lives in the core part; TsSync owns the actual pull/journal/push mechanics.
public sealed partial class MainViewModel
{
    /// <summary>UI hook (set by the window): the open-with-dirty prompt — push / discard / continue-local.
    /// Unset (tests, headless) behaves as <see cref="OpenDirtyDecision.ContinueLocal"/>: never lose edits silently.</summary>
    internal Func<PushReview, Task<OpenDirtyDecision>>? OpenWithDirtyPrompt { get; set; }

    /// <summary>UI hook (set by the window): the push review dialog; returns confirm. Unset confirms
    /// (tests drive push through <see cref="TsSync"/> directly).</summary>
    internal Func<PushReview, Task<bool>>? ConfirmPushPrompt { get; set; }

    /// <summary>The always-visible sync badge: last-synced time + unpushed count (state is displayed, never
    /// recalled — the user must never have to remember cross-session facts).</summary>
    public string SyncBadgeText
    {
        get
        {
            string synced = Sync.Baseline is { } b ? $"synced {FormatWhen(b.RecordedAt)}" : "never pulled";
            string offline = Sync.HasProbed && !Sync.RemoteReachable ? "BIRDWATCHER offline  ·  " : "";
            int unpushed = Sync.Journal.CollapsedCount;   // cached under the journal lock — no Collapse() on the UI thread (review N2)
            return unpushed > 0 ? $"{offline}{synced}  ·  {unpushed} unpushed" : $"{offline}{synced}";
        }
    }

    /// <summary>Spells out the sync model + paths (badge/Push tooltip).</summary>
    public string SyncTooltip =>
        $"Edits land in the local copy and journal; nothing reaches BIRDWATCHER until you push.\n" +
        $"Local: {Sync.LocalPath}\nBIRDWATCHER: {Sync.RemotePath}";

    /// <summary>Push is the one real decision — enabled exactly when unpushed edits exist and no bulk
    /// operation is running (its gate would refuse anyway; the button says so up front).</summary>
    public bool CanPush => Sync.IsDirty && !_isLoading;

    /// <summary>True while a pull-capable operation runs — shows the Cancel-pull affordance. (During a
    /// push only the closing pull honors the cancel; replay writes are never interrupted.)</summary>
    public bool IsPulling
    {
        get => _isPulling;
        private set => Set(ref _isPulling, value);
    }

    /// <summary>The Cancel-pull action: cooperative — the copy stops between chunks, the temp file is
    /// discarded, and the previous local copy (and baseline) stay untouched.</summary>
    public void CancelPull() => _pullCts?.Cancel();

    private bool _isPulling;
    private CancellationTokenSource? _pullCts;

    // One pull-capable operation: a fresh CTS for the Cancel button and a Progress that surfaces the
    // copy as a text percentage on the status line (percentage only — deliberately no progress-bar
    // element). Progress marshals to the construction (UI) thread, so StatusText updates bind safely.
    private async Task<T> WithPullUiAsync<T>(Func<IProgress<int>, CancellationToken, T> operation)
    {
        using CancellationTokenSource cts = new();
        _pullCts = cts;
        Progress<int> progress = new(p => StatusText = $"pulling from BIRDWATCHER … {p}%");
        IsPulling = true;
        try
        {
            return await Task.Run(() => operation(progress, cts.Token));
        }
        finally
        {
            IsPulling = false;
            _pullCts = null;
        }
    }

    // One cancellable pull; false = the user cancelled (previous local copy, if any, untouched).
    private async Task<bool> TryPullAsync(TsDbStat probe)
    {
        try
        {
            await WithPullUiAsync<object?>((p, t) => { Sync.Pull(probe, p, t); return null; });
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// One load: (per <paramref name="policy"/>) probe BIRDWATCHER, gate a dirty journal behind the
    /// push/discard prompt, pull-or-skip; then fresh disk scan + local TS read + resolve; then the automatic
    /// write-back stamps drifted counts into the local db (journaled) and the grid re-reads so it shows them.
    /// Safe to call again (Reload). Heavy work runs off the UI thread.
    /// </summary>
    public async Task LoadAsync(PullPolicy policy = PullPolicy.IfChanged)
    {
        if (!TryBeginBusy()) return;
        Stopwatch sw = Stopwatch.StartNew();
        _targetActiveEdits.Clear();   // a fresh load re-reads TS active; in-session overrides are now authoritative-stale

        try
        {
            // Deliberately NO ConfigureAwait(false) anywhere in this view-model: every continuation needs
            // the UI context back (StatusText/Progress raises, Rows swaps). Library-side code is where
            // ConfigureAwait(false) belongs — don't "fix" this in a sweep.
            string syncNote = await PrepareTsForLoadAsync(policy);
            RaiseSyncState();

            StatusText = $"scanning {DefaultLibrary} …";
            ImageLibraryReport scan = await ReconciliationLoader.ScanLibraryAsync(DefaultLibrary);
            LoadResult result = await ReconciliationLoader.ResolveAsync(scan, Sync.LocalPath, DefaultToleranceDegrees);

            // Write-back stamps the local db AFTER this read — when it changed anything, re-resolve over the
            // same scan so the grid shows the stamped counts (and the journal badge the new entries).
            WriteBackStepResult writeBack = await Task.Run(() => WriteBackStep.Run(result.Graph, result.Report, Sync));
            if (writeBack.PlansStamped > 0)
                result = await ReconciliationLoader.ResolveAsync(scan, Sync.LocalPath, DefaultToleranceDegrees);

            _lastLoad = result;
            _allRows = result.Rows;
            RefreshAmbiguities();
            StatusText = $"library {DefaultLibrary}  ·  TS local copy ({syncNote}){writeBack.Describe()}{AmbiguitySuffix}" +
                $"  ·  loaded in {sw.Elapsed.TotalSeconds:0.0} s";
            ApplyFilters();
        }
        catch (Exception ex)
        {
            Log.Error("reconciliation load failed", ex);
            _lastLoad = null;
            RefreshAmbiguities();
            _allRows = [];
            StatusText = $"load failed: {ex.Message}";
            ApplyFilters();
        }
        finally
        {
            EndBusy();
        }
    }

    // The BIRDWATCHER half of a load: heal a torn local copy, probe (off-thread — the SMB stat can block up
    // to its timeout), route a dirty journal through the user's push/discard decision BEFORE any pull can
    // overwrite local edits, then pull / skip per the baseline rule. Returns the status-line fragment
    // describing what happened.
    private async Task<string> PrepareTsForLoadAsync(PullPolicy policy)
    {
        // Torn-local gate: runs before the skip decision can trust a baseline that never validates local
        // health, and before any reader touches the torn file. Overrides even Reload's no-pull contract —
        // there is nothing intact left to re-read — and skips the dirty prompt: the journal survives the
        // heal untouched, so no local value can be lost by pulling.
        if (await Task.Run(Sync.HealTornLocal))
        {
            TsDbStat? healProbe = await Task.Run(Sync.ProbeRemote);
            if (healProbe is null)
                throw new InvalidOperationException(
                    $"local TS copy was torn and has been discarded, but BIRDWATCHER is unreachable — nothing to load ({Sync.LocalPath})");
            if (!await TryPullAsync(healProbe))
                throw new InvalidOperationException(
                    "pull cancelled with no intact local copy — nothing to load; Pull now when ready");
            return "local copy was torn — discarded + pulled fresh";
        }

        if (policy == PullPolicy.Never)
            return "not re-pulled";

        TsDbStat? probe = await Task.Run(Sync.ProbeRemote);
        if (probe is null)
            return "BIRDWATCHER offline";

        if (Sync.IsDirty)
        {
            PushReview review = Sync.PreparePush(probe);
            OpenDirtyDecision decision = OpenWithDirtyPrompt is null
                ? OpenDirtyDecision.ContinueLocal
                : await OpenWithDirtyPrompt(review);
            switch (decision)
            {
                case OpenDirtyDecision.Push:
                    PushResult push = await WithPullUiAsync((p, t) => Sync.Push(p, t));
                    return push.Outcome == PushOutcome.Success
                        ? push.PulledFresh ? "pushed + pulled fresh" : "pushed (closing pull skipped)"
                        : $"PUSH INCOMPLETE ({DescribePush(push)}) — kept local";
                case OpenDirtyDecision.Discard:
                    // Pull-first: only the swap physically replacing the discarded values makes the
                    // discard true. A cancelled pull changes NOTHING — journal, baseline, badge, and
                    // marks stay intact, so the grid never shows discarded values as clean truth.
                    if (!await TryPullAsync(probe))
                        return "discard not completed — unpushed edits kept";
                    await Task.Run(Sync.Discard);   // bookkeeping only: journal cleared, baseline stays (the pull just recorded it)
                    return "edits discarded · pulled fresh";
                default:
                    return "unpushed edits — pull deferred";
            }
        }

        if (policy == PullPolicy.Force)
            return await TryPullAsync(probe) ? "pulled (forced)" : CancelledPullNote();

        bool pulled;
        try
        {
            pulled = await WithPullUiAsync((p, t) => Sync.PullIfChanged(probe, p, t));
        }
        catch (OperationCanceledException)
        {
            return CancelledPullNote();
        }
        return pulled ? "pulled fresh" : "unchanged — pull skipped";
    }

    // A cancelled pull is fine while a previous local copy exists (the atomic pull never touched it); with
    // none — a cancelled first-ever pull — there is nothing to load, so fail loudly instead of half-opening.
    private string CancelledPullNote(string prefix = "") =>
        File.Exists(Sync.LocalPath)
            ? $"{prefix}pull cancelled — using the previous local copy"
            : throw new InvalidOperationException(
                "pull cancelled before any local copy exists — nothing to load; Pull now when ready");

    /// <summary>
    /// The Push button: probe, build the review (manual edits + write-back with decreases first + staleness
    /// warning), confirm through the dialog hook, replay the journal, and — on full success, which ends in a
    /// fresh pull — re-read the local db so the grid gains whatever BIRDWATCHER accrued overnight.
    /// </summary>
    public async Task PushAsync()
    {
        // TryBeginBusy is the load/push/visible-tonight mutual exclusion (check-and-set on the UI thread):
        // a second Push… click can't stack a second ContentDialog, and Reload can't run a write-back pass
        // while the push replays.
        if (!TryBeginBusy()) return;
        PushResult? result = null;
        try
        {
            if (!Sync.IsDirty)
            {
                StatusText = "nothing to push — no unpushed edits";
                return;
            }

            TsDbStat? probe = await Task.Run(Sync.ProbeRemote);
            RaiseSyncState();
            if (probe is null)
            {
                StatusText = "BIRDWATCHER unreachable — edits stay journaled for a later push";
                return;
            }

            PushReview review = Sync.PreparePush(probe);
            if (ConfirmPushPrompt is not null && !await ConfirmPushPrompt(review))
                return;

            result = await WithPullUiAsync((p, t) => Sync.Push(p, t));
        }
        catch (Exception ex)
        {
            // Every throw that can escape Push precedes the journal rewrite (the closing pull is
            // contained inside Push and reported on the result, never thrown): probe/editor/applier
            // faults leave every entry journaled, and even a faulted journal rewrite leaves the file
            // holding them — so re-push genuinely recovers. Fail loud.
            Log.Error("push threw — journal intact, re-push after fixing the cause", ex);
            StatusText = $"PUSH FAILED: {ex.Message} — edits stay journaled, see tsm.log";
            return;
        }
        finally
        {
            EndBusy();
        }

        if (result.PulledFresh)
            await LoadAsync(PullPolicy.Never);   // the closing pull changed the local db — re-read it
        else
            RefreshAllMarks();   // partial failure / mid-push edits: the journal changed without a reload
        StatusText = DescribePush(result);
    }

    private static string DescribePush(PushResult result) => result.Outcome switch
    {
        PushOutcome.Success => $"pushed {result.AppliedCount} field(s) to BIRDWATCHER" +
            (result.PulledFresh ? " · pulled fresh"
             : result.ClosingPullFailed ? " · closing pull failed — next open pulls fresh (see tsm.log)"
             : " — see the badge for anything still unpushed"),
        PushOutcome.PartialFailure =>
            $"PUSH INCOMPLETE: {result.AppliedCount} applied, {result.Failures.Count} FAILED and kept in the journal — see tsm.log",
        PushOutcome.RefusedBusy => "push refused: TS db busy on BIRDWATCHER (NINA imaging?) — try again later",
        PushOutcome.Refused => $"push refused: {result.RefusalDetail}",
        PushOutcome.Unreachable => "BIRDWATCHER unreachable — edits stay journaled for a later push",
        _ => "nothing to push",
    };

    // Re-raise the sync-facing bindings (badge, Push enable) after anything that can change them.
    private void RaiseSyncState()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SyncBadgeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SyncTooltip)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanPush)));
    }

    private static string FormatWhen(DateTimeOffset at) =>
        at.LocalDateTime.Date == DateTime.Today ? at.LocalDateTime.ToString("HH:mm") : at.LocalDateTime.ToString("ddd HH:mm");
}
