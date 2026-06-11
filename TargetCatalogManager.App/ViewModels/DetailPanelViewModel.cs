using System.Globalization;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Schema;

namespace TargetCatalogManager.App.ViewModels;

/// <summary>
/// The "dossier" for one selected target: its disk ACTUAL (inventory) beside its Target Scheduler PLAN, built
/// from the retained <see cref="CatalogGraph"/> (all already in memory). v1 is read-only; field editing layers
/// on next. The grid is the index; this is the page.
/// </summary>
public sealed class DetailPanelViewModel
{
    public DetailPanelViewModel(CatalogGraph graph, Guid targetId)
    {
        Target t = graph.Targets.First(x => x.Id == targetId);

        Title = t.Name;
        SourceText = t.Source switch
        {
            TargetSource.Both => "Both — on disk and in TS",
            TargetSource.Planned => "TS only — planned, not yet shot",
            _ => "Disk only — shot, no TS plan",
        };
        DirectoryText = t.DirectoryName ?? "—";
        CoordsText = FormatCoords(t.RaHours, t.DecDegreesSigned, t.Epoch);
        RotationText = t.RotationDeg is double rot ? $"{rot:0.#}°" : "—";
        RoiText = t.RoiPercent is double roi ? $"{roi:0.#} %" : "—";
        PriorityText = t.Priority?.ToString() ?? "—";

        DiskRows = [.. graph.InventoryFilters
            .Where(i => i.TargetId == targetId)
            .OrderBy(i => i.FilterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Purpose)
            .ThenBy(i => i.ExposureSeconds)
            .Select(i => new DiskDossierRow(i))];

        Dictionary<Guid, ExposureTemplate> templates = graph.Templates.ToDictionary(x => x.Id);
        PlanRows = [.. graph.Plans
            .Where(p => p.TargetId == targetId)
            .Select(p => new PlanDossierRow(p, templates.GetValueOrDefault(p.ExposureTemplateId)))];

        Project? project = t.ProjectId is Guid pid ? graph.Projects.FirstOrDefault(x => x.Id == pid) : null;
        ProjectName = project?.Name ?? "—";
        ProjectText = project is null ? "(no TS project)" : FormatProject(project);

        HasDisk = DiskRows.Count > 0;
        HasPlan = PlanRows.Count > 0;
    }

    public string Title { get; }
    public string SourceText { get; }
    public string DirectoryText { get; }
    public string CoordsText { get; }
    public string RotationText { get; }
    public string RoiText { get; }
    public string PriorityText { get; }
    public string ProjectName { get; }
    public string ProjectText { get; }
    public IReadOnlyList<DiskDossierRow> DiskRows { get; }
    public IReadOnlyList<PlanDossierRow> PlanRows { get; }
    public bool HasDisk { get; }
    public bool HasPlan { get; }

    // RA decimal hours -> HhMmSs; Dec signed degrees -> ±D°M'S".
    private static string FormatCoords(double? raHours, double? decDeg, Epoch epoch)
    {
        if (raHours is not double ra || decDeg is not double dec) return "—";
        int rh = (int)ra, rm = (int)((ra - rh) * 60); double rs = (ra - rh - rm / 60.0) * 3600;
        double ad = Math.Abs(dec); int dd = (int)ad, dm = (int)((ad - dd) * 60); double ds = (ad - dd - dm / 60.0) * 3600;
        string sign = dec < 0 ? "-" : "+";
        return $"RA {rh:00}h{rm:00}m{rs:00.#}s   Dec {sign}{dd:00}°{dm:00}'{ds:00.#}\"   ({epoch})";
    }

    private static string FormatProject(Project p)
    {
        List<string> parts = [$"state {p.State}", $"priority {p.Priority}"];
        if (p.MinimumAltitudeDeg is double mn) parts.Add($"min alt {mn:0.#}°");
        if (p.MaximumAltitudeDeg is double mx) parts.Add($"max alt {mx:0.#}°");
        if (p.UseCustomHorizon) parts.Add($"custom horizon{(p.HorizonOffsetDeg is double h ? $" {h:0.#}°" : "")}");
        if (p.MeridianWindowMinutes is int mw) parts.Add($"meridian ±{mw}m");
        if (p.EnableGrader) parts.Add("grader on");
        return string.Join("  ·  ", parts);
    }
}

/// <summary>One disk-inventory row in the dossier (read-only ACTUAL).</summary>
public sealed class DiskDossierRow
{
    public DiskDossierRow(InventoryFilter i)
    {
        Filter = i.FilterName;
        Purpose = i.Purpose.ToString();
        Seconds = $"{i.ExposureSeconds:0}s";
        Frames = i.ExposureCount.ToString(CultureInfo.InvariantCulture);
        Hours = (i.TotalIntegrationSeconds / 3600.0).ToString("0.0", CultureInfo.InvariantCulture);
        GainOffset = $"{i.TypicalGain}/{i.TypicalOffset}";
        Binning = $"{i.TypicalBinningX}×{i.TypicalBinningY}";
        Cameras = i.Cameras;
        Dates = $"{Date(i.FirstImagedAt)} – {Date(i.LastImagedAt)}";
    }

    public string Filter { get; }
    public string Purpose { get; }
    public string Seconds { get; }
    public string Frames { get; }
    public string Hours { get; }
    public string GainOffset { get; }
    public string Binning { get; }
    public string Cameras { get; }
    public string Dates { get; }

    private static string Date(long unix) =>
        unix <= 0 ? "—" : DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("yyyy-MM-dd");
}

/// <summary>One TS exposure-plan row in the dossier (read-only PLAN; desired becomes editable next).</summary>
public sealed class PlanDossierRow
{
    public PlanDossierRow(ExposurePlan p, ExposureTemplate? t)
    {
        Filter = t?.FilterName ?? "—";
        Template = t?.Name ?? "—";
        GainOffset = t is null ? "—" : $"{t.Gain?.ToString() ?? "?"}/{t.OffsetAdu?.ToString() ?? "?"}";
        Binning = t?.Binning is int b ? $"{b}×{b}" : "—";
        double? secs = p.ExposureSeconds ?? t?.DefaultExposureSeconds;
        Exposure = secs is double s ? $"{s:0}s" : "—";
        Desired = p.DesiredCount.ToString(CultureInfo.InvariantCulture);
        Acquired = p.AcquiredCount.ToString(CultureInfo.InvariantCulture);
        Accepted = p.AcceptedCount.ToString(CultureInfo.InvariantCulture);
        EnabledText = p.Enabled ? "on" : "off";
    }

    public string Filter { get; }
    public string Template { get; }
    public string GainOffset { get; }
    public string Binning { get; }
    public string Exposure { get; }
    public string Desired { get; }
    public string Acquired { get; }
    public string Accepted { get; }
    public string EnabledText { get; }
}
