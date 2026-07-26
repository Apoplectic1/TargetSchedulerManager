# CONVENTIONS.md — TargetSchedulerManager

**How code is written here, and where it goes.** The sibling reference docs answer different questions:
`ARCHITECTURE.md` = how the system works (design + load-bearing invariants) · `DOMAIN.md` = how the UI looks
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
| A custom control | `Controls` (`UpDownBox`, `TsFieldsEditor`, `BadgeRuns`) |
| Window / app-shell plumbing | `Support`, `MainWindow.*.cs` partials |

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

- **Load:** scan → TS read → resolve → project into rows.
- **`ApplyFilters`:** filter → group → sort → flatten → publish.

Keep it that way. A flow that needs to revisit an earlier stage is a signal the stage boundaries are wrong,
not an invitation to add a loop. (This is also why `Resolve` is one long method with sequential phases rather
than extracted helpers — see *A note on long methods*.)

## The view/view-model seam

- **Code-behind handlers are one-line forwards to the view-model.** Never write application state or row
  content from code-behind. The documented exception is a per-instance *visual* repair for a framework
  template defect — `NarrowNumberBox_Loaded` (`DOMAIN.md` → *WinUI gotchas*), which writes no state.
- **`MainViewModel` holds zero `Microsoft.UI.*` references** — it is testable in a plain host with no XAML
  runtime, which `VERIFICATION.md` depends on.
- **`x:Bind` only; classic `{Binding}` is never used.** `OneWay` where state mutates, left at the `OneTime`
  default for immutable row properties. The search box's `TextChanged` forward is a deliberate exception:
  `x:Bind TwoWay` on `TextBox.Text` updates only on focus-loss, which would kill live filtering.
- **Reach into a row template with an attached property** (`GridColumns.ApplyRuler`, `BadgeRuns.Tokens`),
  never from `MainWindow` code-behind.

## Async and the UI thread

- **Every handler that awaits routes through `UiTask.FireAndLog`** — never a bare `async void`, never a `_ =`
  discard. `FireAndLog` is the app's **only** `async void`, and that is a grep-checkable invariant: without it
  an escaping exception crashes the app with nothing in `tsm.log`, the one place failures are promised to land.
- The UI thread serializes every command through the busy gate; workers do I/O only. `TsJournal` and
  `TsInboundStore` are the only cross-thread mutables, both coarsely locked. No lock-then-await, no
  sync-over-async on the UI thread. Mechanics: `ARCHITECTURE.md` → *Concurrency* / *Busy exclusion*.

## Naming

Private fields are `_camelCase` (99 of them, uniformly). Ported Hungarian forms — `mId`, `sCurrent` — are not
used here and should not arrive with code copied from TargetPlanner.

## A note on long methods

`TargetResolver.Resolve` is ~310 lines of sequential phases and is **deliberately not decomposed**: 28 locals
cross its phase boundaries, so extracting phases would mean threading ~28 pieces of shared state through helper
signatures — trading one readable pipeline for the parameter-explosion the 2026-07-24 review had to fix
elsewhere (M3, the 24-parameter constructor → `row-param-objects`). Length alone is not the smell; *shared
mutable state that has nowhere to live* is. If this ever does get split, the unit is a phase **object** that
owns the state, not a static helper that receives it.

## Not owned here

No-migration / no-back-compat, and fail-fast on input-contract violations, are **global** rules that apply to
every project in this portfolio — they live in `~/.claude/CLAUDE.md` (rules 15 and 16) and are not restated
here. UI look-and-feel conventions are `DOMAIN.md`'s. Build and test mechanics are `VERIFICATION.md`'s.
