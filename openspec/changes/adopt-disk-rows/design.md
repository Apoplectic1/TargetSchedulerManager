# Design — adopt-disk-rows

## Context

See `proposal.md` — Why. Load-bearing current state:

- `Row_RightTapped` (`MainWindow.Flyouts.cs`) already composes a data-gated menu per row type and is
  spec'd as *the* extension point; disk-only rows deliberately fall through with no menu today.
- The journal (`Shared/TsJournal`) knows two kinds, `Manual` and `WriteBack`, both field-level
  `(table, key, column, value, old, label)`. Push-as-replay (`Shared/TsSync`) has two legs keyed by
  per-copy integer ids (plans/templates) or guid (targets/projects).
- TS's two-name identity: integer `Id` is per-copy autoincrement (diverges across copies for inserted
  rows); `guid` is minted at creation and travels — TS-SCHEMA.md already flags it as what an insert
  replay must carry.
- The library's `TargetSchedulerEditor.TrySetField` owns the guard order (schema, read-only, sidecar,
  column presence, OEO last), the cadence-clear transaction, and read-back verify — the natural home
  for an insert primitive (`cadence-safe-ts-editing` is a library contract).
- The retained load graph/report already computes unplanned disk cells (`UnplannedFrames` notes),
  framing clusters (sky vs mechanical), and capture-config dimensions per cell.

## Goals / Non-Goals

**Goals:**
- One reusable, guarded insert path (library primitive → app funnel → journal → replay) that future
  row-creation features can ride.
- The adopted cell reads `Both` immediately after the local write, with correct marks, and survives
  the push round-trip with no phantom inbound.

**Non-Goals:**
- Mosaic adoption (creating `isMosaic` projects/panels) — excluded by spec; revisit on demand.
- Template creation or editing — auto-match holds instead.
- Un-adopt / row deletion — discard-and-pull is the undo; no delete journal kind.
- Any change to write-back's existing-rows-only contract or to reconciliation.
- Project creation from the picker.

## Decisions

### D1 — The insert is a library primitive beside `TrySetField`, not app-side SQL
The guard order, cadence transaction, OEO refusal, and read-back verify already live in
`Astronomy.Catalog`'s editor; duplicating them app-side would fork the contract. The primitive takes a
full column payload + minted guid, refuses structurally (no throws for guardable states), and for
`exposureplan` deletes the parent target's `filtercadenceitem` rows in the same transaction.
*Alternative rejected:* ad-hoc INSERT in `TsSync` — repeats every guard and leaves the cadence clear
unowned. Shared-library discipline applies: the primitive's surface is caller-framed (no TSM terms).

### D2 — Field edits on an unpushed insert fold into the INSERT at replay
A later `desired` edit on an adopted plan journals under the *local* plan id; replaying it as a remote
UPDATE keyed by that id would miss (remote id differs). At push, entries whose (table, key) matches an
unpushed insert's local key are folded into the insert payload — the row lands remotely with final
values, one INSERT, no UPDATE. *Alternatives rejected:* guid-keyed UPDATE after the INSERT (two remote
ops + review noise for zero benefit); forbidding edits on unpushed inserts (gratuitous UX cliff).

### D3 — Phantom-inbound is killed by guid correlation in the differ (revised at implementation)
The closing pull's differ keys plans by integer id, so a pushed insert (local 900 → remote 712) reads
as "new row 712". Originally decided as a push-time guid mask (session state, the `RecordWriteBack`
pattern); **implementation flipped it to guid correlation inside `TsInboundDiff`** — the snapshot also
captures `guid` on id-keyed tables, and a row new-by-key correlates to its before-row by guid (same
guid = same row renumbered → field-diff under the new key; different guid at the same id = a genuinely
different row → new-row entry, never a cross-row field diff). Why the flip: an in-memory mask dies with
the session, so a failed closing pull + restart re-manufactures the phantom on the *next* open's pull;
and the mask never covered the id-collision case (the remote minting our local id for a different row,
which the old differ would field-diff across two unrelated rows). Correlation is stateless, covers
both, and the spec wording ("the differ (or a push-time mask) SHALL correlate … by guid") anticipated
it. Genuinely remote-added rows keep reporting.

### D4 — Eligibility is computed from the retained graph at menu-build time
A disk-only filter row offers adoption iff its target has **no** plan at the row's
`(filter, purpose, seconds)` — the same predicate that makes write-back's `UnplannedFrames` notes, so
split rows (capture-config/framing separations, which *do* have a same-key plan) are excluded for
free. No new state: the menu handler asks an adoption planner service that reads the retained
graph/report. Fully disk-only mosaic parents short-circuit to ineligible.

### D5 — Template matching mirrors the merge rule, implemented in the adoption planner
Same filter + `"Stars "` purpose prefix + gain/offset/bin **expressed and equal** to the cell's.
*(Corrected 2026-08-03 after field feedback — the Abell 78 hold:* the first cut treated a `-1`
camera-default sentinel as compatible-with-anything, but the library's merge rule
(`ReconciliationProjection`, capture-config-keys) deliberately keys a sentinel plan as its own
never-pairing cell — "nothing can be asserted to agree with an unspecified value." A sentinel-matched
adoption would therefore create a plan that lands *beside* the disk row, failing the feature's purpose.)*
Exactly one candidate proceeds; zero or ≥2 refuse with a message naming the situation, including
same-family near-misses (differing or camera-default gain/offset) so the fix is evident. Exposure
override: `-1` sentinel when template default == cell seconds, else explicit. This intentionally reuses
the reconciliation vocabulary so "the plan I create pairs with the row I clicked" is true by construction.
Holds surface in a dialog (`AdoptHoldPrompt`), not only the status line — an explicit menu action that
silently declines reads as "nothing happened" (the Abell 78 report).

### D6 — New-target payload details
- `ra` = disk centroid RA **÷ 15** (degrees → hours), `dec` = centroid degrees.
- `rotation` seeded from the framing cluster's **sky** angle when expressed (fold-180 normalized);
  mechanical/unknown never converts (house invariant) and seeds nothing.
- `projectid` from the picker (existing projects only); plan `profileId` copied from the chosen
  project's `profileId`.
- Coerced-safe defaults for the rest (epoch J2000 code, `active` = 1, priority Default, `roi` = TS
  default 100) — shown in the confirm dialog where user-meaningful.
- Target + plan insert is one atomic local operation (single transaction through the primitive's
  multi-insert form, or two calls under one transaction scope — decided at implementation, atomicity
  is the requirement).

### D7 — Journal shape: one new kind, payload as the entry's value
`TsEditKind.Insert` with the row payload serialized into the existing jsonl line (column = `"*"` or
similar sentinel, value = payload object, key = local id, plus the guid). Collapse rules: an insert
entry is never pruned (no baseline) and never collapsed away; field entries on the same key collapse
normally and fold at replay (D2). Rule 15 applies — the journal format just changes; an unpushed
pre-upgrade journal is not migrated (operational heads-up: push or discard before upgrading).

### D8 — Born complete and enabled (user decision, 2026-08-03)
`desired = acquired = accepted =` disk count; `enabled = 1`. Intent is *record history*: TS sees a
satisfied plan and schedules nothing; raising `desired` later is the normal edit path. The post-load
write-back pass then treats the plan as ordinary (its stamp is a no-op by construction on a fresh
adoption).

## Risks / Trade-offs

- [Phantom `←` if the guid mask misses a path] → the mask lives at the single `TsSync.Pull` choke
  point all four pull paths share; regression test: adopt → push → closing pull asserts a clean row.
- [Insert replay FK lookup fails (template/project deleted remotely since pull)] → existing per-entry
  failure rules: loud report, entry retained; no partial row is written (INSERT is atomic).
- [Journal entry with insert payload grows the line format] → jsonl is append-only and self-describing;
  unknown-kind lines in an old TSM would fail loudly (fail-fast, rule 16), not skip.
- [Template match sensitive to capture-config edge cases (mixed gain within a cell)] → cells are
  already keyed by gain/offset/binning upstream; a cell is homogeneous by construction.
- [User adopts, then TS/NINA writes overnight before push] → ordinary staleness path: review warns
  remote changed since baseline; inserts are additive and collide with nothing by guid.

## Open Questions

None — decisions above cover the spec surface; remaining choices (transaction scope shape in D6,
exact jsonl field names in D7) are implementation-local.
