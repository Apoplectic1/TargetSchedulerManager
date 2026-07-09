# TS-SCHEMA.md — the N.I.N.A. Target Scheduler database (external contract)

**Charter:** the exhaustive, human-readable reference for the TS plugin's SQLite schema — the external contract
TSM reads, edits, and stamps. Two jobs: (1) fix the **vocabulary + hierarchy** so conversations and code agree;
(2) be the **drift check** after a TS/NINA upgrade — regenerate the dump (recipe at bottom), diff against this
file, update in the same commit as any code reaction. Column lists below are **complete** (every table, every
column), from the live working copy.

> **Snapshot:** dumped 2026-07-08 from the local working copy (`schedulerdb.sqlite`), **`user_version` = 28**.
> Row counts are that day's data, illustrative only. Note the reference clones (parent ROADMAP) were pinned when
> the db was `user_version 25` — the rig's TS has moved on; TSM survives this because it validates by **column
> presence** (`IsFieldAvailable`), never by exact version.

## Hierarchy + vocabulary (TS's own terms — use these)

```
NINA profile (equipment lives in NINA; TS references its guid as profileId)
 ├─ profilepreference   1 row per profile: TS's own prefs (grading, sync, API…)
 ├─ exposuretemplate*   per-profile, SHARED by all that profile's projects
 └─ project*            arbitrary list; per-project scheduling policy
     └─ target*         name + RA/Dec (+rotation/roi/priority/active)
         ├─ exposureplan*         ← "the plan list": one row per (template ref)
         │    each row: desired/acquired/accepted counts, enabled,
         │    optional per-plan exposure override (-1 = use template default)
         ├─ filtercadenceitem*    TS's filter-rotation state (regenerable)
         └─ overrideexposureorderitem*   manual exposure-order override
```

- An **Exposure Template** defines *how* to shoot a filter (filter, default exposure, gain, offset, bin, readout
  mode, twilight level, moon avoidance family, humidity cap, dither cadence, minutes offset). Profile-scoped.
- An **Exposure Plan** is a target's *commitment* to a template: **counts live here** (`desired` = intent,
  `acquired`/`accepted` = TS's tallies — the columns write-back stamps), plus `enabled` and the exposure override.
- **Two-name identity system:** integer `Id` (autoincrement, **per-copy** — the FK glue; diverges between local
  copy and BIRDWATCHER for inserted rows) vs `guid` (minted at row creation, travels with the row — the
  **cross-copy stable name**; what TSM keys targets by and must carry through any insert replay).
- **Units:** `target.ra` is **hours** (0–24); `target.dec` degrees. `exposureplan.exposure` / `defaultexposure`
  seconds; `exposure = -1` is the **use-template-default sentinel** (rendered by TSM's sentinel checkbox).
- No uniqueness constraints beyond PKs: duplicate template names, duplicate targets (any level), and same-key
  plans are all **legal to TS** — see `DOMAIN.md` "TS authoring conventions" for why we forbid them by convention.

## Tables (complete column lists; ✎ = TSM edits via `TsEditableSchema`, ⚙ = write-back stamps)

### project — 10 rows · FK: `target.projectid → project.Id`
`Id` PK · `profileId` · `name` · `description` · `state` · `priority` ✎ · `createdate` · `activedate` ·
`inactivedate` · `minimumtime` · `minimumaltitude` · `usecustomhorizon` · `horizonoffset` · `meridianwindow` ·
`filterswitchfrequency` ✎ · `ditherevery` · `enablegrader` · `isMosaic` · `flatsHandling` · `maximumAltitude` ·
`smartexposureorder` · `guid`
TSM: read for grid grouping + project flyout; `isMosaic` drives the mosaic/panel model; per-project policy
(min/max altitude etc.) is the user's intent — TSM displays, never derives.

### target — 102 rows
`Id` PK · `name` · `active` ✎ · `ra` · `dec` · `epochcode` · `rotation` ✎(guarded) · `roi` · `projectid` ·
`unusedOEO` · `guid` · `priority` ✎
TSM: the coordinate anchor for disk matching (ra hours / dec degrees, 0.5° tolerance); `epochcode` is
harden-rule coerced if unknown; `guid` is the write-back/edit address retained as `imported_from_ts_guid`.

### exposureplan — 658 rows · FK: `targetid → target.Id`, `exposureTemplateId → exposuretemplate.Id`
`Id` PK · `profileId` · `exposure` ✎ (−1 sentinel) · `desired` ✎ · `acquired` ⚙ · `accepted` ⚙ ·
`targetid` · `exposureTemplateId` · `enabled` ✎ · `guid`
TSM: the write-back target — `acquired`/`accepted` ← disk count, `desired` ratchets up to ≥ count (never
lowered); effective seconds = `exposure < 0 ? template.defaultexposure : exposure`, rounded to whole seconds
(the cell-identity bucket).

### exposuretemplate — 20 rows (profile-scoped)
`Id` PK · `profileId` · `name` · `filtername` · `gain` · `offset` · `bin` · `readoutmode` · `twilightlevel` ·
`moonavoidanceenabled` · `moonavoidanceseparation` · `moonavoidancewidth` · `maximumhumidity` ·
`defaultexposure` · `moonrelaxscale` · `moonrelaxmaxaltitude` · `moonrelaxminaltitude` · `moondownenabled` ·
`ditherevery` · `minutesOffset` · `guid`
TSM: template manager edits 11 of these fields (see `TsEditableSchema` — the authoritative editable list);
`"Stars "` name prefix is the Light/Stars purpose convention shared with disk directories.

### filtercadenceitem — 165 rows (references `targetid`, no declared FK)
`Id` PK · `targetid` · `order` · `next` · `action` · `referenceIdx`
TS's filter-rotation state. **Restored verbatim by TS; empty = safe regenerate; stale = silently wrong** — so
any TSM edit that breaks cadence must transactionally clear the target's rows (the cadence-safe pattern).

### overrideexposureorderitem — 22 rows (references `targetid`, no declared FK)
`Id` PK · `targetid` · `order` · `action` · `referenceIdx`
Manual exposure ordering (OEO). TSM refuses per-filter clears where an OEO exists (OEO refusal, Part 4).

### acquiredimage — 2103 rows (references `targetId`/`projectId`/`exposureId`, no declared FKs)
`Id` PK · `projectId` · `targetId` · `acquireddate` · `filtername` · `gradingStatus` · `metadata` ·
`rejectreason` · `profileId` · `exposureId` · `guid`
TS's own per-image capture/grading record. **Noise to this user** (grading happens in PixInsight; disk is the
graded truth; history is hand-purged) — TSM never reads or writes it.

### imagedata — 4255 rows · FK: `acquiredimageid → acquiredimage.Id`
`Id` PK · `tag` · `imagedata` BLOB · `acquiredimageid` · `width` · `height`
Thumbnails for acquiredimage rows. Ignored; it's most of the file size.

### ruleweight — 80 rows · FK: `projectid → project.Id`
`Id` PK · `name` · `weight` · `projectid`
Per-project scoring-rule weights (8 per project here). Ignored by TSM (picker internals).

### flathistory — 0 rows
`Id` PK · `targetId` · `lightSessionDate` · `flatsTakenDate` · `profileId` · `flatsType` · `filterName` ·
`gain` · `offset` · `bin` · `readoutmode` · `rotation` · `roi` · `lightSessionId`
TS flats automation history. Unused here (0 rows); ignored by TSM.

### profilepreference — 2 rows
`Id` PK · `profileId` · `enableGradeRMS` · `enableGradeStars` · `enableGradeHFR` · `maxGradingSampleSize` ·
`rmsPixelThreshold` · `detectedStarsSigmaFactor` · `hfrSigmaFactor` · `acceptimprovement` · `exposurethrottle` ·
`parkonwait` · `enableSmartPlanWindow` · `enableSynchronization` · `syncWaitTimeout` · `syncActionTimeout` ·
`syncSolveRotateTimeout` · `enableMoveRejected` · `enableGradeFWHM` · `enableGradeEccentricity` ·
`fwhmSigmaFactor` · `eccentricitySigmaFactor` · `enableDeleteAcquiredImagesWithTarget` ·
`syncEventContainerTimeout` · `delayGrading` · `autoAcceptLevelHFR` · `autoAcceptLevelFWHM` ·
`autoAcceptLevelEccentricity` · `enableSimulatedRun` · `skipSimulatedWaits` · `skipSimulatedUpdates` ·
`enableSlewCenter` · `logLevel` · `enableStopOnHumidity` · `guid` · `enableProfileTargetCompletionReset` ·
`enableAPI` · `apiPort` · `apiPrettyPrint` · `enableSyncedAutoFocus` · `enablePlannerReports` ·
`enableClientUpdatesExposurePlan` · `autoRejectLevelHFR` · `autoRejectLevelFWHM` · `autoRejectLevelEccentricity`
TS's per-profile switchboard (grading thresholds off for this user, sync, API, simulation). Ignored by TSM.

## Drift-check recipe (run after a TS/NINA upgrade)

```bash
py -c "
import sqlite3
con = sqlite3.connect('schedulerdb.sqlite')   # a COPY, not the live file (WAL/journal)
cur = con.cursor()
print('user_version:', cur.execute('PRAGMA user_version').fetchone()[0])
for (t,) in cur.execute(\"SELECT name FROM sqlite_master WHERE type='table' ORDER BY name\"):
    print('==', t); [print('  ', c[1], c[2]) for c in cur.execute(f'PRAGMA table_info({t})')]"
```

Diff the output against this file. If `exposureplan`'s columns changed, write-back and the editor are at risk
(TSM's guards refuse on missing columns, but the *reaction* is a code change); anything else is usually
absorb-and-annotate. Update this file in the same commit as the reaction.
