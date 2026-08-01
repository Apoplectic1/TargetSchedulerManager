# 2026-07-08 — Resolver rejected; TS hygiene by hand; the IS lane opens

*(Glossary updated 2026-08-01: originally written with ISP = the future scheduler plugin; tokens rewritten to IS / ISM when the portfolio renamed, and the file renamed from `…-isp-lane.md`.)*

Decision record from a long explore session that started as "describe the write-back app action" and ended
somewhere much better. Captures the **why** — the decisions live in ROADMAP/DOMAIN/TS-SCHEMA; this is the
reasoning they compress.

> **Status as of 2026-07-26** (body below is untouched as-of-2026-07-08 history; decision numbering is cited
> cross-repo — do not renumber). Decisions **1, 2, 4, 5, 7, 8, 9 stand**. Decision **3** (printable ambiguity
> report) shipped 2026-07-08; decision **6** ("Tonight") shipped 2026-07-23. The *Immediate user actions*
> and the empirical-spine hand fixes (FishHead rename, plan 1040, Dumbell) were all completed in the
> 2026-07-23 BIRDWATCHER pass. One reversal: the spine's claim that **"M27/Dumbell is NOT held"** was
> overturned that same night ("explained ≠ approved" — `NOTEBOOK.md`, 2026-07-08 late) and the alias fold it
> rests on was removed 2026-07-23. The "disk-matcher lane" framing was cancelled 2026-07-24 — under the
> corrected model **TSM manages TS, period**; the authored intent store belongs to ISM.

## The empirical spine (what the data said)

- The write-back manual tray ("held cells") on real data = **7 cells, 2 root causes**:
  **6 = IC 1795** — disk dir `IC 1795 - Fish Head`, TS target `FishHead`; perfect coordinate match, token-based
  name validation fails ("FishHead" concatenated ≠ tokens {fish, head}); the whole target is identity-flagged so
  all 6 filter-cells are held and its frames are never credited to TS.
  **1 = Swan H@900s** — ONE target carrying TWO H900 plans (Id 299 desired 64; Id 1040 desired 1 — a stray).
- **M27/Dumbell is NOT held** — the alias fold (2 plans per cell = 2 alias members) auto-stamps both. The
  2026-07-08 adjudication holds.
- Both causes are fixable **by hand in NINA's TS UI in ~2 minutes** (rename FishHead → IC 1795; delete plan
  1040). Repairs are self-persisting: the next load's detector finds nothing. Expect `manual=0` in `tsm.log`.

## The taxonomy (why the problem decomposed)

Every ambiguity sorted into three kinds: **representation** (TSM folded rows TS keeps separate — twins,
multi-plan display), **join** (which TS row does this disk artifact belong to — name-mismatch, ambiguous
coords), **credit policy** (N rows claim one disk number). TS itself has none of these: it only ever references
its own rows by integer `Id` — the ambiguities are manufactured at the disk↔TS join, which is TSM's product.
Two conventions make the tray *provably empty*: (1) **one name per sky position** (within the 0.5° tolerance),
spelled the same on disk and in TS — aliases only via the adjudicated alias fold; (2) **one plan per
(filter, purpose, whole-second exposure) per target**. Both adopted → `DOMAIN.md`.

## Decisions (each with its why)

1. **Resolver edit-surface REJECTED.** A two-stage interactive resolver (TS hygiene consolidation → disk↔TS
   binding UI) was explored in depth and killed by its own numbers: caseload ≈ 2 items ever + rare future
   accidents; repairs self-persist so there is nothing to remember; hand-edits in NINA's TS UI outsource
   cascade/cadence/guid correctness to the editor that upstream maintains through every schema change; and TS is
   in its retirement lane (IS is the successor) — structural-edit machinery (insert/delete journal vocabulary,
   replay Id-remapping, structural push review) would be significant, churn-exposed code serving a system being
   abandoned. *Thinking-and-doing beats describing-a-code-update at this volume.*
2. **TSM keeps: the matcher, the count write-back, the existing editing surface.** The disk↔TS join is the
   product (and IS needs the identical join later); automatic acquired/accepted stamping + reviewed push is
   "genuinely useful" (user); the current field-editing surface is *enough*. `desired` maintenance moves to
   hand-edits on BIRDWATCHER too.
3. **NEXT TSM change — the printable ambiguity report.** A separate, detailed list of every TS + disk ambiguity
   (what, and *why* it's flagged, with enough context to act on) that the user can print and walk to BIRDWATCHER.
   The detection layer already exists (`CatalogBuildReport` issues + `WriteBackPlan.Manual` + ReconcileNotes);
   this is surfacing, not analysis. Include TS-internal checks the grid can't badge today (same-key plans,
   planned-only twins, duplicate template names). This *is* the "write-back app action" now — a tripwire, not a
   dialog. No persistence/adjudication store unless a permanent exception someday exists (consolidating the
   Dumbell twin by hand would leave zero).
4. **The inversion (strategic): intent's permanent home is a user-owned store; TS becomes a projection.**
   Today TS holds intent (desired, membership) and Catalog.db was planned as a *derived* reconciliation. The
   vision: an **authored** plan store (working name Catalog.db) holds intent + identity; TS is a disposable
   execution cache the picker runs against; IS later reads the store natively (TSM mode-flip; ISM is
   "TSM but for IS"). Ambiguity then dies *by construction* — TS can't contain twins unless the projector
   writes them. **The real price: derived → authored.** An authored store is the one file that can't be
   casually deleted (rule-15 delete-and-rebuild stops applying to it); while TS exists intent can be re-lifted
   after schema churn, post-IS the store is the sole holder. Open fork, leaning plan-only: **plan-db + fresh
   scan join** (mirrors TSM's proven architecture; trivial single-writer; actuals never persisted/stale) vs the
   originally-documented **union db**. The old guardrail "don't design the IS-optimal schema until IS has real
   needs" updates to: *the intent layer's needs have now arrived (from TSM's own transition work); scoring/
   conditions still wait for IS.*
5. **TS ⇄ IS migration is a named requirement.** **Lift** (TS → store): read-only, the reader already reads
   everything intent-shaped; scope = the fields the user actually uses (project names literally encode
   min-altitude policy). **Back-projection** (store → TS, "just in case"): bulk **regeneration of a fresh
   scheduler.db** (carry profile linkage + templates), never in-place surgery — the insurance that imaging can
   fall back to TS mid-transition. Testable invariant: `lift(project(store)) == store`.
6. **"Tonight" (roadmap item, parked).** Goal: make BIRDWATCHER TS usage trivial — only tonight's viable
   targets in play. The populated-"Tonight"-project shape was rejected (copying targets = institutionalized
   duplicate rows — the disease the conventions cure; moving them strips home-project policy). Adopted shape:
   **the sophisticated enable** — TSM computes the visible set (above the user's `.hrz` horizon at min altitude
   for min duration; `Astronomy.Core` + TP's competence entering TSM) and sweeps `target.active`: pure UPDATEs,
   journaled, one push. This is proto-IS: our selection logic, TS reduced to executing flags.
7. **Disk-dir promotion (deferred discussion).** ~33 actual-only targets could be *promoted* to TS/store
   targets; the disk plate-solved centroid becomes the authored coordinate at promotion. Membership stays the
   user's planning intent — never auto-created.
8. **Structural-push identity note (for whenever structure IS written).** Integer `Id` is per-copy (replay
   assigns new); `guid` is the cross-copy name — mint locally, carry through replay. Push-ends-in-pull (already
   shipped) collapses Id divergence immediately; TS need not be running for Id assignment. The one un-guardable
   window: NINA holding planner state in memory between cycles — structural pushes stay a rig-idle operation
   (existing open-sidecar refusal + day/night discipline).
9. **acquiredimage / imagedata / flathistory are noise here** — grading happens in PixInsight; disk is the
   graded truth; the user hand-purges TS image history. TSM never touches these tables.

## Also produced this session

- **`TS-SCHEMA.md`** (new reference doc): exhaustive TS schema (`user_version 28`), hierarchy + vocabulary,
  two-name identity system, per-table TSM usage, drift-check recipe. Genesis: the user's from-memory description
  of TS was ~90% right — the doc exists so neither of us works from memory again.
- **DOMAIN.md**: TS authoring conventions section (the picker charter + the two conventions + hand-edit rule).
- Parent CLAUDE/ROADMAP + ISM ROADMAP: ISM redefined ("TSM but for IS"); Catalog.db direction annotated;
  portfolio status refreshed (was pre-sync-model stale).

## Immediate user actions (outside TSM)

Rename `FishHead` → `IC 1795`, delete Swan plan 1040, optionally consolidate the Dumbell twin (deleting it —
consolidation-is-deletion — `desired` is the one field to eyeball first: intent doesn't self-heal, counts do).
Then a TSM load should log `write-back: … 0 manual` and the grid's `name≠`/`multi-plan` badges disappear.
