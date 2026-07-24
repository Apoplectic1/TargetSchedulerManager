# sentinel-cell — extract `BuildSentinelNumber`'s closure state into a named class

## Why

The last maintainability item of substance from the 2026-07-24 review (M7's deferred half; the re-check's
"one item of any substance left"): `TsFieldsEditor.BuildSentinelNumber` is ~100 lines where three event
lambdas (checkbox Checked/Unchecked, box ValueChanged) share mutated state **through closure captures** —
the `effective` local is written by one handler and read by the others, the box's enabled state flips
across handlers, and the failure paths restore compound state (re-check + disable + restore value).
Correctly editing one handler requires holding all three; a partial reading is how a subtle sentinel bug
gets introduced. Timing rationale: the file is maximally warm (touched twice today — `CommitChain`,
`ClampToSchema`) and the rules are freshly re-derived; doing this cold later costs more.

## What Changes

- A private nested `SentinelCell` class owns the checkbox + box + resolved-default state as **fields**,
  with the three handlers as **named methods** (`OnUseDefaultChecked` / `OnUseDefaultUnchecked` /
  `OnValueConfirmed`) — every sentinel rule individually nameable. `BuildSentinelNumber` becomes a
  one-line delegation. Behavior-preserving: the rules move verbatim (seed-settle guard, arm-don't-write
  unchecking, clamp exemption for the sentinel, compound failure restores, post-commit default
  re-resolution, the serialized commit chain).
- The spec gains the sentinel cell's **interaction contract** — currently stated only in code comments —
  which is exactly what the extraction must preserve.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `schema-driven-field-editor`: codifies (not changes) the sentinel-cell interaction rules — rendered as
  meaning (never the raw −1), checked ⇔ the column holds the sentinel, unchecking only arms the box
  (no silent write), the sentinel value is exempt from the schema clamp, and a failed commit restores
  the full compound state.

## Impact

- **App**: `Controls/TsFieldsEditor.cs` only. **Tests**: none possible — WinUI control code is
  headless-untestable before and after; the existing 226 stay the regression floor for everything else.
- **Verification split (the reason this is NOT auto-archive despite being a pure refactor):** build +
  compile prove the shape; the sentinel rules have **no test net**, so the gate is your hands on the
  plan flyout's Exposure field — check "use default", uncheck-and-type an override, clear the box,
  and (if stageable) a failure revert. Archive on your word after that pass.
- **Docs**: CHANGELOG/ROADMAP digest line, same commit.
