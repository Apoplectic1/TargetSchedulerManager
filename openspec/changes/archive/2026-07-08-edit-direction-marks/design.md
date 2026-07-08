# Design — edit-direction-marks

## Context

The sync model (see `openspec/specs/ts-sync-model/spec.md`) already derives "dirty" from the journal
sidecar and shows it as one badge count. What's missing is *where*: which rows carry unpushed writes, and
which rows arrived changed from BIRDWATCHER. All the outbound facts exist (`TsJournal` entries keyed by
`(table, key, column)`; rows carry `PlanTsKey`/`TsTargetKey`/`ProjectTsKey`). The inbound facts do **not**
exist anywhere: the baseline (`TsSyncState`) is file-level (size + mtime), so "remote changed" is known
only as a boolean, never per row.

Load-bearing current mechanics this design builds on:

- `TsSync.Pull` is the single choke point for every pull — open (`PullIfChanged`), Pull-now (`Force`),
  discard-and-pull, and the closing pull after a fully-applied push all call it.
- Pull copies remote→local via the SQLite online backup API (never a file copy), overwriting the local db.
- `ReconciliationLoader.BuildRows` sets `PlanTsKey` only on 1:1 plan cells; a multi-plan fold carries
  `null`. The retained `CatalogGraph` (in `LoadResult`) knows every plan's TS key and owning target.
- Rows are rebuilt every `ApplyFilters` pass; committed edits mirror in place (`ApplyDesired` etc.) so
  scroll position survives — marks must follow the same in-place discipline.
- Journal key spaces: `ExposurePlan` keys are TS integer Ids as strings (manual `PlanTsKey` and
  write-back `TsExposurePlanId.ToString()` are the **same** space); `Target` keys are TS guids;
  `Project` keys are TS integer Ids as strings. All compares case-insensitive.

## Goals / Non-Goals

**Goals:**

- One mark per row — `←` inbound, `→` outbound, `⇄` both, blank clean — at every hierarchy level
  (target header, mosaic panel header, filter row), with old→new tooltips.
- Inbound detection as a pull-time field diff, session-sticky, zero new persistence.
- Outbound marks derived live from the existing journal — no new state, no stored flags
  (consistent with "dirty is derived, never stored").
- In-place mark updates on edit/push/discard — no grid rebuild, no scroll jump.

**Non-Goals:**

- No marks for exposure-template edits (no grid row maps to a template; badge + push review still cover
  them).
- No inbound persistence across app runs (← is per-invocation information by definition).
- No conflict *resolution* — ⇄ warns; push replay semantics (last-writer-wins per field, write-back
  desired ratchet) are unchanged.
- No change detection for disk-only targets (no TS side to diff; the original request explicitly
  excluded disk rows).
- No library (`Astronomy.Catalog`) changes.

## Decisions

### D1 — Inbound differ: in-memory snapshot around `TsSync.Pull`

`Pull` gains three steps: snapshot the local db's diffable fields (skip when no local file exists — first
run), run the backup exactly as today, snapshot again, diff, and union the result into a session store.

- *Why here:* all four pull paths converge on `Pull`, so open, Pull-now, discard-and-pull, and the
  post-push closing pull are covered by one hook. `TsSync` is UI-free and single-caller (the view-model
  serializes), so no locking.
- *Alternative — backup to a temp file, diff two dbs, swap:* touches the pull's file mechanics
  (atomic-replace, WAL sidecars) for no benefit; the field set is small enough that two in-memory
  snapshots (hundreds of rows × ~25 columns) are trivially cheap.
- *Alternative — diff against the remote read-only over the share (no pull):* would give ← in
  Continue-local/offline sessions, but reintroduces the network-read path the sync model deliberately
  deleted. Rejected; no-pull sessions have no ← (user-confirmed).

### D2 — Diffable field set: one authored list, displayed/editable columns only

A static list in the differ names exactly the columns compared, per table:

- `exposureplan`: `desired`, `acquired`, `accepted`, `exposure`, `enabled`, `exposureTemplateId`
  (a rig-side template swap materially changes the plan and is otherwise invisible).
- `target`: `active`, `priority`, `rotation` (the `TsEditableSchema` set) + identity the grid displays:
  `name`, ra, dec (exact column names verified against `TargetSchedulerReader`'s queries at
  implementation).
- `project`: the `TsEditableSchema` project set (`state`, `priority`, `minimumtime`, altitudes, horizon,
  `meridianwindow`, `ditherevery`, `enablegrader`, `smartexposureorder`, `flatshandling`,
  `filterswitchfrequency`).

- *Why authored, not `PRAGMA table_info`:* diffing every column would mark rows for TS-internal
  bookkeeping the user can't see (noise ←) and couple us to TS schema churn. The authored list is the
  same "semantics live in code" convention `TsEditableSchema` already established. A remote column
  missing from a given db is skipped silently (older TS schema) — the differ observes, it has no
  contract to enforce.
- Rows **added** on the rig produce one inbound entry ("new from BIRDWATCHER" tooltip); rows **deleted**
  on the rig produce nothing (their grid rows vanish at reload — there is nothing to mark).

### D3 — Session store: sticky union, masked by write-back

`TsSync` holds the inbound set in memory: `(table, key) → column → (old, new)` display strings.

- Each pull's diff **unions in** (a mid-session Pull-now adds new arrivals without erasing the open's
  info). Within one (table, key, column), a later diff overwrites old/new (latest observation wins).
- **Sticky for the session** — the post-push closing pull diffs local-vs-remote right after replay, which
  is near-empty; because the store unions rather than replaces, earlier ← survives a push
  (user-confirmed: pushing one edit must not erase the "what the rig did overnight" info).
- **Mask (the actuals override):** `TsSync.RecordWriteBack` — the one place disk-derived stamps journal —
  additionally removes `acquired`/`accepted` inbound entries for that plan key. Disk supersedes the
  rig's actuals: the row reads `→` (not `⇄`) while unpushed, and goes **clean** (not stale-`←`) once
  pushed. `desired` is deliberately *not* masked — a write-back ratchet raise coexisting with a rig-side
  desired change is a genuine both-directions fact (⇄).
- Cleared only by process exit; next open's pull starts a fresh set.

### D4 — Marks resolver: journal + inbound + graph, keyed lookups

A UI-free `Services\SyncMarks` builds, per refresh, two direction lookups from (a) `Journal.Collapse()`
(first-Old → last-Value per field — already exactly the tooltip shape) and (b) the inbound store, plus a
**graph-derived map** `TargetId → [plan TS keys]` and `TargetId → project key` from the retained
`CatalogGraph`.

- Leaf (filter row): marks key on `PlanTsKey` only. Target/project-level changes mark the *header*, not
  leaves — and disk-plane rows (no plan key) structurally never mark. Rollup detail lines carry their own
  `PlanTsKey` and mark like leaves.
- Header (target group / mosaic panel): union of directions over its own `Target` key, its `Project` key
  (group header / mosaic parent only — panels stay clean on project edits, user-confirmed), and **all**
  of its targets' plan keys from the graph map.
- *Why the graph map:* `BuildRows` nulls `PlanTsKey` on multi-plan folds, so visible rows under-report
  plan coverage; a journaled or inbound change on a folded plan must still roll up to its header. The
  graph knows every plan key regardless of grid folding.
- Tooltip: leaves list per-field lines (`← BIRDWATCHER: acquired 10 → 14`, `→ unpushed: desired 20 → 25`);
  headers show direction counts (`← 2 field(s) arrived changed · → 3 field(s) unpushed`). No tooltip when
  blank.

### D5 — Live updates: one in-place sweep, four call sites

Rows and headers gain mutable `MarkGlyph`/`MarkTooltip` (PropertyChanged, `Mode=OneWay` bindings — the
`ApplyDesired` pattern). `MainViewModel.RefreshAllMarks()` walks `_groups` → panels → children → detail
rows and re-resolves each mark, raising PropertyChanged only on actual change. Called: at the end of
`ApplyFilters` (rows are freshly built), after every applied edit (`ApplyOutcome`), after a push that
did not reload (partial failure / mid-push edits skip the closing pull), and after Discard.

- *Why a full sweep, not per-edit targeting:* one code path, no key-routing bugs, and the workload
  (hundreds of rows of dictionary lookups) is microseconds. The sweep mutates in place — never replaces
  the `Rows` collection — so scroll position and in-progress edits survive (the standing in-place rule).
- Push-with-closing-pull already ends in `LoadAsync(PullPolicy.Never)` → `ApplyFilters` → sweep; no extra
  call needed on that path.

### D6 — XAML: fixed 24 px column 0 in all four blocks

Insert `<ColumnDefinition Width="24"/>` at position 0 of the four duplicated width blocks (group
template, filter template, panel template, sticky header row) and bump every `Grid.Column` index by one.
Each row template gets a centered `TextBlock` at column 0 bound to `MarkGlyph` (OneWay) with
`ToolTipService.ToolTip` bound to `MarkTooltip`; the header row's column 0 stays empty (unlabeled,
user-specified). 24 px ≈ 3 characters at the grid's type size and fits `⇄` centered; Segoe UI renders
U+2190/U+2192/U+21C4 natively.

## Risks / Trade-offs

- [After an imaging night, most active targets carry ←] → Accepted deliberately (user chose all-fields
  scope): the wall of ← *is* the "you imaged last night" information; rows where disk disagrees with the
  rig's bookkeeping show → instead via the mask.
- [Discard-then-pull reports the user's own reverted edits as ←] → Accepted and honest: the diff is
  "differs from what you last saw," and a discard genuinely reverted those values; tooltip shows the
  revert. Documented in the spec as a scenario.
- [TS schema drift renames/drops a diffed column] → The differ skips columns absent from either snapshot
  (observation, not contract — no abort, consistent with the differ having no write role). A rename means
  that field silently stops diffing until the authored list is updated; low blast radius.
- [⇄ warns but does not block same-field overwrite] → In scope: mark + tooltip only. Push review already
  warns on staleness at db level; per-field conflict highlighting there is a natural follow-up, not this
  change.
- [Mid-push journal appends] → Already handled: `CommitPush` retains them; the post-push sweep re-resolves
  from the journal as it stands.
- [Snapshot cost on big dbs] → Bounded: three SELECTs over the authored columns, hundreds of rows; runs
  inside the existing off-UI-thread pull.

## Open Questions

None — semantics were settled interactively (mark meanings, all-fields inbound scope, sticky-session
lifetime, no-← in no-pull sessions, parent-only project marks, rollup union, actuals mask, tooltip
inclusion).
