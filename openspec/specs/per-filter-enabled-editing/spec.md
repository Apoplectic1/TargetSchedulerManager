# per-filter-enabled-editing Specification

## Purpose

TSM app capability: enable/disable individual exposure plans (filter rows) in the grid — direct toggles whose
safety is structural (atomic cadence clear + reviewed push), the first consumer of cadence-safe editing.

## Requirements

### Requirement: Filter rows expose an enabled checkbox
Filter rows in the reconciliation grid SHALL present the TS `exposureplan.enabled` state as a checkbox
(following the shipped target-`active` checkbox pattern). Rows without a TS-side exposure plan (disk-only
actuals) SHALL NOT present the checkbox.

#### Scenario: TS-backed filter row shows its enabled state
- **WHEN** the grid renders a filter row whose exposure plan exists in the TS db with `enabled = 1`
- **THEN** the row shows a checked checkbox; an unchecked one when `enabled = 0`; none when the row has no TS plan

### Requirement: Toggles write directly; the atomic clear and the push review are the safety
A click on the enabled checkbox SHALL write immediately with no confirmation dialog (user decision
2026-07-07: the transactional clear makes the toggle produce exactly the TS-expected result - a fresh cadence
from the new plan set, slot-0 restart accepted - and nothing reaches BIRDWATCHER until the reviewed push).
The same applies to cadence-breaking fields committed from the flyouts (filterswitchfrequency included).

#### Scenario: Toggle journals and replays with its clear
- **WHEN** the user unchecks a filter row's enabled checkbox and later pushes
- **THEN** the local write cleared the target's local cadence rows atomically, one journal entry exists, and the push replay performs the same transactional write + clear on BIRDWATCHER

### Requirement: Toggles ride the guarded gate and update in place
A toggle SHALL route through `TsEditGate.ApplyAsync` (guarded, read-back-verified, audited,
off the UI thread). On success the row SHALL update in place (no grid reload; scroll and expansion preserved).
On any refusal or failure the checkbox SHALL revert and the outcome SHALL be surfaced to the user; the new
override-order refusal SHALL be worded to direct the user to the TS editor.

#### Scenario: Successful disable updates the row in place
- **WHEN** the user confirms disabling a filter row and the gate reports the write applied
- **THEN** the row reflects disabled without a reload, and the edit is present in the diagnostics log

#### Scenario: Override-order refusal reverts and explains
- **WHEN** the gate reports the override-order refusal
- **THEN** the checkbox reverts and the user sees a message naming the custom exposure order and pointing at the TS editor
