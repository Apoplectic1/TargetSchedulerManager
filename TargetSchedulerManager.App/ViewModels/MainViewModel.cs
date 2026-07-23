using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using Astronomy.Diagnostics;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels.Rows;
using TargetSchedulerManager.App.Services;

namespace TargetSchedulerManager.App.ViewModels;

/// <summary>One exposure template for the Templates… picker and the plan-row trigger: its TS key, identity,
/// and how many loaded plans point at it — the blast radius a template edit carries (a template is shared;
/// editing it affects every plan using it).</summary>
internal sealed record TemplateInfo(string TsKey, string Name, string Filter, int UsedByPlans);

/// <summary>How the grid is ordered (a sort dropdown for M1; column-header sorting can come later).</summary>
public enum SortMode
{
    TargetName,
    RemainingDesc,
    DiskDesc,
    DeltaDesc,
}

/// <summary>Whether a load consults BIRDWATCHER: the open pulls when the baseline says the local copy may be
/// stale; Reload means "rescan disk + re-read local" and never pulls; Pull-now overrides the skip heuristic.</summary>
public enum PullPolicy
{
    /// <summary>Probe, then pull only when the baseline/sidecar rule says the local copy may be stale (open).</summary>
    IfChanged,
    /// <summary>No probe, no pull — rescan the disk and re-read the local db (Reload).</summary>
    Never,
    /// <summary>Probe and pull unconditionally (the Pull-now override; still routed through the dirty guard).</summary>
    Force,
}

/// <summary>The open-with-dirty decision: unpushed local edits exist and BIRDWATCHER is reachable, so the
/// user chooses before any pull can overwrite them.</summary>
public enum OpenDirtyDecision
{
    /// <summary>Push the journal now (recommended), then pull fresh.</summary>
    Push,
    /// <summary>Drop the unpushed edits deliberately and pull fresh (the debug-session path).</summary>
    Discard,
    /// <summary>Neither: keep working on the local copy, journal intact, no pull this load.</summary>
    ContinueLocal,
}

/// <summary>
/// State + filter logic for the main grid. In WinForms terms: the fields and "refresh the ListView" code
/// you'd have on MainForm, isolated from any UI types. The XAML binds these properties with
/// <c>x:Bind ... Mode=OneWay</c>; raising <see cref="PropertyChanged"/> is what repaints the bound control
/// (the push-based inverse of WinForms' pull-based <c>Invalidate()</c>/re-assign-DataSource cycle).
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    // Dev defaults: paths come from Shared\DevDefaults.cs; the tolerance is the library's own resolver
    // default. A settings page can replace these later.
    public const string DefaultLibrary = DevDefaults.Library;
    public static readonly double DefaultToleranceDegrees = ResolveOptions.Default.MatchToleranceDegrees;

    private IReadOnlyList<ReconciliationRow> _allRows = [];
    private LoadResult? _lastLoad;

    // Grouping state. The VisibleRowTree flattens the filtered groups into the bound row list and splices one
    // node's children in/out on a toggle (insert == remove, one rule), and owns the expansion-key identity over
    // a string-set ExpansionState. Expansion survives filter changes + reloads; collapsed is the default.
    private readonly VisibleRowTree _tree = new(new ExpansionState());

    // The guarded local write + the sync orchestrator (pull/journal/push) live in TsEditGate/TsSync; the
    // view-model holds the gate and reads its Sync for the load path, the badge, and push.
    private readonly TsEditGate _gate;

    private readonly Dictionary<string, bool> _targetActiveEdits = new(StringComparer.OrdinalIgnoreCase);
    private List<TargetGroupRow> _groups = [];
    private int _visibleLeafCount;

    private ObservableCollection<object> _rows = [];
    private string _summaryText = "";
    private string _statusText = "loading…";
    private bool _isLoading;
    private string _searchText = "";
    private int _sourceFilterIndex;   // 0 All · 1 Both · 2 TS-only · 3 Disk-only
    private bool _flaggedOnly;
    private SortMode _sortMode = SortMode.TargetName;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel() : this(TsEditGate.CreateDefault()) { }

    /// <summary>Test seam: inject a gate backed by a stub editor + a temp-path <see cref="TsSync"/>.</summary>
    internal MainViewModel(TsEditGate gate) => _gate = gate;

    internal TsSync Sync => _gate.Sync;

    /// <summary>UI hook (set by the window): the open-with-dirty prompt — push / discard / continue-local.
    /// Unset (tests, headless) behaves as <see cref="OpenDirtyDecision.ContinueLocal"/>: never lose edits silently.</summary>
    internal Func<PushReview, Task<OpenDirtyDecision>>? OpenWithDirtyPrompt { get; set; }

    /// <summary>UI hook (set by the window): the push review dialog; returns confirm. Unset confirms
    /// (tests drive push through <see cref="TsSync"/> directly).</summary>
    internal Func<PushReview, Task<bool>>? ConfirmPushPrompt { get; set; }

    /// <summary>
    /// The flattened tree the ListView shows: one <see cref="TargetGroupRow"/> header per target, followed
    /// by its <see cref="ReconciliationRow"/> children when expanded. ObservableCollection so a chevron
    /// toggle can insert/remove children in place (scroll position survives); filter/sort changes replace
    /// the whole instance instead.
    /// </summary>
    public ObservableCollection<object> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    /// <summary>Both/TS-only/Disk-only counts etc. — the M1 verification numbers, straight from the build report.</summary>
    public string SummaryText
    {
        get => _summaryText;
        private set => Set(ref _summaryText, value);
    }

    /// <summary>Paths + timing, or the load error.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    /// <summary>UI-layer courtesy notes onto the status line (e.g. the project min-time/meridian-window
    /// never-selected warning) — the window raises these; loads and pushes overwrite them naturally.</summary>
    internal void NoteStatus(string text) => StatusText = text;

    /// <summary>The always-visible sync badge: last-synced time + unpushed count (state is displayed, never
    /// recalled — the user must never have to remember cross-session facts).</summary>
    public string SyncBadgeText
    {
        get
        {
            string synced = Sync.Baseline is { } b ? $"synced {FormatWhen(b.RecordedAt)}" : "never pulled";
            string offline = Sync.HasProbed && !Sync.RemoteReachable ? "BIRDWATCHER offline  ·  " : "";
            int unpushed = Sync.IsDirty ? Sync.Journal.Collapse().Count : 0;
            return unpushed > 0 ? $"{offline}{synced}  ·  {unpushed} unpushed" : $"{offline}{synced}";
        }
    }

    /// <summary>Spells out the sync model + paths (badge/Push tooltip).</summary>
    public string SyncTooltip =>
        $"Edits land in the local copy and journal; nothing reaches BIRDWATCHER until you push.\n" +
        $"Local: {Sync.LocalPath}\nBIRDWATCHER: {Sync.RemotePath}";

    /// <summary>Push is the one real decision — enabled exactly when unpushed edits exist.</summary>
    public bool CanPush => Sync.IsDirty;

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

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

    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) ApplyFilters(); }
    }

    public int SourceFilterIndex
    {
        get => _sourceFilterIndex;
        set { if (Set(ref _sourceFilterIndex, value)) ApplyFilters(); }
    }

    public bool FlaggedOnly
    {
        get => _flaggedOnly;
        set { if (Set(ref _flaggedOnly, value)) ApplyFilters(); }
    }

    public SortMode SortMode
    {
        get => _sortMode;
        set { if (Set(ref _sortMode, value)) ApplyFilters(); }
    }

    /// <summary>Test seam: install rows directly (no scan, no logging) and run the filter pipeline —
    /// everything downstream of the load is exercised exactly as <see cref="LoadAsync"/> drives it.
    /// The real loader seam (an injectable interface) is an M2 item.</summary>
    internal void SetRowsForTest(IReadOnlyList<ReconciliationRow> rows)
    {
        _lastLoad = null;
        _allRows = rows;
        ApplyFilters();
    }

    /// <summary>Test seam: install a load result directly (the template surface reads its graph).</summary>
    internal void SetLoadForTest(LoadResult load)
    {
        _lastLoad = load;
        RefreshAmbiguities();
    }

    // ---- Ambiguity report (the tripwire's detail — decision 2026-07-08: detect here, fix by hand in TS) ----

    private AmbiguityReportResult? _ambiguities;

    /// <summary>Action items from the last load's checks (0 before a load). The tripwire number.</summary>
    public int AmbiguityCount => _ambiguities?.ActionCount ?? 0;

    /// <summary>The Ambiguities… button gates on a completed load (the report reads its graph).</summary>
    public bool CanShowAmbiguities => _ambiguities is not null;

    /// <summary>Status-line fragment: silent at zero — the tripwire only speaks when tripped.</summary>
    internal string AmbiguitySuffix =>
        _ambiguities is { ActionCount: > 0 } a
            ? $"  ·  {a.ActionCount} ambiguit{(a.ActionCount == 1 ? "y" : "ies")}"
            : "";

    /// <summary>Rebuilds the report from the retained load (pure, ~ms — re-plans write-back in memory; no
    /// disk rescan). Runs after every load and on the test seam so the count and the file always agree.</summary>
    private void RefreshAmbiguities()
    {
        _ambiguities = _lastLoad is { } load
            ? AmbiguityReport.Build(
                load.Graph, load.Report,
                WriteBackPlanner.Plan(load.Graph.Targets, load.Graph.Plans, load.Graph.Templates,
                                      load.Graph.InventoryFilters, load.Report),
                DateTimeOffset.Now, Sync.LocalPath, DefaultLibrary, DefaultToleranceDegrees)
            : null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AmbiguityCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanShowAmbiguities)));
    }

    /// <summary>Writes the report as a dated Markdown file (default: the app's local Reports folder) and
    /// opens it in the default handler. Launch failure is non-fatal — the file stays, the status line says
    /// where. Returns the path, or null when nothing was written.</summary>
    internal string? WriteAmbiguityReport(string? directory = null, bool open = true)
    {
        if (_ambiguities is not { } ambig)
        {
            StatusText = "no load yet — nothing to report";
            return null;
        }
        try
        {
            string dir = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TargetSchedulerManager", "Reports");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"ambiguities-{DateTime.Now:yyyyMMdd-HHmm}.md");
            File.WriteAllText(path, ambig.Markdown);
            Log.Info($"AMBIGUITY report written: {path} ({ambig.ActionCount} action item(s))");

            if (open)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Error("ambiguity report launch failed", ex);
                    StatusText = $"report written (open it yourself): {path}";
                    return path;
                }
            }
            StatusText = $"ambiguity report — {ambig.ActionCount} action item(s): {path}";
            return path;
        }
        catch (Exception ex)
        {
            Log.Error("ambiguity report failed", ex);
            StatusText = $"ambiguity report failed: {ex.Message} — see tsm.log";
            return null;
        }
    }

    /// <summary>The loaded graph's templates for the Templates… picker: name-ordered, with used-by counts
    /// from the plan edges — zero-use templates included (they have no rows to anchor from). Empty before a
    /// load completes; the caller notes "load first" rather than showing an empty list.</summary>
    internal IReadOnlyList<TemplateInfo> ListTemplates()
    {
        if (_lastLoad is not { Graph: { } graph })
            return [];
        Dictionary<Guid, int> usedBy = graph.Plans
            .GroupBy(p => p.ExposureTemplateId)
            .ToDictionary(g => g.Key, g => g.Count());
        List<TemplateInfo> templates = [];
        foreach (ExposureTemplate template in graph.Templates)
        {
            if (template.ImportedFromTsGuid is not string key)
            {
                // TS-sourced templates always carry their key; a keyless one can't be edited — skip loudly.
                Log.Warn($"template \"{template.Name}\" has no TS key — omitted from the Templates… picker");
                continue;
            }
            templates.Add(new TemplateInfo(key, template.Name, template.FilterName, usedBy.GetValueOrDefault(template.Id)));
        }
        return [.. templates.OrderBy(t => t.Name, NaturalComparer.Instance)];
    }

    /// <summary>Resolves the template behind one plan (the row menu's "Edit template…") through the loaded
    /// graph: plan by TS key → its template + used-by count. Null when unresolved (no load, unknown plan,
    /// keyless template) — the caller offers no item.</summary>
    internal TemplateInfo? TryGetTemplateForPlan(string planTsKey)
    {
        if (_lastLoad is not { Graph: { } graph })
            return null;
        ExposurePlan? plan = graph.Plans.FirstOrDefault(p =>
            string.Equals(p.ImportedFromTsGuid, planTsKey, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
            return null;
        ExposureTemplate? template = graph.Templates.FirstOrDefault(t => t.Id == plan.ExposureTemplateId);
        if (template?.ImportedFromTsGuid is not string key)
            return null;
        return new TemplateInfo(key, template.Name, template.FilterName,
            graph.Plans.Count(p => p.ExposureTemplateId == template.Id));
    }

    /// <summary>
    /// One load: (per <paramref name="policy"/>) probe BIRDWATCHER, gate a dirty journal behind the
    /// push/discard prompt, pull-or-skip; then fresh disk scan + local TS read + resolve; then the automatic
    /// write-back stamps drifted counts into the local db (journaled) and the grid re-reads so it shows them.
    /// Safe to call again (Reload). Heavy work runs off the UI thread.
    /// </summary>
    public async Task LoadAsync(PullPolicy policy = PullPolicy.IfChanged)
    {
        if (IsLoading) return;
        IsLoading = true;
        Stopwatch sw = Stopwatch.StartNew();
        _targetActiveEdits.Clear();   // a fresh load re-reads TS active; in-session overrides are now authoritative-stale

        try
        {
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
            SummaryText = "";
            StatusText = $"load failed: {ex.Message}";
            ApplyFilters();
        }
        finally
        {
            IsLoading = false;
            RaiseSyncState();
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
                    await Task.Run(Sync.Discard);   // also drops the baseline — a cancelled pull below can't strand the discarded values behind a matching skip
                    return await TryPullAsync(probe)
                        ? "edits discarded · pulled fresh"
                        : CancelledPullNote("edits discarded · ");
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
        if (IsLoading) return;
        if (!Sync.IsDirty)
        {
            StatusText = "nothing to push — no unpushed edits";
            return;
        }

        // IsLoading doubles as the load/push mutual exclusion (both check-and-set synchronously on the UI
        // thread): a second Push… click can't stack a second ContentDialog, and Reload can't run a write-back
        // pass while the push replays.
        IsLoading = true;
        PushResult? result = null;
        try
        {
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
            // A thrown push (remote db failed to open mid-push, network drop during the closing pull) never
            // reached the journal rewrite — every entry is still journaled, so re-push recovers. Fail loud.
            Log.Error("push threw — journal intact, re-push after fixing the cause", ex);
            StatusText = $"PUSH FAILED: {ex.Message} — edits stay journaled, see tsm.log";
            return;
        }
        finally
        {
            IsLoading = false;
            RaiseSyncState();
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
            (result.PulledFresh ? " · pulled fresh" : " — see the badge for anything still unpushed"),
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

    /// <summary>Expand/collapse one group by editing the bound list in place (keeps the scroll position).</summary>
    public void ToggleGroup(TargetGroupRow group)
    {
        _tree.Toggle(_rows, group);
        if (Log.IsDiagEnabled("UI"))
            Log.Diag("UI",
                $"group {(group.IsExpanded ? "expand" : "collapse")}: \"{group.Target}\" ({group.Children.Count} rows)");
    }

    /// <summary>Expand/collapse a mosaic panel into its filter rows, in place.</summary>
    public void TogglePanel(PanelGroupRow panel)
    {
        _tree.Toggle(_rows, panel);
        if (Log.IsDiagEnabled("UI"))
            Log.Diag("UI",
                $"panel {(panel.IsExpanded ? "expand" : "collapse")}: \"{panel.Target}\" {panel.Label} " +
                $"({panel.Children.Count} rows)");
    }

    /// <summary>Expand/collapse a mixed-seconds rollup into its one-plane source lines, in place.</summary>
    public void ToggleRollup(ReconciliationRow rollup)
    {
        if (rollup.Detail is not { } detail) return;   // only a mixed-seconds rollup discloses
        _tree.Toggle(_rows, rollup);
        if (Log.IsDiagEnabled("UI"))
            Log.Diag("UI",
                $"rollup {(rollup.IsExpanded ? "expand" : "collapse")}: \"{rollup.Target}\" " +
                $"{rollup.Filter} {rollup.Purpose} ({detail.Count} lines)");
    }

    public void ExpandAll()
    {
        _tree.ExpandAllTargets(_groups.Select(g => g.Target));
        ApplyFilters();
    }

    public void CollapseAll()
    {
        _tree.CollapseAllTargets();
        ApplyFilters();
    }

    /// <summary>A group's effective enable state: a pending in-session toggle if any, else the loaded value.</summary>
    private bool EffectiveEnabled(ReconciliationRow representative) =>
        representative.TsTargetKey is string key && _targetActiveEdits.TryGetValue(key, out bool pending)
            ? pending
            : representative.Enabled;

    // Maps one guarded-write outcome to the status line + side effects, returning whether the value was
    // applied. An applied write also changed the journal, so the badge re-raises.
    private bool ApplyOutcome(EditOutcome outcome, string label)
    {
        switch (outcome)
        {
            case EditOutcome.Applied:
                RaiseSyncState();
                RefreshAllMarks();   // the journal gained an entry — the row's → must appear in place
                return true;
            case EditOutcome.Refused refused:
                StatusText = $"can't change {label}: {RefusalText(refused.Reason)}";
                return false;
            case EditOutcome.Failed:
                StatusText = $"edit failed for {label} — see tsm.log";
                return false;
            default:
                return false;
        }
    }

    private static string RefusalText(RefusalReason reason) => reason switch
    {
        RefusalReason.SchemaIncompatible => "local TS copy schema is incompatible",
        RefusalReason.ReadOnly => "local TS copy is read-only",
        RefusalReason.OpenSidecar => "local TS copy busy (another tool has it open?) — try again",
        RefusalReason.ColumnAbsent => "this TS db has no such column",
        RefusalReason.HasOverrideOrder =>
            "this target has a custom exposure order (index-coupled to its plans) — re-author it in the TS editor",
        _ => "refused",
    };

    public async Task<bool> SetTargetEnabledAsync(TargetGroupRow group, bool enabled)
    {
        if (group.TsTargetKey is not string key)
            return false;
        EditOutcome outcome = await _gate.ApplyAsync(TsTable.Target, key, "active", enabled ? 1 : 0, group.Target);
        bool applied = ApplyOutcome(outcome, group.Target);
        if (applied)
        {
            _targetActiveEdits[key] = enabled;
            group.ApplyEnabled(enabled);   // mirror the in-grid checkbox (a flyout edit must show immediately)
        }
        return applied;
    }

    /// <summary>A mosaic's aggregate panel-enable state for the master switch: true = every TS-backed panel
    /// enabled, false = none, null = mixed (or no TS panels). Honors pending in-session toggles.</summary>
    public bool? GetMosaicEnabledState(TargetGroupRow group)
    {
        if (group.Panels is not { Count: > 0 } panels)
            return null;
        bool anyOn = false, anyOff = false;
        foreach (PanelGroupRow panel in panels)
        {
            if (panel.TsTargetKey is not string key) continue;
            bool on = _targetActiveEdits.TryGetValue(key, out bool pending) ? pending : panel.Children[0].Enabled;
            if (on) anyOn = true; else anyOff = true;
        }
        return anyOn && anyOff ? null : anyOn ? true : anyOff ? false : null;
    }

    /// <summary>The mosaic master enable: fans <c>target.active</c> out to every TS-backed panel target (a
    /// mosaic parent is a grouping node with no TS row of its own). Each write is individually guarded and
    /// audited; false when any failed — the caller re-reads <see cref="GetMosaicEnabledState"/> to display
    /// whatever partial state resulted.</summary>
    public async Task<bool> SetMosaicEnabledAsync(TargetGroupRow group, bool enabled)
    {
        if (group.Panels is not { Count: > 0 } panels)
            return false;
        bool allApplied = true;
        foreach (PanelGroupRow panel in panels)
        {
            if (panel.TsTargetKey is not string key) continue;
            string label = $"{group.Target} · {panel.Label}";
            EditOutcome outcome = await _gate.ApplyAsync(TsTable.Target, key, "active", enabled ? 1 : 0, label);
            if (ApplyOutcome(outcome, label))
                _targetActiveEdits[key] = enabled;
            else
                allApplied = false;
        }
        return allApplied;
    }

    /// <summary>Seeds a field-editor form: the current db values of one TS row's editable columns
    /// (null = row missing or read fault — show an error, not a form).</summary>
    public Task<IReadOnlyDictionary<string, object?>?> ReadTsFieldsAsync(TsTable table, string key, string label) =>
        _gate.ReadFieldsAsync(table, key, label);

    /// <summary>Writes one editable TS field through the guarded gate; true when applied + verified. The generic
    /// path for fields with no in-grid mirror — `target.active` and plan `desired` route through their specific
    /// setters so their grid cells refresh in place.</summary>
    public async Task<bool> SetTsFieldAsync(TsTable table, string key, string column, object? value, string label)
    {
        EditOutcome outcome = await _gate.ApplyAsync(table, key, column, value, label);
        return ApplyOutcome(outcome, label);
    }

    /// <summary>Writes <c>exposureplan.enabled</c> through the guarded gate — the library clears the target's
    /// cadence rows in the same transaction (direct write, no confirm — user decision 2026-07-07; see the
    /// cadence convention in DOMAIN.md). Mirrors the row's checkbox in place on success.</summary>
    public async Task<bool> SetPlanEnabledAsync(ReconciliationRow row, bool enabled)
    {
        if (row.PlanTsKey is not string key)
            return false;
        string label = $"{row.Target} · {row.Filter}";
        EditOutcome outcome = await _gate.ApplyAsync(TsTable.ExposurePlan, key, "enabled", enabled ? 1 : 0, label);
        if (!ApplyOutcome(outcome, label))
            return false;
        row.ApplyPlanEnabled(enabled);
        return true;
    }

    public async Task<bool> SetPlanDesiredAsync(ReconciliationRow row, int desired)
    {
        if (row.PlanTsKey is not string key)
            return false;
        EditOutcome outcome = await _gate.ApplyAsync(TsTable.ExposurePlan, key, "desired", desired, $"{row.Target} · {row.Filter}");
        if (!ApplyOutcome(outcome, $"{row.Target} · {row.Filter}"))
            return false;

        row.ApplyDesired(desired);
        RecomputeOwners(row);
        return true;
    }

    /// <summary>Writes <c>exposureplan.exposure</c> (a positive override, or TS's −1 defer-to-template
    /// sentinel) through the guarded gate. <paramref name="mirrorSeconds"/> is what the Seconds cell should
    /// show afterwards — the rounded override, or the template default when the caller knows it; when null,
    /// the effective value is resolved from the db (plan→template join) so the cell mirrors immediately
    /// either way (standing rule: a flyout edit reflects in its column at once).</summary>
    public async Task<bool> SetPlanExposureAsync(ReconciliationRow row, double exposure, int? mirrorSeconds)
    {
        if (row.PlanTsKey is not string key)
            return false;
        string label = $"{row.Target} · {row.Filter}";
        EditOutcome outcome = await _gate.ApplyAsync(TsTable.ExposurePlan, key, "exposure", exposure, label);
        if (!ApplyOutcome(outcome, label))
            return false;

        mirrorSeconds ??= await _gate.ReadPlanEffectiveSecondsAsync(key, label);
        if (mirrorSeconds is int seconds)
        {
            row.ApplyPlanSeconds(seconds);
            RecomputeOwners(row);
        }
        return true;
    }

    // Re-aggregate the header rows over an in-place-edited leaf (group always; panel when the leaf has one).
    private void RecomputeOwners(ReconciliationRow row)
    {
        TargetGroupRow? group = _groups.FirstOrDefault(g => g.Children.Contains(row));
        group?.Recompute();
        if (row.PanelKey is not null)
            group?.Panels?.FirstOrDefault(p => p.Children.Contains(row))?.Recompute();
    }

    /// <summary>
    /// Re-resolves every row's direction mark in place (never replaces the Rows collection — scroll position
    /// and in-progress edits survive; ApplyMark raises only on real change). One sweep, one code path: every
    /// journal/inbound mutation route calls it — load/filter passes, applied edits, pushes without a reload,
    /// discards. Cheap: keyed dictionary lookups over a few hundred rows.
    /// Header resolution unions the graph's plan-key map (folded multi-plan cells carry no row key) with the
    /// plan keys the visible child rows do carry (covers graph-less states); panels pass no project key —
    /// a project edit marks the group header / mosaic parent only.
    /// </summary>
    internal void RefreshAllMarks()
    {
        SyncMarks marks = SyncMarks.Build(Sync.Journal, Sync.Inbound, _lastLoad?.Graph.Plans ?? []);
        foreach (TargetGroupRow group in _groups)
        {
            if (group.Panels is { } panels)
            {
                foreach (PanelGroupRow panel in panels)
                {
                    string[] panelTargetKeys = panel.TsTargetKey is string pk ? [pk] : [];
                    (string glyph, string? tooltip) = marks.ForKeys(
                        panelTargetKeys, projectKey: null, [panel.TargetId], PlanKeysOf(panel.Children));
                    panel.ApplyMark(glyph, tooltip);
                }
                (string g, string? t) = marks.ForKeys(
                    [.. panels.Select(p => p.TsTargetKey).OfType<string>()],
                    group.ProjectTsKey,
                    [.. panels.Select(p => p.TargetId)],
                    PlanKeysOf(group.Children));
                group.ApplyMark(g, t);
            }
            else
            {
                string[] targetKeys = group.TsTargetKey is string tk ? [tk] : [];
                Guid[] targetIds = group.TargetId is Guid id ? [id] : [];
                (string g, string? t) = marks.ForKeys(
                    targetKeys, group.ProjectTsKey, targetIds, PlanKeysOf(group.Children));
                group.ApplyMark(g, t);
            }

            foreach (ReconciliationRow row in group.Children)
            {
                (string g, string? t) = marks.ForPlan(row.PlanTsKey);
                row.ApplyMark(g, t);
                foreach (ReconciliationRow detail in row.Detail ?? [])
                {
                    (string dg, string? dt) = marks.ForPlan(detail.PlanTsKey);
                    detail.ApplyMark(dg, dt);
                }
            }
        }

        static IEnumerable<string> PlanKeysOf(IReadOnlyList<ReconciliationRow> rows)
        {
            foreach (ReconciliationRow r in rows)
            {
                if (r.PlanTsKey is string key)
                    yield return key;
                foreach (ReconciliationRow d in r.Detail ?? [])
                    if (d.PlanTsKey is string detailKey)
                        yield return detailKey;
            }
        }
    }

    private void ApplyFilters()
    {
        IEnumerable<ReconciliationRow> q = _allRows;

        if (_sourceFilterIndex is >= 1 and <= 3)
        {
            RowSource wanted = (RowSource)(_sourceFilterIndex - 1);
            q = q.Where(r => r.Source == wanted);
        }
        if (_flaggedOnly)
            q = q.Where(r => r.IsFlagged);
        if (!string.IsNullOrWhiteSpace(_searchText))
            q = q.Where(r => r.Matches(_searchText.Trim()));

        // _allRows is (target, panel, filter)-ordered, so grouping preserves order within each level.
        // Headers aggregate only the rows that survived the filters — sums always match what's beneath.
        List<ReconciliationRow> leaves = [.. q];
        List<TargetGroupRow> groups = [.. leaves
            .GroupBy(r => r.Target, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                List<ReconciliationRow> all = [.. g];
                List<PanelGroupRow>? panels = null;
                if (all[0].PanelKey is not null)
                {
                    panels = [.. all
                        .GroupBy(r => r.PanelKey!, StringComparer.OrdinalIgnoreCase)
                        .Select(p => new PanelGroupRow(
                            g.Key, p.Key, p.First().PanelLabel ?? p.Key,
                            p.First().PanelSource ?? all[0].Source, [.. p],
                            _tree.IsPanelExpanded(g.Key, p.Key)))];
                }
                return new TargetGroupRow(
                    g.Key, all, _tree.IsTargetExpanded(g.Key), EffectiveEnabled(all[0]), panels);
            })];

        // The sort dropdown orders the groups by their aggregates; children stay in filter order.
        groups = _sortMode switch
        {
            SortMode.RemainingDesc => [.. groups.OrderByDescending(g => g.Remaining)
                                                .ThenBy(g => g.Target, NaturalComparer.Instance)
                                                .ThenBy(g => g.Project, NaturalComparer.Instance)],
            SortMode.DiskDesc => [.. groups.OrderByDescending(g => g.Disk)
                                           .ThenBy(g => g.Target, NaturalComparer.Instance)
                                           .ThenBy(g => g.Project, NaturalComparer.Instance)],
            SortMode.DeltaDesc => [.. groups.OrderByDescending(g => g.Delta ?? int.MinValue)
                                            .ThenBy(g => g.Target, NaturalComparer.Instance)
                                            .ThenBy(g => g.Project, NaturalComparer.Instance)],
            _ => groups,   // loader's default order: target, project, filter (natural)
        };

        _groups = groups;
        _visibleLeafCount = leaves.Count;

        // Restore nested rollup expansion (rows are rebuilt each pass, visible or not) so a later toggle
        // expands to the remembered state, then flatten the groups into the bound list.
        _tree.RestoreRollupExpansion(groups);
        Rows = new ObservableCollection<object>(_tree.Flatten(groups));

        RefreshAllMarks();   // fresh header objects (and possibly fresh leaves) need their marks resolved

        // One line per applied filter state (incl. each search keystroke) — between USER_OBS markers this
        // is the trail of what the user was looking at. TSM_DIAG-gated; zero overhead when off.
        if (Log.IsDiagEnabled("UI"))
        {
            Log.Diag("UI",
                $"filters: rows={leaves.Count}/{_allRows.Count} groups={groups.Count} " +
                $"expanded={groups.Count(g => g.IsExpanded)} search=\"{_searchText}\" " +
                $"source={SourceFilterName()} flagged={_flaggedOnly} sort={_sortMode}");
        }

        if (_lastLoad is { Report: var r })
        {
            SummaryText =
                $"Both {r.BothCount} · TS-only {r.PlannedOnlyCount} · Disk-only {r.ActualOnlyCount}" +
                $"  —  aliases {r.AliasTsTargets.Count} · duplicates {r.DuplicateTsTargets.Count}" +
                $" · mosaics {r.MosaicsResolved} ({r.PanelsMatched + r.PanelsPlannedOnly + r.PanelsActualOnly} panels)" +
                $"  —  showing {groups.Count} targets · {leaves.Count}/{_allRows.Count} rows";
        }
    }

    /// <summary>App-state snapshot for the Ctrl+N diagnostics window's USER_OBS_END line — captures what
    /// the grid showed when the user committed the note, without them having to type it.</summary>
    public string GetDiagnosticsContext()
    {
        string counts = _lastLoad is { Report: var r }
            ? $"Both={r.BothCount} TsOnly={r.PlannedOnlyCount} DiskOnly={r.ActualOnlyCount} aliases={r.AliasTsTargets.Count} mosaics={r.MosaicsResolved}"
            : "no-load";
        return $"rows={_visibleLeafCount}/{_allRows.Count}, groups={_groups.Count}, " +
               $"expanded={_groups.Count(g => g.IsExpanded)}, search=\"{_searchText}\", " +
               $"source={SourceFilterName()}, flagged={_flaggedOnly}, sort={_sortMode}, " +
               $"sync=\"{SyncBadgeText}\", {counts}";
    }

    private string SourceFilterName() =>
        _sourceFilterIndex switch { 1 => "Both", 2 => "TsOnly", 3 => "DiskOnly", _ => "All" };

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
