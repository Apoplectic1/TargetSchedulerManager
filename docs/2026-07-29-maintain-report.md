# MAINTAIN sweep — 2026-07-29 (graduate & prune)

**Status:** complete. Third TSM maintain sweep (prior: 2026-07-26 morning over the dated `docs/` records,
2026-07-26 evening over the openspec archive). Ran the day `framing-overlap-column` archived, so the newest
journal material was hours old.

## Method

Two rounds, loop-until-dry, **6 workers** at 4–6 documents each — the batch-density cap from the
2026-07-26 method lesson (*an overloaded worker's "dry" is not evidence*; that sweep had one worker carry 16
records and miss 2 finds).

| Round | Worker | Batch | Result |
|---|---|---|---|
| 1 | design-records | the 4 archived `design.md` newer than the last sweep | 4 keep |
| 1 | notebook-and-live-doc | `NOTEBOOK.md` (all entries) + `docs/2026-07-08-resolver-rejection-is-lane.md` | 4 archive-candidates, 4 keep |
| 1 | changelog-recent | `CHANGELOG.md` 2026-07-27 → 07-29 | 5 keep |
| 1 | docs-archive | the 5 records in `docs/archive/` | **1 graduate**, 4 keep |
| 2 | changelog-jul | `CHANGELOG.md` 2026-07-01 → 07-26 (~30 entries) | 7 keep — dry |
| 2 | changelog-jun | `CHANGELOG.md` 2026-06-30 and earlier (~28 entries) | 10 keep — dry |

**Coverage: complete.** Every live journal source was read this sweep — the one dated `docs/` record, all 5
`docs/archive/` records, every `NOTEBOOK.md` entry, all ~58 `CHANGELOG.md` entries, and the 4 archived
`design.md` records postdating the last sweep. The 27 older `design.md` records were swept 2026-07-26 at
corrected density and are not re-read here.

**Headline: the journal owed almost nothing.** 34 items classified, 30 `keep`. That is rule #20
(shift-left graduation) working — ship-time doc updates had already single-sourced the truths, and workers
repeatedly found their candidate already sitting in a spec or reference doc with a line number.

## Held graduates — recorded, NOT applied

### H1 — `Astronomy.Diagnostics` is deliberately not `Astronomy.Catalog` *(portfolio-level, M15)*

- **standing-claim:** Shared app-observation tooling (logging, screen capture) stays out of
  `Astronomy.Catalog` on purpose — Catalog is a schema/build contract, not a grab-bag utility library. That
  is why `Astronomy.Diagnostics` exists as its own assembly.
- **source:** `docs/archive/2026-06-10-code-review-slice1.md` §4.4 — written *before* Diagnostics existed
  (it shipped 2026-06-11), so the rationale predates the thing it explains and was never carried forward.
- **evidence it is standing:** it is a boundary rule that answers a question a future contributor will ask
  ("why not fold these into Catalog?"). Verified absent everywhere: TSM `CLAUDE.md` and the portfolio
  `CLAUDE.md` both *name* Diagnostics as a dependency without saying why it is separate;
  `ARCHITECTURE.md` never mentions it; the Library's own `CLAUDE.md` and `CONSUMERS.md` describe its API
  surface but not the boundary. A grep for the rationale across every portfolio `.md` returns nothing.
- **why held:** the truth is about the **Library repo's** assembly boundaries. No TSM doc's charter can own
  it, and no portfolio `DOMAIN.md` exists (`..\` carries only `CLAUDE.md` + `ROADMAP.md`). M15 forbids
  improvising the placement into the container router, a sibling repo's docs, or a new cross-repo pointer.
- **disposition of source:** `stub` — the archived review keeps the finding; nothing was edited there.
- **needs from the user:** a placement decision — Library `CLAUDE.md`, Library `CONSUMERS.md`, or a new
  portfolio `DOMAIN.md`. Tracked as one ROADMAP open-line.

### H2 — the deliberate `PlanSeconds == 0` conflation *(bloated target, M14)*

- **standing-claim:** `ReconciliationRow` uses `PlanSeconds == 0` as its own "no seconds" marker, so a
  TS-only row renders a literal zero-second plan as the em dash. The conflation is **deliberate and left in
  place**: the invariant that matters is that the in-place mirror and the next reload agree on every plane,
  and they do. Only plan+disk rows display a literal `0`.
- **source:** `NOTEBOOK.md` 2026-07-07 — "exposure 0 is literal; TSM's two `> 0` filters were the leftovers".
- **evidence it is standing:** the *rule* (exposure 0 is a literal zero-second exposure) is single-sourced in
  `openspec/specs/schema-driven-field-editor/spec.md:51` — but the **deliberately-left conflation** is not in
  any reference doc. `DOMAIN.md`'s em-dash convention states the general dash semantics and its Actual-column
  exception, and stops there. Without this, the conflation reads as a bug and invites a "fix" that would
  break the mirror/reload agreement.
- **target:** `DOMAIN.md` → *em-dash convention* (display semantics is that doc's charter).
- **why held:** `DOMAIN.md` fails the M9 content test. It is **41.9 KB** (42,878 bytes) — crossed 40 KB today
  (34.8 → 40.5 → 41.9 over three days) — and of 453 body lines exactly **82** are **domain conventions**
  (*What TSM is for* 54 + *TS authoring conventions* 28) against **~371** of UI/display material (grid idiom,
  columns, sorting, visual language, alignment, editing, chrome, WinUI gotchas, the add-a-UI-element
  checklist) — ~354 if the *TS sync* display section is read as domain rather than UI. The router already
  describes it as two charters in one sentence. That is the same *charter, not size* test that justified the
  `SUBSYSTEMS.md` carve-out — a split job first, not more content.
- **disposition of source:** `keep` — the NOTEBOOK entry is currently the only home; it must not be pruned
  until this lands.
- **split note:** **8 section-scoped cross-refs in the reference tier** must move with the seam —
  ARCHITECTURE → *Chrome* · CONVENTIONS → *WinUI gotchas* and → *Editing* · ROADMAP → *Editing* ·
  SUBSYSTEMS → *WinUI gotchas* · TS-SCHEMA → *TS authoring conventions* · VERIFICATION → *Chrome* ×2 —
  plus 3 in `NOTEBOOK.md`. A further 4 mentions (CLAUDE ×1, CONVENTIONS ×2, VERIFICATION ×1) name the doc
  generically with no section pointer and need no change. *(First count here said "~13 … CONVENTIONS ×4,
  VERIFICATION ×3" — it counted those generic mentions as movable work. Caught by this sweep's verification
  worker; corrected.)*

## Applied

- **`NOTEBOOK.md` 2026-07-29 overlap-fraction traps → `stub` (M5).** The guidance had been lifted to
  `ARCHITECTURE.md` (no-binning + cos(dec) derivation) and `DOMAIN.md` → *What TSM is for* (the compressed
  range, in the detects-not-enforces bullet) at ship time; the entry restated it. Restated guidance replaced by a pointer up; **every
  measurement kept** (2.40 µm @ 5496×3672 vs 4.80 µm @ 2744×1836, the 15.8% bin-2 share, Dec +69.13° /
  cos 0.355 / ~2.8×, the 52/60 ≥ 99.5% distribution, the 57.5–91.9% stray span). Verified by diffing every
  numeric token before and after: nothing lost but the three figures that appeared only inside the deleted
  guidance sentences.
- **`NOTEBOOK.md` 2026-07-29 rotation spike → `stub` (M5).** Same shape: the rules (fold-180 grouping, the
  5°/0.5° constants, mechanical never converted) live in `ARCHITECTURE.md` + the `framing-keys` spec. Kept
  the ≥ 9° separation, ≤ 0.2° jitter, ≤ 0.12° flip-centroid coincidence, 25/30 stability, 19–35° remount
  drift, and the full six-target census.
- **Cross-repo correction (`..\Library\CONSUMERS.md:114`).** Listed `OffsetNormalized` among the accessors
  TSM's scanner uses — that member was **deleted 2026-07-27** (XFM never divided; offset is raw on both
  planes), and the list was stale a second way after this week's framing work. Corrected to the real
  surface: `OffsetNormalized` removed, the six framing/geometry accessors added
  (`RotatorSkyAngleDeg`, `RotatorPosAngleDeg`, `PixelWidth`, `PixelHeight`, `FieldWidthDeg`,
  `FieldHeightDeg`), count `12 of 34` → `17 of 39`, with a dated note on the deletion. Found off-batch while
  verifying a worker's incidental observation.
- **`ROADMAP.md` Status refresh.** The heading was dated 2026-07-27 and the paragraph stopped at
  `rotation-framing-key`, omitting `framing-overlap-column` (shipped, verified and archived the same day).

## Adjudications that reversed a worker

Recorded because the reasoning is the reusable part:

1. **NOTEBOOK 2026-07-29 ×2 — worker said `archive`, adjudicated `stub`.** Both entries *are* duplicated in
   their archived `design.md`, but an archived design record is a per-change document that nothing routinely
   reads (that is precisely why M11 makes it a sweep *source*). Pruning the notebook would push the
   measurements somewhere no one greps. M5 is explicit: the data is not the guidance — lift the guidance,
   keep the data.
2. **NOTEBOOK 2026-07-23 alias removal — worker said `archive`, adjudicated `keep`.** The entry already
   carries its own `Closed 2026-07-23` annotation and points at the doctrine's homes. It is already a
   correctly-dispositioned stub; rewriting it would be churn, not clarity.
3. **NOTEBOOK 2026-07-07 exposure-0 — worker said `archive` (duplicate), adjudicated `keep` + a graduate the
   worker missed.** The headline rule is single-sourced, which is what the worker checked; the *deliberately
   left conflation* in the same entry is not, which makes the entry a single source rather than a duplicate.
   Became H2.
4. **Diagnostics rationale — worker targeted TSM `CLAUDE.md`, adjudicated portfolio-level.** A Library
   assembly-boundary rule does not belong in a consumer's router. Became H1 under M15.

## Code bugs (M12/M13)

**None found.** Six workers checked guarantee-language claims against current code; every one held:
`FramingCluster.OnFootprintFraction = 0.95` matches its spec threshold and test · `IsNonSiderealDirectory`
matches its documented trailing-space contract · `XisfHeader.OffsetNormalized` is genuinely absent ·
`TsInboundDiff` keys Project on `guid`, not `Id` (`TsInboundDiff.cs:110`) · `TsJournal.Append` uses
`File.AppendAllText` with no fsync, exactly matching the documented durability boundary · TSM touches no
`acquiredimage`/`imagedata`/`flathistory` table.

One dangling cross-reference was found and deliberately **not** actioned: both 2026-06-10 archive headers
promise a `ROADMAP.md → Carried forward` section that does not exist. The underlying issue (the
`CancellationToken` false affordance) shipped 2026-07-26 and is recorded in `CHANGELOG.md`; the headers are
immutable dated records, and the item sits in no live open list, so there is nothing to prune.

## Verification pass

A final adversarial worker audited every applied change against its sources — the standing lesson from
2026-07-26, where the same step caught an overstated citation. It did so again: **2 defects, both in this
sweep's own output**, both corrected above.

1. **An overstated pointer.** The stubbed NOTEBOOK entry pointed at `DOMAIN.md` → *Visual language* for the
   compressed-range rule; that content actually sits in *What TSM is for* (`DOMAIN.md:59-61`). *Visual
   language* holds the Hours-gauge rule and the fills — unrelated. A pointer that resolves to the wrong
   section is the exact failure a stub is supposed to prevent.
2. **An inflated count.** The split note claimed ~13 section-scoped cross-refs by counting generic
   charter-naming mentions as movable work, which would have made the split job look larger than it is.

It also independently re-verified and confirmed: no measurement was lost by either stub (numeric-token
diff against `HEAD`), the Library accessor correction (39 accessors, exactly those 17 used by Catalog and no
others, `OffsetNormalized` genuinely absent), the ROADMAP Status claims against `CHANGELOG.md`, H1's absence
from every `.md` in the portfolio, H2's presence in live code (`ReconciliationRow.cs:371-376` — a TS-plane
`PlanSeconds == 0` renders the em dash) and its absence from every reference doc and spec, and the
`schema-driven-field-editor/spec.md:51` citation.

## Accounting (M16)

**Prune / archive candidates found this sweep:** 4 — all in `NOTEBOOK.md` (2026-07-29 overlap traps,
2026-07-29 rotation spike, 2026-07-23 alias removal, 2026-07-07 exposure-0). Adjudicated: **2 stubbed**
(duplicated guidance struck, measurements kept), **2 kept** (one already self-annotated closed, one is a
single source the reference tier does not yet point into). No `docs/archive/` relocations were warranted —
every spent record was already moved there on 2026-07-26. No `prune-only` items: no closed item was found
sitting in a live open/pending list.

**Net reference-tier delta:** **+0 / −0 content.** No reference doc gained or lost a claim this sweep — both
graduates are held (H1 awaiting a placement decision, H2 awaiting the DOMAIN split). The run's net effect is
**−2 duplicated journal passages**, **1 corrected cross-repo API claim** (Library `CONSUMERS.md`), and **1
currency fix** (ROADMAP Status). The ratchet moved on the cut side only — which is the honest outcome when
the journal has already been graduated at ship time.
