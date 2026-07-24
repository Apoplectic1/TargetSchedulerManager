# row-param-objects — `ReconciliationRow`'s 29-parameter constructor → cohesive records

## Why

Review finding M3 (2026-07-24, count corrected from the review's 24): `ReconciliationRow`'s primary
constructor takes **29 positional parameters**, including adjacent runs of same-typed values
(`int planSeconds, int diskSeconds`; `int? desired, int? acquired, int? accepted, int disk, int planCount`).
A transposed pair compiles clean and renders as a subtly wrong grid — the single riskiest shape in the
app for both humans and LLM edits. The loader's three local row factories also re-thread the same
identity arguments (`panelKey, panelLabel, panelSource, enabled, tsTargetKey, projectTsKey, targetId`)
through every call.

## What Changes

- Two records group the cohesive parameters:
  - **`RowIdentity`** — target/project/source + panel triple + enable/TS-key/target-id/project-key: the
    identity shared by every row of one `EmitRows` call, built **once** per emit instead of re-threaded
    through each factory.
  - **`RowNumbers`** — the numeric columns in column order (`PlanSeconds, DiskSeconds, Desired, Acquired,
    Accepted, Disk, PlanCount, PlanHours, DiskHours`), constructed with **named arguments** at every site
    so the same-typed run stays transposition-proof where it now lives.
- The constructor drops to 12 parameters (`id, filter, purpose, plane, numbers, badge, isFlagged` + 5
  defaulted). **The public property surface is unchanged** — `Target`, `PlanSeconds`, `Desired`, every
  `*Text`/`*Visibility` member keeps its name and type, so XAML bindings and all row-consuming code
  compile untouched.
- Call sites updated: the loader's two inline `new ReconciliationRow(...)` + three local factories
  (which shrink to their real per-cell content), and the tests' `Make.Leaf` builder (whose keyword-argument
  surface stays the same, so no test bodies change).
- Behavior-preserving — `BuildRowsTests` + `RowTests` (and the whole suite) green is the verification.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `target-and-plan-flyouts`: codifies (not changes) the in-place mirror rule that today lives only in
  code comments — a committed edit with an in-grid mirror updates its row's cells in place (no grid
  reload; scroll/expansion/in-progress edits survive) and re-aggregates the owning header. This is
  exactly the behavior the row's mutable members (`ApplyDesired`/`ApplyPlanSeconds`/`ApplyPlanEnabled`)
  implement and the records refactor must preserve verbatim.

## Impact

- **App**: `ViewModels/Rows/ReconciliationRow.cs` (records + ctor), `Services/ReconciliationLoader.cs`
  (identity built once per emit; factories re-shaped). **Tests**: `Make.cs` internals only.
  **Docs**: CHANGELOG/ROADMAP digest line, same commit. No XAML changes, no behavior change, no schema.
- Pure refactor ⇒ auto-archives after the suite passes (standing rule 2026-07-24).
