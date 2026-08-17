using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using Astronomy.Core.Time;
using Astronomy.Diagnostics;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels.Rows;

namespace TargetSchedulerManager.App.ViewModels;

/// <summary>One toolbar Project-dropdown entry: a TS project (edit key + numeric id + display name),
/// or the <see cref="All"/> sentinel (null key/id) that keeps the press global and write-free.</summary>
public sealed record TonightProjectChoice(string? Key, long? Id, string Name)
{
    public static readonly TonightProjectChoice All = new(null, null, "All projects");
}

// The report/derived surfaces (review M4 split): the ambiguity tripwire, the Templates… picker data, and
// the Visible-tonight command — everything computed FROM the retained load rather than part of loading or
// editing it.
public sealed partial class MainViewModel
{
    // ---- Ambiguity report (the tripwire's detail — decision 2026-07-08: detect here, fix by hand in TS) ----

    /// <summary>The portfolio clock seam (AL `CONSUMERS.md` clock convention, 2026-08-11): every
    /// "now" in this VM routes through here. Settable for tests; local-time renderings derive
    /// from <c>Clock.UtcNow</c> rather than reading the ambient clock a second way.</summary>
    internal IClock Clock { get; set; } = SystemClock.Instance;

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
                load.Ts,
                new DateTimeOffset(Clock.UtcNow).ToLocalTime(), Sync.LocalPath, DefaultLibrary, DefaultToleranceDegrees,
                load.SkippedFiles)
            : null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AmbiguityCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanShowAmbiguities)));
    }

    /// <summary>The visible-target scope for a user-invoked report, or null for the full report: non-null
    /// exactly when a grid filter (search / source / flagged-only) is active, carrying the surviving
    /// targets and the filter's wording (field obs a5eb: search isolating a target should yield that
    /// target's report). The automatic tripwire count stays global — only report *generation* scopes.</summary>
    private ReportScope? CurrentReportScope()
    {
        bool filtered = !string.IsNullOrWhiteSpace(_searchText)
            || _flaggedOnly
            || _sourceFilterIndex is >= 1 and <= 3;
        if (!filtered)
            return null;

        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(_searchText)) parts.Add($"search \"{_searchText.Trim()}\"");
        if (_sourceFilterIndex is >= 1 and <= 3) parts.Add($"source {SourceFilterName()}");
        if (_flaggedOnly) parts.Add("flagged only");

        HashSet<Guid> ids = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (TargetGroupRow g in _groups)
        {
            names.Add(g.Target);
            foreach (ReconciliationRow r in g.Children)
                if (r.TargetId != Guid.Empty)
                    ids.Add(r.TargetId);
        }
        return new ReportScope(ids, names, string.Join(" · ", parts));
    }

    /// <summary>Writes the report as a dated Markdown file (default: the app's local Reports folder) and
    /// opens it in the default handler. When a grid filter is active the report is scoped to the visible
    /// targets (rebuilt for the occasion; the retained global report backs the tripwire count). Launch
    /// failure is non-fatal — the file stays, the status line says where. Returns the path, or null when
    /// nothing was written.</summary>
    internal string? WriteAmbiguityReport(string? directory = null, bool open = true)
    {
        ReportScope? scope = CurrentReportScope();
        AmbiguityReportResult? scoped = scope is not null && _lastLoad is { } load
            ? AmbiguityReport.Build(
                load.Graph, load.Report,
                WriteBackPlanner.Plan(load.Graph.Targets, load.Graph.Plans, load.Graph.Templates,
                                      load.Graph.InventoryFilters, load.Report),
                load.Ts,
                new DateTimeOffset(Clock.UtcNow).ToLocalTime(), Sync.LocalPath, DefaultLibrary, DefaultToleranceDegrees,
                load.SkippedFiles, scope)
            : null;
        if ((scoped ?? _ambiguities) is not { } ambig)
        {
            StatusText = "no load yet — nothing to report";
            return null;
        }
        string scopeNote = scoped is null ? "" : $" (scoped: {scope!.Description})";
        try
        {
            string dir = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TargetSchedulerManager", "Reports");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"ambiguities-{Clock.UtcNow.ToLocalTime():yyyyMMdd-HHmm}.md");
            File.WriteAllText(path, ambig.Markdown);
            Log.Info($"AMBIGUITY report written: {path} ({ambig.ActionCount} action item(s)){scopeNote}");

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
            StatusText = $"ambiguity report{scopeNote} — {ambig.ActionCount} action item(s): {path}";
            return path;
        }
        catch (Exception ex)
        {
            Log.Error("ambiguity report failed", ex);
            StatusText = $"ambiguity report failed: {ex.Message} — see tsm.log";
            return null;
        }
    }

    /// <summary>The Visible-Tonight button: reconciles <c>target.active</c> / <c>project.state</c>
    /// with tonight's sky — visible = a single contiguous window of at least <paramref name="minDuration"/>
    /// above <paramref name="floorAltitudeDeg"/> at the DevDefaults site (see
    /// <see cref="VisibleTonightPass"/>; both knobs come from the toolbar, defaults 30 min / 30°).
    /// Consumes the load's retained TS snapshot (no re-read), applies through the guarded gate (each flip
    /// journals like a hand edit), reloads without a pull so the grid shows the flips + marks, then
    /// reports the counts on the status line.
    /// <para>With <paramref name="scope"/> (openspec project-scoped-tonight) the press first journals
    /// the selected project's changed constraints — settings flow down — then runs both stages scoped
    /// to that project; state still rolls up from what the sky left enabled. Null scope = the
    /// All-projects press, which never writes a constraint.</para></summary>
    public async Task RunVisibleTonightAsync(
        TimeSpan minDuration, double floorAltitudeDeg, TonightScope? scope = null)
    {
        if (!TryBeginBusy())
            return;
        VisibleTonightTargetPlan targetPlan;
        VisibleTonightProjectPlan projectPlan;
        int failed;
        int constraintsWritten = 0;
        int renameFailed = 0;
        bool anythingLanded;   // reload only when a flip actually landed — an all-refused pass changed nothing
        try
        {
            if (_lastLoad is not LoadResult load)
            {
                StatusText = "no load yet — nothing to reconcile";
                return;
            }

            // The scoped constraint write, inside the same busy scope as the enable stages: only the
            // fields the caller found changed travel (the fill snapshot lives in the view). The enable
            // pass then uses the box values directly — no re-read.
            List<TsFieldEdit> constraintEdits = [];
            int altEditIndex = -1;
            if (scope is not null)
            {
                if (scope.NewMinimumTime is int newMinTime)
                    constraintEdits.Add(new TsFieldEdit(
                        TsTable.Project, scope.EditKey, "minimumtime", newMinTime, $"{scope.Name} — project"));
                if (scope.NewMinimumAltitude is double newMinAlt)
                {
                    altEditIndex = constraintEdits.Count;
                    constraintEdits.Add(new TsFieldEdit(
                        TsTable.Project, scope.EditKey, "minimumaltitude", newMinAlt, $"{scope.Name} — project"));
                }
            }
            IReadOnlyList<EditOutcome> constraintOutcomes = constraintEdits.Count > 0
                ? await _gate.ApplyManyAsync(constraintEdits)
                : [];
            constraintsWritten = constraintOutcomes.Count(o => o is EditOutcome.Applied);

            // The name clause is definitional (openspec project-name-altitude-clause): after the
            // constraint writes settle, a scoped press composes the name from the STORED altitude —
            // clause-less names gain the clause, legacy/stale clauses rewrite, an already-composed name
            // is a no-op — so the press is a nonconformance remedy with or without an altitude change.
            // A refused altitude write skips the rename entirely (compose only from values that actually
            // landed); with no altitude edit the Floor box IS the stored value (the fill snapshot). Its
            // own tiny batch so the gating is on the real outcome, not batch position.
            if (scope is not null)
            {
                bool altApplied = altEditIndex >= 0 && constraintOutcomes[altEditIndex] is EditOutcome.Applied;
                bool altRefused = altEditIndex >= 0 && !altApplied;
                double storedAlt = altApplied ? scope.NewMinimumAltitude!.Value : floorAltitudeDeg;
                if (!altRefused && VisibleTonightPass.ComposeRename(scope.Name, storedAlt) is string newName)
                {
                    IReadOnlyList<EditOutcome> renameOutcome = await _gate.ApplyManyAsync(
                        [new TsFieldEdit(TsTable.Project, scope.EditKey, "name", newName, $"{scope.Name} — project")]);
                    constraintsWritten += renameOutcome.Count(o => o is EditOutcome.Applied);
                    renameFailed = renameOutcome.Count(o => o is not EditOutcome.Applied);
                }
            }

            try
            {
                targetPlan = VisibleTonightPass.PlanTargets(
                    load.Ts, DevDefaults.Site(), Clock.UtcNow, minDuration, floorAltitudeDeg,
                    scope?.ProjectId);
            }
            catch (Exception ex)
            {
                // Fail fast, zero edits: a contract violation (e.g. a TS target without RA/Dec) aborts the
                // whole pass rather than skipping the row.
                Log.Error("VISIBLE-TONIGHT aborted before any edit", ex);
                StatusText = $"Visible tonight aborted: {ex.Message}";
                return;
            }

            // Two sequenced batches, each one worker on one editor session; the busy scope spans both, so
            // the UI-thread seam between them admits no bulk op and no row edit. Project flips derive from
            // the target flips that LANDED (a failed flip contributes the target's old state), so a per-row
            // failure can't orphan a project.state change. Per-flip failures count and log (in the gate).
            IReadOnlyList<EditOutcome> targetOutcomes = await _gate.ApplyManyAsync(
                [.. targetPlan.Edits.Select(e => new TsFieldEdit(e.Table, e.Key, e.Column, e.Value, e.Label))]);
            VisibleTonightEdit[] landed = [.. targetPlan.Edits
                .Where((_, i) => targetOutcomes[i] is EditOutcome.Applied)];

            // Always derived, even with zero target edits — a project can need a flip over already-settled targets.
            projectPlan = VisibleTonightPass.PlanProjects(load.Ts, landed, scope?.ProjectId);
            IReadOnlyList<EditOutcome> projectOutcomes = await _gate.ApplyManyAsync(
                [.. projectPlan.Edits.Select(e => new TsFieldEdit(e.Table, e.Key, e.Column, e.Value, e.Label))]);

            failed = constraintOutcomes.Count(o => o is not EditOutcome.Applied) + renameFailed
                + targetOutcomes.Count(o => o is not EditOutcome.Applied)
                + projectOutcomes.Count(o => o is not EditOutcome.Applied);
            anythingLanded = constraintsWritten > 0 || landed.Length > 0
                || projectOutcomes.Any(o => o is EditOutcome.Applied);
        }
        finally
        {
            EndBusy();
        }

        if (anythingLanded)
            await LoadAsync(PullPolicy.Never);   // after EndBusy — the reload takes the gate itself

        string scopeNote = scope is null ? "" : $" [{scope.Name}]";
        string constraintNote = constraintsWritten > 0 ? $" · {constraintsWritten} project field(s) written" : "";
        StatusText = $"Visible tonight{scopeNote}: {targetPlan.Enabled} enabled · {targetPlan.Disabled} disabled · "
            + $"{targetPlan.Unchanged} unchanged · {projectPlan.Activated + projectPlan.Deactivated} project(s) flipped"
            + constraintNote
            + (failed > 0 ? $" · {failed} FAILED — see tsm.log" : "");
        Log.Info($"VISIBLE-TONIGHT{scopeNote}: enabled={targetPlan.Enabled} disabled={targetPlan.Disabled}"
            + $" unchanged={targetPlan.Unchanged} projOn={projectPlan.Activated}"
            + $" projOff={projectPlan.Deactivated} constraints={constraintsWritten} failed={failed}");
    }

    /// <summary>A scoped Tonight press (openspec project-scoped-tonight): the selected project plus the
    /// constraint values the view found CHANGED against its fill snapshot — null members write nothing.
    /// The view owns the changed-compare because the view did the fill read.</summary>
    public sealed record TonightScope(
        long ProjectId, string EditKey, string Name, int? NewMinimumTime, double? NewMinimumAltitude);

    /// <summary>The toolbar Project dropdown's items: "All projects" first (null key), then every TS
    /// project regardless of state, name-sorted. Rebuilt after each load.</summary>
    public IReadOnlyList<TonightProjectChoice> ProjectChoices
    {
        get => _projectChoices;
        private set => Set(ref _projectChoices, value);
    }

    private IReadOnlyList<TonightProjectChoice> _projectChoices = [TonightProjectChoice.All];

    /// <summary>Rebuilds <see cref="ProjectChoices"/> from the retained load (call after
    /// <c>_lastLoad</c> changes). Every state is listed — Draft/Closed are selectable for constraint
    /// edits even though the pass never writes their lifecycle state.</summary>
    private void RefreshProjectChoices()
    {
        List<TonightProjectChoice> choices = [TonightProjectChoice.All];
        if (_lastLoad is { Ts.Projects: { } projects })
            choices.AddRange(projects
                .OrderBy(p => p.Name, NaturalComparer.Instance)
                .Select(p => new TonightProjectChoice(
                    string.IsNullOrEmpty(p.TsGuid) ? p.Id.ToString(CultureInfo.InvariantCulture) : p.TsGuid,
                    p.Id, p.Name)));
        ProjectChoices = choices;
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
