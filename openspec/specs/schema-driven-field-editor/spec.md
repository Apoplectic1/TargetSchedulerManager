# schema-driven-field-editor Specification

## Purpose

The reusable editing core: a form generated from `TsEditableSchema` for one TS row, seeded from the db,
committing per field through the guarded gate. Library portion (enum maps) is consumer-neutral.

## Requirements

### Requirement: The reference exposes enum value maps
`TsEditableSchema` SHALL expose, for each `EnumName` used by an editable field, an ordered list of
`(code, label)` pairs suitable for populating a selection control: `TargetPriority`
(−1 Default, 0 Low, 1 Normal, 2 High), `ProjectState` (0 Draft, 1 Active, 2 Inactive, 3 Closed),
`ProjectPriority` (0 Low, 1 Normal, 2 High). Lookup by unknown name SHALL return empty/absent, not throw.

#### Scenario: Enum-typed fields resolve to value maps
- **WHEN** a consumer resolves the `EnumName` of every `TsFieldType.Enum` field in the reference
- **THEN** each yields a non-empty ordered code/label list, and `TargetPriority` includes code −1 labeled "Default"

### Requirement: The form renders every editable field, cadence-breaking ones included
The generated form SHALL render every TsEditableSchema field of the entity's table that is present on the
open db, in schema order, choosing the control by TsFieldType - including cadence-breaking fields, which
SHALL commit like any other (no confirmation; the editor's atomic cadence clear and the reviewed push are the
safety - user decision 2026-07-07). All per-field commit semantics (guarded gate, revert on failure,
immediate in-place mirrors) are unchanged.

#### Scenario: Exposure-plan form includes enabled
- **WHEN** the user opens a plan flyout and toggles enabled
- **THEN** the write applies through the guarded gate (with its transactional cadence clear) and the in-grid checkbox mirrors

#### Scenario: Project form ships filter switch frequency
- **WHEN** the user commits a new filterswitchfrequency in the project flyout
- **THEN** the write applies and every target's cadence rows in that project were cleared atomically with it

### Requirement: The form seeds from current database values
Opening the editor SHALL read each rendered field's current value from the currently-selected TS db
(via the editor's read path), off the UI thread, and populate the controls before they accept input. A read
failure SHALL surface an error and present no editable form (no controls defaulting to fabricated values).

#### Scenario: Values reflect the db, not the grid snapshot
- **WHEN** the flyout opens for a target whose `rotation` was changed in the db after TSM's last load
- **THEN** the Rotation control shows the current db value

### Requirement: Each field commits independently through the guarded gate
A changed control SHALL commit on change/focus-loss via `TsEditGate.ApplyAsync` with the entity's audit
label. On a verified write the control keeps the value and any in-grid mirror of the field updates in place
(no grid reload). On refusal or failure the control SHALL revert to the last-known value and the outcome
SHALL be surfaced with the existing refusal/failure wording. Closing or light-dismissing the flyout SHALL
never leave an uncommitted change pending.

Effective-exposure resolution behind the exposure sentinel control SHALL treat 0 as a literal zero-second
exposure: only the −1 defer-to-template sentinel resolves through the template (matching the Library's
adjudicated contract — TS's planner tests `!= -1`). A verified write of 0 SHALL mirror the in-grid Seconds
cell to 0 at once, like any other committed value; a resolved effective exposure of 0 SHALL NOT be discarded
as unknown (null is reserved for a missing row/template or a fault, where the cell is left for the next
reload).

#### Scenario: Successful edit
- **WHEN** the user changes Priority from Default to High and focus leaves the control
- **THEN** one gate write for `target.priority` = 2 occurs, is read-back verified, appears in the diagnostics log, and the flyout stays open showing High

#### Scenario: Refused edit reverts
- **WHEN** a write is refused (e.g. db busy with an open sidecar)
- **THEN** the control returns to its prior value and the existing refusal message is shown

#### Scenario: Out-of-bounds input is bounded
- **WHEN** the user types 500 into Rotation (Max 360)
- **THEN** the committed value is clamped to the schema bounds (no out-of-range write reaches the gate)

#### Scenario: Committed zero exposure mirrors at once
- **WHEN** the user commits 0 in the plan flyout's Exposure control and the gate verifies the write
- **THEN** the row's Seconds cell updates in place without a grid reload, rendering exactly what the next
  load renders (0 on a plan+disk row; the TS-only plane's pre-existing row-model rendering shows zero
  seconds as "—") — the invariant is mirror == reload, with 0 resolved literally on both sides

#### Scenario: Sentinel write still resolves through the template
- **WHEN** the user checks "template default" (a −1 write) and the gate verifies it
- **THEN** the Seconds cell mirrors the template's default exposure resolved via the plan→template join, unchanged from today

### Requirement: Commits from one editing surface serialize in confirmation order
Commits issued from one editing surface (a field-editor form, the grid's inline Desired cell) SHALL
apply one at a time, strictly in confirmation order: a commit SHALL NOT start until every earlier commit
from that surface has completed. A later confirmation SHALL NOT cause an earlier verified write to
report failure or revert, and after the last commit completes the control's displayed value, the local
db, and the journal's collapsed last-write for the field SHALL agree.

#### Scenario: Rapid re-confirmation of one field lands last-value-wins, no spurious revert
- **WHEN** the user confirms a value and immediately confirms a second value in the same field while the first commit is still writing
- **THEN** both commits apply in order, neither reports a false failure, and the control, db, and journal all hold the second value

#### Scenario: A slow commit does not let a later one overtake it
- **WHEN** a commit is slow (e.g. the local db is briefly busy) and another field in the same form is confirmed meanwhile
- **THEN** the second commit starts only after the first completes, and each field's last-known state updates in confirmation order

### Requirement: Field rows carry a leading sync-direction mark
The generated form SHALL render a leading fixed-width mark column before the label column: each field row
shows its own `←`/`→`/`⇄` (blank when clean), resolved through an injected per-field mark resolver (the
same optional-delegate seam style as the commit and effective-value callbacks; when no resolver is
injected, no mark column renders). The mark slot SHALL reserve its width even when blank so field labels
stay mutually aligned. A marked row SHALL carry the field's old→new lines as its mark's tooltip; blank
marks SHALL show no tooltip.

#### Scenario: Pending field is marked in the flyout
- **WHEN** the user opens the template flyout for a template with an unpushed `gain` edit
- **THEN** the Gain row shows `→` with the old→new tooltip and every other clean row shows a blank,
  aligned mark slot

#### Scenario: Exact-field collision is visible where the user edits
- **WHEN** a field has both an unpushed local edit and a recorded rig-side change
- **THEN** its row shows `⇄` and the tooltip lists both directions' lines

### Requirement: Field marks refresh after every commit
After each commit completes — verified, refused, or failed — the form SHALL re-resolve every rendered
field's mark from fresh facts, so a just-committed field shows `→` immediately (and a reverted commit
shows the field's true state). The refresh SHALL resolve all fields in one resolver pass, not one fact
rebuild per field.

#### Scenario: Committing a field lights its mark live
- **WHEN** the user toggles moon avoidance in the template flyout and the write verifies
- **THEN** the Moon avoidance row's mark reads `→` without closing or reopening the flyout

#### Scenario: A refused commit leaves the true state
- **WHEN** a commit is refused and the control reverts
- **THEN** the field's mark still reflects only the facts (no `→` from the refused write)

### Requirement: Sentinel columns render as their meaning with arm-before-write editing
A numeric column carrying a defer-to-default sentinel SHALL render as a "use default" checkbox over a
number box — never as the raw sentinel value. The checkbox SHALL be checked exactly when the column holds
the sentinel (the box then disabled, showing the resolved default when it can be known). Checking SHALL
commit the sentinel; unchecking SHALL only arm the box (enabled, seeded with the resolved default,
focused) — the override value commits only when the user confirms a number, never from the uncheck
gesture alone. The sentinel value itself SHALL be exempt from the schema Min/Max clamp. A failed or
refused commit SHALL restore the full compound state — checkbox, box enablement, and value — to what the
column actually holds.

#### Scenario: Unchecking writes nothing
- **WHEN** the user unchecks "use default" and then light-dismisses the flyout without confirming a number
- **THEN** no write occurred and the column still holds the sentinel

#### Scenario: Failed sentinel write restores the compound state
- **WHEN** checking "use default" commits the sentinel and the write is refused
- **THEN** the checkbox returns to unchecked, the box re-enables, and it shows the last real value

#### Scenario: Failed override while the column holds the sentinel
- **WHEN** the user confirms an override number, the write fails, and the column still holds the sentinel
- **THEN** the cell returns to the checked-default presentation (box disabled, resolved default shown)

### Requirement: The project name field edits as the base name

In the project editor, the `name` field SHALL seed with the **base name** — the stored name minus its
altitude clause — and a commit of that field SHALL write the stored name **composed** from the edited
base and the currently stored `minimumaltitude`. A commit of the `minimumaltitude` field SHALL likewise
recompose the stored name from the current base and the new altitude — journaling **two** per-field
writes (`minimumaltitude`, then `name`), each through the guarded gate, so the push review shows both. A
whitespace-only base SHALL be refused at the control (revert, the rename-verb precedent). The name field
SHALL NOT interpret typed text as an altitude — the altitude field is the only way to change the floor.

#### Scenario: The name field shows the base, not the clause

- **WHEN** the project editor opens on `Nebulae - 45`
- **THEN** the name field shows `Nebulae` and the min-altitude field shows 45

#### Scenario: A base edit recomposes with the stored altitude

- **WHEN** the user edits the base from `Nebulae` to `Nebula Survey` and commits, altitude untouched
- **THEN** one name write journals: the stored name becomes `Nebula Survey - 45`

#### Scenario: An altitude edit recomposes the name

- **WHEN** the user edits min altitude from 45 to 40 and commits
- **THEN** the journal gains a `minimumaltitude = 40` write and a rename to `Nebulae - 40` — two
  push-review lines

#### Scenario: A nonconforming name heals on its next commit

- **WHEN** the editor opens on a clause-less project `Widefield` (altitude 30) and the user commits any
  edit to the name or altitude field
- **THEN** the composed write produces `Widefield - 30` — the dialog edit is a nonconformance remedy

#### Scenario: A whitespace-only base is refused at the control

- **WHEN** the user clears the base name to spaces and commits
- **THEN** the control reverts to the seeded base and nothing journals
