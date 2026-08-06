using System.Globalization;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.TargetScheduler;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Session;

namespace TargetSchedulerManager.App.Services;

/// <summary>One flip the Visible-tonight pass wants applied — the field coordinate + value in exactly the
/// shape <c>TsEditGate.ApplyAsync</c> takes, plus the grid-style label the journal / push review shows.</summary>
internal sealed record VisibleTonightEdit(TsTable Table, string Key, string Column, int Value, string Label);

/// <summary>Stage 1's decision set: the <c>target.active</c> flips plus the target-side summary counts
/// the status line reports.</summary>
internal sealed record VisibleTonightTargetPlan(
    IReadOnlyList<VisibleTonightEdit> Edits,
    int Enabled,
    int Disabled,
    int Unchanged);

/// <summary>Stage 2's decision set: the <c>project.state</c> flips derived from the target flips that
/// actually landed, plus the project-side summary counts.</summary>
internal sealed record VisibleTonightProjectPlan(
    IReadOnlyList<VisibleTonightEdit> Edits,
    int Activated,
    int Deactivated);

/// <summary>
/// The Visible-tonight pass: reconciles <c>target.active</c> / <c>project.state</c> with tonight's sky.
/// A target is "visible tonight" iff it has a single contiguous window of at least the caller's minimum
/// duration above the caller's altitude floor (the toolbar's Duration/Floor knobs, both defaulting 30)
/// between tonight's astronomical dusk and dawn — deliberately independent of TS's own per-project
/// altitude rules, which TS re-applies at plan time. Pure planning: consumes the load's retained
/// <see cref="TsPlanData"/> rows and returns the edits; the caller applies them through the guarded edit
/// gate so they journal like hand edits. Two stages: <see cref="PlanTargets"/> computes the target flips;
/// after the caller applies them, <see cref="PlanProjects"/> derives the project flips from the target
/// edits that actually landed — a failed flip contributes the target's old value, never the intent.
/// </summary>
/// <remarks>
/// "Tonight" is <see cref="NightCalculator.ComputeNight"/>'s bracket convention: the window whose dawn is
/// the next dawn at-or-after <c>utcNow</c> — the current night when pressed after dusk, the upcoming night
/// when pressed in daylight.
/// </remarks>
internal static class VisibleTonightPass
{
    // TS project.state codes (TsEditableSchema "ProjectState": 0 Draft · 1 Active · 2 Inactive · 3 Closed).
    // Only the Active/Inactive pair is ever read or written BY STAGE 2; stage 1 flips every project's
    // targets regardless of state (enables = sky truth; lifecycle separate — obs session 2026-08-05).
    private const int StateActive = 1;
    private const int StateInactive = 2;

    /// <summary>
    /// Stage 1: computes the <c>target.active</c> flips for one button press. Target enables are sky
    /// truth, a separate concept from the project lifecycle (openspec project-scoped-tonight, user
    /// decision): EVERY project's targets are evaluated regardless of project state — narrowed to one
    /// project when <paramref name="onlyProjectId"/> scopes the press. Each target's <c>active</c>
    /// tracks its visibility verdict; unchanged fields yield no edit.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A processed target has no RA/Dec — a TS contract violation; the pass aborts before any edit.
    /// </exception>
    public static VisibleTonightTargetPlan PlanTargets(
        TsPlanData ts, Location site, DateTime utcNow, TimeSpan minDuration, double floorAltitudeDeg,
        long? onlyProjectId = null)
    {
        NightWindow night = NightCalculator.ComputeNight(site, utcNow);
        ScalarHorizonProfile altitudeFloor = new(floorAltitudeDeg);

        List<VisibleTonightEdit> targetEdits = [];
        int enabled = 0, disabled = 0, unchanged = 0;
        HashSet<long> processedProjects = TargetUniverse(ts, onlyProjectId);

        foreach (TsTarget target in ts.Targets)
        {
            if (target.ProjectId is not long projectId || !processedProjects.Contains(projectId))
                continue;

            if (target.Ra is not double raHours || target.Dec is not double decDegrees)
                throw new InvalidOperationException(
                    $"TS target '{target.Name}' (Id {target.Id}) has no RA/Dec — aborting the Visible-tonight pass");

            bool visible = CoarseVisibility.IsAboveHorizonForAtLeast(
                new Astronomy.Core.Targets.Target(
                    target.Name, raHours, decDegrees, north: true, directory: null, enabled: true),
                site, night, altitudeFloor, minDuration);

            bool currentlyActive = target.Active != 0;
            if (visible == currentlyActive)
            {
                unchanged++;
                continue;
            }

            if (visible) enabled++; else disabled++;
            targetEdits.Add(new VisibleTonightEdit(
                TsTable.Target, EditKey(target.TsGuid, target.Id), "active", visible ? 1 : 0, target.Name));
        }

        return new VisibleTonightTargetPlan(targetEdits, enabled, disabled, unchanged);
    }

    /// <summary>
    /// Stage 2: derives the <c>project.state</c> flips from the target flips that actually landed.
    /// Each processed target's effective <c>active</c> is the applied edit's value when one landed for
    /// its key, else the snapshot value — so a refused/failed flip contributes the target's OLD state,
    /// never the intent (overlay-on-snapshot: only the snapshot knows a failed flip's surviving value).
    /// Each project's <c>state</c> then follows "no effectively enabled targets → Inactive, any →
    /// Active". Pure derivation — no visibility math, cannot throw. Unlike stage 1, the universe here
    /// keeps the Active/Inactive gate: a Draft/Closed project's targets may have flipped, but its
    /// lifecycle state is never derived or written (openspec project-scoped-tonight).
    /// </summary>
    public static VisibleTonightProjectPlan PlanProjects(
        TsPlanData ts, IReadOnlyList<VisibleTonightEdit> appliedTargetEdits, long? onlyProjectId = null)
    {
        Dictionary<string, int> appliedByKey = appliedTargetEdits.ToDictionary(e => e.Key, e => e.Value);
        HashSet<long> processedProjects = ProcessedProjects(ts, onlyProjectId);

        Dictionary<long, bool> anyEnabledByProject = processedProjects.ToDictionary(id => id, _ => false);
        foreach (TsTarget target in ts.Targets)
        {
            if (target.ProjectId is not long projectId || !processedProjects.Contains(projectId))
                continue;

            int effectiveActive = appliedByKey.TryGetValue(EditKey(target.TsGuid, target.Id), out int landed)
                ? landed
                : target.Active;
            if (effectiveActive != 0)
                anyEnabledByProject[projectId] = true;
        }

        List<VisibleTonightEdit> projectEdits = [];
        int activated = 0, deactivated = 0;
        foreach (TsProject project in ts.Projects)
        {
            if (!processedProjects.Contains(project.Id))
                continue;

            int desiredState = anyEnabledByProject[project.Id] ? StateActive : StateInactive;
            if (project.State == desiredState)
                continue;

            if (desiredState == StateActive) activated++; else deactivated++;
            projectEdits.Add(new VisibleTonightEdit(
                TsTable.Project, EditKey(project.TsGuid, project.Id), "state", desiredState,
                $"{project.Name} — project"));
        }

        return new VisibleTonightProjectPlan(projectEdits, activated, deactivated);
    }

    /// <summary>The name a project should carry after its <c>minimumaltitude</c> is written to
    /// <paramref name="newAltitudeDeg"/>, or null when no rename is due: names carrying a trailing
    /// altitude clause — short "… - 30" or legacy "… - Above 30" — track the field (the clause is
    /// authoring metadata — the name must not lie about the constraint); a name WITHOUT the clause is
    /// left alone (the press never invents a naming convention), and an already-accurate name yields
    /// no edit. Rewrites always emit the SHORT form (user 2026-08-06 — UI space), so a press also
    /// migrates legacy names and normalizes stray spacing. Stripping rides
    /// <see cref="MosaicConvention.StripAltitudeClause"/>.</summary>
    public static string? RenameForAltitude(string name, double newAltitudeDeg)
    {
        string baseName = MosaicConvention.StripAltitudeClause(name);
        if (baseName == name.TrimEnd())
            return null;   // no clause — never invent one
        string renamed = $"{baseName} - {newAltitudeDeg.ToString("0.#", CultureInfo.InvariantCulture)}";
        return renamed == name ? null : renamed;
    }

    // Stage 1's universe: every project (enables are sky truth), scoped when a press selects one.
    private static HashSet<long> TargetUniverse(TsPlanData ts, long? onlyProjectId) =>
        [.. ts.Projects.Where(p => onlyProjectId is not long only || p.Id == only).Select(p => p.Id)];

    // Stage 2's universe: only the Active/Inactive pair ever has its state read or written.
    private static HashSet<long> ProcessedProjects(TsPlanData ts, long? onlyProjectId) =>
        [.. ts.Projects
            .Where(p => p.State is StateActive or StateInactive)
            .Where(p => onlyProjectId is not long only || p.Id == only)
            .Select(p => p.Id)];

    // The gate's row key convention: the TS guid when the row has one, else the integer Id as a string.
    private static string EditKey(string? tsGuid, long id) =>
        string.IsNullOrEmpty(tsGuid) ? id.ToString(CultureInfo.InvariantCulture) : tsGuid;
}
