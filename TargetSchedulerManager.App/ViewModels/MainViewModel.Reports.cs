using System.ComponentModel;
using System.Diagnostics;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using Astronomy.Diagnostics;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;

namespace TargetSchedulerManager.App.ViewModels;

// The report/derived surfaces (review M4 split): the ambiguity tripwire, the Templates… picker data, and
// the Visible-tonight command — everything computed FROM the retained load rather than part of loading or
// editing it.
public sealed partial class MainViewModel
{
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

    /// <summary>The Visible-Tonight Find button: reconciles <c>target.active</c> / <c>project.state</c>
    /// with tonight's sky — visible = a single contiguous window of at least <paramref name="minDuration"/>
    /// above <paramref name="horizonAltitudeDeg"/> at the DevDefaults site (see
    /// <see cref="VisibleTonightPass"/>; both knobs come from the toolbar, defaults 30 min / 30°).
    /// Consumes the load's retained TS snapshot (no re-read), applies through the guarded gate (each flip
    /// journals like a hand edit), reloads without a pull so the grid shows the flips + marks, then
    /// reports the counts on the status line.</summary>
    public async Task RunVisibleTonightAsync(TimeSpan minDuration, double horizonAltitudeDeg)
    {
        if (!TryBeginBusy())
            return;
        VisibleTonightPlan plan;
        int failed;
        try
        {
            if (_lastLoad is not LoadResult load)
            {
                StatusText = "no load yet — nothing to reconcile";
                return;
            }

            try
            {
                plan = VisibleTonightPass.Plan(
                    load.Ts, DevDefaults.Site(), DateTime.UtcNow, minDuration, horizonAltitudeDeg);
            }
            catch (Exception ex)
            {
                // Fail fast, zero edits: a contract violation (e.g. a TS target without RA/Dec) aborts the
                // whole pass rather than skipping the row.
                Log.Error("VISIBLE-TONIGHT aborted before any edit", ex);
                StatusText = $"Visible tonight aborted: {ex.Message}";
                return;
            }

            // One worker, one editor session for the whole pass — no UI-thread re-entry between flips, so
            // the busy scope has no seams. Per-flip failures count and log (in the gate); the rest apply.
            IReadOnlyList<EditOutcome> outcomes = await _gate.ApplyManyAsync(
                [.. plan.Edits.Select(e => new TsFieldEdit(e.Table, e.Key, e.Column, e.Value, e.Label))]);
            failed = outcomes.Count(o => o is not EditOutcome.Applied);
        }
        finally
        {
            EndBusy();
        }

        if (plan.Edits.Count > 0)
            await LoadAsync(PullPolicy.Never);   // after EndBusy — the reload takes the gate itself

        StatusText = $"Visible tonight: {plan.TargetsEnabled} enabled · {plan.TargetsDisabled} disabled · "
            + $"{plan.TargetsUnchanged} unchanged · {plan.ProjectsActivated + plan.ProjectsDeactivated} project(s) flipped"
            + (failed > 0 ? $" · {failed} FAILED — see tsm.log" : "");
        Log.Info($"VISIBLE-TONIGHT: enabled={plan.TargetsEnabled} disabled={plan.TargetsDisabled}"
            + $" unchanged={plan.TargetsUnchanged} projOn={plan.ProjectsActivated}"
            + $" projOff={plan.ProjectsDeactivated} failed={failed}");
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
}
