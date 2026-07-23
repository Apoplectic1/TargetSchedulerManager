# write-back Specification

## Purpose

Automatic disk→TS count reconciliation inside the sync model: TS `acquired`/`accepted` (and the planner's
desired-raise rule) stamped from disk-ACTUAL against the local db on every load, journaled like field edits,
reviewed at push. The library engine (`WriteBackPlanner` / `TargetSchedulerWriter` contracts) is unchanged.

## Requirements

### Requirement: Write-back runs automatically against the local db on every load
After each load (fresh pull, skipped pull, or offline), TSM SHALL build the write-back plan
(`WriteBackPlanner.Plan`) from the fresh disk scan and the local TS read, and apply every non-no-op change to
the **local** db through the guarded write path, journaling each with a write-back label. No-op changes SHALL
produce no write and no journal entry, so an unchanged system leaves the session clean (nothing to push).

#### Scenario: Counts stamp locally after a load
- **WHEN** a load finds a plan whose disk bucket holds 42 frames but TS records acquired=40
- **THEN** the local db is updated to acquired=accepted=42 and one journaled write-back entry exists

#### Scenario: Unchanged system stays clean
- **WHEN** a load finds every plan's counts already equal to its disk bucket
- **THEN** no writes occur, the journal stays empty, and the dirty flag stays clear

### Requirement: Write-back reaches BIRDWATCHER only through the reviewed push
Write-back changes SHALL ride the same journal and push replay as field edits. The push review SHALL summarize
write-back separately from manual edits, listing **decreases first** (a plan whose disk bucket is empty writes
0 — dangerous when caused by a scan miss rather than reality) with enough identity (target · filter · old →
new) to judge each.

#### Scenario: Decreases are surfaced before any remote write
- **WHEN** the user opens the push review after a write-back that lowered 12 plans
- **THEN** the 12 decreases are listed first with old → new counts, before the confirm

### Requirement: Write-back scope mirrors the library contract
The app SHALL apply write-back to every existing exposure plan (the planner's contract: update existing
rows, never create or delete plans). Disk truth covers absence: a plan on a target with no disk match stamps
to 0 like any other unmet spec, so stray or diverged counters (`accepted ≠ acquired`) on not-yet-shot targets
heal instead of persisting — a clean 0/0 plan diffs to a no-op and journals nothing. Identity-flagged cells
stay manual. Disk-only targets have no plan rows and SHALL be reported as ignored; disk buckets no plan
targets SHALL remain surfaced as notes/badges, not writes.

#### Scenario: TS-only plan with diverged counters heals to zero
- **WHEN** write-back runs over a TS-only target whose plan reads acquired=0, accepted=64
- **THEN** the local db is updated to acquired=accepted=0 and one journaled write-back entry exists

#### Scenario: Clean TS-only plans journal nothing
- **WHEN** write-back runs over a TS-only target whose plans all read acquired=accepted=0
- **THEN** no writes occur and the journal stays empty
