## ADDED Requirements

### Requirement: The journal records row inserts as first-class entries
The journal SHALL support an insert entry kind carrying the table, the full column payload, and the row's
minted guid (the cross-copy name), alongside the existing field-edit kinds. Insert entries SHALL
participate in the derived dirty state, the unpushed count, and the dirty-open prompt exactly like field
entries. Field edits addressing a locally inserted, not-yet-pushed row SHALL journal normally under the
row's local key; the replay requirement below defines how they travel. Insert entries have no baseline and
SHALL NOT be pruned by the net-no-op rule; they clear only by push or discard.

#### Scenario: An adoption journals inserts
- **WHEN** an adoption creates a target and a plan locally
- **THEN** the journal holds one insert entry per created row (payload + guid) and the dirty badge counts them

#### Scenario: Inserts survive restart
- **WHEN** the app restarts with unpushed insert entries
- **THEN** the rows are still present in the local db, still journaled, and still push-eligible

### Requirement: Push replays inserts by guid, minting remote ids
The push SHALL replay insert entries as remote INSERTs **before both field legs**, references before their
referrers: templates, then targets, then plans. The remote autoincrement mints the remote integer id; the
journaled guid travels with the row and is the correlation name. A parent reference whose integer id can
diverge between copies (a plan's `targetid` — its target may itself be a local creation; a target's
`projectid` when the project has a guid; a plan's template reference when the template is itself a local
creation) SHALL be resolved on the remote by parent guid, never by copying a local integer id. The
reference to a template that came from a pull MAY travel as the integer id — such ids are copy-stable by
construction.
Field entries addressing a locally inserted row SHALL be folded into that row's INSERT payload (the
row lands remotely with its final values); they SHALL NOT replay as UPDATEs keyed by the local id. Insert
failures follow the existing per-entry rules: reported loudly, retained in the journal, and a whole-db
refusal aborts remaining entries as not-attempted.

#### Scenario: Target lands before its plan
- **WHEN** one push replays a target insert and its plan insert
- **THEN** the target INSERT runs first and the plan's `targetid` is the remote target row found by guid

#### Scenario: Later edit folds into the insert
- **WHEN** an unpushed adopted plan's `desired` is edited from 42 to 60 before the push
- **THEN** the remote INSERT carries desired 60 and no separate UPDATE replays for it

#### Scenario: Retained insert survives a partial push
- **WHEN** a plan insert fails (e.g. its remote parent lookup finds nothing)
- **THEN** the failure is reported naming the row, the insert entry stays journaled, and other entries applied normally

### Requirement: The closing pull renumbers inserted rows and that is defined behavior
After a push that replayed inserts, the closing pull SHALL replace the local rows with the remote-minted
copies: the local integer ids of inserted rows change to the remote ids, the guid is unchanged, and all
subsequent journaling and marks key off the post-pull ids. No journal entry survives the push to reference
a stale pre-push id.

#### Scenario: Fresh ids after the round-trip
- **WHEN** an adopted plan (local id 900) is pushed and the remote mints id 712
- **THEN** after the closing pull the local plan row carries id 712, the journal is clear, and a subsequent desired edit journals under key "712"

### Requirement: The push review presents creates distinctly
The push review dialog SHALL present insert entries as a distinct creates section — each created row named
by its entity identity (project · target, target · filter) with its key values — separate from the
write-back summary and the manual field list, so a reviewer sees exactly which rows will come into
existence on BIRDWATCHER before confirming.

#### Scenario: Review names the creations
- **WHEN** the user opens the push review with one unpushed adopted target and plan
- **THEN** the review shows a creates section naming the new target (project, coords) and the new plan (filter, seconds, counts)
