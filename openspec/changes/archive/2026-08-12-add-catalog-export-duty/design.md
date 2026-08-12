# Design: add-catalog-export-duty

## Context

See `proposal.md` — Why. TSM implements the writer side of the catalog inbox contract v1
(`..\IntervalSchedulerManager\docs\design\catalog-inbox-contract.md`) and knows nothing of
`Catalog.db` itself. The facts that shape the approach, all pre-existing:

- **BIRDWATCHER is written only inside the push.** In-grid edits, adoptions, and the automatic
  write-back all write the *local* working copy and journal; `TsSync.Push` is the single funnel
  where TS-the-system-of-record changes (SUBSYSTEMS.md → TS sync model).
- **The journal tags entry origin.** Every `TsJournalEntry` carries `TsEditKind` — `Manual`
  (user field edits), `WriteBack` (the automatic count pass), `Insert` (adoption payloads with
  the minted guid). The push replays these as three legs (inserts → write-back → fields).
- **The write-back leg touches an intent-plane field.** Besides `acquired`/`accepted`, write-back
  ratchets `desired` up to ≥ the kept count. That ratchet rides the push journal as a desired
  change — so "actuals never emit" cannot fall out of hook placement alone; it needs the origin
  filter (D2).
- **Push outcomes are truthful** (openspec `truthful-outcome`): `Journal.CommitPush` inside
  `TsSync.CommitAndClose` is the single point where "push committed" becomes true; any throw that
  escapes `Push` precedes the journal rewrite.

## Goals / Non-Goals

**Goals:**

- One emission site, one failure path, mapped 1:1 onto the push's own structure.
- Emission input = exactly what the replay applied; emission values = the rows as committed to TS.
- Zero state beyond the append: no sent-tracking, no retry queue, no Catalog.db knowledge.

**Non-Goals:**

- No re-emit/recovery machinery (idempotent upserts + re-do-and-push is the recovery; contract is
  deliberately stateless on TSM's side).
- No emission for ISM's benefit beyond the contract (no acks, no history retention — files die at
  ingest).
- No surgical/single-target write-back coverage: that path is library-only today, not app-wired;
  if it ever wires up, it journals like everything else and rides the same hook.

## Decisions

### D1 — Emit at push time only

The duty's charter is "update Catalog.db whenever TSM writes TS," and TSM writes TS exactly once:
at push. A Catalog.db row means **"authored intent as committed to TS"** — edit-time emission
would let the store hold intent the live TS never received (journal edited, push abandoned or
trimmed at review), which during coexistence is exactly the divergence the feed exists to prevent.
The review step is part of authorship in TSM's model; emission respects it. The lag (ISM's view
trails until push) is feature-shaped honesty — "unpushed edits aren't real yet" is already how TSM
edits are understood — and the whole path retires with TS, so its latency is not worth optimizing.

*Alternative rejected:* emit at local-edit time (and again at push). Idempotence makes the
duplicates free, but the semantics go dishonest — Catalog.db could hold trimmed/abandoned intent.

### D2 — Origin filter: `Manual` + `Insert` entries emit; `WriteBack` never does

The applied entry set is filtered by `TsEditKind`: `Manual` field edits and `Insert` payloads map
to ops; `WriteBack` entries — `acquired`/`accepted` stamps **and the desired ratchet** — are
excluded. The ratchet is actuals wearing intent's clothes: `desired = max(desired, kept)` is
derived bookkeeping that keeps TS's model coherent, not the user wanting more frames.
`desired_count` in the intent store stays the last user-authored value; ISM genuinely doesn't need
the ratchet (under plan-db + fresh-scan, surplus already yields remaining = 0), and the
"can't-want-less-than-kept" rule is TS's problem, dying with TS. The journal already tags origin,
so the filter is a tag check — no journal schema work. (Contract-side pin, made bilaterally with
this change: the contract's `exposure-plan-upsert` defines `desired_count` as the user-authored
value, ratchets excluded — clarification only, no version bump.)

**Desired sourcing on co-edited rows:** full-value upserts read the row as committed, so a
`Manual` entry on a plan whose desired was ratcheted in the same push would emit the ratcheted
value — and that is *not* planning-neutral over time: disk-ACTUAL can shrink (grading curation
deletes frames), so a leaked ratchet (authored 30, ratcheted 40, later culled to 32) would have
the planner schedule 8 frames the user never asked for. The journal makes the true fix a
one-liner, so the exporter takes it: when a plan row's applied set includes a `WriteBack`
`desired` entry, `desired_count` is sourced from the `Manual` desired entry when present (the
explicit edit outranks the stamp, mirroring replay order), else from the write-back entry's
recorded pre-push old value. Residual approximation, accepted: the pre-push value — and the
one-time import baseline, which lifted TS's values verbatim — may carry *historical* ratchets;
desired purity was approximate at baseline, and ISM's manual-first plan review is the standing
catch.

### D3 — Hook in `MainViewModel.PushAsync`, exporter as a Services-layer step

The exporter is a new `Services\CatalogInboxExporter`, invoked from `PushAsync` after
`Sync.Push(...)` returns with a committed outcome — not inside `TsSync`. `TsSync` is machine/
network policy (UI-free, single-caller, `Shared\`); the export needs row resolution and
user-visible surfacing, both VM/Services-side concerns. `WriteBackStep` and `SyncMarks` are the
precedents for a Services-layer step consuming sync-model state.

`Push` must expose what it applied: extend `PushResult` with the applied entry set (the collapsed
`Manual`/`Insert` entries minus failures — `TsSync` already partitions exactly this to drive the
replay legs). **A row with any failed entry is wholly excluded** from this push's emission: its
journal entries are retained, the local value isn't remote truth yet, and the next successful push
emits the whole row. Emitting it now would send intent TS doesn't hold.

*Ordering note (deliberate):* the export runs after `Journal.CommitPush` — moving it earlier
(inside the replay-to-commit window, so a crash would re-emit via re-push) was rejected because it
couples the journal rewrite to an unrelated I/O step, the exact coupling `truthful-outcome`
rejected for the closing pull. The cost is a bounded crash window (D7, Risks).

### D4 — Values from the local working copy; journal says *which*, the local db says *what*

Ops carry full row values read from the **local working copy after the push** — after replay the
local copy holds every committed value regardless of whether the closing pull landed (edits were
local-first; the closing pull only re-establishes the baseline). The exporter resolves each
applied entry's `(Table, Key)` — target guid, plan/template local integer id, insert `RowGuid` —
to its row via the same local read path the load uses, and builds one op per affected row
(multiple entries on a row collapse into one full-value upsert). This keeps the exporter
independent of the post-push UI reload's timing and of `PulledFresh`.

### D5 — Op mapping, and the template mirror rides *every* exposure-plan upsert

Per affected row: `Target` → `target-upsert`; `ExposurePlan` → `exposure-plan-upsert`;
`Project` → `project-upsert`; an adoption insert group → its target + plan(s) upserts (+ project
if the dialog touched project intent). Records are written references-first (project → template →
target → plan) within the file.

**Uniform mirror rule:** every `exposure-plan-upsert` — adoption *or* plan edit — is accompanied
by the referenced template's `exposure-template-upsert`. The contract mandates the mirror
alongside adoptions; emitting it with every plan upsert is a contract-compatible superset
(idempotent, no new op/field) that closes a real ingest-abort hole the adoption-only rule leaves
open: a plan created in TS's UI against a template authored *after* ISM's one-time import, then
edited in TSM — the plan upsert carries enough payload to create, but its
`exposure_template_ts_guid` would be unresolvable and abort the ingest. With the uniform rule, the
mirror always precedes the reference. TSM still never creates or edits templates (obs 3dfe) —
mirror values are TS-authored, read from the local copy.

### D6 — Transport: one short-lived file per push, published atomically

One file per push, `tsm-<yyyyMMdd-HHmmss>.jsonl` named from the push commit time (also the
envelope `at`, UNIX seconds UTC — consistent with the contract's "when TSM committed the
corresponding TS write": under push-time emission, the push commit *is* the TS write). The
exporter creates the inbox directory if missing, writes all lines to `tsm-<ts>.jsonl.partial`
(UTF-8 no BOM, `\n`), flushes, closes, then renames to `.jsonl`. ISM's glob is `*.jsonl`, so it
never observes an incomplete file; a crashed `.partial` is inert and diagnosable. The short-lived
handle also sidesteps rename contention with ISM's claim-by-rename, and a fresh timestamped name
can't collide with a `*.processing` claim.

*Alternative rejected:* one append-held file per TSM session (contract-permitted) — a handle held
across the session collides with ISM's rename-claim and turns whole-line flushing into a live
concern; per-push files make atomic publication trivial.

### D7 — Failure path (rule #16): abort loudly, journal already truthful, no compensation

Any export fault — directory uncreatable, disk error, rename failure — aborts the remaining
export work: `Log.Error` naming the inbox path and the failed stage/op, and the push status line
carries a loud suffix (push outcome preserved — the push *did* commit): e.g.
`"PUSH OK (…) — CATALOG EXPORT FAILED: see tsm.log"`. No silent continue, no skip-the-record, no
retry queue. Export failure never retains the journal (that would recreate the truthful-outcome
"journal claims dirty over a db that holds the values" lie). Recovery is the user re-doing the
affected edits and pushing again — idempotent full-value upserts make that harmless — or, since
the `.partial` never published, simply touching the rows in any later push.

### D8 — Test seams: pure mapping core + temp-dir file fixtures

Split the exporter into a pure mapping (`applied entries + resolved rows → ordered JSON lines`)
and a thin file writer. The mapping is unit-tested directly (op selection, origin filter incl. the
ratchet case, full-value collapse, mirror accompaniment, reference ordering, envelope fields). The
writer is tested against `SyncTestEnv.NewDir()`-style temp inboxes: naming, no-BOM UTF-8, `\n`
endings, `.partial`→`.jsonl` publication, directory creation, `*.processing` untouched. Per the
spec: no test ever opens `Catalog.db`; fixtures are inbox files only.

## Risks / Trade-offs

- **[Crash between journal commit and export]** → that push's feed is lost (journal cleared, file
  never published, no error surfaced because the process died). Bounded: same-machine, millisecond
  window, coexistence stop-gap; heals when the rows next change (full-value upserts) or the user
  re-does an edit. Accepted over re-ordering the export into the commit window (D3 ordering note).
- **[Concurrent TS-UI edits since baseline]** → a full-value upsert snapshots fields the user may
  have just changed in TS's own UI (push warns-not-blocks on remote drift). Local mirrors remote
  after the closing pull, so the window is one push; last-write-wins upserts self-heal on the next
  push touching the row. Coexistence-window reality, not worth machinery.
- **[Historical ratchets in the desired baseline]** (D2) → the co-edit sourcing rule stops *this
  push's* ratchet from leaking, but ratchets already baked into the baseline (the one-time import
  lifted TS's values verbatim; earlier pushes) still travel as "authored." Consequence is bounded
  — a few unwanted frames in a *proposed* plan, and manual-first review is the standing catch;
  not worth reconstruction machinery in a feed that dies with TSM.
- **[Uniform mirror rule exceeds the contract's original letter]** (D5) → absorbed into the
  contract 2026-08-12 (bilateral clarification, no version bump — same treatment as the
  `desired_count` authored-value pin).

## Migration Plan

None (portfolio rule: no back-compat). Additive feature; first emission creates the inbox
directory. Retirement is deletion: remove the exporter + hook and delete the inbox path when TS
retires — no schema change on either side.
