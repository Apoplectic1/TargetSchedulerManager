# Design: enable-visible-tonight

## Context

TSM edits a local TS working copy through journaled `TsEditableSchema` edits; `target.active` (Bool) and
`project.state` (Enum: Draft/Active/Inactive/Closed) are already editable fields, both cadence-scope-free.
The library already provides the entire visibility computation:
`CoarseVisibility.IsAboveHorizonForAtLeast(target, location, night, horizon, minDuration)` answers
"single contiguous window of at least this duration above the horizon tonight" in closed form, with
`NightCalculator` supplying the astronomical dusk→dawn window and `ScalarHorizonProfile(0)` expressing the
geometric horizon. TSM currently references only `Astronomy.Catalog` + `Astronomy.Diagnostics` and has no
site concept.

**History note:** an earlier draft of this change used a TS-faithful per-project gate (`minimumaltitude` /
custom `.hrz` horizon / `horizonoffset`) and promoted TP's `.hrz` parser into the library. The user
redirected to the literal 0° predicate mid-implementation (2026-07-23): TP depends on the AL horizon
routines for its own horizon handling, and the TS-gate fidelity was unwanted coupling. All library and TP
edits from that draft were reverted; this change now touches **only** the TSM repo.

## Goals / Non-Goals

**Goals:**
- One button press reconciles `target.active` / `project.state` with tonight's sky.
- Zero new math and zero library changes — one existing `CoarseVisibility` call per target plus the
  existing edit journal.

**Non-Goals:**
- No TS-gate fidelity (`minimumaltitude`, custom horizon, `MinimumTime`, twilight levels) — TS applies its
  own gates at plan time; the button answers only "geometrically up tonight for a usable stretch."
- No `.hrz` / horizon-profile involvement and no library (`..\Library`) or TP changes.
- No preview/confirmation dialog — the button applies decisively (user decision: "this is why it's a button").
- No multi-night lookahead; the reference night is strictly tonight.
- No site-settings UI; no NINA-profile parsing; no writes to BIRDWATCHER (Push stays the only remote writer).
- No moon, weather, or exposure-plan-completeness considerations.

## Decisions

### D1 — Predicate: `IsAboveHorizonForAtLeast` at 0° with a 30-minute default
`CoarseVisibility.IsAboveHorizonForAtLeast(target, site, tonight, ScalarHorizonProfile(0),
TimeSpan.FromMinutes(30))`. The 30-minute default lives in `DevDefaults` beside the site constants.
Single-contiguous-window semantics come from the library contract (a split arc doesn't count — one
imaging session can't span a horizon dip), and match `BestSession`'s placement contract.
**Alternative considered:** `IsEverVisible` (any-duration, 0°) — rejected by the user in favor of a
usable-stretch threshold; 30 minutes is the chosen default.

### D2 — Tonight's night window
`NightCalculator` at the configured site using the same night-of convention TP uses (the night belonging
to the current site-local date; verify the anchor in the existing API during implementation rather than
inventing a second convention). Epoch handling: TS rows are effectively J2000; precession is far below
the predicate's resolution — rows are fed to `Target` as-is.

### D3 — Flip rules and ordering
Single pass, projects independent:
1. Skip projects whose `state` is `Draft` or `Closed` — neither the project row nor its child targets are
   read or written.
2. For each target of an `Active`/`Inactive` project: `active ← predicate(target)`.
3. Then `state ← (any child target enabled ? Active : Inactive)` — derived from the **post-pass** `active`
   values, matching the user's rule "if a project does not include any enabled targets, it is disabled."
4. Mosaic panels need no special casing: each panel is an ordinary target row evaluated individually; the
   parent project follows rule 3 like any other.

Unchanged values are not re-written (no-op edits create no journal entries).

### D4 — Apply through the existing edit path
The button issues the same journaled edits the grid makes (`target.active`, `project.state` via
`TsEditableSchema`), so write-back, the dirty badge, and reviewed Push behave identically to hand edits.
Push remains optional and user-initiated. **Alternative considered:** direct SQL bulk update — rejected;
it would bypass the journal and break push-as-replay.

### D5 — Site input (`DevDefaults` `NamedSite`)
`DevDefaults` gains site constants (lat/long/TZ/elevation) materialized as a `NamedSite`/`Location`,
mirroring the db-path pattern. Real values are copied from TP's settings JSON at implementation time.
No horizon file, so no file-input failure mode exists; the only contract inputs are compile-time
constants.

### D6 — Button UX
A toolbar button on the main window ("Visible tonight"); DOMAIN.md's add-a-UI-element checklist applies.
The pass is synchronous (tens of targets × one closed-form call — microseconds). On completion an
InfoBar-style summary reports counts: targets enabled / disabled / unchanged, projects flipped. No
confirmation before applying.

## Risks / Trade-offs

- [Blind two-way apply re-enables deliberately-paused targets] → Accepted by explicit user decision; the
  summary counts make an unexpected flip visible, and a preview dialog can be layered on later without
  spec change.
- [Enabled ≠ schedulable: TS additionally gates on its own altitude rules, `MinimumTime`, twilight] →
  By design; the 0° predicate is intentionally decoupled from TS configuration.
- [DevDefaults site drifts from the NINA profile's real site] → One rig, one site; values are constants a
  few lines from the db paths and reviewed whenever the rig moves.
- [Night-of convention ambiguity when pressed after midnight] → Reuse the library/TP convention (D2);
  implementation verifies the anchor with a test rather than assuming.

## Open Questions

None blocking — all decision points were resolved with the user (0° literal predicate with 30-minute
default duration, blind apply, tonight only, Draft/Closed skipped, DevDefaults site, no library changes).
