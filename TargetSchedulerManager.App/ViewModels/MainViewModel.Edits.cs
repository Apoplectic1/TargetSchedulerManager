using Astronomy.Catalog.TargetScheduler;
using Astronomy.Diagnostics;
using TargetSchedulerManager.App.Models;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels.Rows;

namespace TargetSchedulerManager.App.ViewModels;

// The edit surface (review M4 split): every Set*Async funnel entry (all through the guarded gate, all
// refused while a bulk operation holds the busy exclusion in the core part), the outcome→status mapping,
// in-place row/header updates, and the direction-marks sweep.
public sealed partial class MainViewModel
{
    private readonly Dictionary<string, bool> _targetActiveEdits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A group's effective enable state: a pending in-session toggle if any, else the loaded value.</summary>
    private bool EffectiveEnabled(ReconciliationRow representative) =>
        representative.TsTargetKey is string key && _targetActiveEdits.TryGetValue(key, out bool pending)
            ? pending
            : representative.Enabled;

    // Maps one guarded-write outcome to the status line + side effects, returning whether the value was
    // applied. An applied write also changed the journal, so the badge re-raises.
    private bool ApplyOutcome(EditOutcome outcome, string label)
    {
        switch (outcome)
        {
            case EditOutcome.Applied:
                RaiseSyncState();
                RefreshAllMarks();   // the journal gained an entry — the row's → must appear in place
                return true;
            case EditOutcome.Refused refused:
                StatusText = $"can't change {label}: {RefusalText(refused.Reason)}";
                return false;
            case EditOutcome.Failed:
                StatusText = $"edit failed for {label} — see tsm.log";
                return false;
            default:
                return false;
        }
    }

    private static string RefusalText(RefusalReason reason) => reason switch
    {
        RefusalReason.SchemaIncompatible => "local TS copy schema is incompatible",
        RefusalReason.ReadOnly => "local TS copy is read-only",
        RefusalReason.OpenSidecar => "local TS copy busy (another tool has it open?) — try again",
        RefusalReason.ColumnAbsent => "this TS db has no such column",
        RefusalReason.HasOverrideOrder =>
            "this target has a custom exposure order (index-coupled to its plans) — re-author it in the TS editor",
        _ => "refused",
    };

    public async Task<bool> SetTargetEnabledAsync(TargetGroupRow group, bool enabled)
    {
        if (RefuseIfBusy($"enable change for {group.Target}"))
            return false;
        if (group.TsTargetKey is not string key)
            return false;
        EditOutcome outcome = await WithEditInFlightAsync(() =>
            _gate.ApplyAsync(TsTable.Target, key, "active", enabled ? 1 : 0, group.Target));
        bool applied = ApplyOutcome(outcome, group.Target);
        if (applied)
        {
            _targetActiveEdits[key] = enabled;
            group.ApplyEnabled(enabled);   // mirror the in-grid checkbox (a flyout edit must show immediately)
        }
        return applied;
    }

    /// <summary>A mosaic's aggregate panel-enable state for the master switch: true = every TS-backed panel
    /// enabled, false = none, null = mixed (or no TS panels). Honors pending in-session toggles.</summary>
    public bool? GetMosaicEnabledState(TargetGroupRow group)
    {
        if (group.Panels is not { Count: > 0 } panels)
            return null;
        bool anyOn = false, anyOff = false;
        foreach (PanelGroupRow panel in panels)
        {
            if (panel.TsTargetKey is null) continue;
            // One pending-override rule (review m1): the panel's key IS its first child's key.
            bool on = EffectiveEnabled(panel.Children[0]);
            if (on) anyOn = true; else anyOff = true;
        }
        return anyOn && anyOff ? null : anyOn ? true : anyOff ? false : null;
    }

    /// <summary>The mosaic master enable: fans <c>target.active</c> out to every TS-backed panel target (a
    /// mosaic parent is a grouping node with no TS row of its own). Each write is individually guarded and
    /// audited; false when any failed — the caller re-reads <see cref="GetMosaicEnabledState"/> to display
    /// whatever partial state resulted.</summary>
    public async Task<bool> SetMosaicEnabledAsync(TargetGroupRow group, bool enabled)
    {
        if (RefuseIfBusy($"enable change for {group.Target}"))
            return false;
        if (group.Panels is not { Count: > 0 } panels)
            return false;
        bool allApplied = true;
        foreach (PanelGroupRow panel in panels)
        {
            if (panel.TsTargetKey is not string key) continue;
            string label = Format.Label(group.Target, panel.Label);
            EditOutcome outcome = await WithEditInFlightAsync(() =>
                _gate.ApplyAsync(TsTable.Target, key, "active", enabled ? 1 : 0, label));
            if (ApplyOutcome(outcome, label))
                _targetActiveEdits[key] = enabled;
            else
                allApplied = false;
        }
        return allApplied;
    }

    /// <summary>Seeds a field-editor form: the current db values of one TS row's editable columns
    /// (null = row missing or read fault — show an error, not a form).</summary>
    public Task<IReadOnlyDictionary<string, object?>?> ReadTsFieldsAsync(TsTable table, string key, string label) =>
        WithEditInFlightAsync(() => _gate.ReadFieldsAsync(table, key, label));   // reads hold a connection too

    /// <summary>Writes one editable TS field through the guarded gate; true when applied + verified. The generic
    /// path for fields with no in-grid mirror — `target.active` and plan `desired` route through their specific
    /// setters so their grid cells refresh in place.</summary>
    public async Task<bool> SetTsFieldAsync(TsTable table, string key, string column, object? value, string label)
    {
        if (RefuseIfBusy($"{column} edit for {label}"))
            return false;
        EditOutcome outcome = await WithEditInFlightAsync(() =>
            _gate.ApplyAsync(table, key, column, value, label));
        return ApplyOutcome(outcome, label);
    }

    /// <summary>Writes <c>exposureplan.enabled</c> through the guarded gate — the library clears the target's
    /// cadence rows in the same transaction (direct write, no confirm — user decision 2026-07-07; see the
    /// cadence convention in DOMAIN.md). Mirrors the row's checkbox in place on success.</summary>
    public async Task<bool> SetPlanEnabledAsync(ReconciliationRow row, bool enabled)
    {
        if (RefuseIfBusy($"enable change for {row.Target} · {row.Filter}"))
            return false;
        if (row.PlanTsKey is not string key)
            return false;
        string label = Format.Label(row.Target, row.Filter);
        EditOutcome outcome = await WithEditInFlightAsync(() =>
            _gate.ApplyAsync(TsTable.ExposurePlan, key, "enabled", enabled ? 1 : 0, label));
        if (!ApplyOutcome(outcome, label))
            return false;
        MirrorPlanEdit(row, r => r.ApplyPlanEnabled(enabled));
        return true;
    }

    public async Task<bool> SetPlanDesiredAsync(ReconciliationRow row, int desired)
    {
        if (RefuseIfBusy($"Desired edit for {row.Target} · {row.Filter}"))
            return false;
        if (row.PlanTsKey is not string key)
            return false;
        EditOutcome outcome = await WithEditInFlightAsync(() =>
            _gate.ApplyAsync(TsTable.ExposurePlan, key, "desired", desired, Format.Label(row.Target, row.Filter)));
        if (!ApplyOutcome(outcome, Format.Label(row.Target, row.Filter)))
            return false;

        MirrorPlanEdit(row, r => r.ApplyDesired(desired));
        return true;
    }

    /// <summary>Writes <c>exposureplan.exposure</c> (a positive override, or TS's −1 defer-to-template
    /// sentinel) through the guarded gate. <paramref name="mirrorSeconds"/> is what the Seconds cell should
    /// show afterwards — the rounded override, or the template default when the caller knows it; when null,
    /// the effective value is resolved from the db (plan→template join) so the cell mirrors immediately
    /// either way (standing rule: a flyout edit reflects in its column at once).</summary>
    public async Task<bool> SetPlanExposureAsync(ReconciliationRow row, double exposure, int? mirrorSeconds)
    {
        if (RefuseIfBusy($"exposure edit for {row.Target} · {row.Filter}"))
            return false;
        if (row.PlanTsKey is not string key)
            return false;
        string label = Format.Label(row.Target, row.Filter);
        EditOutcome outcome = await WithEditInFlightAsync(() =>
            _gate.ApplyAsync(TsTable.ExposurePlan, key, "exposure", exposure, label));
        if (!ApplyOutcome(outcome, label))
            return false;

        mirrorSeconds ??= await WithEditInFlightAsync(() => _gate.ReadPlanEffectiveSecondsAsync(key, label));
        if (mirrorSeconds is int seconds)
            MirrorPlanEdit(row, r => r.ApplyPlanSeconds(seconds));
        return true;
    }

    // ---- disk-row adoption (openspec disk-row-adoption) -------------------------------------------------

    /// <summary>Menu gating for "Add TS plan… / Add to TS…": a disk-only cell with no TS plan at its
    /// (filter, purpose, seconds) — split rows and disk-only mosaic panels excluded (planner rule). False
    /// before a load (no snapshot to decide against).</summary>
    public bool IsRowAdoptable(ReconciliationRow row) =>
        _lastLoad is { Ts: { } ts } && AdoptionPlanner.IsEligible(row, ts);

    /// <summary>
    /// The "Add to TS" action: assembles the assignment facts, shows the one dialog every adoption goes
    /// through (<see cref="AdoptPrompt"/> — project + existing template, Accept/Cancel), builds the accepted
    /// choice's inserts (born-complete counts, target payload when the TS target doesn't exist), applies
    /// them atomically through the gate's insert path (journals, marks), and reloads without a pull so the
    /// cell re-reconciles — `Both` when the assigned template pairs, a TS row beside the disk row when the
    /// user accepted the caution. Structural refusals (stale snapshot, no centroid, no projects) surface
    /// through <see cref="AdoptRefusalPrompt"/>; cancel writes nothing.
    /// </summary>
    public async Task<bool> AdoptRowAsync(ReconciliationRow row)
    {
        string label = Format.Label(row.Target, row.Filter);
        if (RefuseIfBusy($"Add to TS for {label}"))
            return false;
        if (_lastLoad is not LoadResult load)
        {
            StatusText = "no load yet — nothing to adopt against";
            return false;
        }

        (AdoptionFacts? facts, string? refusal) = AdoptionPlanner.GetFacts(row, load.Graph, load.Ts);
        if (facts is null)
            return await RefuseAdoptionAsync(refusal!);

        AdoptionChoice? choice = AdoptPrompt is null ? null : await AdoptPrompt(facts);
        if (choice is null)
        {
            Log.Info($"ADOPT cancelled ({label})");
            return false;
        }

        (AdoptionPlan? plan, string? buildRefusal) = AdoptionPlanner.Build(
            row, load.Graph, load.Ts, choice.Project, choice.Template);
        if (plan is null)
            return await RefuseAdoptionAsync(buildRefusal!);

        EditOutcome outcome = await WithEditInFlightAsync(() => _gate.ApplyInsertAsync(plan.Rows, plan.Label));
        if (!ApplyOutcome(outcome, plan.Label))
            return false;

        StatusText = plan.CreatesTarget
            ? $"added TS target + plan for {plan.Label} (template '{plan.Template.Name}', desired {row.Disk}) — unpushed"
            : $"added TS plan for {plan.Label} (template '{plan.Template.Name}', desired {row.Disk}) — unpushed";
        Log.Info($"ADOPT applied: {StatusText}");
        await LoadAsync(PullPolicy.Never);   // the created rows re-reconcile the cell, marks ride the reload
        return true;
    }

    /// <summary>Menu gating for the rollup's "Add to TS… / Add TS plans…": at least one child cell the
    /// per-cell gate would accept — a mosaic parent has none by definition. False before a load.</summary>
    public bool IsTargetAdoptable(TargetGroupRow group) =>
        _lastLoad is { Ts: { } ts } && AdoptionPlanner.EligibleCells(group, ts).Count > 0;

    /// <summary>
    /// The bulk "Add to TS" action (openspec adopt-target-rollup): every individually-eligible cell of one
    /// rollup through one combined dialog (<see cref="BulkAdoptPrompt"/> — project once, per-cell template
    /// assignment, include checkboxes), the accepted set written as ONE atomic insert batch (target payload
    /// first when the TS target doesn't exist), then a no-pull reload re-reconciles every cell — `Both`
    /// where the assigned template pairs, a TS row beside the disk row where the user accepted the caution.
    /// Structural refusals surface through <see cref="AdoptRefusalPrompt"/>; cancel writes nothing.
    /// </summary>
    public async Task<bool> AdoptTargetAsync(TargetGroupRow group)
    {
        if (RefuseIfBusy($"Add to TS for {group.Target}"))
            return false;
        if (_lastLoad is not LoadResult load)
        {
            StatusText = "no load yet — nothing to adopt against";
            return false;
        }

        (BulkAdoptionFacts? facts, string? refusal) = AdoptionPlanner.GetBulkFacts(group, load.Graph, load.Ts);
        if (facts is null)
            return await RefuseAdoptionAsync(refusal!);

        BulkAdoptionChoice? choice = BulkAdoptPrompt is null ? null : await BulkAdoptPrompt(facts);
        if (choice is null)
        {
            Log.Info($"ADOPT bulk cancelled ({group.Target})");
            return false;
        }

        (BulkAdoptionPlan? plan, string? buildRefusal) = AdoptionPlanner.BuildBulk(group, load.Graph, load.Ts, choice);
        if (plan is null)
            return await RefuseAdoptionAsync(buildRefusal!);

        EditOutcome outcome = await WithEditInFlightAsync(() => _gate.ApplyInsertAsync(plan.Rows, plan.Label));
        if (!ApplyOutcome(outcome, plan.Label))
            return false;

        string plans = plan.PlanCount == 1 ? "1 plan" : $"{plan.PlanCount} plans";
        StatusText = plan.CreatesTarget
            ? $"added TS target + {plans} for {group.Target} — unpushed"
            : $"added {plans} to TS for {group.Target} — unpushed";
        Log.Info($"ADOPT applied: {StatusText}");
        await LoadAsync(PullPolicy.Never);   // the created rows re-reconcile their cells, marks ride the reload
        return true;
    }

    // An explicit menu action that declines must decline loudly (the f39f lesson): status line + log +
    // dialog. These are structural refusals — the assignment dialog itself handles the empty-scope case.
    private async Task<bool> RefuseAdoptionAsync(string reason)
    {
        StatusText = reason;
        Log.Warn($"ADOPT refused: {reason}");
        if (AdoptRefusalPrompt is not null)
            await AdoptRefusalPrompt(reason);
        return false;
    }

    // Re-aggregate the header rows over an in-place-edited leaf (group always; panel when the leaf has
    // one) — O(1) via the owner map ApplyFilters maintains (review N6; was a groups × children scan per
    // committed edit). A row not in the map (a rollup's detail line) is a no-op here — MirrorPlanEdit
    // reaches such a row's summary leaf, which IS in the map and carries the recompute.
    private void RecomputeOwners(ReconciliationRow row)
    {
        if (!_ownerByRow.TryGetValue(row, out (TargetGroupRow Group, PanelGroupRow? Panel) owner))
            return;
        owner.Group.Recompute();
        owner.Panel?.Recompute();
    }

    // A committed plan edit mirrors onto EVERY row rendering that plan — one plan can render at two
    // levels at once (a disclosed rollup's summary leaf and its TS detail line share PlanTsKey), and the
    // editor commits on whichever instance hosted the box (obs b4d2: a detail-line desired edit left the
    // collapsed summary reading the old count). Mirrors over _allRows + their detail lines (the instances
    // survive filter passes, so the mirror is durable until the next load), then re-aggregates each
    // mirrored row's owners — detail lines no-op there; the summary leaf carries the group/panel recompute.
    private void MirrorPlanEdit(ReconciliationRow edited, Action<ReconciliationRow> apply)
    {
        if (edited.PlanTsKey is not string planKey)
        {
            apply(edited);
            RecomputeOwners(edited);
            return;
        }
        foreach (ReconciliationRow row in RowsForPlan(planKey))
        {
            apply(row);
            RecomputeOwners(row);
        }
    }

    private IEnumerable<ReconciliationRow> RowsForPlan(string planKey)
    {
        foreach (ReconciliationRow leaf in _allRows)
        {
            if (planKey.Equals(leaf.PlanTsKey, StringComparison.OrdinalIgnoreCase))
                yield return leaf;
            if (leaf.Detail is { } detail)
                foreach (ReconciliationRow line in detail)
                    if (planKey.Equals(line.PlanTsKey, StringComparison.OrdinalIgnoreCase))
                        yield return line;
        }
    }

    /// <summary>
    /// Re-resolves every row's direction mark in place (never replaces the Rows collection — scroll position
    /// and in-progress edits survive; ApplyMark raises only on real change). One sweep, one code path: every
    /// journal/inbound mutation route calls it — load/filter passes, applied edits, pushes without a reload,
    /// discards. Cheap: keyed dictionary lookups over a few hundred rows.
    /// Header resolution unions the graph's plan-key map (folded multi-plan cells carry no row key) with the
    /// plan keys the visible child rows do carry (covers graph-less states); panels pass no project key —
    /// a project edit marks the group header / mosaic parent only.
    /// </summary>
    /// <summary>A fresh marks resolver over the current journal/inbound facts — for non-grid consumers
    /// (the Templates… picker resolves per-template marks from it at open).</summary>
    internal SyncMarks BuildMarks() => SyncMarks.Build(Sync.Journal, Sync.Inbound, _lastLoad?.Graph);

    internal void RefreshAllMarks()
    {
        SyncMarks marks = BuildMarks();
        foreach (TargetGroupRow group in _groups)
        {
            if (group.Panels is { } panels)
            {
                foreach (PanelGroupRow panel in panels)
                {
                    string[] panelTargetKeys = panel.TsTargetKey is string pk ? [pk] : [];
                    (string glyph, string? tooltip) = marks.ForKeys(
                        panelTargetKeys, projectKey: null, [panel.TargetId], PlanKeysOf(panel.Children));
                    panel.ApplyMark(glyph, tooltip);
                }
                (string g, string? t) = marks.ForKeys(
                    [.. panels.Select(p => p.TsTargetKey).OfType<string>()],
                    group.ProjectTsKey,
                    [.. panels.Select(p => p.TargetId)],
                    PlanKeysOf(group.Children));
                group.ApplyMark(g, t);
            }
            else
            {
                string[] targetKeys = group.TsTargetKey is string tk ? [tk] : [];
                Guid[] targetIds = group.TargetId is Guid id ? [id] : [];
                (string g, string? t) = marks.ForKeys(
                    targetKeys, group.ProjectTsKey, targetIds, PlanKeysOf(group.Children));
                group.ApplyMark(g, t);
            }

            foreach (ReconciliationRow row in group.Children)
            {
                (string g, string? t) = marks.ForPlan(row.PlanTsKey);
                row.ApplyMark(g, t);
                foreach (ReconciliationRow detail in row.Detail ?? [])
                {
                    (string dg, string? dt) = marks.ForPlan(detail.PlanTsKey);
                    detail.ApplyMark(dg, dt);
                }
            }
        }

        static IEnumerable<string> PlanKeysOf(IReadOnlyList<ReconciliationRow> rows)
        {
            foreach (ReconciliationRow r in rows)
            {
                if (r.PlanTsKey is string key)
                    yield return key;
                foreach (ReconciliationRow d in r.Detail ?? [])
                    if (d.PlanTsKey is string detailKey)
                        yield return detailKey;
            }
        }
    }
}
