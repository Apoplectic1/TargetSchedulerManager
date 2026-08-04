# DOMAIN.md — TargetSchedulerManager

**Charter:** the domain-conventions home — what TSM is *for* and the TS authoring conventions that follow;
strategy/domain notes that fit neither `ARCHITECTURE.md` (how it works) nor `ROADMAP.md` (what's next).
The **UI design language** (grid look-and-feel, editing surfaces, chrome, WinUI gotchas, the add-a-UI-element
checklist) lives in **`UI.md`** (carved out 2026-08-03). **Current state only** — *how we got here* →
`ROADMAP.md`.

## What TSM is for (the two purposes)

TSM serves **two** purposes, and nearly every design question resolves by asking which one is in play:

1. **Modify the TS database on BIRDWATCHER** (primary) — the plan you will image against.
2. **Display the entire imaging history** (secondary) — what you actually captured, in full fidelity.

Four standing truths follow, and they settle most arguments about the grid:

- **Disk is actual history.** Self-describing, immutable, and *never validated against TS*. A frame captured at
  gain 53 in 2019 is not "failing to match" anything — it is simply what happened. The disk plane's job is
  fidelity, not agreement.
- **TS is the future.** An exposure template describes imaging *going forward*. When one happens to match disk
  files that is a coincidence worth showing, not a correctness condition. Nothing is "broken" when they differ.
- **A dimension can pair the two planes only if both express it.** The disk plane may key more finely than TS —
  that is a consequence of purpose 2, not a defect. Where the planes disagree the grid separates them, and the
  separation *is* the answer to "why don't these numbers add up".
- **`desired` is camera-agnostic.** One NINA profile is used with cameras exchanged between sessions, so TS
  cannot express which camera a plan is for. **TSM must never model camera↔plan attribution** — never apportion
  a desired count between cameras, never infer how many frames a given camera still owes. You decide that at the
  telescope, and automating it would hard-code complexity that is trivially resolved in the moment.

- **A 180° pier flip is the same framing.** A rectangle rotated 180° about the same center covers the
  identical footprint, so flip frames integrate perfectly — flips are routine acquisition events and must
  never split a row or read as a mismatch. That is why every rotation comparison in TSM is **fold-180**
  (measured 2026-07-29: every real flip pair's centroids coincide within 0.12°).
- **Mechanical rotator angle is never converted to a sky angle.** The mech-to-sky zero point shifts when the
  camera is remounted (measured drifting 19–35° across sessions, precisely on the multi-framing targets), so
  a conversion would silently mislabel the exact rows the framing key exists to expose. Mechanical rotation
  is shown marked (`°(M)`), clusters frames disk-side, and never enters the plan comparison.
- **TS's `acquired` counts only frames that serve the plan's framing** (user decision 2026-07-29, after the
  Tulip confusion: TS said 32/80 acquired while zero captured frames matched the re-framed 160° plan).
  Write-back credits by the same serving rule the pairing test uses, so a re-framed target stamps its true
  progress — possibly 0 — and TS schedules the full re-shoot. The grid's TS column and Actual column can
  therefore only diverge transiently (until the next load's write-back stamps); a persistent gap means the
  push hasn't run, not a contradiction.
- **TSM detects framing hazards; WBPP enforces; XFM neither.** A low-count off-footprint framing cluster is
  a PixInsight reference-frame hazard (a good stray reference makes ImageIntegration work a shrunken
  overlap). TSM's job ends at making it visible (the split row + `framing` badge); any grouping/exclusion
  rule belongs in the WBPP lane (PJSR reads `OBJCTROT`/`POSANGLE` itself — zero AL coupling), and XFM's
  grading role gives it no say at all.
- **The hazard is priced, still only detected** (2026-07-29, openspec `framing-overlap-column`). `framing 57%`
  on a stray line = the share of those frames' own footprint landing where the plan **currently** points —
  re-frame the target and the next load re-prices everything (nothing is stored). It prices *footprint*, not
  *stackability*: registration, star commonality, reference choice and mixed-rotation spikes are PixInsight's
  verdict — the number decides "worth carrying into WBPP to find out", nothing more, and it never scales a
  credited count. **Read it against its real range: 57–100%, not 0–100%** — two same-size fields centred
  together still share ~67% at a full 90° turn, so a "bad" framing reads ~60%, not ~5%; only a pointing
  clear of the field approaches zero.

(Contract detail: `openspec/specs/capture-config-keys` + `framing-keys`; telescope deferred — see
`ROADMAP.md`.)

## TS authoring conventions (user-side; decided 2026-07-08)

The charter behind all of them: **TS is a picker** — given a menu of targets and conditions, choose, order,
shoot; everything else in TS is noise here. So: TS's *membership* (which targets/projects exist) is the user's
planning intent — TSM never adds or removes it unasked; TS's *facts about members* (names, counts) mirror disk.

- **One TS row per sky position, no exceptions** (within the 0.5° match tolerance), spelled the same in TS
  and as the disk directory's catalog token (`IC 1795`, not `FishHead` — name validation is token-based;
  concatenations fail). There is **no alias escape**: the fold mechanism (deliberate second names for one
  object auto-resolving unflagged) was removed 2026-07-23 — its sole instance (M27 + Dumbell) was adjudicated
  2026-07-08 as never intentional ("explained ≠ approved"; NOTEBOOK correction), and any multi-claim now
  surfaces as a flagged duplicate to consolidate by hand.
- **Explained ≠ approved — a structurally odd state must surface, even when the numbers reconcile.** The
  standing lesson behind the alias removal, and a forward constraint on anything that resolves ambiguity: a
  mechanism must never quietly auto-fold a multi-claim just because the totals still add up. The fold read as
  benign for weeks and was in fact an unintended twin the user wanted raised
  (NOTEBOOK.md 2026-07-08 late: *"this was not intentional and should be brought to my attention"*). Surface
  it for the decision; don't decide for them.
- **One exposure plan per (filter, purpose, whole-second exposure) per target.** Same filter at *different*
  seconds is fine (different cells, auto-resolve); a same-key second plan makes disk-credit undecidable.
- **Never rely on a camera default for gain or offset — that sentinel is an incorrect state** (decided
  2026-08-04, openspec `pairing-credited-write-back`; framing refined same day). A template `gain` or
  `offset` of `-1` ("use the camera's default") is valid TS but marks a state this library's authoring
  convention forbids: those are fields the user decides explicitly (their sentinel is an affirmative act
  in TS's UI — a checkbox), they are pairing dimensions, and an unspecified value can never pair or
  credit, so such a plan's counts stamp 0 until fixed. TSM never auto-corrects a sentinel; it badges every
  row using the template (`sentinel`, warning tier) and the fix is a hand edit (TSM's template editor or
  NINA's TS UI). Sentinels that are a field's **designed representation of a correct state** are correct
  by construction — never badged, nothing to fix: template `readoutmode` `-1` (TS's blank "camera
  decides" box — never an authoring act, not a pairing dimension; measured: all 20 live templates carry
  it), plan `exposure` `-1` (use the template's default exposure), template `ditherevery` `-1` (defer to
  the project).
- Under these two conventions the write-back manual tray is provably empty; a non-zero tray means a convention
  slipped. **Fixes happen by hand in NINA's TS UI on BIRDWATCHER** — TSM surfaces ambiguities (report/badges);
  its one structural verb is the explicit per-row adoption (spec `disk-row-adoption`; the broader resolver
  was rejected 2026-07-08 — see `docs/2026-07-08-resolver-rejection-is-lane.md` for why). `desired` is
  user-owned planning intent — adoption seeds it once at creation (the disk count when the assigned
  template pairs with the cell, 0 under the non-pairing caution), but it is never
  *derived* from disk afterward and never edited without the user asking (TSM's grid does edit the value;
  see `UI.md` → *Editing*).
- TS's `acquiredimage`/`imagedata`/`flathistory` are disposable noise (grading lives in PixInsight; disk is the
  graded truth); TSM never reads or writes them.
- TS structure reference (tables, columns, identity semantics): **`TS-SCHEMA.md`**.
