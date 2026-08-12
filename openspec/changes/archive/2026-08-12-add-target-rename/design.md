# Design — add-target-rename

## Context

See `proposal.md` for motivation. Current state, all four legs already carrying most of the load:

- **Editable surface** is schema-driven: `TsEditableSchema` (Library) is the whitelist, the generated
  form (`TsFieldsEditor`), the edit gate (`TsEditGate`), journal/replay, and push review all follow it.
  `target.name` is deliberately absent — a resolver-era exclusion ("identity columns must round-trip
  the resolver"); the resolver was rejected 2026-07-08, so the reason no longer holds for `name`.
  The `Guarded` arm-to-edit gesture is generic (`WithArmGuard` wraps any guarded field's control).
- **Inbound diffing exists**: `TsInboundDiff` snapshots the local db before/after every pull and
  reports field-level changes — `target.name` is already in its diffable set (it powers the ← mark
  the user saw when the P9 rename arrived). Guid-correlated, renumber-safe, observation-only today.
- **Export duty exists**: `CatalogInboxExporter` maps applied journal entries to contract-v1 full-value
  upserts; its `target-upsert` already carries `name`. The `catalog-export` spec currently pins **push
  as the sole emitter**.
- **Close-time re-reconcile exists**: an applied edit to a cell-keying field marks the editor session
  `reshaped`; dialog close runs a no-pull `LoadAsync` (obs 4798).

Two-repo impact: the schema row is a Library (`Astronomy.Catalog`) edit; everything else is app-side.

## Goals / Non-Goals

**Goals:**
- Rename a target from TSM like any intent edit: guarded field in the existing target editor →
  journal → reviewed push-as-replay → TS write → `target-upsert` emission. No contract change.
- Close the observed-rename gap: target changes committed on BIRDWATCHER's side flow to ISM's inbox
  automatically at pull (decision D3), so the pending `Cygnus Loop P9` rename flushes with no
  manual action.

**Non-Goals:**
- RA/Dec/`epochcode` stay excluded from the editable surface — pointing identity is not part of this
  change; the rename verb re-admits `name` only.
- No target create/delete/move (the adoption verb remains the one structural add; the 2026-07-08
  resolver rejection stands).
- Remotely-**added** targets do not emit (D3 scope); project/plan/template inbound changes do not
  emit (project settings are the feed-v2 lane; plan inbound columns include actuals).
- No ISM-side change: same ops, same fields, v1 (the ingest already applies `target-upsert` renames
  full-value — live-verified 2026-08-12).

## Decisions

### D1 — `target.name` becomes a schema row, `Guarded`

One `TsField` row (`TsTable.Target`, `"name"`, Text, `Guarded: true`) in `TsEditableSchema`, replacing
the blanket identity-column exclusion with an RA/Dec/epoch-only exclusion. The whole pipeline lights up
from the row: dialog rendering (with the generic arm-to-edit guard), gate whitelist, journal, replay,
push review, outbound → mark (the inbound ← side already covered `name`).

- **Why guarded:** a rename mid-project is deliberate, never incidental — NINA's `$$TARGETNAME$$`
  file naming follows it, so future frames land under a new directory name. Matching is
  coordinate-primary, so both directory generations resolve to the same canonical target (the
  proposal's "historical files still resolve"), but an accidental rename would still split the disk
  library's naming for no reason. Same posture as `rotation`.
- **Alternative rejected:** a dedicated "Rename…" menu verb + one-off dialog — new surface beside the
  schema-generated one, a second editing idiom to maintain, and none of the journal/marks/emission
  legs for free (user decision, this session).

### D2 — a rename re-shapes: close-time re-reconcile, not an in-place-only mirror

`target.name` joins the close-time re-reconcile trigger (the obs-4798 `reshaped` path). It is not a
capture-configuration pairing key, but it is **group identity**: the grid's group header, sort order,
name-claim matching, and mosaic parent grouping (clause-tolerant name matcher) all read it. Name gets
**no live in-place mirror** (amended at implementation): the panel editor — the motivating mosaic case
— carries no row context for a mirror to target, and a name mirror could update the header text but
never the sort/grouping around it; the grid shows the open-time name until the dialog closes, then the
no-pull reload re-reconciles so all name-dependent structure and badges follow at once.

- **Alternative rejected:** pure in-place mirror — leaves name-match badges and mosaic grouping
  asserting the old name until the next manual reload; exactly the staleness obs 4798 closed.

### D3 — pull-diff emission adopted, targets only (user decision, this session)

At every pull (open, Pull-now, and a push's closing pull), the pull's inbound diff is additionally
projected into the inbox: every **Target-table** field change on an **existing** row (guid-correlated;
`(new)` row entries excluded) emits one full-value `target-upsert`, values read from the fresh local
copy post-pull. Precedent: the template mirror's 2026-08-12 widening — "mirror TS-committed state,
whichever surface changed it"; idempotent, no contract bump.

- **Why targets-only is safe on origin:** actuals live on plan columns (`acquired`/`accepted`) —
  the Target table's diffable columns (`active`, `priority`, `rotation`, `name`, `ra`, `dec`) are
  all user-authored intent, whichever surface authored them. So the "actuals never emit" invariant
  cannot be violated by construction, without any origin bookkeeping.
- **Why existing rows only:** a remotely-added target emitted without its plans is half a family;
  proper family emission (plans + template mirrors) is a bigger posture change. Recorded residual:
  targets created on BIRDWATCHER after ISM's one-time import still don't feed the inbox (they never
  did) — revisit with feed v2.
- **Why not the accumulated `TsInboundStore`:** emission consumes each pull's **fresh diff list**,
  not the session-accumulated store (the store is marks UI state; re-emitting it every pull would be
  harmless but noisy). One pull → at most one observed-emission file.
- **The closing pull can't echo the push:** edits are local-first, so TSM's own replayed changes are
  identical on both sides at the closing pull and never diff. Only genuinely external changes emit.
- **Alternative rejected:** decline and keep push-sole-emitter purity — leaves every
  BIRDWATCHER-authored target edit invisible to ISM and the P9 flush manual.

### D4 — observed-emission envelope and transport reuse

Observed records are ordinary contract-v1 `target-upsert` lines: `at` = pull completion time (TSM
observed the change then; TS's own commit time is unknowable — contract ordering is by file order,
not `at`, so this is presentation-only). Transport reuses `WriteInbox` unchanged (atomic
`.partial`→`.jsonl`, `tsm-<yyyyMMdd-HHmmss>.jsonl` naming — ISM's glob is `*.jsonl`). A push and its
closing pull emitting in the same second would collide on `FileMode.CreateNew`; the writer advances
the stamp by one second until free (deterministic, bounded, no suffix scheme leaking into the
contract's naming pattern).

Failure posture mirrors the push-side duty (rule #16): the pull is already committed; an emission
fault aborts the remaining export work loudly (log naming path + operation, user-visible error),
never rolls back the pull, never degrades to skip-the-record. Idempotent upserts make the next
pull/push after the fix harmless.

### D5 — mapping code stays in `CatalogInboxExporter`

The observed emission is a second thin entry point (`ExportObservedTargets`: guids + local db +
observed-at + inbox dir) reusing the existing row read, guid requirement, serialization, and
transport. No parallel emitter class — one home for "how TSM writes the inbox" (CONVENTIONS'
one-plausible-home rule).

## Risks / Trade-offs

- **[Push no longer the sole emitter]** The catalog-export spec's cleanest invariant is spent; the
  spec now names two emission points with distinct origin rules. → Mitigation: the origin invariant
  that actually matters — *actuals never emit* — is preserved structurally by targets-only scope;
  the spec delta states both emission points explicitly.
- **[A BIRDWATCHER rotation/coordinate change emits at pull]** Broader than the motivating rename:
  any Target-table inbound change emits (active, priority, rotation, ra, dec, name). This is the
  point (mirror TS-committed intent), but it means e.g. a TS-side coordinate nudge reaches ISM
  without TSM authoring it. → Accepted deliberately: it is authored intent, just authored on the
  other surface; full-value upserts keep it idempotent.
- **[Rename mid-project splits disk directory naming]** New frames land under the new name; the
  old directory stays. Coordinate-primary matching unifies both under one canonical target, but the
  library tree shows two directories. → Accepted (also true of renames done in TS's UI today);
  the guard gesture makes it deliberate.
- **[Journal label vs. new name]** A rename's journal entry and push-review line carry the target's
  label at edit time; after the rename the row's later entries carry the new name. Cosmetic only —
  keys are guid-stable. → No mitigation needed beyond using the current name at entry creation,
  as every edit already does.

## Open Questions

None — the pull-diff adoption and surface placement were the two open forks; both were settled by
user decision this session (D1/D3).
