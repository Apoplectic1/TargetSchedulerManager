# CONVENTIONS.md — TargetSchedulerManager

**How code is written here, and where it goes.** The sibling reference docs answer different questions:
`ARCHITECTURE.md` = how the system works (design + load-bearing invariants) · `UI.md` = how the UI looks
and behaves (design language + the add-a-UI-element checklist) · `TS-SCHEMA.md` = the external TS contract ·
`VERIFICATION.md` = how to build, run and verify. This file is the one a newcomer needs **before choosing a
file to edit**.

Carved out 2026-07-26. The siting doctrine below was asserted in the 2026-06-10 reviews
(`docs/archive/2026-06-10-code-review-slice1.md` §2.1/§5.2, `…-round2.md` §C1) and independently re-confirmed
by the 2026-07-24 review — it is the property both credit for small diffs and cheap reviews.

## One plausible home per kind of change

The test of this codebase is that a change has an *obvious* destination. If you are unsure where something
goes, that ambiguity is the bug — resolve it before writing code.

| Kind of change | Home |
|---|---|
| Contract rules — schema, scanning, reconciliation, TS interop | `..\Library\Astronomy.Catalog` (a **different repo**) |
| Per-item display state (one row's shape and derived cells) | `ViewModels/Rows` (`ReconciliationRow`, the group/header rows, `RowAggregates`) |
| UI-free display policy (formatting, badge vocabulary, enums) | `Models` (`Format.cs`, `Badges.cs`, `RowEnums.cs`) |
| Sync / guarded-write policy | `Shared` (`TsSync`, `TsEditGate`, `TsJournal`, `TsInboundDiff`, `CommitChain`, …) |
| A whole pipeline stage or report | `Services` (`ReconciliationLoader`, `SyncMarks`, `WriteBackStep`, `AmbiguityReport`, `VisibleTonightPass`) |
| A custom control or attached behavior | `Controls` (`UpDownBox`, `TsFieldsEditor`, `BadgeRuns`, `DragMove`) |
| A static XAML reaches directly — attached property, brush lookup | the **App project root**, `namespace TargetSchedulerManager.App` (`GridColumns`, `ThemeBrushes`) |
| Window / app-shell plumbing | `Support`, `MainWindow.*.cs` partials |

The App-root row is a deliberate placement, not leftovers: a type at the project root resolves through the
existing unqualified `xmlns:local` / enclosing-namespace lookup, so XAML reaches it with no new prefix — the
reason both `GridColumns.ApplyRuler` and `ThemeBrushes` landed there rather than under `Models`/`Support`
(`openspec/changes/archive/2026-07-24-grid-column-ruler/design.md` D3,
`…/2026-07-24-presentation-conventions/design.md` D2).

**Almost all logic is library-side.** When a change is about schema, scanning, reconciliation or TS interop
you are editing `..\Library\Astronomy.Catalog`, not this repo — and there the *shared-library discipline*
applies: no consumer-specific terminology on the public surface, caller/consumer framing, doc strings that
describe the abstract contract (`..\Library\CLAUDE.md`).

## Invariants are written where they are enforced

A load-bearing rule lives as a doc comment **on the code that enforces it** — `EffectiveExposure`, `IssuesFor`,
`TsEditGate`, `TsInboundDiff`'s key spaces. The reference docs then *mirror* those invariants rather than
solely owning them, so a reader who reaches the code first still finds the rule.

The corollary is a maintenance duty: when you change an enforced invariant, change its comment in the same
edit. A comment that outlives its rule is worse than none — it is how `TsInboundDiff` asserted the wrong
project key space for weeks (2026-07-26, fixed) while the code did something else.

## Every major flow is a single forward pass

No back-edges: a stage never reaches backwards to re-run an earlier one.

- **Load:** scan → TS read → resolve → write-back stamp → re-resolve (when it stamped) → project into rows.
- **`ApplyFilters`:** filter → group → sort → flatten → publish.

Keep it that way. No stage silently loops; the load's one re-entry (write-back → re-resolve, reusing the
scan) is explicit, bounded, and named. A flow that needs to revisit an earlier stage is otherwise a signal
the stage boundaries are wrong, not an invitation to add a loop. (This is also why `Resolve` is one long method with sequential phases rather
than extracted helpers — see *A note on long methods*.)

## The view/view-model seam

- **Code-behind handlers are one-line forwards to the view-model.** Never write application state or row
  content from code-behind. The documented exception is a per-instance *visual* repair for a framework
  template defect — `NarrowNumberBox_Loaded` (`UI.md` → *WinUI gotchas*), which writes no state.
- **`MainViewModel` holds zero `Microsoft.UI.*` references** — it is testable in a plain host with no XAML
  runtime, which `VERIFICATION.md` depends on.
- **`x:Bind` only; classic `{Binding}` is never used.** `OneWay` where state mutates, left at the `OneTime`
  default for immutable row properties. The search box's `TextChanged` forward is a deliberate exception:
  `x:Bind TwoWay` on `TextBox.Text` updates only on focus-loss, which would kill live filtering.
- **Reach into a row template with an attached property** (`GridColumns.ApplyRuler`, `BadgeRuns.Tokens`),
  never from `MainWindow` code-behind.
- **A shared control takes identity as delegates, never as a key.** `TsFieldsEditor` serves the target,
  project, template and plan edit dialogs and never learns which table or row it is editing: commit, effective-value
  seed, and mark lookup all arrive as delegates closed over the key at the call site (`CommitField`,
  `EffectiveValue`, `MarkResolver`). Each new cross-cutting concern has taken that same seam rather than
  teaching the control about TS keys — do the same with the next one
  (`openspec/changes/archive/2026-07-06-field-editor-flyout/design.md` D4–D6,
  `…/2026-07-26-flyout-field-marks/design.md` D3–D4).

## An untestable surface gets a pure sibling

When a surface can't carry a unit test — an attached property, a code-behind cell, a renderer into a
virtualized container — the decision logic moves out into a pure static sibling and what remains is a dumb
renderer with nothing left to test. That is the standing answer to "this can't be unit-tested", not an
argument for leaving logic uncovered: `Badges.Split`/`IsWarning` behind `BadgeRuns`, `AmbiguityReport.Build`,
`SyncMarks`, `VisibleTonightPass.PlanTargets`/`PlanProjects`. It is the leaf-level form of the
zero-`Microsoft.UI.*` rule above.

**The exception, and its test:** `TsFieldsEditor.SentinelCell` deliberately keeps its logic in place. The
pattern pays off when a *decision layer* sits above dumb controls; here the state **is** the controls (the
checkbox's `IsChecked`, the box's `IsEnabled`, the captured effective/last-known values), so extraction would
relocate code without buying a test (`openspec/changes/archive/2026-07-24-sentinel-cell/design.md`). Ask which
one you have before reaching for the split.

## Async and the UI thread

- **Every handler that awaits routes through `UiTask.FireAndLog`** — never a bare `async void`, never a `_ =`
  discard. `FireAndLog` is the app's **only** `async void`, and that is a grep-checkable invariant: without it
  an escaping exception crashes the app with nothing in `tsm.log`, the one place failures are promised to land.
- The UI thread serializes every command through the busy gate; workers do I/O only. `TsJournal` and
  `TsInboundStore` are the only cross-thread mutables, both coarsely locked. No lock-then-await, no
  sync-over-async on the UI thread. Mechanics: `ARCHITECTURE.md` → *Concurrency* / *Busy exclusion*.
- **Serialize with UI-thread-confined state, not an async lock.** A plain check-and-set field
  (`TryBeginBusy`) or a UI-thread task chain (`CommitChain`) is the house answer; a `SemaphoreSlim` was
  weighed and rejected independently by both changes that needed serialization
  (`openspec/changes/archive/2026-07-24-busy-gate/design.md` D1,
  `…/2026-07-24-serial-commits/design.md` D1). Confinement makes the ordering readable and keeps the
  no-lock-then-await rule true by construction.
- **Edit-vs-edit ordering belongs to the surface, not the gate.** `CommitChain` is per editing surface (one
  per `TsFieldsEditor`, one per window for inline Desired) because a rapid re-commit of the same field is
  *that surface's* bookkeeping race, not a resource conflict. Pushing it down into the VM funnel or
  `TsEditGate` would serialize unrelated edits app-wide and change `ApplyAsync`'s contract for every caller;
  a busy-gate-style refuse-and-revert would bounce a second perfectly valid keystroke back at the user. Both
  were rejected on those grounds, as was disabling the form (`UI.md` → *Editing*: disabling moves focus
  and re-fires the commit). `openspec/changes/archive/2026-07-24-serial-commits/design.md` D3.

## Naming

Private fields are `_camelCase`, uniformly. Ported Hungarian forms — `mId`, `sCurrent` — are not
used here and should not arrive with code copied from TargetPlanner.

## One helper, two null postures

A shared helper answers exactly one question and stays null-naive; each consumer keeps its own guard, because
consumers legitimately want **opposite** answers for the absent case. Unifying the guard into the helper looks
like removing a duplicate and actually breaks one of the two callers:

- **`TsSync.BaselineMatches`** — the pull-skip rule reads it straight (no baseline ⇒ no match ⇒ pull), while
  the push review's staleness warning negates it behind its own has-a-baseline guard (no baseline ⇒ nothing to
  have changed *since* ⇒ stay silent). Folding the guard inward puts a false "remote changed" warning on every
  first-ever push — which is exactly what the originating review's own fix snippet did
  (`openspec/changes/archive/2026-07-24-push-rule-dedup/design.md` D2).
- **`TsValueText.From`** — one canonical text form, three consumers: `TsJournal` compares against it for
  no-op pruning, `TsSync.FormatValue` maps null to the literal `"null"` (a push-review line must render
  something), `SyncMarks.FormatValue` passes null through (a tooltip's null means "no old value")
  (`openspec/changes/archive/2026-07-24-review-polish/design.md` D5).

Both are written as comments at the enforcement point, per the rule above. If you find a third, add it here.

## Time comes through the clock seam — no ambient reads

TSM reads **no** ambient clock: every time value comes from an injected `Astronomy.Core` `IClock`
(`DateTime.UtcNow`/`Now` appear nowhere in app code — grep-verified at the 2026-08-11 migration, and the
count stays 0). That covers journal entry stamps, sync baseline `RecordedAt`, the resolve's scan stamp, the
Visible-Tonight planning input, and report "generated at" stamps and filenames. The seam is plumbed as an
**optional trailing parameter** so no call site is forced to care: `MainViewModel` exposes a settable
`Clock`; `TsJournal`/`TsSync` take `IClock? clock = null` (sync threads its clock down into the journal it
owns); `ReconciliationLoader.ResolveAsync` takes one likewise. New code that needs *now* takes the clock the
same way rather than reaching for the static — the point is that every provenance stamp is testable and that
ISM inherits a clean seam when it copies the sync/journal shapes. The clock convention itself is AL's
(`..\Library\CONSUMERS.md`); this is TSM's adoption of it.

## A note on long methods

`TargetResolver.Resolve` is ~310 lines of sequential phases and is **deliberately not decomposed**: 28 locals
cross its phase boundaries, so extracting phases would mean threading ~28 pieces of shared state through helper
signatures — trading one readable pipeline for the parameter-explosion the 2026-07-24 review had to fix
elsewhere (M3, the 24-parameter constructor → `row-param-objects`). Length alone is not the smell; *shared
mutable state that has nowhere to live* is. If this ever does get split, the unit is a phase **object** that
owns the state, not a static helper that receives it.

The companion rule where that fix *was* applied: when a parameter object groups a run of same-typed fields
(`RowNumbers` — a run of same-typed ints followed by same-typed doubles), **construct it with named
arguments at every call site**. The
type can't enforce this, and a positional build silently restores the transposition hazard the parameter
object exists to remove (`openspec/changes/archive/2026-07-24-row-param-objects/design.md` D3).

## Not owned here

No-migration / no-back-compat, and fail-fast on input-contract violations, are **global** rules that apply to
every project in this portfolio — they live in `~/.claude/CLAUDE.md` (rules 15 and 16) and are not restated
here. UI look-and-feel conventions are `UI.md`'s; domain conventions are `DOMAIN.md`'s. Build and test mechanics are `VERIFICATION.md`'s.

**The Ctrl+N diagnostics window is not this repo's code** (graduated to the Library 2026-08-10,
`diagnostics-portable-core`). The window, the capture / delayed-capture / checkpoint flow, its
`UiTask.FireAndLog` wrap and the observation session all live in
`..\Library\Astronomy.Diagnostics.WinUI` (`DiagnosticsWindow.ShowOrFocus`); TSM supplies only the icon path
and the accelerator wiring, and `ObservationSession.Begin` is no longer called app-side. The app-side
`Support\DiagnosticsWindow.cs` was deleted — don't reintroduce a local copy or patch behavior here; fix it in
the Library so TP and every other consumer gets the same window.
