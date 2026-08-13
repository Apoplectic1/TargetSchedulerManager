# Design notes — add-inbox-v2-emission

## Authored-by-construction verification for project columns (task 2.5)

The observed-emission path skips origin bookkeeping for project rows on the same argument that
let target rows skip it — every project-table write is user-authored. Verified 2026-08-13
against both writers:

- **TS (reference clone, `release/nightly-3.3`)**: `SaveProject` /
  project-entity updates are called only from `Controls\DatabaseManager\DatabaseManagerVM.cs`
  and `ProjectViewVM.cs` — the Database Manager editing UI. No scheduler-runtime path
  (planner, image-save pipeline, symbol handlers) writes the `project` table; runtime writes
  land in `acquiredimage`/`imagedata`, `exposureplan` (acquired/accepted), `filtercadenceitem`,
  and `overrideexposureorderitem`.
- **TSM**: the write-back (automatic origin) updates `exposureplan` only (desired ratchet +
  count stamps); no `UPDATE project` exists outside test fixtures.

Consequence: any project-row field change observed arriving in a pull is TS-committed
user-authored intent, safe to mirror without origin filtering — same posture as target rows.

## Sentinel translation source

`ZeroUnsentinel` (altitude bounds `0.0 → null`) mirrors AL `TsIntentImporter`'s project
mapping (`$minalt`/`$maxalt`), per the contract v2 sentinel table — the importer is the single
source. All other new fields pass verbatim-or-null; `minimumtime`/`horizonoffset` are hard-cast
required (the importer aborts on their absence; a null here is the same contract violation
surfacing as a cast failure at read).
