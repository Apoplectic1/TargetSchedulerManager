# Design: assign-template-adoption

## Context

See `proposal.md` — Why. The shipped `adopt-disk-rows` flow (archived 2026-08-03) routes adoption through
`AdoptionPlanner.MatchTemplate` acting as a gatekeeper: unique match → silent build; zero/many →
`AdoptionHold`, zero-match carrying a `TemplateCreateOffer` that opens the schema-generated creation form
(`ShowAdoptTemplateFormAsync`) producing a `PendingTemplate` + template insert. The VM
(`MainViewModel.Edits.AdoptRowAsync`) fans out across three dialog hooks (`AdoptHoldPrompt`,
`AdoptTemplateFormPrompt`, target/project dialog). TS's `exposureplan` schema stores no per-plan capture
overrides except `exposure` — verified against the live db 2026-08-03 — so assignment (not creation, not
per-plan overrides) is the only TS-expressible model besides template authoring, which stays in TS.

## Goals / Non-Goals

**Goals**
- One dialog for every adoption; matching only preselects.
- Net deletion: creation form, hold dialogs, offer/pending machinery, and their hooks all come out.
- The caution predicate reuses the same pairing semantics the reconciler applies, so the dialog's promise
  ("will/won't merge") always agrees with what the refreshed grid renders.

**Non-Goals**
- No changes to `Astronomy.Catalog` (`TryInsertRows` template support stays — generic sync capability).
- No changes to journal/replay (`TsJournal`, `TsSync`): target + plan inserts continue exactly as shipped;
  the template insert leg simply stops receiving entries from adoption.
- No template editing or creation anywhere in the adoption path.
- No post-accept auto-opened editor (decision 2026-08-03: "after accept, just update the UI").

## Decisions

1. **`AdoptionPlanner` API reshape** — `Build(row, graph, ts, project, template)` takes the *chosen*
   template; a new `ListCandidates(row, ts, profileId)` returns the strict-scope list (same filter,
   `Bin == BinningX`, `BinningX == BinningY` implied by scope emptiness for non-square) plus a preselect
   index and, per candidate, a `WouldPair` flag driving the caution. Preselect ranking: `WouldPair` (purpose
   + expressed-and-equal gain/offset) → same purpose → list order. `AdoptionHold`, `TemplateCreateOffer`,
   `PendingTemplate`, `TemplateFormResult`, `BuildCreateOffer`, `MatchTemplate` deleted. `desiredOverride`
   and the `pending` parameter go with them (born-complete only).
   *Alternative rejected*: keeping `MatchTemplate` returning holds and translating holds to dialog states —
   preserves dead shapes for no caller.
2. **One VM hook** — `AdoptPrompt: Func<AdoptionFacts, Task<AdoptionChoice?>>` replaces the three hooks.
   `AdoptionFacts` carries: target name, coordinates, the locked-or-choosable project situation
   (`OwningProject` xor `Projects` list), the candidate templates with their capture values + `WouldPair`,
   the preselect index, and the cell's disk values (filter, purpose, gain/offset/bin, seconds, count).
   `AdoptionChoice` = chosen project guid + chosen template id. Null = cancel. The VM then builds and
   applies exactly as today (`ApplyInsertAsync` → status → `LoadAsync(PullPolicy.Never)`).
3. **One dialog** — `ShowAdoptDialogAsync(facts, anchor)` in `MainWindow.Dialogs.cs`: project ComboBox
   (disabled + preselected when locked), template ComboBox (display: name + gain/offset/bin/default-exposure),
   read-only facts panel (disk values beside selected template values, refreshed on selection change),
   caution TextBlock bound to the selection's `WouldPair`, empty-scope message + Accept disabled
   (`IsPrimaryButtonEnabled = false`). Movable + **centered** via the existing `ShowDialogAsync` helper
   (anchor seeding retired mid-change, user call after obs 3eba — seeding the ContentDialog full-window
   overlay races layout and can land the visible box off-screen, an invisible modal reading as a UI
   hang); Ctrl+N PreviewKeyDown ride-along comes free. `ShowAdoptTargetDialogAsync` and
   `ShowAdoptTemplateFormAsync` deleted; `ShowAdoptHoldDialogAsync` deleted (no holds remain — busy/refusal
   paths keep their existing surfaces).
4. **Caution predicate colocation** — the `WouldPair` computation lives in `AdoptionPlanner` beside the
   payload construction (one place owns "what merges"), mirroring the capture-config rule: same purpose
   classification, and for gain/offset: template value expressed (`>= 0`) and equal to the cell's. Bin is
   equal by scope. Sentinel templates (`-1`) list normally but never flag `WouldPair` (the honest reading —
   they still land beside the disk row on merge).
5. **Spec purpose line** — the main spec's Purpose ("…the row reads `Both`") overstates the outcome under
   assignment; per openspec rules, Purpose is edited directly on `openspec/specs/disk-row-adoption/spec.md`
   at sync time ("…so the disk history becomes visible to TS's planner — rendering `Both` when the assigned
   template pairs with the cell").

## Risks / Trade-offs

- [Users can now create never-pairing plans routinely] → that is the requested model; the caution makes it
  an informed choice, and the split rendering is the standing diagnostic. Undo remains discard-and-pull.
- [Deleting the creation form orphans its tests and the insert-review "template create" summaries] →
  generic template-insert support in `TsSync`/`TargetSchedulerEditor` and its library tests stay; only
  app-side tests that reach template creation *through adoption* are reshaped to the new API.
- [Preselect ranking could feel arbitrary when nothing pairs] → ranking is stated in the spec (pair →
  same purpose → order); the dialog always shows the values, so a wrong default is visible before Accept.

## Migration Plan

None — no persisted-state impact. The journal format is unchanged; an unpushed journal containing template
inserts from the prior build would still replay (sync capability untouched), and per the no-back-compat
rule nothing special is built for that case.

## Open Questions

None.
