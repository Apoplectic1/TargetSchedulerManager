using System.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace TargetCatalogManager.App.Models;

/// <summary>
/// The collapsible mini-header for one mosaic panel inside its target group: the panel's own classification
/// and the same column aggregates a target header carries, computed over the panel's leaves only. Mutable
/// only in <see cref="IsExpanded"/>, exactly like <see cref="TargetGroupRow"/>.
/// </summary>
public sealed class PanelGroupRow : INotifyPropertyChanged
{
    private readonly RowAggregates _sums;
    private bool _isExpanded;

    public PanelGroupRow(
        string target, string key, string label, RowSource source,
        IReadOnlyList<ReconciliationRow> children, bool isExpanded)
    {
        Target = target;
        Key = key;
        Label = label;
        Source = source;
        Children = children;
        _isExpanded = isExpanded;
        _sums = RowAggregates.Compute(children);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Target { get; }

    /// <summary>The expansion-state key (<c>target|panelKey</c> uses this panel part).</summary>
    public string Key { get; }

    /// <summary>"Panel 01of16 · CygnusLoop P1" — one name when the panel is one-sided.</summary>
    public string Label { get; }

    /// <summary>The panel's own classification (Both / planned-only / shot-only).</summary>
    public RowSource Source { get; }

    /// <summary>The panel's leaf rows — already filtered/ordered by the view-model.</summary>
    public IReadOnlyList<ReconciliationRow> Children { get; }

    public int? Desired => _sums.Desired;
    public int? Acquired => _sums.Acquired;
    public int? Accepted => _sums.Accepted;
    public int Disk => _sums.Disk;
    public double? HoursDelta => _sums.HoursDelta;
    public string Badge => _sums.Badge;
    public bool IsFlagged => _sums.IsFlagged;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChevronGlyph)));
        }
    }

    /// <summary>Segoe Fluent Icons: ChevronDown when expanded, ChevronRight when collapsed.</summary>
    public string ChevronGlyph => _isExpanded ? "\uE70D" : "\uE76C";

    public string SourceText => Source switch
    {
        RowSource.Both => "Both",
        RowSource.TsOnly => "TS",
        _ => "Disk",
    };

    public string DesiredText => Desired?.ToString() ?? "—";
    public string AcquiredText => Acquired?.ToString() ?? "—";
    public string AcceptedText => Accepted?.ToString() ?? "—";
    public string DiskText => Disk.ToString();

    public string HoursText => HoursDelta switch
    {
        null => "—",
        > 0 => $"+{Format.Hours(HoursDelta.Value)}",
        _ => Format.Hours(HoursDelta.Value),
    };

    public Brush? HoursBackground => HoursDelta switch
    {
        null => null,
        < 0 => ThemeBrushes.Caution,
        _ => ThemeBrushes.Success,
    };
}
