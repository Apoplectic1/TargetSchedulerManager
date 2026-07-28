## Context

TSM serves two purposes: **modify the TS database on BIRDWATCHER** (primary) and **display the entire imaging history** from disk (secondary). The reconciliation currently joins the two planes on `(target, filter, purpose, seconds)`, which is finer than TS needs and coarser than the disk deserves.

Facts established by measuring the live library (18,904 light frames under `Captures/`, excluding `Captures/Calibration` and comets):

| measurement | result |
|---|---|
| frames carrying `GAIN` and `OFFSET` | **18,904 / 18,904** — zero contract violations |
| disk cells today `(target, filter, seconds)` | 471 |
| adding gain + offset | 542 (**+71, +15%**) |
| adding camera, telescope, or binning | **+0 each** |
| buckets that still pair with a plan | 297 of 542; ~245 become Disk-only |
| TS plans unique on `(target, filter, exposure)` | **650 of 650** — no collisions to resolve |
| distinct telescopes / TS profiles | 1 / 1 (`APM107R@531`) |

Two discontinuities explain most of the un-pairing, and both are genuine history rather than defects: broadband moved from **gain 53 to gain 0 in 2024** (narrowband settled on gain 111 in 2019 and never moved), and **offset-50 frames are scattered through every filter** while every template specifies offset 10.

Constraints carried in from the domain: a Target Scheduler profile does not fix a camera — cameras are exchanged between sessions under one profile — so `desired` is camera-agnostic and the user decides at the telescope which camera owes what. TSM must not invent that attribution.

## Goals / Non-Goals

**Goals:**
- Key the disk plane by everything that determines whether frames combine into one integration.
- Make a `Both` row mean exactly one thing: the captured frames match the plan on every key both planes express.
- Make the separation of TS and Disk rows *informative* — the visible answer to "why don't these numbers add up".
- Surface the capture configuration where it decides row identity, without displacing the columns already carrying the primary signal.

**Non-Goals:**
- Rotation as a key. It needs a circular tolerance, disk-side clustering (a problem directories have always solved instead), and a meridian-flip rule guarded by an RA/DEC centroid. Deferred deliberately.
- RA/DEC matching changes.
- Telescope as a UI section — deferred together with the disk directory-layout change a second telescope would bring.
- Comets — never scheduled in TS, captured manually.
- Any per-camera apportioning of a plan's `desired` count.
- Changing what the Hours or Remaining totals mean.

## Decisions

### Pair on shared keys; let each plane key itself
A dimension may take part in pairing only if **both planes express it**. Gain, offset and binning qualify — the disk records them and `exposuretemplate` carries them. Camera does not, so it is carried and displayed but never tested. The disk plane may therefore key more finely than the TS plane, and one plan may face several disk buckets. That is the correct outcome, not a defect to reconcile away.

*Alternative rejected:* keying the joined cell on camera as well. It would dissolve the `Both` row for targets whose plan cannot name a camera, replacing a real signal with a structural artefact — and measurement shows it separates **zero** buckets anyway.

### Camera comes from the directory, and the alias is presentation
The capture directory name is authoritative: it is known before any file is opened, and every frame beneath it belongs to that camera by construction. The alias mapping lives with the app's display conventions rather than in the shared library, keeping rig-specific vocabulary out of a multi-consumer surface.

*Consequence accepted:* two directory spellings of one camera would key separately and render identically. Unreachable today — the library contains exactly `Z183` and `Z533` — and the `camera` badge is the tripwire if that ever changes.

### Offset is read raw
XFM does **not** divide: its `Offset` setter writes the value unchanged and only the comment differs per camera, so the per-camera divisor text is descriptive. Disk offset and `exposuretemplate.offset` are therefore already the same scale, and `OffsetNormalized` — which reports 2 for a Z183 frame recording 10 — produces a number comparable to neither plane. It has exactly one production caller, so it goes.

*Alternative rejected:* keeping the method and gating it on the comment text. That is a defensive fallback papering over a question we have now answered.

### Splits are the product, not the cost
Roughly 245 of 542 buckets stop pairing. This is the change's value rather than its price: each separation states that captured history does not describe planned capture. The user reads the rollup for the Hours figure, then expands to learn why the counts disagree — the separation is that answer, one level earlier than it can be reached today.

### Sort ignores the new columns
The columns sit between Project and Filter, but sort precedence stays on filter, purpose and exposure before configuration. Sorting in column order would group every gain-53 row across all filters ahead of every gain-0 row, splitting one filter's story in two — precisely when the user is expanding to follow that story. The grid's "sort follows column order" convention gains one documented exception.

### Rollups say `mixed` rather than falling silent
Reusing the marker the Seconds column already uses lets a header report *which* dimension is inconsistent before it is expanded. Rendering blank would be indistinguishable from "nothing to say" at the moment the fact to convey is "these differ".

### Badges belong to rows, and rise
`camera` and `cam≠` describe particular frames, so they mark the row they describe plus its ancestors — the first row-scoped badges in a vocabulary that has been target-scoped. Existing target-scope badges are untouched. Severity classification widens only its wording: "repair outside TSM" already covered this, and now says so explicitly for disk-side repairs.

### Write-back is untouched
`WriteBackPlanner` keys on `(target, filter, purpose, seconds)` and **sums** inventory rows into it, so finer disk buckets still total the same `acquired`. Verified against the 650/650 result: there are no plan collisions for finer keys to resolve.

## Risks / Trade-offs

- **The grid looks fuller; a familiar target reads differently overnight.** → The change is explained by two datable events (the 2024 gain switch, the offset-50 frames) that the grid can now show. Rollups carry `mixed` so the reason is visible before expanding.
- **Horizontal budget.** Four columns cost 200 px, leaving the elastic Target column ≈210 px on a 1450 px window; long names such as "NGC 6976 - Pickering's Triangle" will ellipsize. → Accepted; widths live in one ruler, so tuning is a single edit.
- **Every `Grid.Column` index in the row templates shifts.** → The ruler makes a missing attribute fail loudly (all cells collapse into column 0) rather than as subtle misalignment.
- **Reading offset raw makes the pipeline depend on XFM having processed every frame.** A raw NINA file dropped into `Captures/` would key as a different offset from its siblings. → Report it; XFM's own comment marks processed files, so detection is free. Do not convert.
- **`OffsetNormalized` is public surface in a shared library.** → Its only production caller is the scanner; removal is verified across the portfolio.
- **Alias table is untestable against real data** — it is the identity function on every directory that exists. → Cover with synthetic fixtures.
- **Rotation is deferred, and the same frames carry rotation now.** → Deferral is explicit in the specs; the column mechanics and the pairing rule established here transfer to rotation unchanged, so it lands later as one more key rather than a fresh design.

## Migration Plan

No data migration. The catalog is derived and rebuildable, and no application reads `Catalog.db` — its persistence layer's only callers are library tests. A schema change means deleting the file.

Order of work: library first (scanner key, aggregate, schema, projection), then the app (ruler, pairing, rendering, badges), because the app's pairing decision consumes the projection's new fields. The library change is inert until the app reads them, so the two can land in one commit or two without an intermediate broken state.

## Open Questions

- Should `camera` join the search predicate only, or should gain and offset be searchable too? Current decision: camera only — numeric terms would collide with the count columns.
- Does the `cam≠` disagreement belong in the build report as well as on the row, so it surfaces in the Ambiguities report alongside other identity findings?
- When rotation lands, does it reuse the `mixed` rollup marker, or does a tolerance-clustered dimension need its own vocabulary?
