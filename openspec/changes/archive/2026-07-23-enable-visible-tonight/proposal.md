# Proposal: enable-visible-tonight

## Why

Out-of-season plans clutter both the TSM grid and TS's nightly consideration set: targets that never clear
the horizon tonight sit enabled beside the ones actually in play. The user currently curates
`target.active` / `project.state` by hand. The astronomy math and the journaled edit machinery both already
exist — one button can reconcile the enable state with tonight's sky in a single press.

## What Changes

- New TSM toolbar button ("Visible tonight") that bulk-sets, in one pass over the loaded TS working copy:
  - `target.active` ← whether the target has a single contiguous window of at least 30 minutes (default)
    above the geometric horizon (altitude 0°) during tonight's astronomical night at the user's site.
  - `project.state` ← `Inactive` when the project ends the pass with **zero enabled targets**, `Active`
    when it has at least one. Only `Active ↔ Inactive` transitions; `Draft`/`Closed` projects are skipped
    entirely — neither the project row nor any of its child targets are touched.
- The predicate is one existing library call — `CoarseVisibility.IsAboveHorizonForAtLeast` with
  `ScalarHorizonProfile(0)` — pure astronomy, deliberately independent of TS's per-project altitude
  rules (`minimumaltitude` / custom horizon / offset), which TS itself applies at plan time.
- All flips are ordinary journaled edits via the existing `TsEditableSchema` fields (`target.active`,
  `project.state`) — they ride write-back, the dirty badge, and the reviewed Push unchanged. Pushing is
  optional; the button is expected to be pressed per observing night against the local copy.
- TSM gains site awareness: a `NamedSite` (lat/long/TZ/elevation) built from `DevDefaults` constants —
  same pattern as the existing db paths; no settings UI, no horizon file.

## Capabilities

### New Capabilities
- `visible-tonight-toggle`: the bulk enable/disable pass — predicate definition (0° geometric horizon,
  tonight's astronomical night, ≥ 30-minute contiguous window), project/target flip rules, Draft/Closed
  exclusion, journaled-edit integration, and the site input contract.

### Modified Capabilities

None — the button composes existing capabilities (`schema-driven-field-editor` field edits, `write-back`
journaling) without changing their requirements. No library (`..\Library`) changes at all.

## Impact

- **TSM app only:** new toolbar button + bulk-apply command; new `Astronomy.Core` `ProjectReference`
  (pure-managed — build model unchanged); `DevDefaults` gains site constants and the 30-minute default.
- **Data:** writes only `target.active` and `project.state` on the **local** working copy through the
  journal; both fields are cadence-scope-free (no `filtercadenceitem` clearing). BIRDWATCHER is only
  touched by the existing reviewed Push.
- **Known fidelity limits (accepted, by design):** the 0° predicate is looser than TS's own gates
  (`minimumaltitude`, custom horizon, `MinimumTime`, twilight levels) — an enabled target can still be
  passed over by TS at plan time. The button answers "is it geometrically up tonight long enough to
  matter," nothing more.
