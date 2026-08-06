# Proposal: project-scoped-tonight

## Why

TS project settings (Min Time, Min Altitude) cascade to every member target, but editing them today
means opening each project's edit dialog — and the toolbar's Visible-Tonight knobs, which express the
same two ideas, are deliberately disconnected free knobs. The user wants the toolbar to become a
read/write window onto the selected project's actual TS constraints: pick a project, see its values,
adjust, and one Tonight press writes them project-wide and re-runs the enable pass scoped to that
project (2026-08-05 explore session).

## What Changes

- A **Project dropdown** joins the toolbar's Visible-Tonight group (right of the label), listing
  **every** TS project regardless of state, with **All projects** as the default entry.
- Selecting a project **fills** Duration ← `minimumtime` and Floor ← `minimumaltitude` (read-only at
  selection time). Re-selecting or switching discards unsaved box edits.
- With a project selected, **Tonight** becomes the single write gesture: it journals changed
  `minimumtime`/`minimumaltitude` onto the project, then runs the enable pass **scoped to that
  project's targets** using the box values. With All selected, today's global no-write behavior is
  unchanged.
- Knob ranges adopt the schema: Duration 0–999 min; Floor becomes **Real 0–90°** (was integer 0–89) —
  so a fill can never silently clamp a stored value.
- **Enables are sky truth for every project** regardless of state: All mode and scoped mode flip
  Draft/Closed projects' targets like any other's. Project lifecycle stays separate — the pass never
  writes a Draft/Closed project's `state` in either direction (promotion remains a hand edit).
- **Deliberately deferred:** project names encode the altitude ("Nebulae - Above 45") and are NOT
  renamed when Floor writes — a follow-up change.
- Custom-horizon projects get no special case: TS applies the horizon at the telescope; the scoped
  pass uses Min Altitude as its scalar floor regardless.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `visible-tonight-toggle`: the knob-input contract gains the Project dropdown, fill-on-select, and
  the schema-aligned ranges (Real Floor); the target-flip universe widens to every project regardless of
  state (scoped or All) while state derivation stays confined to the Active↔Inactive pair; the
  Draft/Closed requirement is renamed to say exactly that; a new requirement pins the scoped press's
  constraint write-back (settings flow down, state rolls up).

## Impact

- `TargetSchedulerManager.App/MainWindow.xaml` + code-behind — toolbar dropdown, knob ranges.
- `TargetSchedulerManager.App/Controls/UpDownBox.cs` — grows a decimal mode for the Real Floor.
- `TargetSchedulerManager.App/Services/VisibleTonightPass.cs` — scoped project universe parameter.
- `TargetSchedulerManager.App/ViewModels/MainViewModel.Reports.cs` (+ VM surface) — project list for
  the dropdown, on-demand read of the two fields, the scoped write + pass orchestration.
- App tests (pass scoping, write gating); `UI.md` + `SUBSYSTEMS.md` visible-tonight section.
- No Library change (fields already in `TsEditableSchema`; reads use the existing field-read path).
