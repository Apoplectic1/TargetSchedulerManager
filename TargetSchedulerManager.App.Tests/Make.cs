using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.ViewModels.Rows;

namespace TargetSchedulerManager.App.Tests;

/// <summary>Compact builders for the row view-models (the 23-parameter ctor, defaulted).</summary>
internal static class Make
{
    public static ReconciliationRow Leaf(
        string target = "T",
        RowPlane plane = RowPlane.Both,
        string filter = "H",
        string purpose = "Light",
        int planSeconds = 300,
        int diskSeconds = 300,
        RowSource source = RowSource.Both,
        int? desired = 10,
        int? acquired = 5,
        int? accepted = 5,
        int disk = 5,
        int planCount = 1,
        string badge = "",
        bool flagged = false,
        double? planHours = null,
        double? diskHours = null,
        bool mixed = false,
        bool isDetail = false,
        IReadOnlyList<ReconciliationRow>? detail = null,
        string? panelKey = null,
        string? panelLabel = null,
        RowSource? panelSource = null,
        string? planTsKey = null, bool? planEnabled = null,
        string? tsTargetKey = null,
        bool enabled = true) =>
        new(target, "proj", filter, purpose, planSeconds, diskSeconds, source, plane,
            desired, acquired, accepted, disk, planCount, badge, flagged, planHours, diskHours,
            mixed, isDetail, detail, panelKey, panelLabel, panelSource,
            planTsKey: planTsKey, planEnabled: planEnabled, tsTargetKey: tsTargetKey, enabled: enabled);

    /// <summary>A one-plane TS leaf: commitment only (no disk side).</summary>
    public static ReconciliationRow Ts(string target = "T", string filter = "H", int desired = 10,
        int seconds = 300, RowSource source = RowSource.Both, bool flagged = false, string badge = "") =>
        Leaf(target, RowPlane.Ts, filter, planSeconds: seconds, diskSeconds: 0, source: source,
            desired: desired, acquired: 0, accepted: 0, disk: 0, flagged: flagged, badge: badge,
            planHours: desired * (double)seconds / 3600.0, diskHours: null);

    /// <summary>A one-plane Disk leaf: actuals only (no plan side).</summary>
    public static ReconciliationRow Disk(string target = "T", string filter = "H", int frames = 4,
        int seconds = 300, RowSource source = RowSource.Both, bool flagged = false, string badge = "",
        string? panelKey = null, string? panelLabel = null, RowSource? panelSource = null) =>
        Leaf(target, RowPlane.Disk, filter, planSeconds: 0, diskSeconds: seconds, source: source,
            desired: null, acquired: null, accepted: null, disk: frames, planCount: 0,
            flagged: flagged, badge: badge, planHours: null,
            diskHours: frames * (double)seconds / 3600.0,
            panelKey: panelKey, panelLabel: panelLabel, panelSource: panelSource);
}
