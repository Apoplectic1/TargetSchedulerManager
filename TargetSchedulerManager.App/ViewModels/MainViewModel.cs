using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Astronomy.Catalog.Build;
using Astronomy.Diagnostics;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels.Rows;

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
/// <para><b>Partial layout (review M4, 2026-07-24):</b> this core part holds the shared state, the busy
/// exclusion, and the filter/group pipeline; <c>MainViewModel.Sync.cs</c> the load/pull/push surface;
/// <c>MainViewModel.Edits.cs</c> the Set*Async funnel + marks sweep; <c>MainViewModel.Reports.cs</c> the
/// ambiguity/templates/visible-tonight surfaces. One type — fields are visible across parts; the split is
/// purely navigational.</para>
/// </summary>
public sealed partial class MainViewModel : INotifyPropertyChanged
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

    private List<TargetGroupRow> _groups = [];
    private int _visibleLeafCount;

    // Leaf → its owning header rows, rebuilt by ApplyFilters (which touches every row anyway) so an
    // inline edit's re-aggregation is O(1) instead of a scan over groups × children (review N6).
    private readonly Dictionary<ReconciliationRow, (TargetGroupRow Group, PanelGroupRow? Panel)> _ownerByRow = [];

    private ObservableCollection<object> _rows = [];
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

    /// <summary>Paths + timing, or the load error.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    /// <summary>UI-layer courtesy notes onto the status line (e.g. the project min-time/meridian-window
    /// never-selected warning) — the window raises these; loads and pushes overwrite them naturally.</summary>
    internal void NoteStatus(string text) => StatusText = text;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (Set(ref _isLoading, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEdit)));
        }
    }

    /// <summary>Edit surfaces (the row grid + busy-sensitive toolbar buttons) enable off this: edits are
    /// visibly disabled exactly while a bulk operation holds the busy exclusion. The view-model funnel
    /// refuses independently (<see cref="RefuseIfBusy"/>) — this is feedback, that is the invariant.</summary>
    public bool CanEdit => !_isLoading;

    // ---- the busy exclusion (openspec busy-exclusion) --------------------------------------------------
    // One check-and-set gate for every bulk db-touching operation (load/reload, pull, push,
    // visible-tonight). Only these helpers write IsLoading, and both run on the UI thread, so acquisition
    // is atomic by construction — a future bulk command cannot forget the gate, it has to call it.

    private int _gateWorkInFlight;   // funnel calls whose worker hasn't completed (UI-thread ++/--)

    internal bool TryBeginBusy()
    {
        if (_isLoading)
            return false;
        if (_gateWorkInFlight > 0)
        {
            // An edit's worker still holds a db connection — starting a bulk operation now could overlap
            // its write (or the pull's file swap). Refuse loudly; the retry costs one click.
            StatusText = "an edit is still applying — try again in a moment";
            return false;
        }
        IsLoading = true;
        RaiseSyncState();
        return true;
    }

    internal void EndBusy()
    {
        IsLoading = false;
        RaiseSyncState();
    }

    // The funnel backstop: every edit entry point refuses while a bulk operation runs, independent of the
    // view's disabling. True = refused (the caller returns false and its control reverts).
    private bool RefuseIfBusy(string what)
    {
        if (!_isLoading)
            return false;
        StatusText = $"busy — {what} not applied; retry when the load/push finishes";
        Log.Info($"EDIT refused (busy): {what}");
        return true;
    }

    // Counts a funnel call as in flight so TryBeginBusy can refuse while its worker holds a db connection
    // (closes the "edit committed, Reload clicked instantly" window). ++/-- run on the calling (UI) thread.
    private async Task<T> WithEditInFlightAsync<T>(Func<Task<T>> gateCall)
    {
        _gateWorkInFlight++;
        try { return await gateCall(); }
        finally { _gateWorkInFlight--; }
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
        RefreshProjectChoices();
    }

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
        {
            string needle = _searchText.Trim();   // hoisted — the lambda runs per row per keystroke (review N1)
            q = q.Where(r => r.Matches(needle));
        }

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

        // The owner map for O(1) inline-edit re-aggregation (review N6). Detail lines under a rollup are
        // deliberately absent — they aren't top-level leaves; a plan edit committed on one reaches its
        // summary leaf (which IS mapped) through MirrorPlanEdit's plan-key sweep (obs b4d2).
        _ownerByRow.Clear();
        foreach (TargetGroupRow g in groups)
        {
            foreach (ReconciliationRow r in g.Children)
                _ownerByRow[r] = (g, null);
            if (g.Panels is { } gPanels)
                foreach (PanelGroupRow p in gPanels)
                    foreach (ReconciliationRow r in p.Children)
                        _ownerByRow[r] = (g, p);   // refines the group-only entry — panel children are group children too
        }

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

    }

    /// <summary>App-state snapshot for the Ctrl+N diagnostics window's USER_OBS_END line — captures what
    /// the grid showed when the user committed the note, without them having to type it.</summary>
    public string GetDiagnosticsContext()
    {
        string counts = _lastLoad is { Report: var r }
            ? $"Both={r.BothCount} TsOnly={r.PlannedOnlyCount} DiskOnly={r.ActualOnlyCount} dups={r.DuplicateTsTargets.Count} mosaics={r.MosaicsResolved}"
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
