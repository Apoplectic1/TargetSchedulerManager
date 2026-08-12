# catalog-export Specification

## Purpose

Feeds intent authored through TSM's TS-editing surface into ISM's authored intent store during the
TS→IS coexistence window, by appending records to the catalog inbox (per the ISM-owned inbox
contract v1) when a push-as-replay commits intent to TS. A record means "authored intent as
committed to TS." One direction, contract-only: TSM never opens `Catalog.db`.

## Requirements

### Requirement: The push is the sole emitter

TSM SHALL emit inbox records at exactly one point: after a successful push-as-replay commit — the
single funnel where TS, the system of record, actually changes. Each replayed intent change SHALL
map to its full-value upsert op(s) as defined by the catalog inbox contract v1
(`..\IntervalSchedulerManager\docs\design\catalog-inbox-contract.md`): desired-count and
exposure-plan edits to `exposure-plan-upsert`, target-level intent (enable state, coordinates,
name) to `target-upsert`, project-settings edits to `project-upsert`, adoption inserts to the
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

### Requirement: Every exposure-plan upsert carries the template mirror

Whenever TSM emits an `exposure-plan-upsert` — for a replayed adoption or a replayed plan edit —
it SHALL also emit an `exposure-template-upsert` mirroring the referenced template's TS-authored
values, unconditionally (no have-I-sent-this tracking), so a template authored in TS after ISM's
one-time import resolves whenever a plan first references it through the inbox. A replayed
adoption emits `target-upsert`, `exposure-plan-upsert`, and `exposure-template-upsert`, plus a
`project-upsert` when the adoption also touched project intent. A pushed exposure-template edit
(TSM's template manager replaying to TS) likewise refreshes the mirror — one
`exposure-template-upsert` with the template's full committed values, so ISM's copy does not go
stale between plan references. TSM SHALL NOT create or edit exposure templates through this
channel — it mirrors only; every mirrored value was committed to TS first.

#### Scenario: Replayed adoption emits three records

- **WHEN** a push replays a journaled adoption insert and commits
- **THEN** TSM appends `target-upsert`, `exposure-plan-upsert`, and `exposure-template-upsert`
  records for the created target, its plan, and the assigned template

#### Scenario: Plan edit re-emits the referenced template's mirror

- **WHEN** a push replays a desired-count edit on a plan whose template was authored in TS after
  ISM's one-time import
- **THEN** the `exposure-plan-upsert` is accompanied by that template's `exposure-template-upsert`,
  so the plan's template reference resolves at ingest

#### Scenario: Repeat emission of a mirror is unconditional

- **WHEN** a later push emits an `exposure-plan-upsert` referencing a template whose mirror was
  already emitted
- **THEN** TSM emits the `exposure-template-upsert` again unconditionally

#### Scenario: Pushed template edit refreshes the mirror

- **WHEN** a push replays a template-manager edit of an exposure template
- **THEN** TSM emits one `exposure-template-upsert` carrying the template's full committed values
  — a mirror refresh, not authoring through the inbox

### Requirement: Actuals never emit

Changes of automatic write-back origin SHALL NOT emit inbox records — the `acquired`/`accepted`
count stamps **and the desired ratchet** (the write-back's derived raise of `desired` to ≥ the
kept count: actuals-derived bookkeeping, not authored intent). These changes replay inside the
push, so emission SHALL distinguish user-authored changes from write-back changes at the emission
point and exclude the latter; `desired_count` sent to the intent store remains the last
user-authored value, never a ratchet (the inbox contract pins this field as authored-value,
ratchets excluded).

#### Scenario: Per-load count write-back is silent

- **WHEN** a load runs the automatic acquired-count write-back and stamps the local copy
- **THEN** no inbox record is appended

#### Scenario: A pushed desired ratchet emits nothing

- **WHEN** disk count exceeds the authored desired so write-back ratchets `desired` up, and a push
  replays that write-back change with no user-authored edit on the same plan
- **THEN** no `exposure-plan-upsert` is emitted for that plan; the intent store's `desired_count`
  stays the authored value

#### Scenario: Co-edited row emits the authored desired, not the ratchet

- **WHEN** one push carries both a write-back desired ratchet and a user-authored edit of another
  field on the same plan
- **THEN** the emitted `exposure-plan-upsert` carries the authored desired — the explicit desired
  edit's value when the push also carried one, else the pre-push value — never the ratcheted value

### Requirement: Inbox transport per contract v1

TSM SHALL write inbox files exactly as the contract's transport section specifies: files named
`tsm-<yyyyMMdd-HHmmss>.jsonl` in the contract-named inbox directory (a new file per TSM session is
acceptable), UTF-8 without BOM, one JSON object per line with `\n` endings, whole lines flushed
atomically (a line is either complete on disk or absent), append-only within a file. Every record
carries the v1 envelope: `v: 1`, `at` (UNIX seconds UTC of the push commit that carried the
change), `source: "TSM"`. TSM SHALL create the inbox directory if it does not exist, and SHALL NOT
touch files the consumer has renamed to `*.processing`.

#### Scenario: First emission creates the inbox directory

- **WHEN** a push emits and the inbox directory does not exist
- **THEN** TSM creates the directory and writes the records; the emission succeeds

#### Scenario: Records carry the v1 envelope

- **WHEN** any record is emitted
- **THEN** the line parses as a single JSON object with `v` = 1, `source` = "TSM", `at` set to the
  push commit time, and an `op` from the v1 op set

#### Scenario: Consumer-claimed files are left alone

- **WHEN** the inbox contains `*.processing` files at emission time
- **THEN** TSM ignores them and appends only to its own `*.jsonl` session file

### Requirement: Inbox append failure aborts loudly after the committed push

The export duty fires after the push has committed to TS; a failed inbox append SHALL NOT roll
back or prevent the push. On append failure (missing path that cannot be created, disk error,
write fault), TSM SHALL abort the remaining export work and surface the failure — console/log
entry naming the inbox path and the failed operation, plus a user-visible error — and SHALL NOT
silently continue or degrade to skip-the-record. Because ops are idempotent full-value upserts,
the user re-doing the affected edits and pushing again after fixing the fault re-emits the same
intent harmlessly.

#### Scenario: Append fails after a committed push

- **WHEN** a push commits to TS but the inbox append fails
- **THEN** the push remains committed, TSM logs the failure naming the inbox path and operation,
  and the user sees an error — not a silent success

### Requirement: TSM never opens Catalog.db

The export duty SHALL interact with ISM's store exclusively through the inbox contract. TSM SHALL
NOT open, read, or write `Catalog.db` — in product code or in tests. Writer-side verification is
file-level: emission shape, envelope, whole-line flushes, and naming are asserted against inbox
fixture files, not against the store.

#### Scenario: No store dependency

- **WHEN** the export duty runs end to end
- **THEN** no code path opens `Catalog.db`; the only artifact produced is inbox `*.jsonl` content
