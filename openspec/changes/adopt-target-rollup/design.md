# adopt-target-rollup — design

## Context

See `proposal.md` — Why. Everything this change needs already exists as per-cell machinery
(`assign-template-adoption`, archived 2026-08-03): `AdoptionPlanner` (pure planning — eligibility gate,
per-project strict-scope candidates with merge verdicts, insert-payload build), the assignment dialog in
`MainWindow.Dialogs.cs`, the VM funnel `AdoptRowAsync` (busy exclusion → facts → prompt hook → build →
`TsEditGate.ApplyInsertAsync` → no-pull reload). The bulk action is a composition problem, not new
machinery: enumerate the rollup's eligible children, lift the facts/dialog/build steps from one cell to N.

Constraints that shape the design:
- The planner stays pure (no db, no UI) — CONVENTIONS' one-plausible-home map.
- Templates are assigned, never created (obs 3dfe). The per-cell scope/preselect/caution logic is reused
  verbatim per cell — no new matching semantics.
- `AdoptionFacts` precomputes everything per project *before* the dialog opens (the dialog swaps lists on
  project change, it never queries). The bulk facts keep that property.

## Goals / Non-Goals

**Goals**
- One gesture adopts a whole target; the per-cell action remains untouched.
- Zero new pairing/scope semantics — per-cell rules applied per cell.
- One atomic batch through the existing gate insert path; one journal group; one re-reconcile.

**Non-Goals**
- Mosaic parents (panel/target creation under `isMosaic` stays out of scope).
- Cross-target sweeps (project-level or grid-level "adopt everything").
- Editing plan fields in the dialog (unchanged: adjustments happen in the plan editor afterward).

## Decisions

**D1 — Bulk facts generalize `AdoptionFacts`, per-cell candidates nested under each project option.**
New records in `AdoptionPlanner`: `BulkAdoptionFacts` carrying the shared header (label, `ProjectLocked`,
target-creation facts) plus per-project options where each `AdoptionProjectOption` generalizes to a list
of per-cell `(Candidates, PreselectIndex)` — i.e. today's `ListCandidates(row, ts, profileId)` evaluated
for every eligible cell × every pickable project, precomputed. Cell identity travels as the
`ReconciliationRow` itself plus its display facts. Alternative — calling `GetFacts` N times and stitching
in the dialog — rejected: it re-derives the project situation N times and puts composition logic in the
view layer.

**D2 — Eligibility enumeration is the per-cell gate, unchanged.** The rollup's eligible set =
`children.Where(c => AdoptionPlanner.IsEligible(c, ts))`. Menu gating calls the same predicate (any
eligible child ⇒ show the item). Target-exists (label + project lock) keys on any child resolving a TS
target — same `FindTarget` resolution the per-cell path uses. No new eligibility rules, so the two grains
can never disagree about a cell.

**D3 — A separate bulk dialog method, sharing the row-level pieces.** `ShowBulkAdoptDialogAsync` in
`MainWindow.Dialogs.cs` beside the per-cell one, not a parameterized merge: the layouts differ genuinely
(header + repeated cell rows + checkbox column vs the single-cell facts panel). The per-cell row content —
template combo binding a candidate list, caution text updating on selection — is factored into a shared
helper both dialogs use. Project-change handling mirrors the existing dialog: swap every cell row's
candidate list to the selected project's precomputed set (D1), re-deriving preselect/caution/servability —
no live scope queries. The cell list lives in a `ScrollViewer` with a max height so a many-filter target
can't push Accept off-screen.

**D4 — Return shape: the chosen project + per-cell accepted assignments.** `BulkAdoptionChoice(Project,
IReadOnlyList<(ReconciliationRow Row, TsExposureTemplate Template)>)` — only included, servable cells
appear. Null = cancel. A new VM hook `BulkAdoptPrompt` beside `AdoptPrompt` (same pattern: view wires it
at startup; tests stub it).

**D5 — `BuildBulk` assembles one `AdoptionPlan` for the whole batch.** Target payload once (when no TS
target), then one plan payload per accepted cell — each cell's payload built by the existing per-cell
logic (born-complete counts, `-1` exposure sentinel, guid/id reference rules). All rows in one
`AdoptionPlan.Rows` list → one `ApplyInsertAsync` call, which already batches atomically and journals as a
group. Any per-cell structural refusal returns the refusal (naming the cell) and no plan — the funnel
never reaches the gate, so atomicity is trivial.

**D6 — Rotation seed tie-break: first included cell in grid order expressing a sky rotation.** The target
payload is built once but N cells may carry framings. In practice a target's filters share one framing
cluster; when they don't, the first-in-grid-order sky rotation seeds (mechanical/unknown never convert,
per the framing rules). Deterministic, matches what the user sees top-of-list. Alternative — majority vote
— rejected as invented complexity for a case the framing model says is rare and harmless (a rotation-less
or differently-seeded target still credits framings by the fold-180 tolerance rules).

**D7 — VM funnel `AdoptTargetAsync(TargetGroupRow)` mirrors `AdoptRowAsync` exactly.** Busy exclusion →
enumerate eligible cells against the retained load → `GetBulkFacts` → `BulkAdoptPrompt` → `BuildBulk` →
gate insert → no-pull reload (the existing close-time re-reconcile). Refusals through the existing
`AdoptRefusalPrompt`. Logging mirrors the per-cell `ADOPT` lines with a cell count.

## Risks / Trade-offs

- **[Stale snapshot between menu and Accept]** → same exposure as per-cell adoption, N× surface; the
  per-cell backstop refusals in `BuildBulk` abort the whole batch naming the cell (spec'd). No partial
  write is possible.
- **[Dialog complexity: N combos + checkboxes + project re-scope]** → all lists precomputed in facts
  (D1); the dialog only swaps references. The shared row helper (D3) keeps per-cell behavior identical in
  both dialogs by construction.
- **[Many-cell targets overflow the dialog]** → `ScrollViewer` with max height (D3); WinUI `ContentDialog`
  sizing gotchas are known territory (UI.md).
- **[Same-filter multi-seconds cells confuse the row list]** → each row shows seconds + count, the same
  facts the grid shows; no dedup is attempted — cells are the unit, per the settled design.

## Migration Plan

None — additive UI + planner surface; no schema, no persisted state, no Library change. Rollback = revert
the commit.

## Open Questions

None.
