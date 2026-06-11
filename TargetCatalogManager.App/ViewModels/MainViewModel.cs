using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.TargetScheduler;
using TargetCatalogManager.App.Models;
using TargetCatalogManager.App.ViewModels.Rows;
using TargetCatalogManager.App.Services;

namespace TargetCatalogManager.App.ViewModels;

/// <summary>How the grid is ordered (a sort dropdown for M1; column-header sorting can come later).</summary>
public enum SortMode
{
    TargetName,
    RemainingDesc,
    DiskDesc,
    DeltaDesc,
}

/// <summary>Which TS database this session reads + edits — the user's LIVE/LOCAL radio choice.</summary>
public enum TsMode { Live, Local }

/// <summary>
/// State + filter logic for the main grid. In WinForms terms: the fields and "refresh the ListView" code
/// you'd have on MainForm, isolated from any UI types. The XAML binds these properties with
/// <c>x:Bind ... Mode=OneWay</c>; raising <see cref="PropertyChanged"/> is what repaints the bound control
/// (the push-based inverse of WinForms' pull-based <c>Invalidate()</c>/re-assign-DataSource cycle).
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    // Dev defaults: paths come from the linked Shared\DevDefaults.cs (one definition with the console
    // host); the tolerance is the library's own resolver default. A settings page can replace these later.
    public const string DefaultLibrary = DevDefaults.Library;
    public const string DefaultTs = DevDefaults.TsDatabase;
    public static readonly double DefaultToleranceDegrees = ResolveOptions.Default.MatchToleranceDegrees;

    private IReadOnlyList<ReconciliationRow> _allRows = [];
    private LoadResult? _lastLoad;

    // Grouping state. Expansion (targets, mosaic panels, mixed-seconds rollups) survives filter changes and
    // reloads; see ExpansionState for the keying. Collapsed is the default for anything never touched.
    private readonly ExpansionState _expansion = new();

    // In-session target.active toggles, keyed by the target's TS key, so a checkbox flip survives filter/sort
    // rebuilds (the grid re-derives groups each pass). The write already hit TS; this just keeps the displayed
    // state consistent until the next full reload re-reads TS authoritatively (then it's cleared).
    private readonly Dictionary<string, bool> _targetActiveEdits = new(StringComparer.OrdinalIgnoreCase);

    // The TS source for this session. _tsMode is the user's LIVE/LOCAL radio choice; _liveDisabled goes
    // sticky-true once a probe finds BIRDWATCHER unreachable (the LIVE radio greys out — re-launch TCM to retry).
    // Probed on the first load; each LIVE load re-probes and sticky-falls to LOCAL if the rig dropped.
    private TsMode _tsMode = TsMode.Local;
    private bool _liveDisabled;
    private bool _tsProbed;
    private string _tsDbPath = DevDefaults.TsDatabase;
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

    /// <summary>True when this session is set to the LIVE BIRDWATCHER db (the LIVE radio's IsChecked).</summary>
    public bool IsLiveSelected => _tsMode == TsMode.Live;

    /// <summary>True when set to the LOCAL working copy (the LOCAL radio's IsChecked).</summary>
    public bool IsLocalSelected => _tsMode == TsMode.Local;

    /// <summary>Whether the LIVE radio is selectable — false (greyed) once BIRDWATCHER was found unreachable this
    /// session (sticky; re-launch TCM to retry).</summary>
    public bool LiveEnabled => !_liveDisabled;

    /// <summary>Spells out the current source's blast radius (radio tooltip).</summary>
    public string TsSourceTooltip => _liveDisabled
        ? "BIRDWATCHER unreachable this session — editing the LOCAL copy. Re-launch TCM to retry LIVE."
        : _tsMode == TsMode.Live
            ? "LIVE Target Scheduler db on BIRDWATCHER — edits hit the imaging rig immediately."
            : "LOCAL working copy — edits do NOT reach the rig until you copy it back.";

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
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

    /// <summary>Fresh scan + TS read + resolve; safe to call again (Reload). Runs the heavy work off the UI thread.</summary>
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        _targetActiveEdits.Clear();   // a fresh scan re-reads TS active; in-session overrides are now authoritative-stale

        // First load probes BIRDWATCHER (LIVE if reachable, else LOCAL + LIVE greyed); thereafter the radio chooses,
        // but a LIVE load re-probes and sticky-falls to LOCAL if the rig dropped (re-launch TCM to retry LIVE).
        if (!_tsProbed)
        {
            _tsProbed = true;
            bool reachable = await Task.Run(TsDatabaseResolver.IsLiveReachable);
            _liveDisabled = !reachable;
            _tsMode = reachable ? TsMode.Live : TsMode.Local;
            RaiseTsSource();
        }
        else if (_tsMode == TsMode.Live && !await Task.Run(TsDatabaseResolver.IsLiveReachable))
        {
            _liveDisabled = true;
            _tsMode = TsMode.Local;
            RaiseTsSource();
            Support.Log.Warn("BIRDWATCHER unreachable — switched to LOCAL for this session");
        }

        _tsDbPath = _tsMode == TsMode.Live ? DevDefaults.TsDatabaseLive : DevDefaults.TsDatabase;
        StatusText = $"scanning {DefaultLibrary} …";
        try
        {
            LoadResult result = await ReconciliationLoader.LoadAsync(DefaultLibrary, _tsDbPath, DefaultToleranceDegrees);
            _lastLoad = result;
            _allRows = result.Rows;
            StatusText = $"library {DefaultLibrary}  ·  TS {_tsDbPath} ({(_tsMode == TsMode.Live ? "LIVE BIRDWATCHER" : "local copy")})" +
                $"  ·  resolved in {result.Elapsed.TotalSeconds:0.0} s";
            ApplyFilters();
        }
        catch (Exception ex)
        {
            Support.Log.Error("reconciliation load failed", ex);
            _lastLoad = null;
            _allRows = [];
            SummaryText = "";
            StatusText = $"load failed: {ex.Message}";
            ApplyFilters();
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Re-raise the radio bindings after _tsMode / _liveDisabled change.
    private void RaiseTsSource()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLiveSelected)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLocalSelected)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LiveEnabled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TsSourceTooltip)));
    }

    /// <summary>Switch the TS source from the LIVE/LOCAL radios. A sticky-disabled LIVE can't be chosen; a real
    /// change reloads from the newly selected db.</summary>
    public void SetTsMode(TsMode mode)
    {
        if (mode == TsMode.Live && _liveDisabled) { RaiseTsSource(); return; }   // re-pin the radio to the active source
        if (mode == _tsMode) return;
        _tsMode = mode;
        RaiseTsSource();
        _ = LoadAsync();
    }

    /// <summary>Expand/collapse one group by editing the bound list in place (keeps the scroll position).</summary>
    public void ToggleGroup(TargetGroupRow group)
    {
        int index = _rows.IndexOf(group);
        if (index < 0) return;

        if (group.IsExpanded)
        {
            // Remove everything under this header (panel rows, children, any expanded rollup detail).
            while (index + 1 < _rows.Count && _rows[index + 1] is not TargetGroupRow)
                _rows.RemoveAt(index + 1);
            _expansion.SetTarget(group.Target, expanded: false);
        }
        else
        {
            List<object> content = [];
            AppendGroupContent(content, group);
            for (int i = 0; i < content.Count; i++)
                _rows.Insert(index + 1 + i, content[i]);
            _expansion.SetTarget(group.Target, expanded: true);
        }
        group.IsExpanded = !group.IsExpanded;

        if (Support.Log.IsDiagEnabled("UI"))
        {
            Support.Log.Diag("UI",
                $"group {(group.IsExpanded ? "expand" : "collapse")}: \"{group.Target}\" ({group.Children.Count} rows)");
        }
    }

    /// <summary>Expand/collapse a mosaic panel into its filter rows, in place.</summary>
    public void TogglePanel(PanelGroupRow panel)
    {
        int index = _rows.IndexOf(panel);
        if (index < 0) return;
        string key = $"{panel.Target}|{panel.Key}";

        if (panel.IsExpanded)
        {
            // Remove the panel's content (leaves + any expanded rollup detail), stopping at the next
            // panel or target header.
            while (index + 1 < _rows.Count && _rows[index + 1] is ReconciliationRow)
                _rows.RemoveAt(index + 1);
            _expansion.SetPanel(key, expanded: false);
        }
        else
        {
            List<object> content = [];
            AppendLeaves(content, panel.Children);
            for (int i = 0; i < content.Count; i++)
                _rows.Insert(index + 1 + i, content[i]);
            _expansion.SetPanel(key, expanded: true);
        }
        panel.IsExpanded = !panel.IsExpanded;

        if (Support.Log.IsDiagEnabled("UI"))
        {
            Support.Log.Diag("UI",
                $"panel {(panel.IsExpanded ? "expand" : "collapse")}: \"{panel.Target}\" {panel.Label} " +
                $"({panel.Children.Count} rows)");
        }
    }

    // A group's visible content: panel rows (with their remembered expansion) for a mosaic, plain leaves
    // otherwise — shared by the wholesale rebuild and the in-place ToggleGroup insert.
    private static void AppendGroupContent(IList<object> sink, TargetGroupRow group)
    {
        if (group.Panels is not null)
        {
            foreach (PanelGroupRow panel in group.Panels)
            {
                sink.Add(panel);
                if (panel.IsExpanded)
                    AppendLeaves(sink, panel.Children);
            }
            return;
        }
        AppendLeaves(sink, group.Children);
    }

    private static void AppendLeaves(IList<object> sink, IReadOnlyList<ReconciliationRow> leaves)
    {
        foreach (ReconciliationRow leaf in leaves)
        {
            sink.Add(leaf);
            if (leaf is { Detail: not null, IsExpanded: true })
                foreach (ReconciliationRow d in leaf.Detail)
                    sink.Add(d);
        }
    }

    /// <summary>Expand/collapse a mixed-seconds rollup into its one-plane source lines, in place.</summary>
    public void ToggleRollup(ReconciliationRow rollup)
    {
        if (rollup.Detail is not { } detail) return;
        int index = _rows.IndexOf(rollup);
        if (index < 0) return;

        if (rollup.IsExpanded)
        {
            for (int i = 0; i < detail.Count; i++)
                _rows.RemoveAt(index + 1);
            _expansion.SetRollup(RollupKey(rollup), expanded: false);
        }
        else
        {
            for (int i = 0; i < detail.Count; i++)
                _rows.Insert(index + 1 + i, detail[i]);
            _expansion.SetRollup(RollupKey(rollup), expanded: true);
        }
        rollup.IsExpanded = !rollup.IsExpanded;

        if (Support.Log.IsDiagEnabled("UI"))
        {
            Support.Log.Diag("UI",
                $"rollup {(rollup.IsExpanded ? "expand" : "collapse")}: \"{rollup.Target}\" " +
                $"{rollup.Filter} {rollup.Purpose} ({detail.Count} lines)");
        }
    }

    private static string RollupKey(ReconciliationRow r) => $"{r.Target}|{r.PanelKey}|{r.Filter}|{r.Purpose}";

    public void ExpandAll()
    {
        _expansion.ExpandTargets(_groups.Select(g => g.Target));
        ApplyFilters();
    }

    public void CollapseAll()
    {
        _expansion.CollapseAllTargets();
        ApplyFilters();
    }

    /// <summary>A group's effective enable state: a pending in-session toggle if any, else the loaded value.</summary>
    private bool EffectiveEnabled(ReconciliationRow representative) =>
        representative.TsTargetKey is string key && _targetActiveEdits.TryGetValue(key, out bool pending)
            ? pending
            : representative.Enabled;

    /// <summary>
    /// Immediately writes <c>target.active</c> for one group's target into the local TS working copy (read-back
    /// verified + audited), off the UI thread. Records the new state so it survives filter/sort rebuilds; no grid
    /// reload (active changes no counts/hours). Returns false on failure so the caller can revert the checkbox.
    /// </summary>
    public async Task<bool> SetTargetEnabledAsync(TargetGroupRow group, bool enabled)
    {
        if (group.TsTargetKey is not string key)
            return false;   // no TS target behind this group (the checkbox should be hidden)

        try
        {
            (TargetEditResult? result, string? refusal) = await Task.Run<(TargetEditResult?, string?)>(() =>
            {
                using TargetSchedulerEditor editor = new(_tsDbPath);
                // Refuse a write that isn't safe — esp. an open -wal/-shm/-journal sidecar on the LIVE db, which
                // means TS is mid-transaction on the imaging rig. Clear reason over a generic failure.
                string? reason =
                    !editor.HasRequiredColumns ? "TS db schema is incompatible"
                    : editor.IsReadOnly ? "TS db file is read-only"
                    : editor.HasOpenSidecar ? "TS database busy (open in NINA?) — try again"
                    : null;
                return reason is not null
                    ? (null, reason)
                    : (editor.SetTargetActive(key, enabled), null);
            });

            if (refusal is not null)
            {
                Support.Log.Warn($"target.active write refused for \"{group.Target}\": {refusal}");
                StatusText = $"can't change {group.Target}: {refusal}";
                return false;
            }
            if (result is not { Succeeded: true })
            {
                Support.Log.Error(
                    $"target.active write failed for \"{group.Target}\" (found={result?.RowFound} verified={result?.Verified})");
                StatusText = $"enable change failed for {group.Target} — see tcm.log";
                return false;
            }

            _targetActiveEdits[key] = enabled;
            Support.Log.Info(
                $"EDIT target.active \"{group.Target}\": {result.OldActive} -> {(enabled ? 1 : 0)} on {(_tsMode == TsMode.Live ? "LIVE" : "local")} {_tsDbPath}");
            return true;
        }
        catch (Exception ex)
        {
            Support.Log.Error($"target.active write threw for \"{group.Target}\"", ex);
            // A LIVE write that throws because BIRDWATCHER dropped sticky-disables LIVE and falls to LOCAL for the
            // session (re-launch to retry); any other fault just reports.
            if (_tsMode == TsMode.Live && !await Task.Run(TsDatabaseResolver.IsLiveReachable))
            {
                _liveDisabled = true;
                _tsMode = TsMode.Local;
                RaiseTsSource();
                StatusText = "BIRDWATCHER unreachable — switched to LOCAL for this session (re-launch TCM to retry LIVE).";
                _ = LoadAsync();
            }
            else
            {
                StatusText = $"enable change failed: {ex.Message}";
            }
            return false;
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
                            _expansion.IsPanelExpanded($"{g.Key}|{p.Key}")))];
                }
                return new TargetGroupRow(
                    g.Key, all, _expansion.IsTargetExpanded(g.Key), EffectiveEnabled(all[0]), panels);
            })];

        // The sort dropdown orders the groups by their aggregates; children stay in filter order.
        groups = _sortMode switch
        {
            SortMode.RemainingDesc => [.. groups.OrderByDescending(g => g.Remaining)
                                                .ThenBy(g => g.Target, StringComparer.OrdinalIgnoreCase)],
            SortMode.DiskDesc => [.. groups.OrderByDescending(g => g.Disk)
                                           .ThenBy(g => g.Target, StringComparer.OrdinalIgnoreCase)],
            SortMode.DeltaDesc => [.. groups.OrderByDescending(g => g.Delta ?? int.MinValue)
                                            .ThenBy(g => g.Target, StringComparer.OrdinalIgnoreCase)],
            _ => groups,   // loader's default order: target name
        };

        _groups = groups;
        _visibleLeafCount = leaves.Count;

        ObservableCollection<object> visible = [];
        foreach (TargetGroupRow g in groups)
        {
            // Restore nested expansion for every rollup (rows are rebuilt each pass), visible or not,
            // so a later toggle expands to the remembered state.
            foreach (ReconciliationRow child in g.Children)
            {
                if (child.Detail is not null)
                    child.IsExpanded = _expansion.IsRollupExpanded(RollupKey(child));
            }

            visible.Add(g);
            if (!g.IsExpanded) continue;
            AppendGroupContent(visible, g);
        }
        Rows = visible;

        // One line per applied filter state (incl. each search keystroke) — between USER_OBS markers this
        // is the trail of what the user was looking at. TCM_DIAG-gated; zero overhead when off.
        if (Support.Log.IsDiagEnabled("UI"))
        {
            Support.Log.Diag("UI",
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

    /// <summary>App-state snapshot for the Ctrl+N observation window's USER_OBS_END line — captures what
    /// the grid showed when the user committed the note, without them having to type it.</summary>
    public string GetObservationContext()
    {
        string counts = _lastLoad is { Report: var r }
            ? $"Both={r.BothCount} TsOnly={r.PlannedOnlyCount} DiskOnly={r.ActualOnlyCount} aliases={r.AliasTsTargets.Count} mosaics={r.MosaicsResolved}"
            : "no-load";
        return $"rows={_visibleLeafCount}/{_allRows.Count}, groups={_groups.Count}, " +
               $"expanded={_groups.Count(g => g.IsExpanded)}, search=\"{_searchText}\", " +
               $"source={SourceFilterName()}, flagged={_flaggedOnly}, sort={_sortMode}, {counts}";
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
