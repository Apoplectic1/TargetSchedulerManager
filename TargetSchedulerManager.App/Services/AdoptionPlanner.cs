using System.Globalization;
using Astronomy.Catalog.Build;
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

/// <summary>
/// The pure planning half of "Add to TS" (openspec `disk-row-adoption`): decides which disk-only rows offer
/// the action, assembles the assignment dialog's facts (project options, strict-scope template candidates
/// with their merge verdicts), and builds the insert payloads for the accepted choice — all from the
/// retained load (graph + TS snapshot), no db access. The write itself goes through
/// <c>TsEditGate.ApplyInsertAsync</c>. Templates are assigned, never created or edited (obs 3dfe): TS is
/// the authoring surface; TSM only points at existing rows.
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
            // Only a sky angle seeds rotation (fold-180 normalized, the comparison space); mechanical and
            // unknown never convert. No rotation stays NULL — a rotation-less target credits any framing.
            seededRotation = row.Config.Rotation == RotationExpression.Sky ? row.Config.RotationFoldDeg : null;

            IReadOnlyList<TsProject> pickable = PickableProjects(ts);
            if (pickable.Count == 0)
                return (null, "no TS projects to adopt into — create one in NINA's TS editor first");
            options.AddRange(pickable.Select(p => ProjectOption(p, row, ts)));
        }

        string emptyReason = row.Config.BinningX != row.Config.BinningY
            ? $"{row.Config.BinningX}×{row.Config.BinningY} binning — no TS template can express a non-square bin"
            : $"no {row.Filter} template at bin {row.Config.BinningX} in this profile — create one in NINA's TS editor first";

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
    /// Assembles the accepted adoption: the assigned template's plan with born-complete counts, preceded —
    /// when the target is not in TS — by the target payload from the disk centroid, landing in
    /// <paramref name="project"/>. Refusals here are unreachable after <see cref="GetFacts"/> succeeded and
    /// exist as a backstop; never writes.
    /// </summary>
    public static (AdoptionPlan? Plan, string? Refusal) Build(
        ReconciliationRow row, CatalogGraph graph, TsPlanData ts, TsProject project, TsExposureTemplate template)
    {
        TsTarget? tsTarget = FindTarget(ts, row.TsTargetKey);
        string label = Format.Label(row.Target, row.Filter);

        string planGuid = Guid.NewGuid().ToString();
        List<TsRowInsert> rows = [];
        object targetReference;

        if (tsTarget is not null)
        {
            // Reference by guid whenever the target has one — its integer id is only copy-stable when the
            // row itself came from a pull, and an unpushed adopted target's id is local-minted.
            targetReference = (object?)tsTarget.TsGuid ?? tsTarget.Id;
        }
        else
        {
            Astronomy.Catalog.Schema.Target? diskTarget = graph.Targets.FirstOrDefault(t => t.Id == row.TargetId);
            if (diskTarget is null)
                return (null, $"{label}: the disk target is missing from the retained graph — reload and retry");
            if (diskTarget.RaHours is not double ra || diskTarget.DecDegreesSigned is not double dec)
                return (null, $"{label}: no plate-solved centroid on disk — TS needs coordinates");

            // Only a sky angle seeds rotation (fold-180 normalized, the comparison space); mechanical and
            // unknown never convert. No rotation stays NULL — a rotation-less target credits any framing.
            double? seededRotation = row.Config.Rotation == RotationExpression.Sky ? row.Config.RotationFoldDeg : null;

            string targetGuid = Guid.NewGuid().ToString();
            Dictionary<string, object?> targetPayload = new(StringComparer.OrdinalIgnoreCase)
            {
                ["guid"] = targetGuid,
                ["projectid"] = (object?)project.TsGuid ?? project.Id,
                ["name"] = diskTarget.Name,
                ["active"] = 1,
                ["ra"] = ra,                 // graph coords are already TS's units (hours / signed degrees)
                ["dec"] = dec,
                ["epochcode"] = 2,           // NINA Epoch.J2000 — disk plate solves are J2000
                ["roi"] = 100.0,
                ["priority"] = -1,           // TargetPriority.Default
            };
            if (seededRotation is double rot)
                targetPayload["rotation"] = rot;
            rows.Add(new TsRowInsert(TsTable.Target, targetPayload));
            targetReference = targetGuid;    // resolved in the same batch locally, after its insert remotely
        }

        // Born complete (record history): desired = acquired = accepted = the cell's disk count. The
        // exposure override only when the template default differs (the -1 defer-to-template sentinel).
        // The template always pre-exists (assignment, never creation), so its copy-stable integer id is
        // the reference.
        rows.Add(new TsRowInsert(TsTable.ExposurePlan, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["guid"] = planGuid,
            ["profileId"] = project.ProfileId,
            ["targetid"] = targetReference,
            ["exposureTemplateId"] = template.Id,
            ["exposure"] = (int)Math.Round(template.DefaultExposure) == row.DiskSeconds ? -1.0 : (double)row.DiskSeconds,
            ["desired"] = row.Disk,
            ["acquired"] = row.Disk,
            ["accepted"] = row.Disk,
            ["enabled"] = 1,
        }));

        return (new AdoptionPlan(rows, label, template, CreatesTarget: tsTarget is null), null);
    }

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
        List<string> reasons = [];
        FilterPurpose templatePurpose = FilterPurposeClassifier.Classify(t.Name);
        if (templatePurpose != purpose)
            reasons.Add($"purpose {templatePurpose} vs {purpose}");
        if (t.Gain != config.Gain)
            reasons.Add(t.Gain < 0 ? $"camera-default gain vs {config.Gain}" : $"gain {t.Gain} vs {config.Gain}");
        if (t.Offset != config.Offset)
            reasons.Add(t.Offset < 0 ? $"camera-default offset vs {config.Offset}" : $"offset {t.Offset} vs {config.Offset}");
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
