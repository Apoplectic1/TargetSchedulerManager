using System.Globalization;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Reconcile;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.ViewModels.Rows;

namespace TargetSchedulerManager.App.Services;

/// <summary>Everything the adoption UI needs before anything is written: the insert rows (one plan, or a
/// target followed by its plan), the grid-style label, and the assigned template. Nothing here has touched
/// the db; the caller routes <see cref="Rows"/> through the gate's insert path on Accept.</summary>
internal sealed record AdoptionPlan(
    IReadOnlyList<TsRowInsert> Rows,
    string Label,
    TsExposureTemplate Template,
    bool CreatesTarget);

/// <summary>One assignable template in the dialog's strict scope (the cell's filter and square binning),
/// with the merge verdict precomputed where the pairing semantics live: <see cref="WouldPair"/> mirrors the
/// reconciler's key comparison (purpose + gain/offset equality, the camera-default sentinel compared as the
/// value it is), so the dialog's caution and the refreshed grid can never disagree. A non-null
/// <see cref="MismatchReason"/> is the caution's wording.</summary>
internal sealed record AdoptionCandidate(TsExposureTemplate Template, bool WouldPair, string? MismatchReason);

/// <summary>A project the adoption may land in, carrying its profile's template scope for the dialog: the
/// candidates and the preselect index (best match first: pairs → same purpose → list order; −1 when the
/// scope is empty — Accept disables then).</summary>
internal sealed record AdoptionProjectOption(
    TsProject Project, IReadOnlyList<AdoptionCandidate> Candidates, int PreselectIndex);

/// <summary>The assignment dialog's entire input, assembled before anything is shown: the project situation
/// (locked to the owning project when the TS target exists — one entry — or the pickable list for a
/// target-creating adoption), each project's template scope, the disk cell's facts for the read-only panel,
/// and the target-creation facts (name/centroid/seeded rotation; null when the target exists).</summary>
internal sealed record AdoptionFacts(
    string Label,
    bool ProjectLocked,
    IReadOnlyList<AdoptionProjectOption> Projects,
    string Filter, string Purpose, int Gain, int Offset, int Bin, int Seconds, int DiskCount,
    string EmptyScopeReason,
    string? TargetName, double? RaHours, double? DecDegrees, double? SeededRotationDeg);

/// <summary>What the assignment dialog returns on Accept: the chosen (or locked) project and the assigned
/// template. Null from the prompt means cancel — nothing is written.</summary>
internal sealed record AdoptionChoice(TsProject Project, TsExposureTemplate Template);

/// <summary>One eligible cell's read-only facts in the bulk dialog — the same facts the per-cell dialog
/// shows, carried per row so the dialog never reaches back into the row model. The row itself travels for
/// the accepted choice; <see cref="EmptyScopeReason"/> is that cell's wording when a project's scope for it
/// is empty.</summary>
internal sealed record BulkAdoptionCell(
    ReconciliationRow Row, string Filter, string Purpose, int Gain, int Offset, int Bin, int Seconds,
    int DiskCount, string EmptyScopeReason);

/// <summary>One cell's template scope under one project's profile: the strict-scope candidates with their
/// merge verdicts and the preselect index — <see cref="AdoptionPlanner.ListCandidates"/> per (cell, project),
/// precomputed so the dialog only ever swaps lists.</summary>
internal sealed record BulkCellScope(IReadOnlyList<AdoptionCandidate> Candidates, int PreselectIndex);

/// <summary>A project the bulk adoption may land in, carrying one scope per cell (parallel to
/// <see cref="BulkAdoptionFacts.Cells"/>).</summary>
internal sealed record BulkAdoptionProjectOption(TsProject Project, IReadOnlyList<BulkCellScope> CellScopes);

/// <summary>The bulk dialog's entire input: the project situation (locked owner or pickable list — the
/// per-cell rules), every eligible cell with its per-project scopes, and the target-creation facts (null
/// when the TS target exists). Assembled before anything is shown; the dialog never queries.</summary>
internal sealed record BulkAdoptionFacts(
    string Label,
    bool ProjectLocked,
    IReadOnlyList<BulkAdoptionCell> Cells,
    IReadOnlyList<BulkAdoptionProjectOption> Projects,
    string? TargetName, double? RaHours, double? DecDegrees, double? SeededRotationDeg);

/// <summary>What the bulk dialog returns on Accept: the chosen (or locked) project and the included,
/// servable cells with their assigned templates. Null from the prompt means cancel — nothing is written.</summary>
internal sealed record BulkAdoptionChoice(
    TsProject Project,
    IReadOnlyList<(ReconciliationRow Row, TsExposureTemplate Template)> Assignments);

/// <summary>The bulk counterpart of <see cref="AdoptionPlan"/>: one insert batch (a target payload when
/// creating, then one plan per accepted cell), the grid-style label, and the counts the status line
/// reports. Nothing here has touched the db.</summary>
internal sealed record BulkAdoptionPlan(
    IReadOnlyList<TsRowInsert> Rows, string Label, bool CreatesTarget, int PlanCount);

/// <summary>
/// The pure planning half of "Add to TS" (openspec `disk-row-adoption`): decides which disk-only rows offer
/// the action, assembles the assignment dialog's facts (project options, strict-scope template candidates
/// with their merge verdicts), and builds the insert payloads for the accepted choice — all from the
/// retained load (graph + TS snapshot), no db access. The write itself goes through
/// <c>TsEditGate.ApplyInsertAsync</c>. Templates are assigned, never created or edited (obs 3dfe): TS is
/// the authoring surface; TSM only points at existing rows. Adoption has two grains sharing every rule:
/// per-cell (one row, one plan) and per-target (a rollup's eligible cells through one combined dialog,
/// one atomic batch) — the bulk members compose the per-cell ones, so the grains can never disagree.
/// </summary>
internal static class AdoptionPlanner
{
    /// <summary>
    /// Menu gating: a disk-only leaf cell (concrete seconds, no plan key) whose target has NO TS plan at the
    /// cell's (filter, purpose, seconds) — exactly the unplanned-frames condition. A disk row that split
    /// from a same-key plan (capture-config/framing disagreement) fails the no-plan test and is excluded —
    /// creating one would mint a same-key duplicate; the separation is the diagnostic. A disk-only row
    /// under a mosaic whose panel has no TS target is excluded (mosaic adoption is out of scope — creating
    /// panels needs isMosaic project wiring).
    /// </summary>
    public static bool IsEligible(ReconciliationRow row, TsPlanData ts)
    {
        if (row.Plane != RowPlane.Disk || row.PlanTsKey is not null || row.DiskSeconds <= 0 || row.Disk <= 0)
            return false;
        if (row.TsTargetKey is null && row.PanelKey is not null)
            return false;   // a disk-only panel — target creation under a mosaic is out of scope
        TsTarget? target = FindTarget(ts, row.TsTargetKey);
        if (row.TsTargetKey is not null && target is null)
            return false;   // a TS key the snapshot can't resolve — stale row; don't offer a write against it
        return target is null || !HasPlanAtCell(ts, target, row.Filter, RowPurpose(row), row.DiskSeconds);
    }

    /// <summary>The projects a created target may join — every project in the snapshot except mosaic
    /// projects (a normal target must not land in an isMosaic project's panel space). Never creates one.</summary>
    public static IReadOnlyList<TsProject> PickableProjects(TsPlanData ts) =>
        [.. ts.Projects.Where(p => p.IsMosaic == 0).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Everything the assignment dialog shows, or the refusal explaining why it can't open: the project
    /// situation (locked to the existing target's owner — which also pins every later filter of the same
    /// target to that project — or the pickable list with the target-creation facts), and per project its
    /// profile's strict-scope candidates. Refusals here are structural (stale snapshot, no centroid, no
    /// projects), not template holds — an empty template scope still opens the dialog, which says so.
    /// </summary>
    public static (AdoptionFacts? Facts, string? Refusal) GetFacts(
        ReconciliationRow row, CatalogGraph graph, TsPlanData ts)
    {
        string label = Format.Label(row.Target, row.Filter);
        TsTarget? tsTarget = FindTarget(ts, row.TsTargetKey);

        string? targetName = null;
        double? raHours = null, decDegrees = null, seededRotation = null;
        List<AdoptionProjectOption> options = [];

        if (tsTarget is not null)
        {
            TsProject? owning = ts.Projects.FirstOrDefault(p => p.Id == tsTarget.ProjectId);
            if (owning is null)
                return (null, $"{label}: the TS target's project is missing from the snapshot — reload and retry");
            options.Add(ProjectOption(owning, row, ts));
        }
        else
        {
            Astronomy.Catalog.Schema.Target? diskTarget = graph.Targets.FirstOrDefault(t => t.Id == row.TargetId);
            if (diskTarget is null)
                return (null, $"{label}: the disk target is missing from the retained graph — reload and retry");
            if (diskTarget.RaHours is not double ra || diskTarget.DecDegreesSigned is not double dec)
                return (null, $"{label}: no plate-solved centroid on disk — TS needs coordinates");

            targetName = diskTarget.Name;
            raHours = ra;
            decDegrees = dec;
            seededRotation = SeededRotation([row]);

            IReadOnlyList<TsProject> pickable = PickableProjects(ts);
            if (pickable.Count == 0)
                return (null, "no TS projects to adopt into — create one in NINA's TS editor first");
            options.AddRange(pickable.Select(p => ProjectOption(p, row, ts)));
        }

        string emptyReason = EmptyScopeReason(row);

        return (new AdoptionFacts(label, ProjectLocked: tsTarget is not null, options,
            row.Filter, RowPurpose(row).ToString(), row.Config.Gain, row.Config.Offset,
            row.Config.BinningX, row.DiskSeconds, row.Disk, emptyReason,
            targetName, raHours, decDegrees, seededRotation), null);
    }

    /// <summary>
    /// The strict template scope for one profile — same filter, same square binning only (user rule
    /// 2026-08-03: a different-binning template is a different integration and is never listed; a
    /// non-square cell has an empty scope by definition) — each candidate carrying its merge verdict, plus
    /// the preselect index: the first pairing candidate, else the first same-purpose one, else the top of
    /// the list (−1 on an empty scope).
    /// </summary>
    public static (IReadOnlyList<AdoptionCandidate> Candidates, int PreselectIndex) ListCandidates(
        ReconciliationRow row, TsPlanData ts, string profileId)
    {
        if (row.Config.BinningX != row.Config.BinningY)
            return ([], -1);

        FilterPurpose purpose = RowPurpose(row);
        List<AdoptionCandidate> candidates = [.. ts.Templates
            .Where(t => string.Equals(t.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.FilterName, row.Filter, StringComparison.OrdinalIgnoreCase)
                && t.Bin == row.Config.BinningX)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t =>
            {
                string? reason = MismatchReason(t, purpose, row.Config);
                return new AdoptionCandidate(t, WouldPair: reason is null, reason);
            })];

        int preselect = candidates.FindIndex(c => c.WouldPair);
        if (preselect < 0)
            preselect = candidates.FindIndex(c => FilterPurposeClassifier.Classify(c.Template.Name) == purpose);
        if (preselect < 0)
            preselect = candidates.Count == 0 ? -1 : 0;
        return (candidates, preselect);
    }

    /// <summary>
    /// Assembles the accepted adoption: the assigned template's plan with counts seeded by the pairing
    /// verdict (born complete when the template pairs with the cell, all zeros for a cautioned non-pairing
    /// assignment), preceded — when the target is not in TS — by the target payload from the disk centroid,
    /// landing in <paramref name="project"/>. Refusals here are unreachable after <see cref="GetFacts"/>
    /// succeeded and exist as a backstop; never writes.
    /// </summary>
    public static (AdoptionPlan? Plan, string? Refusal) Build(
        ReconciliationRow row, CatalogGraph graph, TsPlanData ts, TsProject project, TsExposureTemplate template)
    {
        TsTarget? tsTarget = FindTarget(ts, row.TsTargetKey);
        string label = Format.Label(row.Target, row.Filter);

        List<TsRowInsert> rows = [];
        object? targetReference;
        if (tsTarget is not null)
        {
            targetReference = ExistingTargetReference(tsTarget);
        }
        else
        {
            string? refusal = AppendCreatedTarget(rows, row, [row], graph, project, label);
            if (refusal is not null)
                return (null, refusal);
            targetReference = rows[0].Payload["guid"];
        }

        rows.Add(PlanInsert(row, project, template, targetReference!));
        return (new AdoptionPlan(rows, label, template, CreatesTarget: tsTarget is null), null);
    }

    /// <summary>
    /// A rollup's individually-eligible cells, grid order preserved — the bulk action's unit of work and
    /// its menu gate (any ⇒ offer). A mosaic parent has none by definition (panel/target creation under
    /// isMosaic stays out of scope, matching the per-cell exclusion).
    /// </summary>
    public static IReadOnlyList<ReconciliationRow> EligibleCells(TargetGroupRow group, TsPlanData ts) =>
        group.IsMosaic ? [] : [.. group.Children.Where(c => IsEligible(c, ts))];

    /// <summary>
    /// Everything the bulk dialog shows, or the refusal explaining why it can't open: the project
    /// situation resolved once (locked owner / pickable list + target-creation facts — the per-cell
    /// rules), every eligible cell's facts, and each cell's template scope under every offered project,
    /// all precomputed. Refusals are structural; empty per-cell scopes still open the dialog, which greys
    /// those cells with the reason.
    /// </summary>
    public static (BulkAdoptionFacts? Facts, string? Refusal) GetBulkFacts(
        TargetGroupRow group, CatalogGraph graph, TsPlanData ts)
    {
        IReadOnlyList<ReconciliationRow> eligible = EligibleCells(group, ts);
        if (eligible.Count == 0)
            return (null, $"{group.Target}: no adoptable cells — reload and retry");

        TsTarget? tsTarget = FindTarget(ts, eligible[0].TsTargetKey);
        string? targetName = null;
        double? raHours = null, decDegrees = null, seededRotation = null;
        List<TsProject> projects = [];

        if (tsTarget is not null)
        {
            TsProject? owning = ts.Projects.FirstOrDefault(p => p.Id == tsTarget.ProjectId);
            if (owning is null)
                return (null, $"{group.Target}: the TS target's project is missing from the snapshot — reload and retry");
            projects.Add(owning);
        }
        else
        {
            Astronomy.Catalog.Schema.Target? diskTarget = graph.Targets.FirstOrDefault(t => t.Id == eligible[0].TargetId);
            if (diskTarget is null)
                return (null, $"{group.Target}: the disk target is missing from the retained graph — reload and retry");
            if (diskTarget.RaHours is not double ra || diskTarget.DecDegreesSigned is not double dec)
                return (null, $"{group.Target}: no plate-solved centroid on disk — TS needs coordinates");

            targetName = diskTarget.Name;
            raHours = ra;
            decDegrees = dec;
            seededRotation = SeededRotation(eligible);

            IReadOnlyList<TsProject> pickable = PickableProjects(ts);
            if (pickable.Count == 0)
                return (null, "no TS projects to adopt into — create one in NINA's TS editor first");
            projects.AddRange(pickable);
        }

        List<BulkAdoptionCell> cells = [.. eligible.Select(r => new BulkAdoptionCell(
            r, r.Filter, RowPurpose(r).ToString(), r.Config.Gain, r.Config.Offset, r.Config.BinningX,
            r.DiskSeconds, r.Disk, EmptyScopeReason(r)))];
        List<BulkAdoptionProjectOption> options = [.. projects.Select(p => new BulkAdoptionProjectOption(
            p,
            [.. eligible.Select(r =>
            {
                (IReadOnlyList<AdoptionCandidate> candidates, int preselect) = ListCandidates(r, ts, p.ProfileId);
                return new BulkCellScope(candidates, preselect);
            })]))];

        return (new BulkAdoptionFacts(group.Target, ProjectLocked: tsTarget is not null, cells, options,
            targetName, raHours, decDegrees, seededRotation), null);
    }

    /// <summary>
    /// Assembles the accepted bulk adoption as one batch: the target payload (when the TS target doesn't
    /// exist — rotation seeded from the first included cell expressing a sky angle) followed by one plan
    /// per accepted assignment, each seeded by its own cell's pairing verdict. Any structural refusal
    /// aborts the whole batch naming the offending cell — no partial adoption is ever built. Never writes.
    /// </summary>
    public static (BulkAdoptionPlan? Plan, string? Refusal) BuildBulk(
        TargetGroupRow group, CatalogGraph graph, TsPlanData ts, BulkAdoptionChoice choice)
    {
        if (choice.Assignments.Count == 0)
            return (null, $"{group.Target}: no cells included — nothing to adopt");

        ReconciliationRow first = choice.Assignments[0].Row;
        TsTarget? tsTarget = FindTarget(ts, first.TsTargetKey);

        List<TsRowInsert> rows = [];
        object? targetReference;
        if (tsTarget is not null)
        {
            targetReference = ExistingTargetReference(tsTarget);
        }
        else
        {
            string? refusal = AppendCreatedTarget(rows, first, choice.Assignments.Select(a => a.Row),
                graph, choice.Project, Format.Label(group.Target, first.Filter));
            if (refusal is not null)
                return (null, refusal);
            targetReference = rows[0].Payload["guid"];
        }

        foreach ((ReconciliationRow row, TsExposureTemplate template) in choice.Assignments)
            rows.Add(PlanInsert(row, choice.Project, template, targetReference!));

        int count = choice.Assignments.Count;
        string label = Format.Label(group.Target, count == 1 ? "1 plan" : $"{count} plans");
        return (new BulkAdoptionPlan(rows, label, CreatesTarget: tsTarget is null, PlanCount: count), null);
    }

    // Reference by guid whenever the target has one — its integer id is only copy-stable when the row
    // itself came from a pull, and an unpushed adopted target's id is local-minted.
    private static object ExistingTargetReference(TsTarget tsTarget) =>
        (object?)tsTarget.TsGuid ?? tsTarget.Id;

    // Appends the created-target insert from the disk centroid (rotation seeded from the given cells), or
    // returns the structural refusal naming the offending cell. The payload's minted guid is the batch's
    // target reference — resolved in the same batch locally, after its insert remotely.
    private static string? AppendCreatedTarget(
        List<TsRowInsert> rows, ReconciliationRow anchor, IEnumerable<ReconciliationRow> seedCells,
        CatalogGraph graph, TsProject project, string label)
    {
        Astronomy.Catalog.Schema.Target? diskTarget = graph.Targets.FirstOrDefault(t => t.Id == anchor.TargetId);
        if (diskTarget is null)
            return $"{label}: the disk target is missing from the retained graph — reload and retry";
        if (diskTarget.RaHours is not double ra || diskTarget.DecDegreesSigned is not double dec)
            return $"{label}: no plate-solved centroid on disk — TS needs coordinates";

        Dictionary<string, object?> payload = new(StringComparer.OrdinalIgnoreCase)
        {
            ["guid"] = Guid.NewGuid().ToString(),
            ["projectid"] = (object?)project.TsGuid ?? project.Id,
            ["name"] = diskTarget.Name,
            ["active"] = 1,
            ["ra"] = ra,                 // graph coords are already TS's units (hours / signed degrees)
            ["dec"] = dec,
            ["epochcode"] = 2,           // NINA Epoch.J2000 — disk plate solves are J2000
            ["roi"] = 100.0,
            ["priority"] = -1,           // TargetPriority.Default
        };
        if (SeededRotation(seedCells) is double rot)
            payload["rotation"] = rot;
        rows.Add(new TsRowInsert(TsTable.Target, payload));
        return null;
    }

    // Counts seed by the pairing verdict — the same pure MismatchReason the dialog's caution used, on the
    // same inputs, so the promise and the payload cannot disagree. Pairing → born complete (record
    // history): desired = acquired = accepted = the cell's disk count. Non-pairing (the cautioned split) →
    // all three 0: no disk files correspond to the plan being created, and disk is truth from the plan's
    // first moment, not something the next write-back pass has to correct. The exposure override only when
    // the template default differs (the -1 defer-to-template sentinel). The template always pre-exists
    // (assignment, never creation), so its copy-stable integer id is the reference.
    private static TsRowInsert PlanInsert(
        ReconciliationRow row, TsProject project, TsExposureTemplate template, object targetReference)
    {
        int seed = MismatchReason(template, RowPurpose(row), row.Config) is null ? row.Disk : 0;
        return new(TsTable.ExposurePlan, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["guid"] = Guid.NewGuid().ToString(),
            ["profileId"] = project.ProfileId,
            ["targetid"] = targetReference,
            ["exposureTemplateId"] = template.Id,
            ["exposure"] = (int)Math.Round(template.DefaultExposure) == row.DiskSeconds ? -1.0 : (double)row.DiskSeconds,
            ["desired"] = seed,
            ["acquired"] = seed,
            ["accepted"] = seed,
            ["enabled"] = 1,
        });
    }

    // Only a sky angle seeds rotation (fold-180 normalized, the comparison space); mechanical and unknown
    // never convert. No rotation stays NULL — a rotation-less target credits any framing. Over several
    // cells, the first in grid order expressing a sky angle wins (design D6: a target's filters share one
    // framing cluster in practice; a divergent seed is harmless under the fold-180 tolerance rules).
    private static double? SeededRotation(IEnumerable<ReconciliationRow> cells)
    {
        foreach (ReconciliationRow cell in cells)
            if (cell.Config.Rotation == RotationExpression.Sky)
                return cell.Config.RotationFoldDeg;
        return null;
    }

    // The one wording for a cell whose strict template scope is empty: names the non-square bin (nothing
    // can ever serve it) or the missing filter/bin template (the remedy lives in TS).
    private static string EmptyScopeReason(ReconciliationRow row) =>
        row.Config.BinningX != row.Config.BinningY
            ? $"{row.Config.BinningX}×{row.Config.BinningY} binning — no TS template can express a non-square bin"
            : $"no {row.Filter} template at bin {row.Config.BinningX} in this profile — create one in NINA's TS editor first";

    private static AdoptionProjectOption ProjectOption(TsProject project, ReconciliationRow row, TsPlanData ts)
    {
        (IReadOnlyList<AdoptionCandidate> candidates, int preselect) = ListCandidates(row, ts, project.ProfileId);
        return new AdoptionProjectOption(project, candidates, preselect);
    }

    // The merge verdict, worded for the caution: mirrors the reconciler's plan-plane key (purpose from the
    // "Stars " name prefix; gain/offset compared as stored, the -1 camera-default sentinel included — the
    // projection keys it as the value it is, so a sentinel template lands beside an expressed disk cell).
    // Bin is equal by scope. Null means the assigned plan merges into Both.
    private static string? MismatchReason(TsExposureTemplate t, FilterPurpose purpose, RowConfig config)
    {
        // Gain/offset semantics are CaptureConfigPairing's (value equality, the camera-default sentinel
        // compared as the value it is — it pairs with nothing); this wording layer only names what differed.
        // Binning needs no line: the candidate scope is already strictly same-bin.
        List<string> reasons = [];
        FilterPurpose templatePurpose = FilterPurposeClassifier.Classify(t.Name);
        if (templatePurpose != purpose)
            reasons.Add($"purpose {templatePurpose} vs {purpose}");
        if (t.Gain != config.Gain)
            reasons.Add(t.Gain == CaptureConfigPairing.Sentinel
                ? $"camera-default gain vs {config.Gain}" : $"gain {t.Gain} vs {config.Gain}");
        if (t.Offset != config.Offset)
            reasons.Add(t.Offset == CaptureConfigPairing.Sentinel
                ? $"camera-default offset vs {config.Offset}" : $"offset {t.Offset} vs {config.Offset}");
        return reasons.Count == 0 ? null : string.Join(", ", reasons);
    }

    // A target's TS plans at one (filter, purpose, effective-seconds) cell — the no-plan-at-key predicate.
    private static bool HasPlanAtCell(TsPlanData ts, TsTarget target, string filter, FilterPurpose purpose, int seconds)
    {
        Dictionary<long, TsExposureTemplate> templates = ts.Templates.ToDictionary(t => t.Id);
        foreach (TsExposurePlan plan in ts.Plans.Where(p => p.TargetId == target.Id))
        {
            if (!templates.TryGetValue(plan.ExposureTemplateId, out TsExposureTemplate? template))
                continue;   // a dangling template ref can't define a cell
            if (string.Equals(template.FilterName, filter, StringComparison.OrdinalIgnoreCase)
                && FilterPurposeClassifier.Classify(template.Name) == purpose
                && EffectiveExposure.Seconds(plan, template) == seconds)
                return true;
        }
        return false;
    }

    private static TsTarget? FindTarget(TsPlanData ts, string? tsTargetKey) =>
        tsTargetKey is null ? null : ts.Targets.FirstOrDefault(t =>
            string.Equals(t.TsGuid, tsTargetKey, StringComparison.OrdinalIgnoreCase)
            || t.Id.ToString(CultureInfo.InvariantCulture) == tsTargetKey);

    private static FilterPurpose RowPurpose(ReconciliationRow row) =>
        Enum.TryParse(row.Purpose, ignoreCase: true, out FilterPurpose purpose) ? purpose : FilterPurpose.Light;
}
