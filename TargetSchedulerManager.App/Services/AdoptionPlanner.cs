using System.Globalization;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.ViewModels.Rows;

namespace TargetSchedulerManager.App.Services;

/// <summary>Everything the adoption UI needs before anything is written: the insert rows (one plan, or a
/// target followed by its plan), the grid-style label, the matched template, and — when a target will be
/// created — the confirm-dialog facts (name, disk centroid, seeded rotation). Nothing here has touched the
/// db; the caller routes <see cref="Rows"/> through the gate's insert path on confirm.</summary>
internal sealed record AdoptionPlan(
    IReadOnlyList<TsRowInsert> Rows,
    string Label,
    TsExposureTemplate Template,
    bool CreatesTarget,
    string? TargetName,
    double? RaHours,
    double? DecDegrees,
    double? SeededRotationDeg);

/// <summary>A zero-template-match hold's escape hatch: the values a created template would carry (the
/// cell's expressed capture config; default exposure = the cell's seconds so the plan defers via the −1
/// sentinel) plus the donor whose non-key policy fields (moon avoidance, twilight, dither…) it clones —
/// always a same-profile template, preferably the same filter/purpose family. Offered, never automatic:
/// the user confirms in the hold dialog.</summary>
internal sealed record TemplateCreateOffer(
    string ProposedName, string ProfileId, string DonorTsKey, string DonorName,
    int Gain, int Offset, int Bin, int Seconds);

/// <summary>Why an adoption did not proceed, plus the create offer when the hold is a zero-match a new
/// template would resolve (ambiguity, non-square bin, and missing-centroid holds carry none).</summary>
internal sealed record AdoptionHold(string Message, TemplateCreateOffer? Offer = null);

/// <summary>A template the caller decided to create (offer confirmed + donor fields read): the full column
/// payload for the insert, the minted guid the plan references it by, and the display facts.</summary>
internal sealed record PendingTemplate(
    IReadOnlyDictionary<string, object?> Payload, string Guid, string Name, double DefaultExposure);

/// <summary>
/// The pure planning half of "Add to TS" (openspec `disk-row-adoption`): decides which disk-only rows offer
/// the action, matches the exposure template by the pairing rule, and assembles the insert payloads — all
/// from the retained load (graph + TS snapshot), no db access. The write itself goes through
/// <c>TsEditGate.ApplyInsertAsync</c>.
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

    /// <summary>The confirm-dialog seeds for a target-creating adoption: the disk target's name, its
    /// plate-solved centroid (TS units), and the rotation a sky framing would seed. Null when the graph
    /// cannot supply a centroid — the caller surfaces <see cref="Build"/>'s refusal instead of a dialog.</summary>
    public static (string Name, double RaHours, double DecDegrees, double? SeededRotationDeg)? TargetFacts(
        ReconciliationRow row, CatalogGraph graph) =>
        graph.Targets.FirstOrDefault(t => t.Id == row.TargetId) is
            { RaHours: double ra, DecDegreesSigned: double dec } target
            ? (target.Name, ra, dec,
                row.Config.Rotation == RotationExpression.Sky ? row.Config.RotationFoldDeg : null)
            : null;

    /// <summary>The projects a created target may join — every project in the snapshot except mosaic
    /// projects (a normal target must not land in an isMosaic project's panel space). Never creates one.</summary>
    public static IReadOnlyList<TsProject> PickableProjects(TsPlanData ts) =>
        [.. ts.Projects.Where(p => p.IsMosaic == 0).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Assembles the adoption for an eligible row: template auto-match (hold when unclear), born-complete
    /// counts, and — when the target is not in TS — the target payload from the disk centroid
    /// (<paramref name="project"/> is required then, and ignored on a TS-known target). A confirmed
    /// template-create offer re-enters as <paramref name="pending"/>: matching is skipped, the template's
    /// insert leads the batch, and the plan references it by guid. Returns the plan or a hold; never writes.
    /// </summary>
    public static (AdoptionPlan? Plan, AdoptionHold? Hold) Build(
        ReconciliationRow row, CatalogGraph graph, TsPlanData ts, TsProject? project,
        PendingTemplate? pending = null)
    {
        TsTarget? tsTarget = FindTarget(ts, row.TsTargetKey);
        string label = Format.Label(row.Target, row.Filter);

        // The profile scopes the template space: the existing target's project's profile, or the picked one.
        TsProject? owningProject = tsTarget is not null
            ? ts.Projects.FirstOrDefault(p => p.Id == tsTarget.ProjectId)
            : project;
        if (owningProject is null)
            return (null, new AdoptionHold(tsTarget is not null
                ? $"{label}: the TS target's project is missing from the snapshot — reload and retry"
                : $"{label}: pick a project for the new target"));

        TsExposureTemplate template;
        if (pending is null)
        {
            (TsExposureTemplate? matched, AdoptionHold? hold) = MatchTemplate(row, ts, owningProject.ProfileId, label);
            if (matched is null)
                return (null, hold);
            template = matched;
        }
        else
        {
            // A display-side stand-in for the not-yet-inserted template (Id 0 — the payload's guid is the
            // reference the plan uses; the review and status line only need the name/values).
            template = new TsExposureTemplate(0, owningProject.ProfileId, pending.Name, row.Filter,
                row.Config.Gain, row.Config.Offset, row.Config.BinningX, pending.DefaultExposure);
        }

        string planGuid = Guid.NewGuid().ToString();
        List<TsRowInsert> rows = [];
        if (pending is not null)
            rows.Add(new TsRowInsert(TsTable.ExposureTemplate, pending.Payload));
        string? targetName = null;
        double? raHours = null, decDegrees = null, seededRotation = null;
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
                return (null, new AdoptionHold($"{label}: the disk target is missing from the retained graph — reload and retry"));
            if (diskTarget.RaHours is not double ra || diskTarget.DecDegreesSigned is not double dec)
                return (null, new AdoptionHold($"{label}: no plate-solved centroid on disk — TS needs coordinates"));

            targetName = diskTarget.Name;
            raHours = ra;
            decDegrees = dec;
            // Only a sky angle seeds rotation (fold-180 normalized, the comparison space); mechanical and
            // unknown never convert. No rotation stays NULL — a rotation-less target credits any framing.
            seededRotation = row.Config.Rotation == RotationExpression.Sky ? row.Config.RotationFoldDeg : null;

            string targetGuid = Guid.NewGuid().ToString();
            Dictionary<string, object?> targetPayload = new(StringComparer.OrdinalIgnoreCase)
            {
                ["guid"] = targetGuid,
                ["projectid"] = (object?)owningProject.TsGuid ?? owningProject.Id,
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
        // Template reference: guid for a same-batch creation (its local id doesn't exist yet, and the
        // remote's never will match); the copy-stable integer id for a template that came from a pull.
        rows.Add(new TsRowInsert(TsTable.ExposurePlan, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["guid"] = planGuid,
            ["profileId"] = owningProject.ProfileId,
            ["targetid"] = targetReference,
            ["exposureTemplateId"] = pending is not null ? pending.Guid : template.Id,
            ["exposure"] = (int)Math.Round(template.DefaultExposure) == row.DiskSeconds ? -1.0 : (double)row.DiskSeconds,
            ["desired"] = row.Disk,
            ["acquired"] = row.Disk,
            ["accepted"] = row.Disk,
            ["enabled"] = 1,
        }));

        return (new AdoptionPlan(rows, label, template, CreatesTarget: tsTarget is null,
            targetName, raHours, decDegrees, seededRotation), null);
    }

    // The pairing rule as a template filter: same filter, same purpose (the "Stars " name-prefix
    // convention), and gain/offset/bin EXPRESSED AND EQUAL to the cell's. A -1 use-camera-default
    // sentinel never pairs — the merge rule's honest reading (capture-config-keys): nothing can be
    // asserted to agree with an unspecified value, so a plan from such a template would land beside the
    // disk row, not merge with it. Exactly one candidate proceeds; zero or several hold with a message —
    // a zero-match hold carries a create OFFER (a template minted from the cell's numbers, policy fields
    // cloned from a same-profile donor), decided by the user in the hold dialog, never automatically.
    private static (TsExposureTemplate? Template, AdoptionHold? Hold) MatchTemplate(
        ReconciliationRow row, TsPlanData ts, string profileId, string label)
    {
        if (row.Config.BinningX != row.Config.BinningY)
            return (null, new AdoptionHold(
                $"{label}: {row.Config.BinningX}×{row.Config.BinningY} binning — no TS template can express a non-square bin"));

        FilterPurpose purpose = RowPurpose(row);
        List<TsExposureTemplate> profileTemplates = [.. ts.Templates.Where(t =>
            string.Equals(t.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))];
        List<TsExposureTemplate> family = [.. profileTemplates.Where(t =>
            string.Equals(t.FilterName, row.Filter, StringComparison.OrdinalIgnoreCase)
            && FilterPurposeClassifier.Classify(t.Name) == purpose)];
        List<TsExposureTemplate> candidates = [.. family.Where(t =>
            t.Bin == row.Config.BinningX && t.Gain == row.Config.Gain && t.Offset == row.Config.Offset)];

        if (candidates.Count == 1)
            return (candidates[0], null);
        if (candidates.Count > 1)
            return (null, new AdoptionHold(
                $"{label}: {candidates.Count} templates match ({string.Join(", ", candidates.Select(c => $"'{c.Name}'"))}) "
                + "— ambiguous; adopt after disambiguating in TS"));

        // Zero matched — name the nearest misses so the fix is obvious, and offer to create the missing
        // template from the cell's numbers (the donor lends its policy fields: moon avoidance, twilight,
        // dither…). Historical cells shot under a configuration no current template expresses are the
        // normal case here, not the exception.
        string near = family.Count == 0 ? "" : " — close: " + string.Join(", ", family.Select(t =>
            $"'{t.Name}' ({(t.Gain < 0 ? "camera-default gain" : $"gain {t.Gain}")}, "
            + $"{(t.Offset < 0 ? "camera-default offset" : $"offset {t.Offset}")})"));
        return (null, new AdoptionHold(
            $"{label}: no template matches (filter {row.Filter}, {purpose}, gain {row.Config.Gain}, "
            + $"offset {row.Config.Offset}, bin {row.Config.BinningX}){near}",
            BuildCreateOffer(row, purpose, profileId, profileTemplates, family)));
    }

    // The create offer for a zero-match hold: proposed name from the cell's numbers ("Stars B g53 o10",
    // "H g100 o50 2x2"), donor = a family template when one exists (same filter/purpose — the closest
    // policy source), else any same-profile template. No donor (empty profile) or an unresolvable name
    // collision → no offer; the hold stands alone.
    private static TemplateCreateOffer? BuildCreateOffer(
        ReconciliationRow row, FilterPurpose purpose, string profileId,
        List<TsExposureTemplate> profileTemplates, List<TsExposureTemplate> family)
    {
        TsExposureTemplate? donor = family.FirstOrDefault() ?? profileTemplates.FirstOrDefault();
        if (donor is null)
            return null;

        string baseName = (purpose == FilterPurpose.Stars ? FilterPurposeClassifier.StarsPrefix : "")
            + $"{row.Filter} g{row.Config.Gain} o{row.Config.Offset}"
            + (row.Config.BinningX > 1 ? $" {row.Config.BinningX}x{row.Config.BinningX}" : "");
        bool Taken(string name) => profileTemplates.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        string name = !Taken(baseName) ? baseName : $"{baseName} (adopted)";
        if (Taken(name))
            return null;   // both names taken by different-valued templates — hand-resolve in TS

        return new TemplateCreateOffer(name, profileId, donor.Id.ToString(CultureInfo.InvariantCulture),
            donor.Name, row.Config.Gain, row.Config.Offset, row.Config.BinningX, row.DiskSeconds);
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
