using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TargetCatalogManager.App.Models;
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

/// <summary>
/// State + filter logic for the main grid. In WinForms terms: the fields and "refresh the ListView" code
/// you'd have on MainForm, isolated from any UI types. The XAML binds these properties with
/// <c>x:Bind ... Mode=OneWay</c>; raising <see cref="PropertyChanged"/> is what repaints the bound control
/// (the push-based inverse of WinForms' pull-based <c>Invalidate()</c>/re-assign-DataSource cycle).
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    // Dev defaults, mirroring Program.cs in the console host (kept in the app, not the shared library —
    // they're machine-specific). A settings page can replace these later.
    public const string DefaultLibrary = @"E:\Photography\Astro Photography\Processing";
    public const string DefaultTs = @"E:\Photography\Astro Photography\Processing\Catalog\TS Database\schedulerdb.sqlite";
    public const double DefaultToleranceDegrees = 0.5;

    private IReadOnlyList<ReconciliationRow> _allRows = [];
    private LoadResult? _lastLoad;

    // Grouping state. Expansion is keyed by target name (plus target|panel for mosaic panels and
    // target|panel|filter|purpose for the nested mixed-seconds rollups) so it survives filter changes
    // and reloads; collapsed is the default for anything never touched.
    private readonly HashSet<string> _expandedTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedPanels = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedRollups = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>Fresh scan + TS read + resolve; safe to call again (Reload). Runs the heavy work off the UI thread.</summary>
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusText = $"scanning {DefaultLibrary} …";
        try
        {
            LoadResult result = await ReconciliationLoader.LoadAsync(DefaultLibrary, DefaultTs, DefaultToleranceDegrees);
            _lastLoad = result;
            _allRows = result.Rows;
            StatusText = $"library {DefaultLibrary}  ·  TS {DefaultTs}  ·  resolved in {result.Elapsed.TotalSeconds:0.0} s";
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
            _expandedTargets.Remove(group.Target);
        }
        else
        {
            List<object> content = [];
            AppendGroupContent(content, group);
            for (int i = 0; i < content.Count; i++)
                _rows.Insert(index + 1 + i, content[i]);
            _expandedTargets.Add(group.Target);
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
            _expandedPanels.Remove(key);
        }
        else
        {
            List<object> content = [];
            AppendLeaves(content, panel.Children);
            for (int i = 0; i < content.Count; i++)
                _rows.Insert(index + 1 + i, content[i]);
            _expandedPanels.Add(key);
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
            _expandedRollups.Remove(RollupKey(rollup));
        }
        else
        {
            for (int i = 0; i < detail.Count; i++)
                _rows.Insert(index + 1 + i, detail[i]);
            _expandedRollups.Add(RollupKey(rollup));
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
        foreach (TargetGroupRow g in _groups)
            _expandedTargets.Add(g.Target);
        ApplyFilters();
    }

    public void CollapseAll()
    {
        _expandedTargets.Clear();
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
                            _expandedPanels.Contains($"{g.Key}|{p.Key}")))];
                }
                return new TargetGroupRow(g.Key, all, _expandedTargets.Contains(g.Key), panels);
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
                    child.IsExpanded = _expandedRollups.Contains(RollupKey(child));
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
