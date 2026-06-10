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

    private IReadOnlyList<ReconciliationRow> _rows = [];
    private string _summaryText = "";
    private string _statusText = "loading…";
    private bool _isLoading;
    private string _searchText = "";
    private int _sourceFilterIndex;   // 0 All · 1 Both · 2 TS-only · 3 Disk-only
    private bool _flaggedOnly;
    private SortMode _sortMode = SortMode.TargetName;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The filtered, sorted rows the ListView shows.</summary>
    public IReadOnlyList<ReconciliationRow> Rows
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

        q = _sortMode switch
        {
            SortMode.RemainingDesc => q.OrderByDescending(r => Math.Max(0, (r.Desired ?? 0) - r.Disk))
                                       .ThenBy(r => r.Target, StringComparer.OrdinalIgnoreCase),
            SortMode.DiskDesc => q.OrderByDescending(r => r.Disk).ThenBy(r => r.Target, StringComparer.OrdinalIgnoreCase),
            SortMode.DeltaDesc => q.OrderByDescending(r => r.Delta ?? int.MinValue)
                                   .ThenBy(r => r.Target, StringComparer.OrdinalIgnoreCase),
            _ => q,   // loader's default order: target, then filter
        };

        List<ReconciliationRow> rows = [.. q];
        Rows = rows;

        // One line per applied filter state (incl. each search keystroke) — between USER_OBS markers this
        // is the trail of what the user was looking at. TCM_DIAG-gated; zero overhead when off.
        if (Support.Log.IsDiagEnabled("UI"))
        {
            Support.Log.Diag("UI",
                $"filters: rows={rows.Count}/{_allRows.Count} search=\"{_searchText}\" " +
                $"source={SourceFilterName()} flagged={_flaggedOnly} sort={_sortMode}");
        }

        if (_lastLoad is { Report: var r })
        {
            SummaryText =
                $"Both {r.BothCount} · TS-only {r.PlannedOnlyCount} · Disk-only {r.ActualOnlyCount}" +
                $"  —  aliases {r.AliasTsTargets.Count} · duplicates {r.DuplicateTsTargets.Count}" +
                $" · mosaics {r.MosaicsResolved} ({r.PanelsFolded} panels)" +
                $"  —  showing {rows.Count}/{_allRows.Count} rows";
        }
    }

    /// <summary>App-state snapshot for the Ctrl+N observation window's USER_OBS_END line — captures what
    /// the grid showed when the user committed the note, without them having to type it.</summary>
    public string GetObservationContext()
    {
        string counts = _lastLoad is { Report: var r }
            ? $"Both={r.BothCount} TsOnly={r.PlannedOnlyCount} DiskOnly={r.ActualOnlyCount} aliases={r.AliasTsTargets.Count} mosaics={r.MosaicsResolved}"
            : "no-load";
        return $"rows={Rows.Count}/{_allRows.Count}, search=\"{_searchText}\", source={SourceFilterName()}, " +
               $"flagged={_flaggedOnly}, sort={_sortMode}, {counts}";
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
