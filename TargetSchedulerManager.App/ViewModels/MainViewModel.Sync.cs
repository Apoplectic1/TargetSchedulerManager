using System.ComponentModel;
using System.Diagnostics;
using Astronomy.Catalog.Scan;
using Astronomy.Diagnostics;
using TargetSchedulerManager.App.Models;
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

    /// <summary>UI hook (set by the window): a structural adoption refusal (stale snapshot, no plate-solved
    /// centroid, no projects to adopt into) — the user explicitly asked and the planner declined, so the
    /// answer deserves a dialog, not just a status line easily missed after a menu click. Unset (tests)
    /// falls back to the status line.</summary>
    internal Func<string, Task>? AdoptRefusalPrompt { get; set; }

    /// <summary>UI hook (set by the window): the assignment dialog every adoption goes through — project
    /// (locked when the TS target exists) + existing exposure template (strict scope, best match
    /// preselected, non-pairing caution), Accept/Cancel. Returns the choice, or null on cancel. Unset
    /// (tests) cancels.</summary>
    internal Func<AdoptionFacts, Task<AdoptionChoice?>>? AdoptPrompt { get; set; }

    /// <summary>UI hook (set by the window): the combined bulk-adoption dialog — project once, one
    /// template assignment + include checkbox per eligible cell, unservable cells greyed with the reason.
    /// Returns the choice covering only included servable cells, or null on cancel. Unset (tests) cancels.</summary>
    internal Func<BulkAdoptionFacts, Task<BulkAdoptionChoice?>>? BulkAdoptPrompt { get; set; }

    /// <summary>The always-visible sync badge: last-synced time + unpushed count (state is displayed, never
    /// recalled — the user must never have to remember cross-session facts).</summary>
    public string SyncBadgeText
    {
        get
        {
            string synced = Sync.Baseline is { } b ? $"synced {Format.When(b.RecordedAt)}" : "never pulled";
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

    /// <summary>True while a cancellable operation runs — shows the Cancel affordance. A load holds it for
    /// every phase (pull → scan → resolve). (During a push only the closing pull honors the cancel; the
    /// replay writes are never interrupted.)</summary>
    public bool IsCancellable
    {
        get => _isCancellable;
        private set => Set(ref _isCancellable, value);
    }

    /// <summary>The Cancel action: cooperative throughout. A pull stops between chunks with its temp file
    /// discarded and the previous local copy (and baseline) untouched; a scan or resolve stops at its next
    /// checkpoint and the grid keeps the rows it was already showing.</summary>
    public void CancelLoad() => _cancelCts?.Cancel();

    private bool _isCancellable;
    private CancellationTokenSource? _cancelCts;

    // The cancellable-operation scope: one CTS, and the Cancel affordance live for as long as it runs.
    //
    // Scopes are PER PHASE, never one token for a whole load, and that is load-bearing: cancelling a *pull*
    // deliberately does not abort the load — it falls through with a note and reads the intact local copy
    // (the discard path depends on it: "a cancelled discard-pull changes NOTHING", see PrepareTsForLoadAsync
    // and ARCHITECTURE's sync-model section). A single shared token would poison the following scan and
    // silently convert that into an aborted load. The load's phases run in sequence, so the one Cancel button
    // still covers all of them — it simply cancels whichever phase is running.
    private async Task<T> WithCancelUiAsync<T>(Func<CancellationToken, Task<T>> body)
    {
        using CancellationTokenSource cts = new();
        _cancelCts = cts;
        IsCancellable = true;
        try
        {
            return await body(cts.Token);
        }
        finally
        {
            IsCancellable = false;
            _cancelCts = null;
        }
    }

    private Task WithCancelUiAsync(Func<CancellationToken, Task> body) =>
        WithCancelUiAsync<object?>(async ct => { await body(ct); return null; });

    // One pull-capable operation, inside a cancel scope: a Progress that surfaces the copy as a text
    // percentage on the status line (percentage only — deliberately no progress-bar element). Progress
    // marshals to the construction (UI) thread, so StatusText updates bind safely.
    private Task<T> WithPullUiAsync<T>(Func<IProgress<int>, CancellationToken, T> operation) =>
        WithCancelUiAsync(async ct =>
        {
            Progress<int> progress = new(p => StatusText = $"pulling from BIRDWATCHER … {p}%");
            return await Task.Run(() => operation(progress, ct));
        });

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

            await WithCancelUiAsync(async ct =>
            {
                StatusText = $"scanning {DefaultLibrary} …";
                ImageLibraryReport scan = await ReconciliationLoader.ScanLibraryAsync(DefaultLibrary, ct);
                LoadResult result = await ReconciliationLoader.ResolveAsync(scan, Sync.LocalPath, DefaultToleranceDegrees, ct);

                // Write-back stamps the local db AFTER this read — when it changed anything, re-resolve over the
                // same scan so the grid shows the stamped counts (and the journal badge the new entries).
                // Deliberately NOT cancellable: it writes, and a half-applied stamp pass is worse than waiting
                // out a fast one (the same rule the push replay follows).
                WriteBackStepResult writeBack = await Task.Run(() => WriteBackStep.Run(result.Graph, result.Report, Sync));
                if (writeBack.PlansStamped > 0)
                    result = await ReconciliationLoader.ResolveAsync(scan, Sync.LocalPath, DefaultToleranceDegrees, ct);

                _lastLoad = result;
                _allRows = result.Rows;
                RefreshAmbiguities();
                RefreshProjectChoices();
                // Unreadable frames speak only when nonzero (the indication means "something was lost", not
                // "a scan ran"); the ⚠ glyph is the caution emphasis a single-brush status TextBlock can
                // carry. The paths live in the ambiguity report (openspec framing-overlap-column, 4a).
                string unreadable = result.SkippedFiles.Count is int n and > 0
                    ? $"  ·  ⚠ {n} unreadable file{(n == 1 ? "" : "s")} — see Ambiguities…"
                    : "";
                StatusText = $"library {DefaultLibrary}  ·  TS local copy ({syncNote}){writeBack.Describe()}{AmbiguitySuffix}{unreadable}" +
                    $"  ·  loaded in {sw.Elapsed.TotalSeconds:0.0} s";
                ApplyFilters();
            });
        }
        catch (OperationCanceledException)
        {
            // A cancel is a decision, not a failure: keep whatever the grid was already showing (on a
            // first-ever load that is legitimately nothing) and never blank it the way the catch below does.
            StatusText = _lastLoad is null
                ? "load cancelled"
                : "load cancelled — showing the previous scan";
        }
        catch (Exception ex)
        {
            Log.Error("reconciliation load failed", ex);
            _lastLoad = null;
            RefreshAmbiguities();
            RefreshProjectChoices();
            _allRows = [];
            StatusText = $"load failed: {ex.Message}";
            ApplyFilters();
        }
        finally
        {
            EndBusy();
        }
    }

    // The BIRDWATCHER half of a load: heal a torn local copy, probe (await-friendly — no thread parks for
    // the SMB timeout, review N8), route a dirty journal through the user's push/discard decision BEFORE
    // any pull can overwrite local edits, then pull / skip per the baseline rule. Returns the status-line
    // fragment describing what happened.
    private async Task<string> PrepareTsForLoadAsync(PullPolicy policy)
    {
        // Torn-local gate: runs before the skip decision can trust a baseline that never validates local
        // health, and before any reader touches the torn file. Overrides even Reload's no-pull contract —
        // there is nothing intact left to re-read — and skips the dirty prompt: the journal survives the
        // heal untouched, so no local value can be lost by pulling.
        if (await Task.Run(Sync.HealTornLocal))
        {
            TsDbStat? healProbe = await Sync.ProbeRemoteAsync();
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

        TsDbStat? probe = await Sync.ProbeRemoteAsync();
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
        string? exportNote = null;
        try
        {
            if (!Sync.IsDirty)
            {
                StatusText = "nothing to push — no unpushed edits";
                return;
            }

            TsDbStat? probe = await Sync.ProbeRemoteAsync();
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
            exportNote = await ExportToCatalogInboxAsync(result);
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
        StatusText = DescribePush(result) + exportNote;
    }

    /// <summary>The catalog inbox directory the export duty publishes to — the contract-named path in
    /// production; tests point it at a temp dir.</summary>
    internal string CatalogInboxDir { get; set; } = DevDefaults.CatalogInbox;

    /// <summary>
    /// The catalog-export duty (openspec <c>catalog-export</c>): after the push committed, project the
    /// applied entries into ISM's catalog inbox. Runs strictly after the commit and never blocks or rolls
    /// back the push — a fault aborts the export, logs loudly, and rides the status line (rule #16: abort +
    /// surface, never skip-the-record); the user re-doing the affected edits and pushing again re-emits the
    /// same intent harmlessly (idempotent upserts). Own catch: an export fault must never reach
    /// <see cref="PushAsync"/>'s push catch, whose "edits stay journaled" message would be a lie here.
    /// </summary>
    private async Task<string?> ExportToCatalogInboxAsync(PushResult result)
    {
        if (result.AppliedEntries is not { Count: > 0 } applied || result.CommittedAt is not { } committedAt)
            return null;
        try
        {
            int records = await Task.Run(() =>
                CatalogInboxExporter.Export(Sync.LocalPath, applied, committedAt, CatalogInboxDir));
            return records == 0 ? null : $" · catalog inbox {records} record(s)";
        }
        catch (Exception ex)
        {
            Log.Error($"CATALOG EXPORT FAILED — TS push is committed; intent reaches the inbox on the next push touching these rows ({CatalogInboxDir})", ex);
            return " — CATALOG EXPORT FAILED: see tsm.log";
        }
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
}
