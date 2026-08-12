# catalog-export — delta (add-target-rename)

## RENAMED Requirements

- FROM: `### Requirement: The push is the sole emitter`
- TO: `### Requirement: The push emits TSM-authored changes`

## MODIFIED Requirements

### Requirement: The push emits TSM-authored changes
TSM SHALL emit inbox records for its own authored changes at exactly one point: after a successful
push-as-replay commit — the single funnel where TS, the system of record, actually changes by TSM's
hand. (Target changes TSM merely observes arriving from TS's side emit through the observed-emission
path — see *Observed inbound target changes emit at pull*; no other emission point exists.) Each
replayed intent change SHALL map to its full-value upsert op(s) as defined by the catalog inbox
contract v1 (`..\IntervalSchedulerManager\docs\design\catalog-inbox-contract.md`): desired-count and
exposure-plan edits to `exposure-plan-upsert`, target-level intent (enable state, coordinates,
name — including a rename committed through TSM's editing surface) to `target-upsert`,
project-settings edits to `project-upsert`, adoption inserts to the
adoption record set. Local edits, adoption inserts, and project-settings edits write the local
working copy and journal only and SHALL NOT emit at edit time — a Catalog.db row means "authored
intent as committed to TS," so intent the user abandons or trims at push review is never emitted.
Emission SHALL cover only changes of user-authored origin: the push distinguishes user-authored
changes from automatic write-back changes, and only the former emit (see *Actuals never emit*). Every
record is a full-value upsert carrying the complete row values as committed to TS — even when the
triggering edit changed a single field — keyed by TS guid identity, with one deliberate exception:
`desired_count` carries the authored value even when a same-push write-back ratchet moved the
committed value (see *Actuals never emit*). Repeated emission of the same
values is permitted (idempotent upserts); TSM SHALL NOT keep sent-tracking state.

#### Scenario: Pushed desired-count edit emits a full exposure-plan row

- **WHEN** a push replays a journaled desired-count edit and the push commits
- **THEN** TSM appends one `exposure-plan-upsert` record carrying the plan's full committed values
  (template reference, exposure seconds, desired count, enabled), not just the changed count

#### Scenario: Pushed rename emits a full target row

- **WHEN** a push replays a journaled target rename and commits
- **THEN** TSM appends one `target-upsert` record carrying the target's full committed values, the
  new name among them

#### Scenario: Local edits alone emit nothing

- **WHEN** the user edits desired counts, toggles enables, adopts disk rows, or edits project
  settings — journaling to the local working copy — and does not push
- **THEN** no inbox record is appended

#### Scenario: Entries trimmed at push review are never emitted

- **WHEN** the user removes a journaled change during push review and then pushes the rest
- **THEN** the trimmed change produces no inbox record; only the replayed changes emit

#### Scenario: Pushed project-settings edit emits a project upsert

- **WHEN** a push replays a journaled project-settings edit and commits
- **THEN** TSM appends one `project-upsert` record with the project's full committed values

### Requirement: Inbox transport per contract v1

TSM SHALL write inbox files exactly as the contract's transport section specifies: files named
`tsm-<yyyyMMdd-HHmmss>.jsonl` in the contract-named inbox directory (a new file per TSM session is
acceptable), UTF-8 without BOM, one JSON object per line with `\n` endings, whole lines flushed
atomically (a line is either complete on disk or absent), append-only within a file. When the stamped
name is already taken (a push and a pull emitting within the same second), the stamp SHALL advance to
the next free second — never overwrite, never a name outside the contract's pattern. Every record
carries the v1 envelope: `v: 1`, `at` (UNIX seconds UTC — the push commit that carried the change,
or the pull completion time for observed-emission records, which is when TSM observed the TS-committed
value), `source: "TSM"`. TSM SHALL create the inbox directory if it does not exist, and SHALL NOT
touch files the consumer has renamed to `*.processing`.

#### Scenario: First emission creates the inbox directory

- **WHEN** a push emits and the inbox directory does not exist
- **THEN** TSM creates the directory and writes the records; the emission succeeds

#### Scenario: Records carry the v1 envelope

- **WHEN** any record is emitted
- **THEN** the line parses as a single JSON object with `v` = 1, `source` = "TSM", `at` set to the
  push commit time (or the observing pull's completion time), and an `op` from the v1 op set

#### Scenario: Consumer-claimed files are left alone

- **WHEN** the inbox contains `*.processing` files at emission time
- **THEN** TSM ignores them and appends only to its own `*.jsonl` session file

#### Scenario: Same-second emissions get distinct files

- **WHEN** a push commits and its closing pull carries an observed target change within the same
  second
- **THEN** two files publish under distinct stamps, neither overwriting the other

## ADDED Requirements

### Requirement: Observed inbound target changes emit at pull

At every pull that overwrites the local working copy (the open pull, a manual Pull-now, a push's
closing pull), TSM SHALL project the pull's inbound diff into the inbox: each **target-table** field
change observed on an **existing** row (correlated by TS guid across the pull) SHALL emit one
full-value `target-upsert` whose values are read from the fresh local copy after the pull — mirroring
TS-committed intent whichever surface authored it, per the same posture as the template mirror.
Target-table columns are user-authored intent by construction, so no origin bookkeeping applies on
this path. The scope is deliberately narrow: remotely-**added** targets SHALL NOT emit (a target
without its plans is half a family — an accepted residual), and inbound changes on project, plan, or
template rows SHALL NOT emit through this path (project settings are the feed-v2 lane; plan columns
include actuals). A pull whose inbound diff contains no target-table field changes SHALL emit
nothing — no empty file. Records are ordinary contract-v1 upserts; emission consumes the single
pull's diff, not accumulated session state, so one pull produces at most one observed-emission file.

#### Scenario: A BIRDWATCHER rename flows at the next pull

- **WHEN** a target was renamed in TS's UI on BIRDWATCHER and TSM's next pull observes the name
  arriving changed
- **THEN** TSM appends one `target-upsert` carrying the target's full post-pull values, the new name
  among them

#### Scenario: The closing pull never echoes the push's own changes

- **WHEN** a push commits TSM-authored changes and its closing pull returns them
- **THEN** those fields do not diff (local-first edits are already identical on both sides) and no
  observed-emission records are produced for them

#### Scenario: A remotely-added target stays silent

- **WHEN** a pull observes a target row that exists remotely but not locally
- **THEN** no observed-emission record is produced for it

#### Scenario: Inbound plan actuals stay silent

- **WHEN** a pull observes changed `acquired`/`accepted` (or any other plan/project/template field)
  arriving from BIRDWATCHER
- **THEN** the observed-emission path produces nothing for those rows

#### Scenario: Quiet pull, no file

- **WHEN** a pull's inbound diff is empty or touches no target-table fields
- **THEN** no observed-emission file is written

#### Scenario: Observed-emission failure is loud and the pull stands

- **WHEN** the observed emission fails after a completed pull (disk error, uncreatable path)
- **THEN** the pull remains applied, TSM logs the failure naming the inbox path and operation, and
  the user sees an error — not a silent skip
