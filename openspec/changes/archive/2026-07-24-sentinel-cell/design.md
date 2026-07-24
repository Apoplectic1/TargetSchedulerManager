# sentinel-cell — design

## Context

`BuildSentinelNumber` renders a sentinel-bearing numeric column (today: `exposureplan.exposure`, −1 =
defer-to-template) as its meaning: a "use \<default\> (value unit)" checkbox over a number box. Three
closures share four pieces of mutable state (the `effective` captured local, `box.IsEnabled`,
`useDefault.IsChecked`, the owner's `_lastKnown[column]`), plus the owner's `_reverting` guard, `Revert`
helper, and serialized `Commit` chain.

## Goals / Non-Goals

**Goals:** captures → fields; lambdas → named methods; zero behavior change; the visual tree byte-identical.

**Non-Goals:** making the cell headless-testable (it holds WinUI controls either way); any rule change;
touching the plain `BuildNumber`.

## Decisions

- **D1 — private sealed nested class holding the owner reference.** Nested-private keeps full access to
  the owner's `_reverting`/`Revert`/`Commit`/`_lastKnown`/`_effective` without widening any surface. The
  cell builds its whole visual (checkbox + box+unit stack) in its constructor and exposes one
  `FrameworkElement Root`; `BuildSentinelNumber(field, seeded)` becomes
  `new SentinelCell(this, field, seeded).Root`.
- **D2 — one method per rule, bodies verbatim:** `OnUseDefaultChecked` (seed-settle guard → sentinel
  commit → on success re-resolve + relabel + show default; on failure restore uncheck+enable+value),
  `OnUseDefaultUnchecked` (arm only: enable, seed the override with the resolved default, focus — no
  write), `OnValueConfirmed` (cleared-box handling, `ClampToSchema`, no-op check, commit; failure
  restores to the checked-default compound state when the column still holds the sentinel, else to the
  last value), `LabelFor`. The `_effective` field replaces the captured local — same
  only-trustworthy-while-column-holds-sentinel discipline, now stated on the field.
- **D3 — verification is hands-on, not tests** (see proposal). The one mechanical safety rail: the diff
  should show the handler bodies moving without edits — review the diff for verbatim moves, the flyout
  pass for behavior.

## Risks / Trade-offs

- [No test net — the only real risk of this change] → mitigated by verbatim-move discipline + the
  user's sentinel-flyout pass gating archive; the change is one file and trivially revertible.

## Migration Plan

None. Clean rebuild.

## Open Questions

None.
