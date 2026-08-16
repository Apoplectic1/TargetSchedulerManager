# catalog-export Specification

## Purpose

Feeds intent authored through TSM's TS-editing surface into ISM's authored intent store during the
TS→IS coexistence window, by appending records to the catalog inbox (per the ISM-owned inbox
contract v1) when a push-as-replay commits intent to TS — plus a narrow observed-emission mirror:
target changes TS's side committed, projected at pull. A record means "intent as committed to TS."
One direction, contract-only: TSM never opens `Catalog.db`.
## Requirements
### Requirement: The push emits TSM-authored changes

TSM SHALL emit inbox records for its own authored changes after **every** successful push-as-replay
commit, whichever surface triggered it — the Push button and the open-with-dirty prompt's push
alike. The push-as-replay commit is the single funnel where TS, the system of record, actually
changes by TSM's hand, and the emission belongs to that commit rather than to the surface that asked
for it: the commit consumes the journal, so a commit that does not emit leaves authored intent with
no later push able to carry it. (Target changes TSM merely observes arriving from TS's side emit
through the observed-emission path — see *Observed inbound target changes emit at pull*; no other
emission point exists.) Each
replayed intent change SHALL map to its full-value upsert op(s) as defined by the catalog inbox
contract v2 (`..\IntervalSchedulerManager\docs\design\catalog-inbox-contract.md`): desired-count and
exposure-plan edits to `exposure-plan-upsert`, target-level intent (enable state, coordinates,
name — including a rename committed through TSM's editing surface) to `target-upsert`,
project-settings edits to `project-upsert` — whose v2 field set carries the full settings block
(`minimum_time_minutes`, `minimum_altitude_deg`, `maximum_altitude_deg`, `use_custom_horizon`,
`horizon_offset_deg`, `meridian_window_minutes`, `filter_switch_frequency`, `dither_every`,
`smart_exposure_order`) and `is_mosaic` — and adoption inserts to the adoption record set. TS
sentinel values in the new fields SHALL translate to JSON null exactly as the one-time import
translated them (the contract's v2 table, transcribed from AL's importer). Local edits, adoption
inserts, and project-settings edits write the local working copy and journal only and SHALL NOT
emit at edit time — a Catalog.db row means "authored intent as committed to TS," so intent the user
abandons or trims at push review is never emitted. Emission SHALL cover only changes of
user-authored origin: the push distinguishes user-authored changes from automatic write-back
changes, and only the former emit (see *Actuals never emit*). Every record is a full-value upsert
carrying the complete row values as committed to TS — even when the triggering edit changed a
single field — keyed by TS guid identity, with one deliberate exception: `desired_count` carries
the authored value even when a same-push write-back ratchet moved the committed value (see *Actuals
never emit*). Repeated emission of the same values is permitted (idempotent upserts); TSM SHALL NOT
keep sent-tracking state.

#### Scenario: Pushed desired-count edit emits a full exposure-plan row

- **WHEN** a push replays a journaled desired-count edit and the push commits
- **THEN** TSM appends one `exposure-plan-upsert` record carrying the plan's full committed values
  (template reference, exposure seconds, desired count, enabled), not just the changed count

#### Scenario: Pushed rename emits a full target row

- **WHEN** a push replays a journaled target rename and commits
- **THEN** TSM appends one `target-upsert` record carrying the target's full committed values, the
  new name among them

#### Scenario: The open-with-dirty prompt's push emits like any other

- **WHEN** the user reopens TSM with unpushed edits journaled and chooses **Push** at the
  open-with-dirty prompt, and that push commits
- **THEN** TSM appends the same records the Push button would have — the commit carries the
  emission, not the surface

#### Scenario: Local edits alone emit nothing

- **WHEN** the user edits desired counts, toggles enables, adopts disk rows, or edits project
  settings — journaling to the local working copy — and does not push
- **THEN** no inbox record is appended

#### Scenario: Entries trimmed at push review are never emitted

- **WHEN** the user removes a journaled change during push review and then pushes the rest
- **THEN** the trimmed change produces no inbox record; only the replayed changes emit

#### Scenario: Pushed project-settings edit emits a project upsert

- **WHEN** a push replays a journaled project-settings edit (e.g. minimum altitude 0 → 30) and
  commits
- **THEN** TSM appends one `project-upsert` record carrying the project's full committed v2
  values — the changed setting among them — with TS sentinels translated to null

### Requirement: Every exposure-plan upsert carries the template mirror

Whenever TSM emits an `exposure-plan-upsert` — for a replayed adoption or a replayed plan edit —
it SHALL also emit an `exposure-template-upsert` mirroring the referenced template's TS-authored
values, unconditionally (no have-I-sent-this tracking), so a template authored in TS after ISM's
one-time import resolves whenever a plan first references it through the inbox. The mirror's v2
field set includes the moon-relax triplet (`moon_relax_scale`, `moon_relax_max_altitude_deg`,
`moon_relax_min_altitude_deg`). A replayed adoption emits `target-upsert`, `exposure-plan-upsert`,
and `exposure-template-upsert`, plus a `project-upsert` when the adoption also touched project
intent. A pushed exposure-template edit (TSM's template manager replaying to TS) likewise
refreshes the mirror — one `exposure-template-upsert` with the template's full committed values,
so ISM's copy does not go stale between plan references. TSM SHALL NOT create or edit exposure
templates through this channel — it mirrors only; every mirrored value was committed to TS first.

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

#### Scenario: Mirror carries the relax triplet

- **WHEN** any `exposure-template-upsert` is emitted for a template with authored relax values
- **THEN** the record carries `moon_relax_scale`, `moon_relax_max_altitude_deg`, and
  `moon_relax_min_altitude_deg` alongside the v1 moon-avoidance fields

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

### Requirement: Observed inbound target and project changes emit at pull

At every pull that overwrites the local working copy (the open pull, a manual Pull-now, a push's
closing pull), TSM SHALL project the pull's inbound diff into the inbox: each field change
observed on an **existing target-table or project-table row** (correlated by TS guid across the
pull) SHALL emit one full-value v2 upsert (`target-upsert` / `project-upsert`) whose values are
read from the fresh local copy after the pull — mirroring TS-committed intent whichever surface
authored it, per the same posture as the template mirror. Target and project columns are
user-authored intent by construction, so no origin bookkeeping applies on this path. The scope
stays deliberately narrow: remotely-**added** rows SHALL NOT emit (a target without its plans —
or a project without its targets — is half a family; an accepted residual), and inbound changes
on plan or template rows SHALL NOT emit through this path (plan columns include actuals; the
plan-push mirror keeps templates current). A pull whose inbound diff contains no target- or
project-table field changes SHALL emit nothing — no empty file. Records are ordinary contract-v2
upserts; emission consumes the single pull's diff, not accumulated session state, so one pull
produces at most one observed-emission file.

#### Scenario: A BIRDWATCHER rename flows at the next pull

- **WHEN** a target was renamed in TS's UI on BIRDWATCHER and TSM's next pull observes the name
  arriving changed
- **THEN** TSM appends one `target-upsert` carrying the target's full post-pull values, the new
  name among them

#### Scenario: A BIRDWATCHER settings edit flows at the next pull

- **WHEN** a project's minimum altitude was edited in TS's UI on BIRDWATCHER and TSM's next pull
  observes the field arriving changed
- **THEN** TSM appends one `project-upsert` carrying the project's full post-pull v2 values, the
  changed setting among them

#### Scenario: The closing pull never echoes the push's own changes

- **WHEN** a push commits TSM-authored changes and its closing pull returns them
- **THEN** those fields do not diff (local-first edits are already identical on both sides) and
  no observed-emission records are produced for them

#### Scenario: A remotely-added target stays silent

- **WHEN** a pull observes a target row that exists remotely but not locally
- **THEN** no observed-emission record is produced for it

#### Scenario: A remotely-added project stays silent

- **WHEN** a pull observes a project row that exists remotely but not locally
- **THEN** no observed-emission record is produced for it

#### Scenario: Inbound plan actuals stay silent

- **WHEN** a pull observes changed `acquired`/`accepted` (or any other plan or template field)
  arriving from BIRDWATCHER
- **THEN** the observed-emission path produces nothing for those rows

#### Scenario: Quiet pull, no file

- **WHEN** a pull's inbound diff is empty or touches no target- or project-table fields
- **THEN** no observed-emission file is written

#### Scenario: Observed-emission failure is loud and the pull stands

- **WHEN** the observed emission fails after a completed pull (disk error, uncreatable path)
- **THEN** the pull remains applied, TSM logs the failure naming the inbox path and operation,
  and the user sees an error — not a silent skip

### Requirement: Inbox transport per contract v2

TSM SHALL write inbox files exactly as the contract's transport section specifies: files named
`tsm-<yyyyMMdd-HHmmss>.jsonl` in the contract-named inbox directory (a new file per TSM session is
acceptable), UTF-8 without BOM, one JSON object per line with `\n` endings, whole lines flushed
atomically (a line is either complete on disk or absent), append-only within a file. When the stamped
name is already taken (a push and a pull emitting within the same second), the stamp SHALL advance to
the next free second — never overwrite, never a name outside the contract's pattern. Every record
carries the v2 envelope: `v: 2`, `at` (UNIX seconds UTC — the push commit that carried the change,
or the pull completion time for observed-emission records, which is when TSM observed the TS-committed
value), `source: "TSM"`. TSM SHALL create the inbox directory if it does not exist, and SHALL NOT
touch files the consumer has renamed to `*.processing`.

#### Scenario: First emission creates the inbox directory

- **WHEN** a push emits and the inbox directory does not exist
- **THEN** TSM creates the directory and writes the records; the emission succeeds

#### Scenario: Records carry the v2 envelope

- **WHEN** any record is emitted
- **THEN** the line parses as a single JSON object with `v` = 2, `source` = "TSM", `at` set to the
  push commit time (or the observing pull's completion time), and an `op` from the v2 op set

#### Scenario: Consumer-claimed files are left alone

- **WHEN** the inbox contains `*.processing` files at emission time
- **THEN** TSM ignores them and appends only to its own `*.jsonl` session file

#### Scenario: Same-second emissions get distinct files

- **WHEN** a push commits and its closing pull carries an observed target change within the same
  second
- **THEN** two files publish under distinct stamps, neither overwriting the other

