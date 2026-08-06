# Design: project-scoped-tonight

## Context

See `proposal.md` — Why. Established in the 2026-08-05 explore session; all user decisions are
recorded there and in the delta spec. Key existing machinery: `VisibleTonightPass` (pure planner,
two-stage, edits through `TsEditGate`), the toolbar `UpDownBox` knobs (integer-only by design),
`TsEditableSchema` (project `minimumtime` Whole 0–999 min; `minimumaltitude` Real 0–90°), and the
schema-driven field-edit path the dialogs use (`ViewModel.SetTsFieldAsync` / `ReadTsFieldsAsync`).

Two arrows, both true, different planes — the framing the spec uses: **settings flow down**
(project constraints cascade to member targets at TS plan time), **state rolls up** (the pass derives
`project.state` from what the sky left enabled).

## Goals / Non-Goals

**Goals**
- The toolbar as a read/write window onto one project's Min Time / Min Altitude.
- Scoped enable pass; All mode keeps today's shape but its target universe widens to every project
  regardless of state (enables = sky truth; lifecycle separate).
- Fill can never silently alter a stored value (ranges match schema; Real floor).

**Non-Goals**
- No project rename when the altitude changes (follow-up change; the "Above 45" names go stale
  deliberately).
- No horizon-aware visibility (custom horizon is TS's business at the telescope; the pass stays a
  scalar-floor test).
- No new write path: constraint writes ride `SetTsFieldAsync` exactly like dialog edits (journal,
  marks, push review all inherit).

## Decisions

1. **Dropdown = ComboBox in the toolbar**, populated at load from the retained `TsPlanData` project
   list (name-sorted, "All projects" first, reselected-by-key across reloads when possible; falls back
   to All when the project vanished). No new read plumbing for the *list* — `TsPlanData.Projects`
   already carries names/states.
2. **Fill reads via the existing field-read path** (`ReadTsFieldsAsync(TsTable.Project, key)`), the
   same route the project edit dialog seeds from — not a new `TsPlanData` column. One code path for
   "current value of an editable field" stays one path.
3. **`UpDownBox` grows a decimal mode** (`DecimalPlaces` 0/1, default 0 — existing callers
   unchanged): Floor uses one decimal place, range 0–90; Duration stays whole, range 0–999. Rationale:
   the schema says Real; refusing fractional fills (the alternative) would violate the
   nothing-silently-altered rule the moment a fractional altitude exists.
4. **Scoping is a parameter, not a fork**: `PlanTargets`/`PlanProjects` take an optional selected
   project id; All mode passes null — identical code path, no divergence to maintain. The universe
   split (user decision): the TARGET stage considers every project regardless of state (enables are
   sky truth); the STATE stage keeps the existing `ProcessedProjects` Active/Inactive gate — Draft and
   Closed project state is never derived or written. `PlanTargets` therefore drops its project-state
   filter; `PlanProjects` keeps it.
5. **Write order on a scoped press**: constraint edits first (their own batch, only-if-changed), then
   the enable pass reading the box values directly (not re-reading the fields). The enable stage and
   the constraint write hold the same bulk-op exclusion as one operation, so no edit can interleave.
6. **Selection fill is view-model state, not journal state**: switching selections refills and
   discards box edits without any prompt — the boxes are a viewport, Set is the only commit.

## Risks / Trade-offs

- [Fill shows stale values if another session edited the field since load] → fill re-reads from the
  local working copy at selection time (decision 2), not from the load snapshot.
- [User expects the write without pressing Set] → the fill-is-read / Set-is-write model is
  pinned in the spec and UI.md; the sync marks light on the project row after a write, giving
  immediate feedback that the commit happened.
- [A Draft project's targets now flip on an All press — a behavior change from today] → intended
  (user decision: enables are sky truth for every project); the status-line counts and journal make
  the flips visible, and Draft/Closed `state` itself is provably untouched (spec scenario).

## Migration Plan

App-side only; no schema or data change. Ship; user field-verifies (dropdown fill, scoped enables,
constraint write visible in push review, All mode unchanged); archive on their word.

## Open Questions

None — all decision points were resolved in the explore session.
