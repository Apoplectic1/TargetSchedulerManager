# SUBSYSTEMS.md — TargetSchedulerManager

**Charter:** how each long-running subsystem works, in detail — the four machines that move data between the
disk library, the local TS working copy, and BIRDWATCHER. Read it for *how this particular machine behaves*;
**grep by subsystem**. The system-level design they hang off — the source-of-truth model, the component map,
and the load-bearing invariants — stays in `ARCHITECTURE.md`, which is the doc to read first.

| Subsystem | What it does | Formal contract |
|---|---|---|
| **TS sync model** | pull → edit local → push-as-replay | `openspec/specs/ts-sync-model/` |
| **Sync-direction marks** | `←` / `→` / `⇄` per row and per field | `openspec/specs/edit-direction-marks/` |
| **TS write-back** | disk-derived counts → TS's cached columns | `openspec/specs/write-back/` |
| **Visible-tonight pass** | reconcile TS enable state with tonight's sky | `openspec/specs/visible-tonight-toggle/` |

> **Carved out of `ARCHITECTURE.md` 2026-07-26** (sections moved verbatim, no content change). The four had
> grown to 61 % of that file while answering a different question than its charter asks — "how does this
> subsystem behave" rather than "how is the system designed and what must stay true". Each already had the
> parallel formal contract listed above; this file is the prose beside those specs. `ARCHITECTURE.md` → *Key
> facts* still carries the one-line invariant mirrors, so a reader who starts there is pointed here.

## TS sync model (pull → edit local → push-as-replay; shipped 2026-07-06)

TSM's one editing world. Design principle throughout: **buttons carry decisions, guards carry facts** —
correctness never depends on the user remembering cross-session state. All state lives in `Shared/TsSync` +
two sidecars beside the local db (`*.tsm-sync.json` baseline, `*.tsm-edits.jsonl` journal).

- **Pull on open, baseline-skipped.** When BIRDWATCHER answers the ~1.5 s stat probe, the open refreshes the
  local copy via the SQLite **online backup API** (torn-copy-safe while NINA holds the file; never `File.Copy`)
  — EXCEPT when the persisted baseline (remote size + mtime at last pull) matches **and** no remote
  `-wal`/`-shm`/`-journal` exists (WAL hides content changes from the main file's mtime). Unbaselined always
  pulls; the baseline is recorded from the *pre*-pull stat, so a mid-copy write can only cost an extra pull,
  never a false skip. Rapid test relaunches therefore skip the copy. Offline opens proceed on the local copy.
  One residual remains: a same-second, same-size, non-WAL remote write *can* still false-skip — **Pull now**
  is the override, and SQLite's file-change counter (4 bytes at offset 24) is the pre-chosen upgrade path if
  SMB mtime ever proves unreliable (`openspec/changes/archive/2026-07-06-sync-model/design.md` D1).
- **Pull is atomic, observable, cancellable (hardened 2026-07-23).** The backup lands in `<local>.pull-tmp`
  and is swapped over the local db only on completion (`ClearAllPools` first — a pooled reader handle would
  fail the swap), so a process death at *any* moment leaves the previous copy intact; a dead pull's tmp is
  swept by the next pull. The copy is chunked `sqlite3_backup` steps (~2 MB): the status line shows a **text
  percentage** (deliberately no progress-bar element) and **Cancel** stops between chunks — tmp
  discarded, no baseline recorded, previous copy untouched (during a push only the closing pull cancels;
  replay writes never do). The log carries `PULL starting` + completion duration — an interrupted pull used
  to be invisible, which is why the incident (app killed at ~87% of a latency-degraded ~40 s pull, leaving a
  hot journal the read-only reader could never recover and the baseline skip faithfully preserved) was
  undiagnosable live. The **torn-local gate** closes that skip-rule blind spot: a `-journal`/`-wal` beside
  the local db at open is healed loudly (`LOCAL TORN` log line; discard local + sidecars + baseline; pull
  fresh; torn + offline fails the load loudly instead) — the edit journal is untouched, so unpushed edits
  survive and replay at push. It **heals rather than aborts** because the local copy is disposable derived
  state over intact upstream truth; the house fail-fast doctrine governs input-*contract* violations, where
  continuing would mask an upstream bug (`openspec/changes/archive/2026-07-23-harden-ts-pull/design.md` D2).
  The loud log line is what preserves the forensic trail either way.
- **Every edit writes locally and journals.** The gate targets the local path only; each verified write appends
  `(seq, kind, table, key, column, absolute value, old, label, at)` to the journal. **Dirty ≡ journal
  non-empty** — derived from the persisted file, never a stored flag, so it is crash-safe by construction. The
  toolbar badge ("synced HH:mm · N unpushed") displays the facts; Push is enabled exactly when dirty.
  *Durability boundary (2026-07-24):* appends are flushed to the OS before becoming visible — they survive
  a **process** crash; an OS/power failure can lose the final line, and nothing here can close that (the
  SQLite commit and the journal append are two separate durability events, never atomic). The loss mode is
  bounded: the local db still holds the write, only its replay at push is lost.
  *Net-no-op pruning (2026-07-26):* the journal never retains a field whose value returned to its
  **baseline** (the first journaled old since the last push, remembered per field) — such a write prunes
  the field's entries instead of appending, and a first-touch same-value commit journals nothing (the
  editor verifies those without writing). One producer-side rule, so every consumer heals at once: marks
  on all surfaces, the unpushed count, the push review/replay (no no-op writes — which for cadence-clearing
  fields would clear remote cadence for nothing), and the dirty-open prompt. Inbound facts are a separate
  store, so a field's pre-edit `←` survives the round-trip.
- **Push = journal replay, never a file copy.** A file push is a time machine — it would revert everything
  BIRDWATCHER accrued since the pull (NINA's nightly counts, `acquiredimage` history, XFM's grades). Instead
  the collapsed journal (last write per field, first write's old for review) replays: **write-back entries**
  re-execute the write-back contract per plan via `TargetSchedulerWriter` (desired ratchets against the
  *remote* desired); **manual entries** replay per-field via the guarded, read-back-verified
  `TargetSchedulerEditor.TrySetField` — writer leg first, so an explicit desired edit outranks the stamp.
  Only journaled fields are touched. A remote open sidecar refuses the whole push; per-entry failures (row
  gone, verify mismatch) are reported loudly and retained in the journal — and a whole-db refusal met
  mid-leg **aborts the remaining field entries** (retained as not-attempted), so one dead remote costs one
  write attempt, not N (`openspec/changes/archive/2026-07-24-push-decomposition/design.md` D3). The two legs
  are **structural, not stylistic**: `TsEditableSchema` deliberately omits `acquired`/`accepted` (stat columns
  must never reach a generated edit UI), so `TrySetField`'s whitelist refuses them and write-back entries can
  *only* replay through `TargetSchedulerWriter` — merging the legs would silently drop write-back replay.
  A fully-applied push ends in an
  immediate pull — the invariant everything hangs on: **a baseline is recorded exactly when the local copy
  mirrors the remote**. *Consumer caveat:* a pushed field lands in BIRDWATCHER's db immediately, but NINA's TS
  plugin can ignore external db writes **mid-session until NINA restarts** (user-reported, unfixed upstream,
  2026-06-20) — so "verified on BIRDWATCHER" is not "TS will act on it tonight", and a push is best treated as
  a between-sessions act.
- **Push outcomes are truthful (2026-07-24, openspec `truthful-outcome`).** The journal clears exactly when
  the writes applied and verified, and the report never contradicts it: a **closing-pull fault** (network
  drop mid-backup, swap failure) is contained inside `Push` and reported as a successful push whose pull
  didn't land (`ClosingPullFailed` on the result → "closing pull failed — next open pulls fresh"), never as
  "PUSH FAILED / edits stay journaled". The convergence gap heals itself: the push changed the remote mtime,
  so the baseline rule pulls at the next open. Corollary the VM's catch relies on: any throw that *escapes*
  `Push` precedes the journal rewrite — "journal intact, re-push recovers" is guaranteed accurate. Deferring
  the rewrite until *after* the closing pull lands reads like the safer ordering and was rejected: it couples
  journal truth to an unrelated network op and reopens the mirror-image lie (writes applied, journal still
  claims dirty, so a re-push replays onto a db that already holds the values)
  (`openspec/changes/archive/2026-07-24-truthful-outcome/design.md`).
- **Open-with-dirty prompts before any pull** (reachable + dirty): push (recommended — replay makes offline
  edits pushable at reconnect) / discard-and-pull (the deliberate debug-session path) / continue local. The
  push review dialog shows manual edits + the write-back summary with **decreases first**, and warns (not
  blocks) when the remote changed since the baseline — silently so when there is no baseline yet (nothing can
  have changed *since* nothing), the opposite null posture from the pull-skip rule, which counts a missing
  baseline as an unconditional mismatch; one comparison, two consumers, each keeping its own guard
  (`openspec/changes/archive/2026-07-24-push-rule-dedup/design.md` D2, `CONVENTIONS.md` → *One helper, two
  null postures*). Field replay makes cross-field interleaving safe;
  same-field collisions stay covered by the edits-by-day discipline. **Discard is pull-first (2026-07-24):**
  the discarding pull runs before anything clears; `Discard` is bookkeeping (journal only — the baseline
  stays, that pull just recorded it) invoked exactly when the pull lands and the swap has physically
  replaced the discarded values. A cancelled discard-pull changes *nothing* — journal, baseline, badge, and
  marks stay intact ("discard not completed — unpushed edits kept"), so the grid can never show discarded
  values as clean, journal-less truth; a crash between pull and clear just re-prompts at the next open.
- **Retired (2026-07-06):** the LIVE/LOCAL radios, direct SMB writes, `TsSource`, `EditOutcome.LiveDropped` +
  sticky-fall, and the post-write `ClearAllPools` SMB workaround. Edits can no longer fail from BIRDWATCHER
  dropping because they never travel over SMB.

## Sync-direction marks (grid column 0; shipped 2026-07-08)

Every row level (target header / mosaic panel / filter row / rollup detail line) carries one mark in the new
leftmost 24 px column: **`←`** = inbound (BIRDWATCHER arrived different at a pull), **`→`** = outbound
(unpushed journal writes — manual edits *and* write-back stamps), **`⇄`** = both, blank = clean. Tooltips:
per-field `old → new` lines on leaves (template-inherited lines attributed "— template '<name>'"); on
headers, attributed lines for own-scope target/project fields + direction counts for rolled-up plan/template
fields. Spec: `openspec/specs/edit-direction-marks/`.

- **Outbound is the journal, re-read.** No new state: a row marks `→` iff a journal entry's (table, key)
  matches its `PlanTsKey` / `TsTargetKey` / `ProjectTsKey` — so marks survive restarts (the journal sidecar
  persists) and a partial push's retained failures keep exactly their rows marked.
- **Inbound is a pull-time field diff** (`Shared/TsInboundDiff`): `TsSync.Pull` — the single choke point all
  four pull paths share (open / Pull-now / discard-and-pull / the closing pull after push) — snapshots the
  local db's diffable fields before the backup overwrites it, diffs against the fresh copy, and unions into a
  **session-sticky in-memory store** (`TsSync.Inbound`). The diffed set is authored (the columns TSM displays
  or edits — the `TsEditableSchema` convention), never `PRAGMA`-discovered, so TS-internal bookkeeping can't
  produce noise; the `exposuretemplate` columns are *derived* from `TsEditableSchema` (2026-07-26) so ←
  coverage can't drift from what the flyout edits. First-ever pull (no local file) diffs nothing; no-pull sessions (offline / Continue-local)
  have no `←`; a remotely-added row reports one "new row" entry; deletions report nothing. A diffable column
  **absent from either snapshot is silently skipped** — a deliberate carve-out from the house fail-fast posture,
  because the differ is *observation, not contract*: a TS-side rename simply stops diffing that column instead
  of aborting the pull (`openspec/changes/archive/2026-07-08-edit-direction-marks/design.md` D2).
- **The actuals mask:** when write-back stamps a plan's `acquired`/`accepted`, `RecordWriteBack` drops those
  columns from the plan's inbound entries — disk supersedes the rig's totals, so the row reads `→` (never
  `⇄`) and goes clean after push, not stale-`←`. `desired` is deliberately not masked: a rig-side goal change
  coexisting with a ratchet raise is a genuine `⇄`.
- **Template changes mark every plan using them** (2026-07-26, reversing the earlier no-row carve-out):
  `SyncMarks.Build` takes the retained graph and derives plan→template-key / target→template-keys maps
  (template key space = integer `Id` string, matching the journal and inbound diff), so `ForPlan` unions a
  plan's own entries with its template's — tooltip lines attributed so an inherited change is never mistaken
  for a row edit. A header counts a pending (template, field) once regardless of how many of its plans share
  the template; a zero-use template marks no grid row but shows its mark in the Templates… picker
  (`ForTemplate`, resolved fresh at picker open via `MainViewModel.BuildMarks`).
- **Headers roll up the union of their subtree's directions** (`Services/SyncMarks`): own target key +
  project key (group header / mosaic parent only — a project edit never lights panels) + every plan and
  template key of their target ids from the retained graph — the graph map matters because a plan
  folded into a multi-plan rollup row carries no row-level key — plus the plan keys visible child rows do
  carry. Own-scope target/project fields render as attributed old→new lines (the header is their only home);
  rolled-up fields stay counts. Sticky inbound means a push collapses `⇄` to `←` (the rig's change stays
  visible) rather than wiping the overnight info.
- **One in-place sweep** (`MainViewModel.RefreshAllMarks`): rebuilds the resolver from journal + inbound +
  graph and re-applies every mark via PropertyChanged (raise-on-change only, never a collection rebuild — the
  scroll-preserving in-place rule). Called from `ApplyFilters`, every applied edit, and a push without a reload
  (Discard refreshes marks via its own full reload, not a direct sweep).
- **Per-field marks in the flyouts** (2026-07-26): `SyncMarks.ForField(table, key, column)` resolves one
  field's own directions (unattributed — the flyout names the entity; the row-scoped new-row fact never
  surfaces per field), and `TsFieldsEditor` renders a leading fixed-width mark column through an optional
  batched `MarkResolver` delegate (one `BuildMarks()` per refresh pass, re-run after every commit — a
  just-committed field flips `→` live). A per-field `⇄` is the exact-field collision signal: an unpushed
  local write and a rig-side change on that one field. The hand-built mosaic flyout wires the same marks
  (master enable = union over the panels' `target.active`, per-panel tooltip lines).

## TS write-back (engine built 2026-06-08; app action shipped 2026-07-06)

`TargetSchedulerWriter` pushes disk-derived counts back into TS so its planner reflects ACTUAL. The engine
(`WriteBackPlanner` / `SingleTargetPlanner` / `TargetSchedulerWriter`) lives in AL and is fully tested; in the
app it runs **automatically after every load** (`Services/WriteBackStep`): plan from the fresh scan + local TS
read, stamp every non-no-op change into the **local** db, journal each changed column with the write-back kind
— so an unchanged system journals nothing and the session stays clean. BIRDWATCHER sees write-back only through
the reviewed push (decreases first). It is a **stop-gap** until IS consumes `Catalog.db` directly, so it
stays minimal and cleanly deletable. Load-bearing invariants (the formal contract is
`openspec/specs/write-back/`; `ROADMAP.md` Phase 4 is the *plan* entry and points here, so don't re-document
the mechanism there):

- **Disk is master, one-way.** Write-back only ever flows ACTUAL → TS, never the reverse; conflicts overwrite the
  TS value up or down.
- **Counts only, cached columns only.** Sets `exposureplan.acquired` *and* `.accepted` = disk count and ratchets
  `desired` **up** to ≥ that (never lowered — a goal can't be below what was kept); touches no `acquiredimage` rows.
  TS never recomputes counts from images, so the cached columns are authoritative (its own Database-Manager UI
  hand-edits them) — that is why a column write suffices and survives.
- **`(target, filter, purpose, seconds)` is the join.** Purpose (Light vs Stars) is the `"Stars "` naming
  convention, symmetric across disk directories and TS templates. **The plan's whole-second exposure is its
  spec**: effective seconds = round(plan exposure ?? template default); each plan receives the disk count at
  exactly that bucket — **0 when none match** (a flagged decrease; 600 s frames never satisfy a 900 s plan).
  Same-purpose plans at *different* durations are different cells and auto-resolve; disk buckets no plan
  targets are surfaced as `UnplannedFrames` notes, never written and never manual — **write-back updates
  existing plan rows only** (plan creation/deletion is an M2 concern).
- **Only serving framings credit** (2026-07-29, openspec `rotation-framing-key`). Within the join's bucket,
  a frame counts toward `acquired` only when its framing serves the target's rotation
  (`FramingCluster.ServesPlanRotation` — the same rule the grid pairs and badges by): sky framing must agree
  fold-180 within tolerance; mechanical/unknown framing and rotation-less targets always credit. A re-framed
  target therefore stamps its true progress (possibly 0) and TS schedules the full re-shoot. The surgical
  path surfaces a withheld cell as a `FramingMismatch` note rather than skipping it silently.
- **Uncertain identity → manual.** ≥2 plans collapsing onto one `(target,filter,purpose,seconds)` (a same-key
  multi-plan or a dup-fold target), **and** any target whose match is flagged (name-mismatch / ambiguous coord),
  are held for manual resolution with full info, never auto-written — a false-positive coordinate match must not
  overwrite a real TS target.
- **Guarded, and copy-isolated by the sync model.** The automatic pass stamps the **local** copy; BIRDWATCHER
  is written only inside the reviewed push replay. The hard guards apply at both dbs — refuse an open
  `-wal`/`-shm`/`-journal` sidecar (TS mid-transaction), a read-only file, or missing `exposureplan` columns
  (*not* an exact schema version, which the NINA-nightly bumps) — plus diff-first (no-ops produce no write and
  no journal entry), one transaction, and read-back verify. No app-side backups — the daily Macrium image is
  the recovery path and both DBs are recreatable. The writer uses a private SQLite cache (so it doesn't inherit
  the build-reader's read-only shared cache); a fresh re-scan each run can't push stale numbers.
- **Surgical single-target.** The single-target path (was `tsm writeback --target "<dir>"`) scans one directory
  only (no catalog rebuild) and writes just its cells; a **mosaic writes per panel** — each panel dir
  coordinate-anchors to its TS panel *within the same-named isMosaic project*, and each `(filter, purpose,
  binning, seconds)` cell lands on that panel's matching plan (binning guards a 2×2 cell off a 1×1 plan; seconds
  guard 600 s frames off a 900 s plan — a same-seconds plan at another binning is a `NoMatchingPlan` manual with
  context, a pure duration mismatch is an `UnplannedFrames` note). The unit is a filter-cell, so a normal target
  is one unit and a mosaic is N panel units. Unmatched units (beyond tolerance / ambiguous) are **reported,
  never forced**; reuses the same writer (acq/acc + `desired` ratchet + verify) and guards. **Deliberate
  asymmetry:** the surgical path never zeroes plans with no matching cell (a per-cell push tool must not let a
  partial scan silently zero the target's other plans); the bulk path does. Surface: `SingleTargetPlanner`
  (pure) + `ImageLibraryScanner.ScanUnitsAsync` (per-panel scan). **Not app-wired yet** — the surgical path is a
  tested library capability (it backed the retired `tsm writeback --target`), but no TSM UI invokes it today; the
  app runs only the bulk automatic pass.
- **Audited.** The automatic write-back pass logs its outcome to the diagnostics log (`tsm.log` under
  `%APPDATA%\TargetSchedulerManager\Logs\`): one summary line (plans stamped, fields journaled, manual, ignored),
  a warning when cells need manual reconciliation, and a line per verify failure — no per-write `old→new` trail
  and no dry-run mode (both went with the retired CLI's `WriteBackAuditLog`). The reviewed **Push** dialog is
  where every stamp is shown `old→new` (decreases first) before it reaches BIRDWATCHER.
- **Held decisions surface as the ambiguity report** (`Services/AmbiguityReport`, 2026-07-08): a pure builder
  over the retained graph/report + a fresh in-memory `WriteBackPlanner.Plan` rolls every held cell, identity
  flag, and TS-internal check (same-key plans across all TS-sourced targets, planned-only twins, duplicate
  template names) into one printable Markdown file with hand-fix instructions — the tripwire's detail. TSM
  never resolves these itself (resolver rejected 2026-07-08; fixes are hand-edits in NINA's TS UI).

## Visible-tonight pass (toolbar group; shipped 2026-07-23)

A "Visible Tonight:" toolbar group — **Duration** (whole minutes, 15–480, default 30) and **Floor**
(whole degrees, 0–89, default 30) numeric up-downs + a **Tonight** button (it replaced the toolbar's old
load-summary text, removed same day). One press reconciles the enable state with tonight's sky — no
confirm dialog (user decision: "this is why it's a button"), push stays optional. The up-downs are
`Controls/UpDownBox` — an app-local WinForms-style NumericUpDown (a narrow inline `NumberBox` is
unreachable; `DOMAIN.md` → *WinUI gotchas*) — sized to their digit budgets (Duration 3, Floor 2). The knob was called **Horizon** until 2026-07-26; renamed **Floor** because the word
collided with two unrelated "horizon"s in the same files (TS's `usecustomhorizon`/`horizonoffset` columns
and `Astronomy.Core.Horizons`), and because floor is what the code always called it internally.

- **Predicate (deliberately TS-independent):** a target is *visible tonight* iff it has a **single
  contiguous window ≥ Duration** above the **Floor altitude** between tonight's astronomical
  dusk and dawn — one library call,
  `CoarseVisibility.IsAboveHorizonForAtLeast(target, site, night, ScalarHorizonProfile(floorDeg), minDuration)`
  (the library keeps its own horizon-profile vocabulary — only the TSM-side knob is named Floor).
  Meridian / pier-flip downtime is a deliberate **non-goal** — it is TS/NINA's runtime concern and is never
  modeled here (asked and settled 2026-07-24).
  TS's own gates (`minimumaltitude`, custom horizon/offset, `minimumtime`, twilight levels) are **not**
  consulted — TS re-applies them itself at plan time; a rejected earlier draft that mirrored the TS gate
  (and promoted TP's `.hrz` parser into the library) was reverted 2026-07-23. "Tonight" is
  `NightCalculator.ComputeNight`'s bracket: the window whose dawn is the next dawn at-or-after now (the
  current night mid-night, the upcoming night in daylight).
- **Flip rules:** `target.active ← verdict` for every target of an `Active`/`Inactive` project; then
  `project.state ← any-enabled-child ? Active : Inactive` over the **applied** values — recomputed after the
  target batch lands, so a refused/failed target flip contributes the target's *old* value and can never
  orphan a project flip whose premise didn't land (2026-07-24, `visible-tonight-applied-states`; a project
  with no effectively enabled targets — including one with no targets — goes Inactive). `Draft`/`Closed`
  projects and their targets are never read or written. Panels are ordinary target rows. No-op values
  journal nothing.
- **Data + writes:** consumes the load's retained `TsPlanData` snapshot (`LoadResult.Ts` — the single TS
  read; no re-open), plans as pure records (`Services/VisibleTonightPass` — `PlanTargets` then
  `PlanProjects` over the landed target edits, unit-tested without SQLite), applies through
  `TsEditGate.ApplyManyAsync` in two sequenced batches — so every flip journals, marks, badges, and replays
  at Push exactly like a hand edit — then reloads (no pull) **only when a flip actually landed** and reports
  counts on the status line (project counts are actual, post-apply). Fail-fast: a processed TS target
  without RA/Dec aborts the whole pass **before any edit**.
- **Site input:** `DevDefaults` constants (Penns Park lat/long/TZ/elevation, mirroring TP's preset)
  materialized by `DevDefaults.Site()` — the app's first `Astronomy.Core` dependency (pure-managed; build
  model unchanged). The Duration/Floor knobs live only on the toolbar — no `DevDefaults` constants.
