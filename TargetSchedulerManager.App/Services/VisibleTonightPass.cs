using System.Globalization;
using Astronomy.Catalog.TargetScheduler;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Session;

namespace TargetSchedulerManager.App.Services;

/// <summary>One flip the Visible-tonight pass wants applied — the field coordinate + value in exactly the
/// shape <c>TsEditGate.ApplyAsync</c> takes, plus the grid-style label the journal / push review shows.</summary>
internal sealed record VisibleTonightEdit(TsTable Table, string Key, string Column, int Value, string Label);

/// <summary>The pass's decision set: the edits to apply (targets first, then the project flips derived from
/// the targets' post-pass state) and the summary counts the status line reports.</summary>
internal sealed record VisibleTonightPlan(
    IReadOnlyList<VisibleTonightEdit> Edits,
    int TargetsEnabled,
    int TargetsDisabled,
    int TargetsUnchanged,
    int ProjectsActivated,
    int ProjectsDeactivated);

/// <summary>
/// The Visible-tonight pass: reconciles <c>target.active</c> / <c>project.state</c> with tonight's sky.
/// A target is "visible tonight" iff it has a single contiguous window of at least the configured minimum
/// duration above the geometric horizon (0°) between tonight's astronomical dusk and dawn — deliberately
/// independent of TS's own per-project altitude rules, which TS re-applies at plan time. Pure planning:
/// consumes the load's retained <see cref="TsPlanData"/> rows and returns the edits; the caller applies
/// them through the guarded edit gate so they journal like hand edits.
/// </summary>
/// <remarks>
/// "Tonight" is <see cref="NightCalculator.ComputeNight"/>'s bracket convention: the window whose dawn is
/// the next dawn at-or-after <c>utcNow</c> — the current night when pressed after dusk, the upcoming night
/// when pressed in daylight.
/// </remarks>
internal static class VisibleTonightPass
{
    // TS project.state codes (TsEditableSchema "ProjectState": 0 Draft · 1 Active · 2 Inactive · 3 Closed).
    // Only the Active/Inactive pair is ever read or written; Draft/Closed projects are skipped wholesale.
    private const int StateActive = 1;
    private const int StateInactive = 2;

    /// <summary>
    /// Computes the flips for one button press. Projects outside the Active/Inactive pair are skipped
    /// entirely (their targets too); each remaining target's <c>active</c> tracks its visibility verdict,
    /// then each project's <c>state</c> follows "no enabled targets → Inactive, any → Active" over the
    /// post-pass values. Unchanged fields yield no edit.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A processed target has no RA/Dec — a TS contract violation; the pass aborts before any edit.
    /// </exception>
    public static VisibleTonightPlan Plan(
        TsPlanData ts, Location site, DateTime utcNow, TimeSpan minDuration)
    {
        NightWindow night = NightCalculator.ComputeNight(site, utcNow);
        ScalarHorizonProfile geometricHorizon = new(0.0);

        List<VisibleTonightEdit> targetEdits = [];
        List<VisibleTonightEdit> projectEdits = [];
        int enabled = 0, disabled = 0, unchanged = 0, activated = 0, deactivated = 0;

        Dictionary<long, bool> anyEnabledByProject = [];
        HashSet<long> processedProjects = [];
        foreach (TsProject project in ts.Projects)
        {
            if (project.State is StateActive or StateInactive)
            {
                processedProjects.Add(project.Id);
                anyEnabledByProject[project.Id] = false;
            }
        }

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
                site, night, geometricHorizon, minDuration);

            if (visible)
                anyEnabledByProject[projectId] = true;

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

        return new VisibleTonightPlan(
            [.. targetEdits, .. projectEdits], enabled, disabled, unchanged, activated, deactivated);
    }

    // The gate's row key convention: the TS guid when the row has one, else the integer Id as a string.
    private static string EditKey(string? tsGuid, long id) =>
        string.IsNullOrEmpty(tsGuid) ? id.ToString(CultureInfo.InvariantCulture) : tsGuid;
}
