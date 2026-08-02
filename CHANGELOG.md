# TargetSchedulerManager (TSM) — Changelog

**Charter:** the shipped-history journal — every completed unit of work (`▶ SHIPPED` / `▶ DECIDED` / dated
built-narrative), **newest first**, append-only. Read it for *what shipped and when*. `ROADMAP.md` keeps only the
forward plan + current status and points here; git remains the commit-level backstop. New entries go **at the top**.

> **Reading dated entries:** "Pending / Awaiting / Next" language inside an entry records the state *as of
> that date*. Every such pass or queue was subsequently completed — each later entry built on verified prior
> work — and explicit **Closed** notes are added only where the resolution isn't recorded by a later entry
> (sweep 2026-07-24, resolving docs-audit flags #7/18/20/21).

---

**▶ SHIPPED 2026-08-02 — window title is name + version, nothing else** (user obs 8573 + follow-up):
`Target Scheduler Manager 1.1.0` — MinVer's informational version cut at `+sha`; an F5 build keeps its
`-alpha…` shape, so the title is also the at-a-glance dev-vs-installed disambiguator. The former
`— local TS copy · push to BIRDWATCHER` suffix is gone by user call (the sync badge + tooltip already
carry the sync model). Same log read
confirmed the whole 2026-08-02 arc live in one session: installed **1.1.0** running, `update check: up to
date`, `RecordedAt refreshed` on the skipped pull, badge `synced 26/08/02 01:59 PM`.

**▶ SHIPPED 2026-08-02 — `velopack-self-update`: TSM ships as a self-updating Velopack installer**
(openspec change, spec `self-update` — 15th capability; plan user-approved with two decisions:
startup-only check surface, first tag `v1.1.0`). TP's proven model with the two WinUI deltas:
**owned entry point** (`DISABLE_XAML_GENERATED_MAIN` + `Program.cs` — `VelopackApp.Build().Run()`
must precede everything, even `Log.Init`, to service Setup.exe/Update.exe hook relaunches; trap
hit: `Application.Start(_ => …)` binds `_` as the callback *parameter*, so `_ = new App()`
assigned to it and broke MarkupCompilePass2 — 15 phantom XAML errors from one CS0029) and a
**ContentDialog prompt** (`Services/UpdateService.cs`: `IsInstalled` guard = dev no-op, silent-
catch with log trail, `GithubSource` public/no-token/`prerelease:false` + MinVer alpha-shaping =
dev builds can never roll out). `scripts/release.ps1` packs the **publish** output (unpackaged
self-contained WinUI needs the runtimes in the payload — TP packs bare build output; TSM can't).
MinVer stamps from the same `vX.Y.Z` tags that gate `main` pushes. Docs: RELEASING.md distribution
flips local-build (AL ships as DLLs, source stays unpublished), README Install section,
VERIFICATION release recipe. Tests 324/324 via slnx. **Closed same day:** dry-run install verified,
`v1.1.0` then `v1.1.1` released (vpk CLI + library both 1.2.0 for the latter), and the installed
1.1.0 → prompt → accept → restart-as-1.1.1 hop **user-verified** — every part of the mechanism proven.

**▶ SHIPPED 2026-08-02 — first publish: README + MIT LICENSE + `v1.0.0` to the GitHub mirror.** The
storefront README (what/grid screenshot/status/repo-layout/license — user-approved) plus MIT `LICENSE`
(© Skyhawk Consulting, Inc.), and the tag rule joined `RELEASING.md`: **`main` only pushes with a
`vX.Y.Z` tag** (XFM's form; no tag → no push — the tag is what makes a `main` state published). Grid
screenshot committed at `docs/images/main-window.png`. First push: `main` @ `v1.0.0`.

**▶ DECIDED 2026-08-02 — GitHub mirror + publish rules (`RELEASING.md`).** The user created
`github.com/Apoplectic1/TargetSchedulerManager` (public, empty) as TSM's public face — **local repo stays
ground truth**; GitHub is distribution. `RELEASING.md` (new, router-listed) carries the rules: `dev` =
working and never pushes, publish = ff `main` → `git push origin main` at natural completion points;
source-only distribution (no installers/tags); README-as-storefront with a repo-layout paragraph for the
visible workshop dirs; site coordinates + machine paths in `DevDefaults.cs` deliberately public (TP's
precedent). Known gap at decision time: no `README.md` yet, and a public clone can't build — the three
`ProjectReference`s point into the unpublished sibling Library repo (publish-Library fork open).

**▶ SHIPPED 2026-08-02 — sync badge: "synced" now means *last proven in sync*, full-stamp format** (user
obs 0cf4). The badge's time was the baseline's `RecordedAt` — the last physical pull — so an open whose
verified skip proved the local copy current still showed a days-old stamp ("synced Wed 21:31" after a
Sunday open). Since the skip rule proves local == remote exactly as strongly as a pull would, the verified
skip now re-records the baseline with `RecordedAt = now` (remote size/mtime — the comparison key — stay
untouched, so a skip can never turn into a false pull or vice versa). `Format.When` becomes the full
`yy/MM/dd hh:mm AM|PM` stamp always (invariant culture; the bare-HH:mm-today / ddd-otherwise compaction
died — a day-name is ambiguous past a week). One live caller (the badge), so no collateral. Spec
`ts-sync-model` pins the proven-in-sync semantics. 324/324 App.Tests (+2: skip-refresh, When format).

 `TreatWarningsAsErrors` on both projects.** Portfolio-wide
follow-on to AL's same-day sweep (45 xUnit1051s had accumulated silently in its test bench precisely because
warnings break nothing). Both TSM projects were already clean — verified by forced non-incremental rebuilds
in **Debug and Release** before the switch went on — so this locks in the existing state; the next compiler
or analyzer warning is a build break. Note the ratchet also arrives transitively: AL's projects carry it too,
so an AL warning now fails TSM's build (it will *look* like a TSM build error — check which project the
message names). 322/322 App.Tests green under enforcement.

**▶ SHIPPED 2026-07-29 — Hours becomes a progress gauge** (user obs 01b7 + in-chat decisions; rides the open
`framing-overlap-column` change as group 4c, `reconciliation-grid` delta). Replaces the signed-sum model
(every row a signed contribution, parents the literal sum) with: while any plan beneath a level still owes
images, show the **remaining time** (negative, caution); once nothing is owed — goals met or no goals at all
— show the **captured disk total** (green, unsigned; the `+` surplus prefix died — a positive value is a
total). "Owed" is **acquired-based** (desired − TS acquired, clamped per plan cell), which makes the gauge
framing-aware: M81-R's "full" disk of 132 frames with only 46 serving reads **−7.2 brown**, where the old
disk−desired gap read 0.0 green ("met") — the exact blind spot the user spotted. Debt **survives disable**
(Visible-Tonight flips `target.active` nightly; progress must not churn with the sky). Deepest lines state
plain facts (Disk = total; TS = owed, dash once complete; desired-0 keeps its critical tripwire). The
"Sort: remaining ↓" key moved to the same acquired basis so sort and gauge never disagree. Parents are
deliberately no longer the sum of their children. Tests: App 322.

**▶ SHIPPED 2026-07-29 — the framing badge prices itself: `framing 57%`** (openspec `framing-overlap-column`;
library `f7f5f8e`, app with this entry). The `rotation-framing-key` deferral said the overlap-% needed "pixel
dimensions nothing exposes" — thin: the **mandatory XISF `<Image geometry>` attribute** covers 100.0% of the
18,650-frame library; `XisfHeaderReader` simply never read it (it harvested only `<FITSKeyword>`). Now it
does, `XisfHeader` derives the angular field (`206.265 × XPIXSZ / FOCALLEN` — **no binning factor**: `XPIXSZ`
is already binning-adjusted; a `× XBINNING` would double the field for the 15.8% of the library shot bin 2),
`Astronomy.Core.FieldFootprint` clips rotated rectangles on a tangent plane (**RA scaled by cos(dec)** — at
M81's Dec +69° the error would be ~2.8×), and each framing cluster carries its footprint (dominant sensor
when frames span two, never a blend). The fraction = share of a cluster's **own** footprint landing inside
the plan's, both rectangles the cluster's own sensor — so no neighbouring framing's camera can move a number.
It prices **off-footprint for any reason**: a disagreeing cluster always reports (a stray just past tolerance
still overlaps ~95% — silencing it would blank the badge's row); a serving cluster reports only below
`OnFootprintFraction` 0.95 (measured: 52/60 serving framings ≥ 99.5%; the 3 genuinely displaced ones —
Markarian's Chain 86%, FishHead 88%, M51 94% — were invisible before). **Surface decision at
implementation** (user, after measurement): ~14 populated rows library-wide in a compressed **57–100%**
range (two centred same-size fields share ~67% even at 90°) is badge-sized, not column-sized — the planned
58 px column + 1560→1640 widen died; the number rides the badge **on the deepest visible line only**
(render-layer decoration; `Badge` strings stay bare for search/flagging/headers), no-badge facts go to the
ambiguity report (off-plan pointings that serve; the mixed-sensor qualifier), and a spec claim died honestly
(differently-shaped sensors do NOT give identical fractions for identical displacements — the true property
is each-against-its-own-sensor). **Crediting untouched** — write-back stays boolean `ServesPlanRotation`; a
partially overlapping frame is not a fractional frame; the boundary is now explicit in `framing-keys`.
**Bonus (4a):** `ImageLibraryReport.SkippedFiles` was written and never read — an unreadable frame silently
lowered Actual counts; now the load status line counts them (silent at 0) and the ambiguity report lists
path + reason as action items ("Fix on disk — unreadable files"), with a missing-geometry frame landing in
that same corrupt-file category. Tests: Catalog 236 · XISF 51 · Core 472 · NINA 45 · Contracts 61 · App 320.

**▶ SHIPPED 2026-07-29 — framing keys the disk plane, and only a serving framing credits a plan** (openspec
`rotation-framing-key`; library `9e6d893` + `6057cc7`, app `30bed1b`…`a66e492`). `capture-config-keys` deferred
rotation and RA/DEC as two separate open questions. They were **one concept**: a **framing** is a
(field-center, sky-rotation) pair — the thing that decides whether frames share a footprint and can integrate
together. Until this change the grid folded two framings of a target into one row, so a **re-framed target read
as already satisfied**: its plan stayed credited with frames pointing somewhere else, and the scheduler
consuming `acquired` under-scheduled the re-shoot.

A 2026-07-29 measurement spike over the live library (**18,650 frames, 104 units**) dissolved every open
question the deferral had recorded. Real framings sit **≥ 9° apart** fold-180 while within-framing jitter is
**≤ 0.2°** (NINA snaps the rotator), so *any* tolerance in 1–5° yields identical clusters — it stopped being a
judgement call. The old "M33/M51 sit on the 5° boundary" worry **did not reproduce** under fold-180: they
measure 0.56° and 0.10°. Every true meridian-flip pair's centroids coincide within **0.12°**.

`FramingClusterer` partitions a unit's frames by rotation **expression** first — sky (`OBJCTROT`, 71.9% of
frames), mechanical-only (`POSANGLE`, 28.1%), unknown (3 frames) — then single-linkage clusters the angle
**folded mod 180°** and splits each angle group by field center. The fold makes a **pier flip merge** (a
rectangle rotated 180° about its center covers the identical footprint — a routine acquisition event, not a
different framing), while the centroid guard keeps 180°-apart fields with genuinely different centers apart,
and catches translated strays at an unchanged angle. **A single stray frame IS a cluster** — low-count
off-footprint framings are exactly the PixInsight reference-frame hazard this exists to surface. The cluster
then joins the aggregate key beside gain/offset/binning, and pairs fold-180 against the TS target's rotation.

**Mechanical rotation is never converted to sky.** The spike found the sky−mech zero point stable mod-180
within a session block but **drifting 19–35° across remounts** in 5 of 30 (unit, camera) groups — and those
groups are precisely the multi-framing targets the key exists to expose, so a conversion would mislabel the
rows that matter most. A mechanical-only cluster is displayed marked as mechanical and, like camera, simply
does not participate in the pairing test.

Two consequences beyond the key. **Write-back** (`6057cc7`) keeps its coarser `(target, filter, purpose,
seconds)` key but now sums **only rows whose framing serves the target's rotation**, so a re-framed plan stamps
its true progress; the surgical single-target path surfaces a withheld cell as a `FramingMismatch` note rather
than skipping it silently. **The grid** gained a centered `Rot` column (excluded from sorting, like the other
capture-configuration columns) and a warning-severity row-scoped `framing` badge — which forced the
generalization that a **row-scoped badge displays at the deepest visible level**: on the target summary always,
on a collapsed rollup that hides the triggering line, and on the triggering line itself once expanded, never
both. That rule now governs `camera`/`cam≠` identically rather than being a framing special case, and a
related pass made **sentinels render as their meaning everywhere**, including inside old→new displays.

Adds **`framing-keys`** as the 14th capability spec; `capture-config-keys`, `image-library-scan` and
`write-back` took deltas. **Deferred, deliberately:** the **overlap-% column** (footprint intersection of a
stray framing against the plan framing — the number that *prices* the hazard) needs image pixel dimensions
`XisfHeader` does not expose; it lands later as a pure column addition. TSM's job ends at making the hazard
visible — WBPP enforces, XFM neither.

**▶ SHIPPED 2026-07-27 — non-sidereal targets never enter the library scan** (openspec `skip-comet-targets`;
library `ddada41`, app `6295579`). Comets are captured by hand with their own setup and are **never scheduled in
TS** — the live database contains no comet target at all — so the four `Comet …` directories produced Disk-only
rows that reconciled against nothing. Worse, their capture trees nest **date-named session folders where filter
directories belong**, so the filter parser took the session name verbatim: the grid published
`2024-10-18 - Track Comet` as a **filter code**, a vocabulary no other target shares.

`ImageLibraryScanner.IsNonSiderealDirectory` now skips them at the walk, beside the existing `Calibration` skip —
*a directory that is never walked, not a result filtered afterwards.* The predicate is the `"Comet "` prefix
(**the trailing space is load-bearing**: it excludes a sidereal target that merely contains the word), and the
guard sits in `ScanTargetAsync`, which both entry points (`ScanAsync`, `ScanUnitsAsync`) already funnel through,
so neither can bypass it. Removes one target of 84 and **254 of 18,904** light frames (1.3%); the other three
comet directories hold no lights. TSM itself changed nothing — the grid simply stops receiving the rows.

The change's second half is documentary: the new **`image-library-scan`** spec (TSM's 13th) states what the scan
reads and, decisively, what it never reads. The **calibration skip had existed since the scanner was written but
was never written down** — it gets its first contract, and its first tests, here.

**▶ SHIPPED 2026-07-27 — the capture configuration keys the disk plane** (openspec `capture-config-keys`;
library `53f6c49`, app `c53e655`). The grid reconciled disk against TS on `(target, filter, purpose, seconds)`,
so frames captured at **different gain, offset or binning folded into one row and read as one stack** — when they
will not stack at all. The library's own history is the proof: broadband moved from **gain 53 to gain 0 in 2024**,
and **offset-50 frames are scattered through every filter** while every TS template specifies offset 10. A target
showed one tidy `Both` row asserting that all of it counted toward the plan.

**Gain, offset and binning became reconciliation keys; camera became a disk-side label.** The dividing rule is
that a dimension may pair only if **both planes express it** — the disk records camera, but a TS profile does not
fix one (cameras are exchanged between sessions under a single profile), so camera is carried and displayed and
never tested. The pairing rule is now explicit: a row is **`Both` if and only if the disk bucket matches the plan
on every key both planes express**; otherwise it renders as a TS row plus one or more Disk rows. **The separation
*is* the diagnostic** — the visible answer to "why don't these numbers add up", one expand-level earlier than it
could be reached before.

Measured against the live library first (18,904 lights): `GAIN` and `OFFSET` present on **18,904 / 18,904** — zero
contract violations; disk cells **471 → 542** (+15%) from gain + offset, while camera, telescope and binning each
split **zero** buckets today; **650 of 650** TS plans already unique on `(target, filter, exposure)`, so no plan
collisions needed resolving. Roughly **245 of 542 buckets stop pairing** — the change's value, not its price.

Four columns (Camera · Gain · Offset · Bin) sit between Project and Filter, and are **deliberately excluded from
sort** — the grid's "sort follows column order" convention gains its one documented exception, because sorting in
column order would group every gain-53 row across all filters ahead of every gain-0 row, splitting one filter's
story exactly when the user expanded to follow it. Rollups render the value when children agree and a `mixed`
caution pill when they don't, reusing the Seconds column's idiom. Two new badges — `camera` (a directory name
resolving to no known camera) and `cam≠` (the directory disagreeing with the file's own `INSTRUME`) — are the
vocabulary's **first row-scoped tokens**: they mark the rows drawing on those frames plus their ancestors, never
unaffected siblings.

**`XisfHeader.OffsetNormalized` was deleted** (BREAKING, shared-library public surface). XFM never divided — its
`Offset` setter writes the value unchanged and only the per-camera *comment* differs — so disk offset and
`exposuretemplate.offset` were always the same scale, and `OffsetNormalized` reported **2 for a Z183 frame
recording 10**, a number comparable to neither plane. Its one production caller was the scanner. Gating it on the
comment text was rejected: a defensive fallback papering over a question now answered.

`WriteBackPlanner` is **verified unchanged** — it keys coarser and *sums* inventory rows, so finer disk buckets
still total the same `acquired`. Hours and Remaining are likewise identical: `RowAggregates` sums components
rather than per-row gaps. Standing truths graduated to `DOMAIN.md` ("What TSM is for", the capture-configuration
block and its sort exception) and `ARCHITECTURE.md` (the capture configuration as the cell key). **Deferred
deliberately** — rotation as a key (needs a circular tolerance, disk-side clustering with no precedent, and a
meridian-flip rule guarded by an RA/DEC centroid), RA/DEC refinements, and telescope as its own UI section
(100% uniform today; design it *with* the disk layout change a second scope would bring). Reasons in `ROADMAP.md`.

**▶ SHIPPED 2026-07-26 (second sweep) — graduate-and-prune over the openspec archive: 12 truths lifted from
shipped rationale** — the morning sweep (`feb8f96`) mined the *dated* journal; this one mined the class it
left untouched, **27 archived `openspec/changes/archive/*/design.md` records**. That class is a genuine
blind spot: shipped rationale concentrates there and nothing ever reads the archive again, so a decision's
"why" and its rejected alternatives quietly stop being reachable the moment the change is archived. Six
fan-out rounds; the archived records themselves were never touched (immutable change records — every
graduate is a one-line claim plus a pointer written into the reference doc).

**`CONVENTIONS.md` took seven of the twelve** (6.2 → 11.4 KB — the doc carved this morning turning out to be
exactly the missing home the sweep predicted): the **App-project-root** siting rule for statics XAML reaches
directly (`GridColumns`, `ThemeBrushes` — the unqualified `xmlns:local` lookup is why, decided twice);
**shared controls take identity as delegates, never as a key** (`TsFieldsEditor` serves four flyouts and has
never learned a TS table — commit, effective-value seed, and mark lookup all arrive closed over the key);
**an untestable surface gets a pure sibling** (`Badges.Split`, `AmbiguityReport.Build`, `SyncMarks`,
`VisibleTonightPass.Plan*`) **plus its exception** — `SentinelCell` keeps its logic because the state *is*
the controls, so extraction would relocate code without buying a test; **serialize with UI-thread-confined
state, not an async lock** (`SemaphoreSlim` rejected independently by both changes that needed it);
**edit-vs-edit ordering belongs to the surface, not the gate** (why `CommitChain` is per-surface, and the
three alternatives that were rejected); **named arguments at every same-typed parameter-object call site**;
and a new **"One helper, two null postures"** section.

That last one is the sweep's own find rather than any single record's: `BaselineMatches` and
`TsValueText.From` are the *same shape* — one helper, consumers that legitimately want **opposite** answers
for the absent case, and a guard that must stay at the consumer. Folding it inward looks like removing a
duplicate and puts a false "remote changed" warning on every first-ever push. Both were already commented at
the enforcement point; the doc now names the pattern so the third instance gets recognized.

**`ARCHITECTURE.md`** gained three clauses, all re-proposal guards: the push-review staleness warning's
**null asymmetry** against the pull-skip rule; **why `ApplyManyAsync` exists** at all (the gate would make a
per-edit loop equally atomic — the batch is there to collapse N connection-opens and N `Task.Run` hops);
and the **rejected reorder** of `CommitPush` after the closing pull, which reads as the safer ordering and
reopens the mirror-image lie. **`DOMAIN.md`** gained **edit-only — never structural** (declared a Non-Goal
independently by two of the three 2026-07-06 flyout efforts; `ruleWeights` out for the same reason — the
sweep's first draft claimed all three and the verification pass caught it) and **"explained ≠
approved"**, graduated from NOTEBOOK as a forward constraint on any future ambiguity-resolving mechanism
rather than a closed alias-fold anecdote.

**Corrections + prune:** `45acbff` renamed the button but left `ARCHITECTURE.md`'s busy-exclusion
parenthetical and the **live** `openspec/specs/busy-exclusion` contract saying "Cancel pull" — both now say
Cancel and describe the phase-scoped behavior. ROADMAP's "add TSM to TP's glossary" was struck: **it had
already been done** in TP's own rename commit and never reported back — the cross-repo shape where a chore
closed in one repo leaves another repo's list asserting it open indefinitely. Status re-dated to 07-26 with
the day's units.

**Method note, since the yield curve is the interesting part:** rounds returned 5 · 6 · 1 · 2 · 2 · —, and
the dedicated dry-check came back empty in round 3 while a *completeness critic* kept finding material. The
diagnosis was batch imbalance, not exhaustion: round 4 gave one worker 16 records and two workers 6 and 5;
the small batches came back empty and the overloaded one found two. Re-running that same set split four ways
found **two more the overloaded worker had skimmed**. Reading density, not lens choice, was the binding
constraint — worth remembering for the next sweep. Round 6 closed it out: both lens workers returned empty
(dry), and a **verification worker auditing the applied diff against the source** caught one overstated
citation, which is the round that earned its keep.

**And then the split the sweep argued for — `SUBSYSTEMS.md`, a fifth reference doc.** The sweep had added to
`ARCHITECTURE.md` three times against its own "split before you stuff" rule, so the question went to the
author, who took the full split. The four feature sections moved **verbatim** (sync model 8.9 KB ·
sync-direction marks 5.1 KB · write-back 5.7 KB · visible-tonight 3.7 KB): **`ARCHITECTURE.md` 38.6 → 16.1 KB**,
`SUBSYSTEMS.md` 25.4 KB. The justification is charter, not size — "how is the system designed and what must
stay true" is a different question from "how does this subsystem behave", and the second was 61 % of a file
whose charter claims the first. Each moved section already had a parallel formal contract under
`openspec/specs/`, so both files now table that pairing explicitly. `ARCHITECTURE.md` → *Key facts* keeps the
one-line invariant mirrors and ends with a routing table; six live cross-refs re-pointed (CLAUDE router,
DOMAIN ×2, ROADMAP ×3) and five in-file "see below" forward refs rewritten. The archived records under
`docs/archive/` and `openspec/changes/archive/` keep their historical `ARCHITECTURE.md` wording on purpose.

The move surfaced a **pre-existing mutual-pointer loop**: the write-back section said "full spec in
`ROADMAP.md` Phase 4" while Phase 4 said the spec was single-sourced in the section pointing at it. Neither
doc owned it. Now the section names `openspec/specs/write-back/` as the formal contract and says plainly that
Phase 4 is the plan entry — a defect that had survived every prior audit because each half looked correct
from the other side.

---

**▶ SHIPPED 2026-07-26 — the toolbar Cancel now covers the whole load, phase by phase** — spending the
capability the token-threading landed the same day. `Cancel pull` becomes **`Cancel`**, visible while *any*
cancellable phase runs (`IsPulling` → `IsCancellable`, `CancelPull()` → `CancelLoad()`), and `LoadAsync` opens
a cancel scope around the scan/resolve it previously ran unguarded.

**The design decision worth recording: the scope is PER PHASE, not one token for the whole load** — and that
was a course correction found mid-implementation. Wrapping the entire load in one token reads as the obvious
shape, but it would have silently broken a deliberate invariant: cancelling a *pull* does **not** abort a
load. It falls through with an explanatory note and reads the intact local copy, which is exactly what the
2026-07-24 pull-first discard rule depends on ("a cancelled discard-pull changes NOTHING — journal, baseline,
badge and marks stay intact"). A shared token would have poisoned the following scan and converted that into
an aborted load, with no test to catch it. So each phase owns its scope; the phases are sequential, so one
button still covers them all — it cancels whichever is in flight. The reasoning is now a comment on
`WithCancelUiAsync`, where the next person to "simplify" it will read it.

Cancelling the scan/resolve is **not** a failure: a new `catch (OperationCanceledException)` ahead of the
general catch keeps the rows the grid was already showing and reports `load cancelled — showing the previous
scan` (plain `load cancelled` on a first-ever load), instead of the general catch's `load failed:` + blanked
`_allRows`. Write-back stays uncancellable by design — it writes, and a half-applied stamp pass is worse than
waiting out a fast one (the same rule the push replay follows).

**Verification gap, stated rather than papered over:** cancelling mid-scan needs a race the suite has no seam
for, so this is *not* unit-tested; `VERIFICATION.md` carries the hands-on recipe instead and says so. 279 tests
still green (no regression), but the new behavior is author-verified only.

---

**▶ SHIPPED 2026-07-26 — `CONVENTIONS.md`: a fourth reference doc for *how code is written and where it goes***
— placing the one standing truth the graduate-and-prune sweep held rather than graduated. The **code-siting
doctrine** (asserted in the 2026-06-10 reviews, independently re-confirmed 2026-07-24) had no home: it is not
architecture, not UI design language, and both natural targets were already at the size where the sweep rule
says *split before you stuff*. It now leads a doc of its own — one plausible home per kind of change (as a
table of the real folder map, verified against the tree), invariants written at the point that enforces them
(with the maintenance duty that makes them trustworthy — the `TsInboundDiff` wrong-key-space comment is the
cautionary case), and every major flow a single forward pass with no back-edges. Also gathered there: the
**view/VM seam** (code-behind forwards only · zero `Microsoft.UI.*` in `MainViewModel` · `x:Bind` only, no
classic `{Binding}` · the `TextChanged` exception and why), the **`FireAndLog` async-void invariant**, the
`_camelCase` field convention (99 fields, zero ported Hungarian), and a note on why `TargetResolver.Resolve`
stays one ~310-line method — **28 locals cross its phase boundaries**, so extraction would trade a readable
pipeline for the parameter explosion the 2026-07-24 review had to fix elsewhere (M3 → `row-param-objects`);
if it ever splits, the unit is a phase *object* that owns the state, not a static helper that receives it.
Router updated; `DOMAIN.md` checklist step 9 shrinks to a pointer (the seam rules were never UI design
language). Global rules — no-back-compat, fail-fast — are deliberately *not* restated; they stay in
`~/.claude/CLAUDE.md`. **Effect on the binding constraint:** DOMAIN 29.8 → 29.1 KB — modest, because the point
was never bulk relief but giving code conventions a *non-bloated* home to graduate into next sweep.

---

**▶ SHIPPED 2026-07-26 — the load path's `CancellationToken` is no longer a false affordance** (paired
Library + app change) — closing the last open finding from the 2026-06-10 review, raised again in round 2,
deferred with the M2 work and never re-raised. `ReconciliationLoader.ResolveAsync` passed `ct` only to
`Task.Run(…, ct)`, which cancels *scheduling*: once the body started, cancelling did nothing, because neither
`TargetSchedulerReader.ReadPlanData()` nor `TargetResolver.Resolve(...)` accepted a token. (The disk half,
`ScanLibraryAsync`, had always threaded it — that asymmetry was the tell.) Library: `ReadPlanData` and the five
`Read*` methods take an optional token and forward it to the private `Query`, the **one read choke point**, which
now checks per row (a cancel mid-read releases the connection promptly rather than at end-of-table);
`TargetResolver.Resolve` takes one and checks at each of its phase boundaries plus **per TS target** in the
anchoring pass — the one super-linear loop. App: `ResolveAsync` forwards its token to both. `CatalogBuilder.BuildAsync`
was fixed by the same change — it already accepted a token and had the identical gap, and its doc now says
"observed throughout" instead of "during the disk scan". Guarded by `Resolve_ObservesCancellation`.
**No behavior change today:** the view-model calls the loader with no token at all, so this is contract honesty
plus capability — a Cancel-load button is now UI work, not plumbing (noted in ROADMAP). Library 172 tests, app 279.

---

**▶ SHIPPED 2026-07-26 — `TsInboundDiff` keyed projects by `Id`; project `←` marks silently never resolved**
— found by the docs graduate-and-prune sweep while graduating the journal/mark key spaces into `TS-SCHEMA.md`
(the doc claim was written wrong first, and checking it against code surfaced the bug). Project journal and
mark keys are the TS **guid** — `TargetResolver.Provenance` returns `tsGuid ?? Id`, and NINA mints a guid in
the project ctor, so in practice always the guid — but `TsInboundDiff.FieldSet` selected `Id` as the project
key column. Inbound project changes were therefore stored under an `Id`-string and looked up by guid
(`MarkResolver.ForField` ← `MainWindow.Flyouts.cs`): a **silent miss**, never a throw. Outbound `→` was
unaffected (journal and lookup both use the guid) and project *editing* worked either way, because
`TargetSchedulerEditor` resolves guid-or-Id — which is why the defect survived unnoticed. Fix: `FieldSet`'s
Project entry → `"guid"` (its in-code comment had asserted the wrong space too). **Why no test caught it:**
`TsInboundDiffTests`' fixture db had no `guid` column on `project` and had *no project diff test at all*,
while `SyncMarksTests` used a `"7"` project fixture — both encoded the same wrong assumption. Added
`PullDiff_ProjectChange_IsKeyedByGuid_NotId` (verified failing against the old code before the fix), gave the
fixture its `guid` column, and moved the marks fixture to a guid key. 279 tests green.

---

**▶ SHIPPED 2026-07-26 — Template `ditherevery` gains its −1 sentinel ("project default")** — found by
side-by-side comparison against TS 5.10.3's own UI (which shows "(Project)" where TSM showed a raw −1).
In TS, template `DitherEvery = −1` defers to the project's dither setting (`DitherManager` tests `>= 0`;
verified in the released `v5.10.3.0` tag) — the same defer-to-default shape as gain/offset/readout's
camera sentinels, but TSM's editable schema had it as a plain `Min 0` Whole: it displayed the raw −1 and,
once edited, could never write −1 back (a one-way door out of "inherit from project"). One-line fix in
`Astronomy.Catalog`'s `TsEditableSchema` (Library repo, same-day commit): `Sentinel: −1,
SentinelLabel: "project default"` — the flyout's existing sentinel rendering (checkbox over number box,
arm-before-write, clamp exemption) lights up for free. No spec change: the sentinel-rendering requirement
is generic; which columns carry sentinels is schema data. Comparison also confirmed **no 5.10.3 schema
drift** (PRAGMA column list == TS-SCHEMA.md) and full moon-field coverage. 171 Library + 278 TSM tests.

**▶ SHIPPED 2026-07-26 — Net-no-op pruning: reverting a field to its baseline clears it everywhere**
(openspec `noop-edit-pruning`; user observed via the new flyout marks that a toggle round-trip kept its
`→`, chose option 1 — and "apply this anywhere syncmarks are used"). Root cause was one layer: the editor
already short-circuits same-value db writes as verified-without-writing, but the journal appended every
verified write and `Collapse()` never asked whether first-Old == last-Value — so a round-trip marked all
surfaces, counted "unpushed", showed "On → On" in the push review, and replayed a no-op to BIRDWATCHER
(which, for cadence-clearing fields like plan `enabled`, cleared remote filter-cadence rows for nothing).
**Mechanism — prune at the producer, not filter at the consumers:** `TsJournal.Append` (now
`TsJournalEntry?`) remembers each field's **baseline** — the first journaled Old since the last push (the
user-identified state to remember) in a map beside the badge's field-key set (same lock, same rebuild
sites via a shared `RebuildIndexesLocked`) — and a write whose canonical value text equals the baseline
prunes the field's entries (crash-safe rewrite; sidecar deleted when the journal empties, so no phantom
dirty-open prompt) and returns null; a first-touch same-value commit journals nothing. Invariant: *the
journal never holds a net-no-op field* — grid/header/picker/flyout marks, the unpushed count, push
review/replay, and the dirty-open prompt all heal with zero per-surface code. Equality is the one
invariant text rule both sides already share (`TsValueText` ≡ the editor's `ToText`; `Canonicalize` folds
300.0→300 so whole-valued doubles compare true); mismatch fails safe to retention. **The user's gotcha
holds:** inbound is a separate store, so a field carrying `←` before the edits reads `←` again after the
revert — never blank. Push retention resets baselines (pushed value = next baseline). 278 tests (269 + 9:
seven journal pruning units incl. baseline-is-first-old, push-reset, reload-persistence,
whole-double equality; two SyncMarks surface/inbound-survival units). Visual verification pending
(toggle round-trip clears flyout + grid marks live).

**▶ SHIPPED 2026-07-26 — Per-field sync marks inside the edit flyouts** (openspec `flyout-field-marks`;
user request same day, follow-on to `template-change-marks`). Every field row in the four schema-generated
flyouts (target / project / exposure plan / template) now carries a leading `←`/`→`/`⇄` mark — blank when
clean, fixed-width slot so labels stay aligned — with the field's old→new lines as tooltip. **The
per-field `⇄` is the point:** an unpushed local write and a rig-side change colliding on *that exact
field* (the row-level `⇄` could only say "somewhere on this row"). **Mechanism:** new
`SyncMarks.ForField(table, key, column)` (unattributed — the flyout names the entity; the inbound new-row
fact is row-scoped and never surfaces per field); `TsFieldsEditor` gains an optional **batched**
`MarkResolver` delegate (`columns → per-column (glyph, tooltip)`, same seam style as
`CommitField`/`EffectiveValue` — one `BuildMarks()` per refresh pass, not one per column) and a
`RefreshMarks()` pass run at construction and in the `CommitChain` continuation after **every** commit
(verified, refused, failed) — so toggling a field flips its `→` on live, and a refused commit shows the
true facts. No resolver injected → no mark column (the editor stays sync-agnostic). The hand-built
mosaic flyout wires the same marks: master enable = union over the panels' `target.active` field states
with per-panel tooltip lines (a fan-out control carries a fan-in mark), priority =
`ForField(Project, key, "priority")`. 269 tests (264 + 5 `ForField` units: out/in/exact-field-collision
with sibling isolation/clean-blank/new-row-excluded). Layout half is XAML-runtime — visual verification
pending (mark column + alignment, live `→` on commit, `⇄` collision, mosaic rows).

**▶ SHIPPED 2026-07-26 — Template + project changes now visible in the direction marks: template edits
light every using plan row, headers attribute their own-scope fields** (openspec `template-change-marks`;
USER_OBS d14e — moon avoidance enabled on 'H900'/49 plans showed "2 unpushed" but no `→` anywhere).
**Reverses the 2026-07-08 carve-out** ("exposure-template edits mark no row"). Root causes were two
distinct gaps: the marks sweep never queried the `ExposureTemplate` journal key space, and the pull diff's
`FieldSet` omitted `exposuretemplate` entirely — a rig-side template change could never produce `←` (while
a plan *reassigned* to another template could, via the plan's diffed `exposureTemplateId`). **Mechanism:**
`SyncMarks.Build` now takes the retained `CatalogGraph` (was: plans only) and derives plan→template-key,
target→template-keys, and key→display-name maps — template key space = integer `Id` string
(`TargetResolver` provenance), matching journal + inbound. `ForPlan` unions the plan's entries with its
template's; inherited tooltip lines are attributed (`→ unpushed — template 'H900': moonavoidanceenabled
0 → 1`) so they read as inherited, not row edits. A header counts a pending (template, field) **once**,
however many of its plans share the template; a zero-use template marks no row but shows its mark + tooltip
in the Templates… picker (new `ForTemplate`, resolved at picker open via `MainViewModel.BuildMarks`).
Inbound: `exposuretemplate` joined the diff `FieldSet` keyed by `Id` with columns **derived from
`TsEditableSchema`** (18 editable fields, no literal copy — coverage can't drift from the flyout).
**Second user decision (same session): project flyout changes stay header-only but gain attribution** —
header tooltips now render own-scope target/project fields as attributed old→new lines (`→ unpushed —
project 'Nebulae - Above 45': minimumaltitude 30 → 45`) and keep counts only for rolled-up plan/template
fields, i.e. detail lives where the mark is authoritative. 264 tests (252 + 12: template inheritance/
attribution/once-per-header/zero-use/`ForTemplate` in `SyncMarksTests`, template pull-diff old→new +
untouched-records-nothing in `TsInboundDiffTests`, an in-place sweep integration in
`MainViewModelMarkTests`). Visual verification pending (row arrows on a template edit, picker marks,
header attribution tooltips).

**▶ SHIPPED 2026-07-26 — Badges column reads by severity: per-token two-tier colour, and `no-coords`
promoted to a genuine flag** (openspec `badge-severity-color`). The column painted **every** token
`SystemFillColorCautionBrush` (hard-coded in all three row templates), so `mosaic` — a neutral structural
fact — shouted exactly as loud as `duplicate`. Now **warning** (caution foreground) =
`duplicate · name≠ · ambiguous · multi-plan · acc≠acq · no-coords`, **informative** (dimmed
`TextFillColorSecondaryBrush`) = `mosaic · no data`, resolved **per token** so `mosaic · multi-plan` shows
one of each. **Mechanism:** a new `Controls/BadgeRuns` attached property fills one `TextBlock`'s `Inlines`
with a coloured `Run` per token — chosen over a `StackPanel`/`ItemsRepeater` of TextBlocks because a panel
can't ellipsis-trim, and a TextBlock trims across inlines so the 150 px column needed no thought. The
informative tier uses a dimmed **brush**, not the grid's usual `Opacity="0.7"`: a `Run` is a `TextElement`
and **`TextElement` exposes no `Opacity`** (dimming the parent would have muted the amber too). Green was
rejected for the quiet tier — it already means "goal met" in the Hours/Seconds fills, and one colour
shouldn't carry two meanings grid-wide. **New `Models/Badges.cs`** takes ownership of the vocabulary: the
eight token consts (previously inline literals in the loader), the `" · "` separator (previously duplicated
in the loader and `RowAggregates`), the severity predicate, and the pure `Split`/`Join` the renderer walks —
so the classification is unit-testable with no XAML runtime, and `BadgeRuns` holds no logic. **Behaviour
change worth knowing:** `no-coords` (a TS target with null RA/Dec — unschedulable by TS, can never accrue
disk credit) now sets `IsFlagged`, so **flagged-only counts can rise** on a database carrying
coordinate-less TS targets. That's deliberate: colouring it amber while leaving it unflagged would have let
the flagged-only filter *hide* a row just painted as a warning. `no data` (valid coords, no plans, no frames)
stays informative and unflagged — queued work, not breakage. **Bonus fix:** the header badge rollup deduped
whole child *strings*, so a mosaic with one multi-plan filter rendered `mosaic · mosaic · multi-plan`; it now
dedupes tokens, making DOMAIN.md's long-standing "distinct union" description true. 252 tests (235 + 17: a
`BadgesTests` severity/round-trip suite, two `BuildRowsTests` flag assertions + an unanchored-with-a-plan
case, two header-rollup cases). Badge *text* is byte-identical, so the search vocabulary is untouched.
DOMAIN.md's Badges bullet + checklist steps 3-4 rewritten. **Author's visual check pending:** per-token
colours across all three row kinds, and whether the secondary brush reads as "quiet fact" rather than
"disabled".

**▶ SHIPPED 2026-07-26 — Visible-Tonight toolbar: up-downs right-sized, **Horizon** knob deep-renamed
to **Floor**, and the button's `Find` → `Tonight` drift corrected in the docs** (openspec
`toolbar-floor-knob`; driven by observations `obs-9b52` → `obs-d589` → `obs-1fe4`). **Sizing — final
shape: the knobs are `Controls/UpDownBox`, a new ~100-line app-local WinForms-style NumericUpDown**
(TextBox + stacked chevron `RepeatButton`s; integer `Value` clamped to `Minimum`/`Maximum`, chevrons and
↑/↓ step by `SmallChange` committing typed text first, focus-loss/Enter commits, unparseable input
reverts — the old `InvalidInputOverwritten` contract, hand-rolled). Duration `Width="60"`, Floor
`Width="52"`, vs ~110 px stock. The route mattered more than the destination: three visual passes failed
trying to narrow the stock inline `NumberBox`, each against a different hard-coded template width —
**120** (forced input `MinWidth`; why a bare `Width` never sticks), **76** (the chevron pair; `obs-d589`
caught 80/70 clipping the up button), **72** (`SpinButtonsColumn`, a constant reserved for text in the
*inner* TextBox template — so shrinking the actual buttons reclaims nothing, and `obs-1fe4`'s
centered-then-vanishing digits fell out of it). Verdict, now a DOMAIN rule: **a narrow inline NumberBox
is unreachable in WinUI 3** — WinUI ships no NumericUpDown, `NumberBox` assumes it is wide, and app
resources can't shadow its template constants (a `StaticResource` inside a framework `ControlTemplate`
resolves in `generic.xaml`). `NarrowNumberBox_Loaded` (the generalized `DesiredBox_Loaded`) survives for
the hidden-spinner grid cells only: center digits, zero the inner `MinWidth`. The user flagged the
accumulating template surgery as overly complicated mid-saga — correctly; `UpDownBox` replaced all of it
with code that owns its layout. `LargeChange` (PageUp/PageDown) not carried over. **Rename:** `VisibleHorizon` → `VisibleFloor`,
label `"Horizon:"` → `"Floor:"`, `horizonAltitudeDeg` → `floorAltitudeDeg` through
`RunVisibleTonightAsync` / `VisibleTonightPass.PlanTargets` / all call sites and test arguments, test
`HorizonAltitudeFloor_GatesLowTargets` → `AltitudeFloor_GatesLowTargets`, plus tooltips and comments.
Motive: "Horizon" collided with two unrelated horizons in the same files, and *floor* is what the
implementation always called it (`ScalarHorizonProfile altitudeFloor`, the spec's "altitude floor").
**Deliberately not renamed** — TS's `usecustomhorizon` / `horizonoffset` columns (external contract) and
`Astronomy.Core`'s `Horizons` namespace / `ScalarHorizonProfile` / `IsAboveHorizonForAtLeast` plus
geometric-horizon prose (shared library, different concept); zero `..\Library` edits. **Drift fix:** the
button's label had been changed `Find` → `Tonight` in the working tree and never committed, leaving eight
stale "Find" references — corrected in the `visible-tonight-toggle` + `busy-exclusion` specs and
ARCHITECTURE/DOMAIN/VERIFICATION (user confirmed keeping "Tonight"). DOMAIN's integer-edit-box convention
now carries both cases (hidden spinners ~40 px vs inline spinners = digits + ~56 px) and names the shared
handler in WinUI-gotchas + checklist step 6. No behavior change: predicate, ranges, defaults, busy
exclusion, and journaling untouched. Verified: build clean, App.Tests 235/235; **visual pass pending the
author's run** (box widths, `480`/`89` fully visible, both chevrons present and clear of the digits).

**▶ SHIPPED 2026-07-24 — DiagnosticsWindow thins to the WinUI shell over the Library's new
`ObservationSession` (AL Diagnostics consolidation; this window was the model the type was lifted
from).** The observation orchestration — id minting, START/CAP/END/CANCEL sequencing with the
single-terminator guarantee, capture counting, status wording, the hide → 450 ms settle → grab →
reshow choreography, the 5 s delayed capture, and the guarded context-provider call — now lives in
`Astronomy.Diagnostics.ObservationSession` (AL commit `731a245`, contract assumption #25). The window
keeps only framework glue: the `Window`/controls, the `_current` singleton (focus-existing, no second
START), Ctrl+Enter commit, `CenterOverOwner`, and the three delegates (owner `AppWindow` bounds /
`Hide` / `Show`+`Activate`+focus-notes). `Closed` now just calls the idempotent `_session.Cancel()` —
the `_terminationLogged` flag is gone; OK honors `CompleteAsync`'s false return (capture in flight →
stay open). Net ~−60 lines; zero behavior change intended. Also fixed the stale header claim that
TP's dialog "shoots only at OK" (TP has had a repeatable Capture button all along). Verified: TSM sln
builds, App.Tests 235/235; visual pass 2026-07-24 (session id=8296: open → OK, END line + ctx +
screenshot on disk, one terminator, build stamp = this commit). The capture-button paths were
exercised in TP's pass of the same shared session type (TP session id=d162: instant + delayed
captures both clean); the TSM-side ghost check remains covered by the 450 ms settle default.


**▶ DECIDED 2026-07-24 — disk-matcher design lane CANCELLED.** The lane (a phrase from the 2026-07-08
resolver-rejection entry, never defined further) assumed TSM would bridge TS ↔ IS's `Catalog.db`; under
the corrected model TSM manages TS, period — IS is its own project, and merging TS's targets into it is a
future, separate effort. No live deficiency exists (matching is validated, ambiguity report zero), the join's
semantics already live in the shared `Astronomy.Catalog` (available to any future consumer with that effort's
real requirements in hand — the "don't design for IS until IS has real needs" guardrail), and the orphaned
sub-items were never TSM's: disk-dir promotion + the TS→store lift belong to the future ISM/IS efforts
(recorded in `docs/2026-07-08-resolver-rejection-is-lane.md`, decisions 5 + 7); the rig key remains
extend-when-it-lands. Commit `78ced80`.

---

**▶ DECIDED 2026-07-24 — docs-audit flags #7/18/20/21 RESOLVED; "post-BIRDWATCHER refresh" retired.**
The phrase (from the 2026-07-10 audit commit `977b259`) never meant a machine event — it meant "fix these
four stale-forward-pointer flags during the ROADMAP tidy after the user's BIRDWATCHER hand-fix pass." That
pass completed 2026-07-23 (Swan · Rosette · Dumbell, done by hand), satisfying the deferral; the phrase then
decayed into looking like a rig rebuild we were waiting on. Resolved by the 2026-07-24 CHANGELOG sweep: a
charter reading-note (dated "Pending/Awaiting" language records state as-of-date) plus explicit
**Closed/Superseded** annotations on the six entries whose resolution no later entry recorded. Commit `9e5d84b`.

---

**▶ SHIPPED 2026-07-24 — visible-tonight applied-state derivation
(`openspec/changes/visible-tonight-applied-states`; review m5 un-parked by user — the last parked review
item; slate now fully terminal).** Project `state` flips now derive from the target flips that
**landed**, not the intended set: `VisibleTonightPass.Plan` split into `PlanTargets` (verdicts + target
edits; RA/Dec fail-fast unchanged) and `PlanProjects(ts, appliedTargetEdits)` (pure derivation —
applied-edit overlay on the snapshot by `EditKey`; a refused/failed flip contributes the target's OLD
value). `RunVisibleTonightAsync` applies two sequenced `ApplyManyAsync` batches (targets → recompute →
projects) under one unbroken busy scope — the seam between them admits no bulk op and no row edit —
failure counts sum across both, and the closing reload now runs **only when a flip actually landed** (an
all-refused pass changed nothing; also lets the VM-level test avoid a real disk scan). Free win: a whole
target batch failing (editor can't open) now derives projects against unchanged states — zero orphaned
flips by construction, not by batch luck. Spec: both `visible-tonight-toggle` requirements modified
(applied-derivation + two-sequenced-batches), 4 new scenarios. Tests 230→235: matrix retargeted to the
two-stage API (all-applied overlay reproduces the old combined behavior), 4 new derivation tests
(failed enable / failed disable / partial landing / zero-target-edit project flip), VM wiring test
(refused flip ⇒ empty journal, "0 project(s) flipped", no reload). Happy path byte-identical —
auto-archived per standing rule. Context: the meridian-flip question that triggered the m5 sanity check
also pinned a spec non-goal (`e98127a`): pier-flip downtime is TS/NINA's runtime concern, never modeled
in the visibility predicate.**

---

**▶ SHIPPED 2026-07-24 — presentation conventions (`openspec/changes/presentation-conventions`;
P3+P4+P5 folded per user call — the presentation lane's close-out).** **P3:** `Models/Format.cs` is the
display-convention home — `Dash`/`CountOrDash` (the em-dash empty convention, 15 sites across 5 files
routed), `When` (was the VM's private `FormatWhen`), `Cell` (was `AmbiguityReport`'s private; note
`FilterPurpose` lives in `Astronomy.Catalog.Scan`), `Label` (the `·` identity convention, 9 sites —
output byte-identical: labels persist in the journal, shape is contract). **P4:** `TsFieldsEditor.
MakeNumberBox` + `UnitLabel` (each config existed twice: plain + sentinel). **P5:** `ThemeBrushes`
promoted to the app root namespace (enclosing-namespace lookup keeps row-model references compiling) +
`CautionText` (foreground caution); the two raw `Application.Current.Resources` casts route through it,
adopting the defensive null-on-missing posture. Spec delta codifies the dash/zero/hours conventions on
`reconciliation-grid`; DOMAIN checklist step 2 rewritten with the homes. 230 App.Tests (one transient
1/230 flake, name uncaptured, 4× green after — NOTEBOOK entry). Auto-archived (test-locked conventions;
config moves byte-identical).

**▶ SHIPPED 2026-07-24 — grid column ruler (`openspec/changes/grid-column-ruler`; presentation P1, the
consultation's highest-leverage item).** The 14-column geometry (`24,36,110,*,170,60,70,80,88,60,60,60,
45,150`) existed as four byte-identical XAML blocks (3 row templates + the hand-rolled header) kept
aligned by hand. Now `GridColumns.cs` is the ONE named ruler — a `(name, width)` table doubling as the
grid's column documentation — and all four grids stamp their `ColumnDefinitions` from it via the
`local:GridColumns.ApplyRuler="True"` attached property (chosen over width resources: `StaticResource`
into `GridLength` is unreliable in the UWP lineage; the callback stamps at parse, before children/layout,
once per Grid instance — recycled containers keep theirs). Scope honesty: cell `Grid.Column` indexes stay
per-template (DataTemplates can't share cells) — a new column starts at the ruler, then places cells.
A forgotten attribute fails loudly (cells collapse into column 0), never as subtle misalignment. New
`reconciliation-grid` capability seeds the codified alignment invariant. DOMAIN's add-a-UI-element
checklist step 1 rewritten. 230 App.Tests = regression floor; XAML rendering has no test net — **the
user's visual pass GATES archive** (alignment across row kinds, star-column resize, hover/Desired/Hours
pill unchanged).

**▶ SHIPPED 2026-07-24 — MainWindow partial split (presentation prep P1 of the post-review consultation;
plain commit per the M4 precedent — flyout triggers/anchoring/gestures are fully specced in
`target-and-plan-flyouts`, so a pure file reorganization has no honest delta).** The 589-line code-behind
grab-bag → three partials, members verbatim: **core** (ctor, toolbar/grid handlers, view fix-ups, 174) ·
**`MainWindow.Flyouts.cs`** (edit triggers, row context menu, Templates… picker, mosaic + schema-driven
flyouts, commit routing, 308) · **`MainWindow.Dialogs.cs`** (open-with-dirty + push review ContentDialogs
+ shared review body, 132). Chosen first from the presentation-readiness consultation because upcoming
work is flyout-heavy — "open the flyout file" now beats "scroll the grab-bag". The class doc maps the
layout. 230 App.Tests green unchanged; XAML handler wiring compile-verified by the XamlCompiler.
**Remaining consultation items (proposed, not started):** P1 grid column-ruler single-sourcing (the
14-column definitions exist in 4 verbatim copies — needs a mechanism spike + visual pass), P3 display-
convention home (Format consolidation), P4 TsFieldsEditor NumberBox/unit-label factory, P5 brush/theme
single home, then the DOMAIN.md "add a UI element" checklist refresh.

**▶ SHIPPED 2026-07-24 — await-friendly probe (review N8, the FINAL review item, unparked to finish the
cycle).** `TsDatabaseResolver.StatAsync` — the same stat, the same abandoned-worker hard-timeout semantics
(a hung SMB call on a down host is abandoned and completes harmlessly later), but via `Task.WaitAsync`:
no thread parks for the wait. `TsSync.ProbeRemoteAsync` wraps it, and the view-model's three probe sites
drop their `Task.Run(Sync.ProbeRemote)` — with a free correctness dividend: the UI-thread continuation
now writes `LastProbe`/`HasProbed` on the same thread that reads them for the badge, retiring the
re-check's by-convention concurrency note #2 for the VM paths. The sync `Stat`/`ProbeRemote` pair stays
for the push replay (which runs wholly on a worker by design) and shares one `StartProbe` worker. 230
App.Tests (3 new `StatAsync` mirrors). Plain commit (probe plumbing isn't a specced capability).
**Every item from the 2026-07-24 review cycle now terminates in shipped or declined-with-reasons —
nothing remains parked.**

**▶ SHIPPED 2026-07-24 — inline-edit owner map (review N6, unparked on the user's call — the image
library will definitely keep growing, so the trigger is a when, not an if).** `RecomputeOwners` was a
groups × children scan per committed inline Desired/exposure edit; now `ApplyFilters` (which touches
every row anyway) maintains `_ownerByRow` — leaf → (group, panel?) — and re-aggregation is O(1). The
panel refinement rides the same map (panel children are group children; the second pass refines their
entry), and a rollup's detail lines are deliberately absent — the same no-op the old scan produced.
Behavior-preserving; new test pins the panel path (`SetPlanDesired_OnPanelLeaf_RecomputesPanelAndGroup`).
227 App.Tests (1 new). Plain commit (in-place re-aggregation is already specced via the in-place-mirror
requirement; the map is implementation). N8 is now the sole parked-on-evidence review item.

**▶ SHIPPED 2026-07-24 — async-void elimination (N3 completed to uniformity; the re-check's "micro-hole",
scope corrected upward twice).** The re-check counted five plain `async void` handlers; the sweep found
nine: three code-behind (`TargetEnable_Click`, `PlanEnable_Click`, `Desired_Committed`), six editor sites
(toggle/number/combo/text + both sentinel handlers), and three more in `DiagnosticsWindow` the re-check
missed entirely (capture / delayed capture / OK). All now route through `FireAndLog`, promoted from
`MainWindow` to `Shared/UiTask` (`using static` keeps call sites unchanged). Why beyond uniformity: an
exception escaping `async void` crashes the app with nothing in tsm.log, and the prior safety ("everything
they await self-handles") was a non-local invariant nothing enforced — now the guarantee is structural at
every handler. **The invariant is grep-clean: the only `async void` in the app is `UiTask.FireAndLog`
itself.** Success paths byte-identical; plain commit (fail-loud plumbing isn't a specced capability);
226 App.Tests green unchanged.

**▶ SHIPPED 2026-07-24 — ambiguity-report section builders (review N9, the last surviving review item;
plain commit like M4 — the report's sections are already fully specced in `ts-ambiguity-report`, so a
pure reorganization has no honest delta).** `AmbiguityReport.Build`'s ~170 inline lines → five section
builders (`BuildIdentitySection` / `BuildDuplicateSection` / `BuildPlanSection` / `BuildTemplateSection`
/ `BuildInfoSection`), bodies verbatim; `Build` now reads as the report's table of contents (context
setup → five calls → count → assembly), and each check is individually testable without composing the
whole markdown. 226 App.Tests green unchanged (`AmbiguityReportTests` = the lock). **This closes the
2026-07-24 review cycle completely — every item from both review docs is now shipped, declined with
recorded reasons, or parked on evidence (N6/N8).**

**▶ SHIPPED 2026-07-24 — sentinel cell extraction (`openspec/changes/sentinel-cell`; M7's deferred half —
the re-check's "one maintainability item of any substance left"; done warm on user call, the file having
been touched twice the same day).** `TsFieldsEditor.BuildSentinelNumber`'s ~100 lines — three event
lambdas sharing mutated state through closure captures (the `effective` local written by one handler,
read by the others; compound failure restores) — became a private nested `SentinelCell`: captures →
fields, lambdas → named rules (`OnUseDefaultCheckedAsync` / `OnUseDefaultUnchecked` /
`OnValueConfirmedAsync`), bodies verbatim; the builder is a one-line delegation. Spec delta codifies the
interaction contract (rendered as meaning never raw −1; checked ⇔ column holds sentinel; unchecking only
ARMS — no silent write; sentinel exempt from the clamp; failure restores compound state) on
`schema-driven-field-editor`. 226 tests = the regression floor only — the cell is WinUI control code with
no test net before or after, so the user's Exposure-flyout pass gates archive (deliberately NOT
auto-archived despite being a pure refactor).

**▶ SHIPPED 2026-07-24 — view-model partial split (review M4; plain commit, no openspec change —
the spec-driven schema requires a requirements delta and a pure file reorganization has none to offer
honestly).** `MainViewModel.cs` (1082 lines, six concerns by gravity) → four partial files, members moved
verbatim: **core** (state, busy exclusion, filter/group pipeline, 370) · **`.Sync.cs`** (load/pull/push
commands, pull UI, badge, 304) · **`.Edits.cs`** (the Set*Async funnel, outcome mapping, marks sweep, 262)
· **`.Reports.cs`** (ambiguity tripwire, templates picker, visible-tonight, 189). One type — fields visible
across parts, zero behavior change, no binding churn; the class doc maps the layout. "Fix the push flow"
now loads a 300-line file instead of a 1100-line one. 226 App.Tests green unchanged.

**▶ SHIPPED 2026-07-24 — review polish (`openspec/changes/review-polish`; the review's remaining accepted
small items, one sweep).** M2 became a DOC fix (the review's `Flush(true)` was rejected — the SQLite commit
and the journal append are separate durability events, so no flush closes the power-loss window; `TsJournal`
docs + the spec now state the honest boundary: survives process crashes, an OS/power failure can lose the
tail line whose local write persists but whose replay is lost). N2: `TsJournal.CollapsedCount` cached under
the journal lock — `SyncBadgeText` stops running `Collapse()` on the UI thread per raise. M7: one
`ClampToSchema` (was verbatim ×2) + the flyout's column-routing lambda became the named
`TryCommitMirroredField` switch. N1 hoisted search needle · N3 `FireAndLog` on all ten `_ =` discards
(unexpected UI faults now land in tsm.log) · N4 `TsValueText.From` (one conversion rule; each display keeps
its own null spelling deliberately) · N5 `MaxBusyRetries = BusyTimeoutMs/RetrySleepMs` + cancel-aware nap ·
N7 `DiagnosticsWindow` `m`/`s` prefixes → `_camelCase` + the duplicated `Row_ItemClick` comment removed ·
N10 primary ctors (`TsEditGate`, `VisibleRowTree`; `SyncMarks` kept its private ctor) + the deliberate
no-`ConfigureAwait` note · blind-m1 `GetMosaicEnabledState` reuses `EffectiveEnabled`. **Deliberately
skipped, recorded in the proposal:** N6 (scale-fine), N8 (by design), N9 (mildest — pure + reads well), m5
(visible-tonight planned-vs-applied project-flip edge, parked), m6 (cross-repo haversine — Library-side),
m7 (debounce — first knob if the library grows). 226 App.Tests (1 new). Auto-archived (doc/refactor sweep).

**▶ SHIPPED 2026-07-24 — serial commits (`openspec/changes/serial-commits`; the review cross-check's last
open correctness item, scope widened).** Every commit handler in `TsFieldsEditor` (all six control kinds —
the review flagged only the number boxes) and the grid's inline `Desired_Committed` ran
`await commit(...)` with the surface still live: two rapid confirms overlapped their write + read-back
verify on separate connections — completion order could leave the control, db, and journal disagreeing,
and the first write's verify could observe the second's value → `Failed` → a spurious revert of a good
edit. Fix: `Shared/CommitChain` — a UI-thread task chain (no locks, no `SemaphoreSlim`): each commit
starts only after every earlier one from the same surface completed; callers await their own task, so
per-site revert handling is untouched and confirmation order is preserved end-to-end. One chain per
editor form + one for the grid's Desired boxes. Rejected: disabling the form (focus moves fire
`TextBox.LostFocus` commits re-entrantly — the cure invokes the disease) and refuse-while-busy (bounces a
valid second value). Also fixes the stale "reloads on success" comment on `Desired_Committed` (blind
review m4). Spec delta: serialization requirement on `schema-driven-field-editor`. 225 App.Tests (4 new
`CommitChainTests`). Bugfix (not a pure refactor) — awaiting the user's rapid-edit sanity pass, then archive.
**Closed same day:** user-verified, archived (`ef8b299`), `schema-driven-field-editor` synced.

**▶ SHIPPED 2026-07-24 — row parameter objects (`openspec/changes/row-param-objects`; review M3, count
corrected 24→29).** `ReconciliationRow`'s 29-positional-parameter constructor — adjacent same-typed runs
where a transposed pair compiles clean and renders a subtly wrong grid — became 12 parameters over two
records: `RowIdentity` (target/project/source + panel triple + enable/TS-keys/target-id — built ONCE per
`EmitRows` and shared by every row of the emit, ending the eight-argument re-thread through the three
local factories) and `RowNumbers` (the numeric columns in column order, constructed with NAMED arguments
at every site — the review's own snippet built it positionally, which would have re-created the hazard one
level down). Public property surface byte-for-byte unchanged (XAML bindings, aggregates, tests untouched);
`Make.Leaf` kept its keyword surface so zero test bodies changed. Spec delta codifies the in-place-mirror
rule (a committed edit updates its cells in place, no grid reload) on `target-and-plan-flyouts` — the
behavior the row's mutable members implement, previously stated only in code comments. 221 App.Tests green
unchanged. Pure refactor → auto-archived same session (standing rule).

**▶ SHIPPED 2026-07-24 — push decomposition (`openspec/changes/push-decomposition`; review M1).**
`TsSync.Push` went from one ~170-line method (five concerns + two state-capturing local functions) to a
~10-line orchestrator over named parts, bodies moved verbatim: `PushReplayState` (FailedSeqs/Failures +
the one `Fail` rule — replay state is now an explicit parameter, not closure captures),
`ProbePushPreconditions` (unreachable/busy refusals), `ReplayWriteBackLeg` (non-null return = whole-db
structural refusal, nothing written), `ReplayFieldLeg` (seq-order guarded replay + the abort cascade),
`CommitAndClose` (retention → partial/mid-push-edit returns → the contained closing pull). The spec delta
codifies two existing behaviors the decomposition must preserve — leg order (write-back before fields, so
a later manual desired edit outranks the ratchet) and the whole-db-refusal abort cascade — and the
cascade, untested before, is now pinned (`RecordingEditor` gained `TrySetFieldCalls`; one attempt, no
hammering, everything retained). Behavior-preserving: the full push suite (incl. the truthful-outcome
group) passed unchanged. 221 App.Tests (1 new).

**▶ SHIPPED 2026-07-24 — push-rule dedup (`openspec/changes/push-rule-dedup`; review M6).** The push path's
two twice-spelled rules became one definition each: `PreparePush` now selects its count entry through the
replay's `CountEntry` (deriving "desired-only ⇒ no count pair" from the returned column — the review can
never show a count change the replay won't perform), and a single `BaselineMatches(probe)` serves both the
pull skip rule (straight — no baseline ⇒ pull) and the push review's staleness warning (negated behind its
own has-a-baseline guard — no baseline ⇒ no "changed since" claim; the review doc's own fix snippet dropped
that guard and would have introduced a false warning). Behavior-preserving; spec delta records the
review-replay-agreement invariant on `ts-sync-model`. 220 App.Tests (2 new: mixed acquired+desired group
keeps its count pair; no-baseline makes no staleness claim while ShouldPull still pulls).

**▶ SHIPPED 2026-07-24 — truthful outcomes (`openspec/changes/truthful-outcome`; the review cross-check's
two misreports).** (1) **Closing-pull containment:** `TsSync.Push` rewrote the journal *before* the closing
pull but only caught cancellation there — a `SqliteException`/`IOException` in the pull escaped into
`PushAsync`'s catch, which reported "PUSH FAILED … edits stay journaled" with the journal already empty and
every remote write landed (the code comment asserted the backwards claim). Now any closing-pull fault is
contained inside `Push` and surfaces as `PushResult.ClosingPullFailed` → "pushed N · closing pull failed —
next open pulls fresh"; the baseline rule heals convergence (push changed the remote mtime). The catch's
premise is now guaranteed AND test-pinned: every throw that escapes `Push` precedes the journal rewrite.
(2) **Discard pull-first:** the open-with-dirty Discard used to clear journal+baseline *then* pull — a
cancelled pull stranded the discarded values in the grid as clean, journal-less truth for the session. The
discarding pull now runs first; `Discard` shrank to journal-only bookkeeping invoked when the pull lands
(baseline stays — the pull just recorded it; the old baseline-drop guard existed only for the crash window
the reordering removed). Cancelled ⇒ "discard not completed — unpushed edits kept", everything intact.
`TsPullHardeningTests`' old Discard-clears-baseline test replaced by the inverted-guard pin. 218 App.Tests
(5 new). (`openspec/changes/busy-gate`; from the 2026-07-24 code review's
concurrency cluster — C1 + M5 + the blind cross-check's grid-gating finding).** The "IsLoading doubles as
the mutual exclusion" convention became structural: `MainViewModel.TryBeginBusy()`/`EndBusy()` are now the
only writers of `IsLoading` (check-and-set on the UI thread), adopted by load, push, **and the
visible-tonight pass — which previously only checked the flag**, leaving Reload/Push/second-Find free to
interleave writers mid-pass. Row edits are gated both ways: every `Set*Async` funnel entry refuses while
busy (`RefuseIfBusy` — status note, control reverts) and edit surfaces disable off a new `CanEdit`
(whole-ListView + Find/Reload/Pull-now/Templates; `CanPush` now busy-aware; Cancel-pull/search/filters/
Ambiguities stay live); in the reverse direction an in-flight funnel call (edit or read — both hold a db
connection) blocks `TryBeginBusy`, closing the "edit committed, Reload clicked instantly" window against
the pull's atomic swap. The pass batches its flips through new `TsEditGate.ApplyManyAsync` — one worker,
one editor session, per-edit outcomes; `ApplyAsync` is its one-element case — so an N-flip pass opens 1
connection instead of N and has no UI-thread seams. Main spec seeded conceptually as `busy-exclusion` +
a `visible-tonight-toggle` delta. 213 App.Tests (9 new). (`openspec/changes/remove-alias-fold`; paired lib commit
`306f6fd` in `..\Library`).** The agreed 2026-07-08 removal (NOTEBOOK correction: "explained ≠ approved" — the
fold masked the unintentional M27/Dumbell twin): a multi-claim is always a flagged **duplicate** (resolver's
`IsAliasName` branch, `AliasTsTarget`/`AliasMemberCount`/`TargetMatchIssues.Alias`, and the planner's
member-count auto-write exemption all deleted — ex-alias multi-plan cells hold as `ManualGroup(DuplicateFold)`);
TSM drops the `alias` badge, the `!isAlias` multi-plan suppression, the report's alias info-lines + same-key
exemption ("intentional alias" wording gone), and the `aliases=` counters. DOMAIN convention is now **one TS
row per position, no exceptions**; the active `ts-ambiguity-report` delta was amended in place (alias
requirement/scenarios dead). Single-target naming freedom unaffected. Lib 171 / TSM 204 tests. **Heads-up:**
until the Dumbell consolidation on BIRDWATCHER, M27/Dumbell surfaces every load as duplicate badge + held
write-back cell + report action items — the doctrine working, not a regression (and the Visible-tonight pass
enabled the formerly-disabled twin, so consolidation is now time-sensitive: TS may image both).
**User-verified on real data + ARCHIVED same day** (`archive/2026-07-23-remove-alias-fold`; no main-spec
sync needed — the amendment lives in the active `ts-ambiguity-report` delta).

**▶ SHIPPED 2026-07-23 — Visible-Tonight toolbar group (`openspec/changes/enable-visible-tonight`).** A
**Duration** (min, 15–480, default 30) + **Horizon** (whole °, 0–89, default 30) numeric-up-down pair and a
**Find** button (replacing the toolbar's load-summary text — removed with its orphaned `SummaryText` VM
property): one press reconciles `target.active` / `project.state` with tonight's sky — visible = a **single
≥ Duration window above the Horizon floor** tonight (`CoarseVisibility.IsAboveHorizonForAtLeast` at the
DevDefaults Penns Park site — deliberately ignoring TS's own altitude gates, which TS re-applies at plan
time; a TS-gate-faithful draft incl. a `.hrz`-parser library promotion was built then **reverted** on user
redirect). Projects follow their targets (none enabled → Inactive; Draft/Closed untouched); flips ride the
edit gate/journal like hand edits (push optional); pass consumes the load's retained `TsPlanData`
(`LoadResult.Ts` — no second TS read); a target missing RA/Dec aborts before any edit. No confirm dialog —
per-press decisive, per observing night. 204 App.Tests (13 new; an earlier "191" was a stale-binary run —
see VERIFICATION.md's new slnx-vs-csproj test trap). **User-verified in app + ARCHIVED same day**
(`openspec/changes/archive/2026-07-23-enable-visible-tonight`; main spec seeded:
`openspec/specs/visible-tonight-toggle`). First `Astronomy.Core` reference in the app.

**▶ SHIPPED 2026-07-23 — pull hardening (`openspec/changes/harden-ts-pull`).** Root-caused a real incident:
the app killed mid-pull (a latency-degraded ~40 s Pull Now, ~87% done and healthy — the backup is ~37k
synchronous 4 KB SMB page reads, so 2 s and 40 s are both normal) left a hot 132 MB rollback journal the
read-only reader could never recover (SQLite Error 8), and the baseline skip — which never checks local
health — preserved the wreckage every launch. Fixed structurally: **atomic pull** (backup into
`<local>.pull-tmp`, `ClearAllPools`, swap on completion — a kill at any moment leaves the previous copy
usable; stale tmp swept next pull), **torn-local heal gate** at open (`-journal`/`-wal` beside the local db
→ `LOCAL TORN` log, discard local + baseline, pull fresh; torn + offline fails loudly; `.tsm-edits.jsonl`
untouched so unpushed edits survive), and **pull observability** (chunked `sqlite3_backup`: status-line
**text percentage** — no progress-bar element, user's call — + **Cancel pull** that discards the tmp and
never interrupts push replay writes; `PULL starting` + duration log lines — an interrupted pull used to be
invisible). `Discard` now also drops the baseline (interruption after it can't strand discarded values
behind a matching skip). 189 App.Tests (13 new). Awaiting the user's visual pass (percentage, cancel, heal).
**Closed 2026-07-23:** user-verified + archived (`archive/2026-07-23-harden-ts-pull`).

**▶ SHIPPED 2026-07-08 — printable ambiguity report (`openspec/changes/ts-ambiguity-report`).** The tripwire's
detail (what the "write-back app action" became — DECIDED block below): toolbar **Ambiguities…** writes a dated
Markdown report to `%APPDATA%\TargetSchedulerManager\Reports\` and opens it — every TS/disk ambiguity as
what · why · **the exact hand fix for NINA's TS UI**, named in rig vocabulary (project › target; plans by
template name + desired/acq counts — never raw ids/guids; real-data feedback round same evening), sections
grouped by fix location, explicit `✓ none` clean markers, alias folds + unplanned-frames as info (never
action items; unplanned compress to one line per target).
`Services/AmbiguityReport` = pure builder over the retained graph/report + a fresh in-memory
`WriteBackPlanner.Plan`; identity-held write-back cells fold into their target's one item. Three new
TS-internal checks the grid can't badge: same-key plans across ALL TS-sourced targets (de-duped against held
cells), planned-only twins (same name or inside-tolerance pair — previously invisible), duplicate template
names per profile (alias folds exempt, mirroring the planner — the M27 flood fix). Status line:
`· N ambiguities` when non-zero. 176 App.Tests (17 new). First real run also caught a genuine fresh issue:
disk panel `Panel Center` (new frames) coordinate-claims TS `Rosette P4` at 0.196° with a failing panel
token — the author adjudicates. **Closed 2026-07-23:** hand-fix pass done (Swan · Rosette · Dumbell; FishHead
earlier), re-run fully clean (0 action items), ARCHIVED (`archive/2026-07-23-ts-ambiguity-report`; main spec
seeded — in the post-alias-removal form: no info-tier alias requirement).

**▶ DECIDED 2026-07-08 — resolver rejected; hygiene by hand; IS lane opens** (full why:
`docs/2026-07-08-resolver-rejection-is-lane.md`; conventions → `DOMAIN.md`; TS contract → new `TS-SCHEMA.md`).
The explored two-stage TS/disk **resolver edit-surface is REJECTED** — real caseload was 7 held cells = 2
hand-fixes (rename `FishHead`→`IC 1795`; delete Swan's stray H900 plan 1040), repairs self-persist, NINA's TS
UI is the schema-correct editor upstream maintains, and TS is in its retirement lane. TSM keeps the matcher +
count write-back + existing field editing; `desired` and all structural fixes are hand-edits on BIRDWATCHER.
Two authoring conventions adopted (one name per position; one plan per filter+purpose+seconds per target) →
tray provably empty; a non-zero tray = a slipped convention. **Next TSM change: the printable ambiguity
report** — every TS + disk ambiguity, what + why, printable to walk to BIRDWATCHER (detection already computed:
report issues + manual tray + notes; add TS-internal checks the grid can't badge — same-key plans, planned-only
twins, duplicate template names). No adjudication store unless a permanent exception someday exists.
**Strategic lane (IS):** intent inverts — an **authored** plan store (working name `Catalog.db`; plan-db vs
union fork open, leaning plan-db + fresh-scan join) becomes permanent truth; TS becomes a disposable projection;
**ISM = "TSM but for IS"**. Named requirement: **TS ⇄ IS migration** — lift (read-only) + back-projection =
bulk-regenerate a fresh `scheduler.db` ("just in case" insurance), invariant `lift(project(store)) == store`.
Parked roadmap item: **"Tonight"** — the sophisticated enable (TSM computes tonight's visible set via
`Astronomy.Core` + `.hrz` horizon, sweeps `target.active`; rejected shape: a populated Tonight project =
institutionalized duplicates). Deferred discussion: promoting ~33 disk-only dirs (centroid becomes the authored
coordinate).

**▶ SHIPPED 2026-07-08 — sync-direction marks (`openspec/changes/edit-direction-marks`).** New leftmost
grid column: one mark per row level — `←` BIRDWATCHER arrived different (new pull-time field differ
`TsInboundDiff`: `TsSync.Pull` snapshots the authored displayed/editable field set before the backup, diffs
after, unions into a session-sticky in-memory `TsInboundStore`) · `→` unpushed journal writes (manual +
write-back; pure journal re-read, restart-safe) · `⇄` both · blank clean. Actuals mask: an
acquired/accepted write-back stamp drops those inbound entries (disk supersedes the rig's totals → `→`,
clean after push; `desired` never masked). Headers union their subtree via `Services/SyncMarks` — graph
plan-key map covers plans folded into multi-plan rollups; a mosaic project edit marks the parent only.
Tooltips: per-field old→new (leaves), direction counts (headers). One in-place sweep
(`RefreshAllMarks` — never a collection rebuild) from load/edit/push/discard. Accepted gap: template edits
mark no row (badge + push review still carry them). 159 App.Tests (28 new: differ/pull matrix, mask,
resolver, VM lifecycle). Visual pass clean; **archived 2026-07-08**
(`openspec/changes/archive/2026-07-08-edit-direction-marks`; main spec seeded `edit-direction-marks`).

**▶ SHIPPED 2026-07-07 — cadence-safe TS edits (Part 4; `openspec/changes/archive/2026-07-07-cadence-safe-ts-edits`; library
`76bbae0` + `c606ba5`).** Per-filter `enabled` (checkbox on 1:1 plan rows + plan flyout) and
`project.filterswitchfrequency` (project flyout) with the **transactional cadence clear**: library
`TsField.Clears` (`None`/`Target`/`Project`, replaced `CadenceSafe` — breaking, no shim);
`TargetSchedulerEditor` deletes the scoped `filtercadenceitem` rows in ONE transaction with the column write
(TS restores cadence verbatim, regenerates only from empty), skips unchanged values (verified no-op, no
clear), and refuses target-scope edits with `RefusalReason.HasOverrideOrder` when hand-authored override
orders exist. **Sync-model composition falls out free:** the clear lives inside `TrySetField`, so the push
replay re-derives the delete scope on the remote; a locally toggled-back field replays as a no-op and
correctly keeps the remote's still-valid cadence (seam-tested), and an OEO refusal at replay is a retained
partial failure. UI: direct commits, no confirm (revised 2026-07-07 -
the atomic clear makes toggles produce the TS-expected result; push review stays the gate); plan `enabled`
flows reader → resolver → projection cell → row
(1:1 rule like `PlanTsKey`). 170 lib tests (scoped clears, no-op, OEO, rollback-atomicity via RAISE(ABORT)
trigger) + 131 App.Tests. **User-run pass verified clean 2026-07-07** (direct toggles — no dialog — local `filtercadenceitem` cleared + journaled, push → BIRDWATCHER cleared + TS regenerates, fsf via project flyout).

**▶ SHIPPED 2026-07-06 — template manager (editing-surface Part 3; `openspec/changes/archive/2026-07-06-template-manager`; library
`201dd50`).** The full exposure-template surface, edit-only: the library's `TsEditableSchema` grew 11
exposuretemplate rows (18 total) — `twilightlevel` (new `TwilightLevel` enum map, codes from the TS source;
column spelling is TS's EF rename of `twilightlevel_col`), `minutesoffset` (±720, negatives legal), the moon
avoidance suite (relax min altitude floor −90° — TS ships −15), `moondownenabled`, `ditherevery`,
`maximumhumidity` — all cadence-safe, so the schema-generated form renders them with zero UI code. Two entry
points (user decision — templates have no grid rows): toolbar **Templates…** picker (graph-sourced: name ·
filter · used-by count, zero-use included; empty before a load → status note) and plan-row **"Edit template…"**
(plan → template resolved through the retained graph, no row-model change). Blast radius always stated:
flyout title + push-review label read "Template '<name>' — used by N plan(s)". Add/delete/duplicate stay TS
functions. 163 library tests (surface/bounds/enum pins) + 128 App.Tests (picker list order/used-by/zero-use/
keyless-skip, plan→template resolution, template journal seam). **User-run pass verified clean 2026-07-06**
(picker sanity, row item, moon-suite + twilight edits, gain/offset sentinels intact, push → NINA verify).

**▶ SHIPPED 2026-07-06 — project-settings flyout (editing-surface Part 2; `openspec/changes/archive/2026-07-06-project-settings-flyout`).**
Right-click "Edit project…" on any row resolving a TS project key (target groups, panels, plan rows — the
context menu went additive: a row offers its own editor plus the project's; mosaic parents keep "Edit mosaic
project…") opens the schema-generated flyout for `TsTable.Project` — all 12 cadence-safe fields including
`state`, whose recorded hazard was **retired by reading the TS source**: nothing stamps
`ActiveDate`/`InactiveDate` on transitions (schema setters, planner, and `ProjectViewVM.Save()` are plain
writes), so state is an ordinary `ProjectState` enum edit. TS's one cross-field save rule replicates as
**warn-never-block** (`Shared/ProjectRules.IsNeverSelected`): committing `minimumtime`/`meridianwindow` while
the pair means never-selected shows a persistent caution under the form (evaluated from seed + verified
commits, clears when fixed, warns at open too) + a status note; the write always proceeds. Commits ride the
existing gate → journal → reviewed push untouched. 122 App.Tests (+ pair-rule theory, project-key journal
seam). **User-run pass verified clean 2026-07-06** (each row shape's menu, a knob per field type, state
flip in the push review, warn appear/clear, push → NINA verify).

**▶ SHIPPED 2026-07-06 — TS sync model: pull → edit local → push-as-replay (`openspec/changes/archive/2026-07-06-sync-model`).**
Replaces the LIVE/LOCAL two-world editing (radios, direct SMB writes, sticky-fall, `EditOutcome.LiveDropped`,
post-write `ClearAllPools` — all deleted) with one editing world: **`Shared/TsSync`** pulls BIRDWATCHER's db
over the local copy at open via the SQLite **online backup API**, skipped when the persisted baseline
(`*.tsm-sync.json`: remote size+mtime) matches and no remote sidecar exists — rapid test relaunches skip the
copy. Every verified edit lands locally and appends to the persisted **journal** (`*.tsm-edits.jsonl`;
dirty ≡ journal non-empty, crash-safe by derivation). **Push** (toolbar, review `ContentDialog`) replays the
collapsed journal: write-back entries per-plan via `TargetSchedulerWriter` (desired ratchets against *remote*),
manual entries per-field via `TrySetField` — only journaled fields are touched, so NINA's nightly
counts/`acquiredimage`/XFM grades can't be clobbered; remote sidecar refuses the whole push; per-entry failures
retain loudly; full success ends in a fresh pull (baseline invariant: recorded ⇔ local mirrors remote).
**Write-back went automatic**: `Services/WriteBackStep` stamps drifted counts into the local db after every
load and journals them; the push review lists them decreases-first. Open-with-dirty prompts push/discard/not-now
BEFORE any pull; offline sessions journal and become pushable at reconnect (softened rule — Discard preserves
the debug path). Toolbar: sync badge (`synced HH:mm · N unpushed`) + Push… + Pull now; Reload never pulls.
113 App.Tests (pull/skip matrix on real temp SQLite, push-replay seams, journal round-trip/collapse/torn-line,
write-back step). **User-run pass verified clean 2026-07-06** (fresh pull / skip / offline / edit→push→NINA
verify / decreases review / dirty-prompt-after-kill).

**▶ SHIPPED 2026-07-06 — context-sensitive edit flyout (editing-surface Part 1;
`openspec/changes/archive/2026-07-06-field-editor-flyout`).** One schema-generated form (`Controls/TsFieldsEditor.cs`) renders any
TS row's cadence-safe editable fields straight from the library's `TsEditableSchema` (Bool→ToggleSwitch,
Whole/Real→NumberBox with Min/Max clamp, Enum→ComboBox from the new `EnumValues` maps, Text→TextBox; Unit
suffix, Notes tooltip) — adding a field to the reference lights it up with zero UI code. Triggers: a
hover-revealed pencil glyph and a right-click menu on TS-backed target rows ("Edit target…") and 1:1 plan rows
("Edit exposure plan…"), both opening a row-anchored `Flyout`. Values seed **fresh from the current db**
(`TsEditGate.ReadFieldsAsync`; drifted columns omitted, row-missing/read-fault → error content, never fabricated
defaults); each field commits independently through the guarded gate (light-dismiss can't lose work), failures
revert the control, `active`/`desired` route through their existing setters so the grid mirrors in place (the
enable checkbox went `Mode=OneWay` + `ApplyEnabled`). Cadence-breaking fields are excluded via
`IsCadenceBreaking` until the parked cadence change ships. Library side: declarative `TsEnumValue` maps
(`6a2cabf`, 156 lib tests). 89 App.Tests, 0 warnings; ships the queued "target `priority` editing" item.
**Visual verification pending (user).** **Closed:** verified across the edit-flyout Parts 1–4 passes
(all verified by 2026-07-07).

**▶ SHIPPED 2026-06-26 — closed the `TargetCells` projection leak (review's full set now done).** `BuildRows`
was indexing `graph.Targets` to read one target's `ImportedFromTsGuid` for a planned-only mosaic panel's key —
reaching past the projection into graph internals. It turns out `TargetCells` already carries that value as
`TsTargetKey` (the projection assigns `t.ImportedFromTsGuid` to it), so the fix was a 2-line consumer-side swap
(`child.TsTargetKey ?? child.Name`) that drops the `graph.Targets` dictionary — `TargetCells` is now the complete
contract for the grid shaping (`graph` stays only as the projection's input). No library change, zero behavior
change (identical value, same source), 85 App.Tests, 0 warnings. Candidate C — the last of the four
architecture-review candidates (A/B/D shipped above).

**▶ SHIPPED 2026-06-26 — the two header rows share an `AggregateHeaderRow` base.** `TargetGroupRow` and
`PanelGroupRow` were ~90% identical — both wrapped `RowAggregates` and hand-rolled the same `IsExpanded`/chevron,
`*Text`, `HoursText`/`HoursBackground`, and `Recompute` (a fill/format change meant editing both — a live drift
risk). New `ViewModels/Rows/AggregateHeaderRow.cs` (an abstract **base class** — not an interface, so WinUI
`x:Bind` resolves the members on each template's concrete `x:DataType`) owns every shared aggregate-display rule
once; the two concretes keep only their specifics (target: enable checkbox · panels · project · target id;
panel: key · label). The vestigial always-0 `TargetGroupRow.FilterCount` was deleted. Zero behavior change (the
chevron glyphs are byte-identical); +3 parity tests prove the two headers render identically for the same
children. 85 App.Tests, 0 warnings. Candidate D from the architecture review.

**▶ SHIPPED 2026-06-26 — grid flatten/splice extracted to a tested `VisibleRowTree`.** The "visible rows"
derivation was encoded twice — a wholesale rebuild (`AppendGroupContent`/`AppendLeaves`) and three toggle
methods whose **remove** side re-derived the structure by scanning runtime types (`while next is not
TargetGroupRow`, `while next is ReconciliationRow`, `remove detail.Count`) while their **insert** side already
shared the rebuild rule. New `ViewModels/VisibleRowTree.cs` owns ONE `ExpandedContent(node)` ("the rows a node
contributes when expanded") driving the rebuild **and** every toggle's insert *and* remove, the in-place
`Toggle` splice, and the node-identity / expansion-key formats (target · `target|panel` ·
`target|panel|filter|purpose`) that were scattered across the loader, `MainViewModel.RollupKey`, and
`ExpansionState`. The three `MainViewModel` toggles collapse to one-liners; `AppendGroupContent`/`AppendLeaves`/
`RollupKey` + the type-scanning removes are gone; `ExpansionState` is now a dumb string-set behind the tree.
**Zero behavior change** (the existing toggle/filter/sort tests pass unchanged); +11 module tests pin
`ExpandedContent` per node type, the key formats, and the headline **splice == rebuild** invariant (collapse
removes exactly what expand inserts). Pure over the row objects' `IsExpanded` flags — no VM, no XAML. 82
App.Tests, 0 warnings. The second "Strong" deepening from the architecture review (Candidate A; B = the TS-write
seam below).

**▶ SHIPPED 2026-06-26 — guarded TS write extracted to `TsSource` + `TsEditGate` (deep, injected, tested).**
The LIVE/LOCAL state machine + the guarded write — previously smeared across `MainViewModel` + `TsDatabaseResolver`
+ the library editor (the safety-critical rig-write path, with **zero test coverage**) — became two App-side
`Shared\` modules: **`TsSource`** (LIVE/LOCAL paths · injected reachability probe · mode + sticky state; consulted
by the load *and* the gate) and **`TsEditGate`** (one `ApplyAsync(...) → EditOutcome` =
`Applied`/`Refused`/`Failed`/`LiveDropped`, over an injected `ITsEditor`; delegates the sticky-fall to `TsSource`
on a live drop; audits every write attempt). The library half is the consumer-neutral
**`TargetSchedulerEditor.TrySetField → (FieldEditResult?, RefusalReason)`**, folding the four open-db guard
predicates (required-columns / read-only / open-sidecar / column-present) into one structured-refusal call.
`MainViewModel.ApplyFieldEditAsync` (~60 lines) + the four `_tsMode/_liveDisabled/_tsProbed/_tsDbPath` fields
deleted; the VM now holds the gate and maps `EditOutcome` to status + side-effects. **~16 new tests** drive the
whole machine (first-probe / re-probe-drop / sticky / `TrySelectMode` / refusal / verify-fail / live-drop-on-write)
with **no SQLite or SMB** — the probe and editor are injected. Built **subagent-driven** from
`docs/archive/2026-06-26-plan-guarded-ts-write.md` (per-task TDD + reviews + an opus whole-branch review that caught a
swallowed live-drop exception, now fixed). 71 App.Tests + 153 library, **0 warnings**. A future **WriteBack** app
action reuses the gate via an `ApplyPlanAsync` sibling (deliberately not built — YAGNI). Commits: TSM
`1cda326`→`6150b7d`, Library `8d863e5`. **Pending: user's live-app pass** — one `desired`/`enable` write hitting
the actual TS db (unit tests can't cover the live write). **Closed:** in-grid `desired`/enable verified live
in NINA (pre-sync-model; recorded in ROADMAP's status).

**▶ SHIPPED 2026-06-20 — count columns reframed: `Acq`→`TS`, `Disk`→`Actual`, `Acc` hidden + `acc≠acq` badge.**
Display-only grid change (grill-me design). The old `Acq`/`Acc`/`Disk` trio mixed two TS-side bookkeeping numbers
with the on-disk truth; now it reads **`Desired`** (TS goal) · **`TS`** (TS's recorded `acquired` — the number TS
schedules on with the grader off) · **`Actual`** (on-disk frames). `Acc` (accepted) left the grid: with grading
off TS increments acquired + accepted together (`ImageSaveWatcher` auto-accepts) and write-back re-sets them
equal, so it only mirrors acquired — a rare in-session `accepted ≠ acquired` drift now shows as a flagged
**`acc≠acq` badge** (data kept in the row model, column gone). The `TS` header intentionally doubles the per-row
Source token (user's call); the em-dash on a Disk-only row (no TS plan) survives. *Rationale from the grill:*
TS's grader-off `PercentComplete` divides acquired by `ExposureThrottle × Desired` (default **125 %**), so a
target shot to `Desired` reads ~80 % and TS over-schedules it — TSM's `remaining = max(0, Desired − Actual)` is
the honest view, which is why `Actual` is the headline. **Deferred:** the `acc = acq = disk` *write* stays the
Phase-4 contract and lands with a future **WriteBack button** (today the app writes only `enable`/`desired`).
Caveat for that button: NINA's TS plugin can ignore external db writes mid-session until a NINA restart
(user-reported, unfixed upstream) — live write-back will want a between-sessions / restart-to-apply note. Reflow
across the header + 3 row templates; `BuildRows` adds the badge + flag; 49 App.Tests (+2), 0 errors. **Pending:
user's visual grid pass.**

**▶ SHIPPED 2026-06-11 — project renamed to TargetSchedulerManager (TSM).** The "Catalog"
name was legacy (the catalog builder left with the CLI; `Catalog.db` goes to the planned ISM) — the app manages
the N.I.N.A. **Target Scheduler** db, so it is now named for what it does. Deep rename: solution
`TargetSchedulerManager.slnx`, projects/namespaces `TargetSchedulerManager.App[.Tests]`, assembly **`tsmui`**,
log identity **`tsm.log` / `TSM_DIAG` / `%APPDATA%\TargetSchedulerManager\Logs\`** (old notes stay in the old
folder; no migration by design), window title, app.manifest, docs (CLAUDE / ARCHITECTURE re-framed to app-only
reality). Top source dir renamed to
`…\Astronomy\TargetSchedulerManager` (user-performed). Same day, earlier: the Ctrl+N window was renamed
**Observation → Diagnostics** (`ca97d89`; helper label dropped; TP mirrored in `a48f7f2` incl. its verify-ui
literals) — the shared `USER_OBS_*` log protocol keeps its name in `Astronomy.Diagnostics`. Library doc-comments
that named TSM were degenericized to consumer-neutral wording per shared-lib discipline (separate Library
commit). 47 tests green, 0 warnings.

**▶ SHIPPED 2026-06-11 — logging extracted to shared `Astronomy.Diagnostics`; TSM + TP adopt it.** The
hand-ported `Support/Log.cs` (TSM had copied it from TP, and they'd drifted) became a new pure-managed library
**`Astronomy.Diagnostics`** in the `Library` repo (lib `f0b0fda`) — *convention-as-code*: `Log` (two verbosity
axes — always-on Info/Warn/Error + gated Diag channels, default all-in-Debug / off-in-Release via the app's env
var; session rotation; `%APPDATA%\<app>\Logs\` structure; USER_OBS protocol — all driven by `AppLogIdentity`) +
`ScreenCapture.ToPng`. **TSM** (`7e908f1`) deleted its `Support/Log.cs` and calls `Log.Init("TargetSchedulerManager",
"tsm.log", "TSM_DIAG", …)` at startup (the Debug/Release diag default is passed by the app — a shared lib can't read
the consumer's `#if DEBUG`); `System.Drawing.Common` moved to the library. **TP** (WinForms, `2a60d65`) adopts the
*same* lib — **proving the contract is consumer-agnostic (one engine, two UI frameworks)**; a `global using` kept
its ~140 call sites. TSM's Ctrl+N observation window also gained a **repeatable Capture button** (mid-session
screenshots + a status readout) and **local-time** filenames (`8491e3b`). 47 TSM + 187 TP + 148 library tests, 0
warnings. The per-app dialog stays per-app (WinForms `Form` vs WinUI `Window`); an `ObservationSession` to share the
START/CAP/END/CANCEL orchestration is deferred.

**▶ SHIPPED 2026-06-11 — M2 editing slice 2: in-grid `desired` editing + the `TsEditableSchema` reference.**
The Desired cell on a **1:1 plan leaf row** is now an editable `NumberBox` (headers, disk rows, and mixed-seconds
rollups stay read-only — the 1:1 rule, tested); committing on focus-loss writes `exposureplan.desired` to the
**live BIRDWATCHER db**, **verified end-to-end in NINA**. Built **reference-driven** (the user's "a global
reference to our copy of the TS tables, not guess-per-field" call): new library **`TsEditableSchema`** — one
declarative row per editable TS column (table · exact SQLite column · type · cadence-safe? · enum/range), authored
from the TS plugin schema, since `PRAGMA` yields column names/types but not *which* are user-editable vs stats/keys
nor which break cadence (domain knowledge). The editor drives off it: generic **`SetField`/`ReadField`** validated
against the reference (which doubles as the SQL-injection whitelist) + **`IsFieldAvailable`** (a `PRAGMA` drift
guard); the three typed setters became thin wrappers. Cadence-breakers (`exposureplan.enabled`,
`project.filterswitchfrequency`) are **flagged, not handled** — a plain UPDATE, so a caller must warn/defer. App
side: one guarded primitive **`ApplyFieldEditAsync(table, key, column, value)`** now shared by the enable checkbox
*and* desired (LIVE/LOCAL + open-sidecar/read-only/column-absent refusal + read-back verify + audit +
BIRDWATCHER-drop sticky-fall). Edits apply **in place** — the leaf takes the new count and its group/panel totals
re-aggregate via INPC — instead of reloading, so **scroll position and a half-typed next cell survive and rapid
edits aren't torn down**; `SqliteConnection.ClearAllPools()` after each write fixes a stale read over SMB (a pooled
reader was serving cached pages, `tsRead=0.00s`, showing a verified write as if it hadn't taken). Library
`ReconciliationCell` now carries `PlanTsKey`/`TemplateTsKey` (single-plan) + `TargetCells.ProjectTsKey` as the
write-back addresses. Library 148 tests, TSM 46, 0 warnings. Commits: library `563836d`, TSM `d4dc39d`
(on `70bace1` panel-removal).

**Two UI decisions this session — recorded so they're not re-litigated.** (1) The **docked dossier panel was
built then dropped** ("a waste of space") — editing goes **in-grid**, in the existing flattened-`ListView` idiom.
(2) **WinUI.TableView was evaluated and rejected** as the editing surface: the overview grid is a *hierarchical
tree* (target → panel → leaf → rollup) a flat data-grid can't render, and the app's coherence — one paradigm, zero
deps, DB-as-truth re-derive — wins over a foreign editable grid (re-addable on a branch if a flat whole-catalog
spreadsheet ever emerges). **NEXT (same lane, ~one field each):** `priority` (target/project, cadence-safe) →
per-filter `enabled` (cadence-**breaking** — adds the `FilterCadenceItem` clear, lifting TS's `ToggleExposurePlan`)
→ the **load-split** (Reload re-reads TS-only against the cached disk scan, ~0.3 s vs ~2 s).

**▶ SHIPPED 2026-06-11 — CLI removed; TSM is app-only.** The transitional `tsm` console host (`Program.cs`,
`Cli\`, the root csproj, `Cli.Tests`) was deleted — it had become a dual-head maintenance tax (every feature done
twice). TSM is now purely the WinUI **TS-database manager**. `DevDefaults` + `TsDatabaseResolver` moved into the
App (`App\Shared\`); the resolver tests moved to `App.Tests` (43 green, 0 warnings). **Nothing lost:** the
catalog-build engine is one AL call (`CatalogBuilder.BuildAsync`, disk-only via `tsDb: null`) and the write-back
engine stays in AL. Catalog-build moves to a planned **IntervalSchedulerManager (ISM)** (sibling dir
`..\IntervalSchedulerManager`, ROADMAP template there); write-back resurfaces later as a TSM app action. `Catalog.db`
is currently unbuilt — unconsumed, so fine. Reframe: TS = disposable (TSM manages it), `Catalog.db`/IS = permanent
(ISM owns it); the two no longer tangle. Phases 1–4 below describe the **library's** catalog/write-back engine,
which is unchanged and still consumed by TSM (in-memory) + the future ISM.

**▶ SHIPPED 2026-06-11 — live BIRDWATCHER TS db (read + write), local fallback.** TSM no longer edits only a
local copy. `TsDatabaseResolver` (`Shared\`, both heads) probes `\\BIRDWATCHER\SchedulerPlugin\schedulerdb.sqlite`
under a ~1.5 s timeout (so a down host can't hang startup on SMB) → **LIVE when reachable, else the local working
copy**. The CLI `--ts` default + the app's load both flow through it (an explicit `--ts` still wins); the CLI
banner + a caution-colored app toolbar badge say which, and writeback's audit logs `live=`. This **reverses the
old "never the live db" invariant** — risk accepted + mitigated: daily Macrium image of BIRDWATCHER (corruption →
restore), night-image/day-edit rhythm (rig idle when editing), plus the existing open-sidecar / read-only refusals
+ read-back verify. Verified: `tsm` reads the live db over SMB (banner LIVE, 102 TS / 44·25·33, 2.0 s); resolver
tests (reachable→live, missing/bad-path→local); 56 TSM tests, 0 warnings. **Pending user's pass:** app badge reads
LIVE, a target-enable toggle lands on the live db (`py`+sqlite3 on the UNC), BIRDWATCHER-off shows LOCAL.

**▶ SHIPPED 2026-06-11 — M2 editing slice 1: target-enable checkbox (immediate write).** First TS edit. A compact
checkbox is the grid's new **leftmost column**, on the **target group-header only** (hidden on disk-only +
mosaic-parent rows — no TS target behind them). Toggling writes `target.active` to the **local TS working copy
immediately** — read-back verified + audited to `tsm.log` (`EDIT target.active …`), off the UI thread, **no grid
reload** (active changes no counts/hours); a failed/unverified write reverts the box. `target.active` is the
**one T1 edit that doesn't touch filter cadence**, so it's a plain `UPDATE` — the safest first edit. New library
`TargetSchedulerEditor` (`SetTargetActive`, resolves by-guid-or-Id + verify; mirrors the Writer's hardening —
private cache, column guards); `TargetCells` now carries `Enabled` + TS provenance (the provenance R1 deferred).
A VM override keeps a flip consistent across filter/sort rebuilds (cleared on Reload). Real data: 102 TS targets
all guid-keyed, 59 active. Library 134 tests (+5), TSM 53, 0 warnings. **Pending: user's click-test** (toggle a
target, confirm via `py`+`sqlite3` that `target.active` flipped). Next T1 edits (`desired`, `priority`, per-filter
`enabled`) reuse this editor; `enabled` adds the filtercadence-clear.

**▶ SHIPPED 2026-06-11 — M2 prep refactor: R1 cell-projection → library + ExpansionState.** Behaviour-preserving
cleanup of the 215-line `BuildRows` ahead of the TS editor (own reviewable slice, no functional change). **R1:**
the cell join (plans + inventory → per-`(target, filter, purpose, seconds)` cells tagged with match-state) moved
to the library as `Reconcile/ReconciliationProjection.Project` → `IReadOnlyList<TargetCells>` (UI-agnostic;
reusable by IS); the app's `BuildRows` shrank to **shaping only** (planes / rollups / signed hours / fills /
panels / sort over the cells) with its signature unchanged, so the 7 `BuildRowsTests` pin behaviour. **ExpansionState:**
the three expansion `HashSet`s left `MainViewModel` for a tested `ExpansionState` value object. Library 129 tests
(+8 projection); TSM 53 (+6 ExpansionState); 0 warnings; grid output unchanged. **Next — the editing slice:** a
**dialog-based** TS editor (select target → `desired`/`enabled` per filter + `active`/`priority` → Save in one
txn via a new library `TargetSchedulerEditor`, clearing the target's `filtercadence` iff `enabled` toggled,
audited; reader gains the `enabled` column). Structural add/remove of projects + filter-plans stays M3.
**Pending: user's visual grid pass** (confirm 786 rows / 102 groups / 44·25·33 unchanged).

**▶ Phase 4 — `TargetSchedulerWriter` — DONE (built 2026-06-08).** `tsm writeback [--apply]` fresh-rebuilds the
catalog, then pushes disk-derived counts into a **local** TS copy (dry-run by default). Validated on real data:
**182 plans written / 13 held for manual / 92 ignored-missing**, the motivating case `Sh2-142 Wizard H 0 → 140` is
fixed, re-apply idempotent. **`tsm writeback --target "<dir>"`** adds a surgical single-target write — no catalog
rebuild, and for a **mosaic it writes each panel's** counts to that panel's own TS plan (`Mosaic - Cygnus Loop` →
16 panels, 96 cells matched / 80 writes, apply-verify OK, idempotent). Details in Phase 4 below.

**Shipped 2026-06-08 (this session):** `tsm writeback --target` (surgical single-target; per-panel for mosaics) —
**verified live on BIRDWATCHER with NINA/TS running**. CLI hardened so the verb is dash-tolerant (`--writeback`
routes) and `--target` without the verb prints a hint instead of silently running a full build. All committed
(library + TSM, branch `dev`).

**▶ SHIPPED 2026-06-10 — M27/Dumbell = alias, option B** (treat aliases as one object). An **alias** = every
colliding TS name **exactly** matches a disk identity facet (directory / catalog / common / object; normalized, no
substring) — `M27` + `Dumbell` are the two halves of disk `M27 - Dumbell`; the strict rule keeps genuine variants
like `M42` + `M42 core` flagged as real duplicates. Implemented: `AliasTsTarget` in `CatalogBuildReport`,
`TargetResolver.IsAliasName`, and `WriteBackPlanner` auto-writes an alias cell when its plan count equals the alias
member count (disk count to **every** member's plan; any other multiplicity stays `MultiPlan` manual). Verified on
real data: duplicates 1→0, aliases 1, the 6 held cells became 12 writes (both members converge, one `desired`
ratchet 129→169), manual bucket M27-free, dry-run idempotent. 78 library tests (+3).

**▶ Phase 3 planned (grill-me session 2026-06-10) — TS Editor (WinUI 3).** TS stays the daily scheduler until IS
exists; TSM bridges: view + edit the **local TS working copy** with disk-ACTUAL beside every number. Full spec in
Phase 3 below. Build order: **(1) alias rule (above — ✅ shipped) → (2) M1 read-only grid (✅ built 2026-06-10) →
(3) M2 edits → (4) M3 resolution + structural.**

**▶ M1 BUILT 2026-06-10 — `TargetSchedulerManager.App`** (WinUI 3, WindowsAppSDK 2.2.0, unpackaged, x64, exe
`tsmui`): read-only reconciliation grid — flat (target, filter, purpose, seconds) per-plane rows, plan vs DISK from a fresh
in-memory scan+resolve (no Catalog.db), search / source filter / flagged-only / sort, match-state badges, mosaic
rollup rows. Self-verified: launches and matches the console exactly (Both 44 / TS-only 25 / Disk-only 33, alias 1,
mosaics 6/38 panels). **Pending: user's hands-on UI pass** (filters, scroll perf, badge readability). Gotcha
captured: the console csproj sits at the repo root, so it must `DefaultItemExcludes` the nested app dir.

**Target grouping (built 2026-06-10, from the M1 pass):** the grid is now a flattened tree — one collapsible
`TargetGroupRow` header per target (chevron disclosure, **collapsed by default**) aggregating its visible
filter rows (Σ desired/acq/acc/disk, Δ, badge union); whole-row click or chevron toggles, Expand/Collapse-all
in the toolbar. Expansion keyed by target name (survives filter changes + reloads); toggle edits the bound
`ObservableCollection` in place so scroll position holds; sort dropdown orders groups by aggregates. Search
respects manual expand state (headers of matching groups appear collapsed; aggregates cover only surviving
children). WinUI shape: two `DataTemplate`s + `DataTemplateSelector`, no real TreeView — the VM owns the
visible-row list (TreeListView-in-VirtualMode style). Smoke-tested: launch clean, `groups=102 expanded=0`.
**Pending: user's visual pass** (chevron alignment, group-row readability, click feel). The accelerator's
floating Ctrl+N hover hint is suppressed (`KeyboardAcceleratorPlacementMode=Hidden`).

**Seconds column + per-exposure rows (built 2026-06-10):** exposure time joined the cell identity end to end.
Library (`b195e31`): scanner buckets `FilterAggregate`s per (filter, purpose, whole-second exposure);
`inventory_filter.exposure_seconds` (renamed from `typical_exposure_seconds`) joins the PK — schema change,
so `Catalog.db` was deleted + rebuilt (482 inventory rows). **Write-back contracts unchanged** (planners
fold/sum splits back to their (filter, purpose[, bin]) keys; 82 tests, +4 covering the folds). App: grid rows
key on (filter, purpose, seconds) — plan and disk join when sub lengths agree, drift shows as separate
plan-only/disk-only rows; "Seconds" column right of Filter; group headers count distinct filters. Verified on
live data: 715 rows / 102 groups, report counts unchanged. (The "match by exposure in write-back" follow-up
shipped the same day — see below.)

**Hours column + plane-split rows (built 2026-06-10, from the user's Ctrl+N notes):** the Δ column is gone;
leaf rows carry per-plane Hours — a TS row (Desired/Acq/Acc; **Hours = desired × seconds**) and/or a Disk row
(frame count; **Hours = count × seconds**). Group header Hours = **disk hours − desired hours** (signed F1,
pill fill: caution = needs telescope time, green = plan goals met). Frame-count Δ survives as the sort key
only; the Source dropdown still classifies whole targets. **Refined same day — Both-row rollups with nested
disclosure (from the user's Ctrl+N notes):** one `Both` rollup per (filter, purpose) that has both a plan and
a disk side, aggregating **every** sub length (counts + hours). Sub lengths all one value → plain merged row.
**2+ distinct times → Seconds reads `mixed`** (caution pill) and the rollup gets its own chevron, expanding
in place into **one source line per sub length** (seconds ascending, deeper indent): a bucket carrying both
planes is a nested `Both` line (plan values + disk count, its own gap hours + fill), one-sided buckets are
TS/Disk lines — answering "where do these times come from". **Hours model (user-refined to fully additive signs):**
every cell is the row's signed contribution — TS lines show **−(desired × seconds)** (the deficit), Disk lines
+(frames × seconds), Both/header cells the disk−plan gap — so each parent is the literal sum of its children's
displayed Hours. Fills: gap cells caution/green by sign; **TS lines filled at every level** (caution while
outstanding; a desired-0 plan shows the **critical/error fill** — data that shouldn't exist); Disk lines stay
plain by choice. Tiny non-zero values render F2 so they never read as 0.0. A Both rollup's Hours = its **disk − desired gap** with
the caution/green fill (per-filter mini header); one-plane rows stay plain everywhere. Rollup expansion is
keyed `target|filter|purpose` (survives filters/reloads); whole-row click toggles, exactly like target
groups. Group `Remaining` is per-row (rollups self-pair). Per-row hours are loader-computed
(`PlanHours`/`DiskHours`) since a mixed rollup has no single seconds value. Verified live: 582 top-level
rows / 102 groups, header deltas unchanged (Abell 21 −10.3 = 15.8 disk − 26.1 desired). **Pending user's
visual pass** (nested chevron feel, `mixed` pill readability) — dark-theme caution/success fills are subtle;
stronger brushes are a one-line swap (`ThemeBrushes.cs`) if wanted.

**▶ SHIPPED 2026-06-10 — review round 2: verified + XS fixes** (`docs\archive\2026-06-10-code-review-round2.md`,
fixes `b67ddfa` + library `7ce569d`). Independent re-review verified every slice-1 claim against the code
(library mounted; all round-1 caveats resolved; no regressions). Fixed its findings: **B1** `--apply <value>`
now warns + stays dry-run (was: any value armed apply on a db-writing verb), **B2** unknown options warn AND
are ignored (key + value pair), **B3** thread-safety doc line on the report's lazy indexes, **B4**
console-capture caveat comment. Cli tests 11 → 13 (48 TSM total). M2 backlog confirmed: R1 opening move,
§7.2 cancellation threading, TsEditSession + loader seam, §7.5 ExpansionState.

**▶ SHIPPED 2026-06-10 — TSM test projects** (`2f74a9f`): the repo's "no tests, thin host" rationale retired.
`TargetSchedulerManager.Cli.Tests` (11 — `CliOptions` parsing/warnings; dies with the transitional CLI head) +
`TargetSchedulerManager.App.Tests` (34 — `BuildRows` cell projection pinned ahead of R1, `MainViewModel`
filter/toggle/expansion pipeline via the internal `SetRowsForTest` seam, Hours sign convention, `RowAggregates`
additivity, `Format.Hours`). The WinUI head tests run in a **plain test host** — no XAML runtime; only the two
`Brush` getters are out of bounds. `dotnet test TargetSchedulerManager.slnx` runs everything. Also this date:
no-migration rule confirmed portfolio-wide (none present; TS guards are refusal-only), doc dates corrected to
user-local (machine clock runs ahead in the evening), logs switched to local-time stamps (`603f3a9`).

**▶ SHIPPED 2026-06-10 — code-review slice 1** (review in `docs\archive\2026-06-10-code-review-slice1.md`, executed-status
table at its top; library `c381a2e`+`8bf1aef`, host `b3d8b5d`, app `651abb6`). Three drift hazards
single-sourced into the library — `EffectiveExposure` (THE effective sub-length rule; was 3 hand copies),
`CatalogBuildReport.IssuesFor(...)` issue-membership API (planner + loader stop hand-indexing the report),
`Reconciler.MergeFamilies` (parent rollup; console consumes) — plus host `Program.cs` split into
`Cli\{CliOptions, BuildCommand, WriteBackCommand, ConsoleRenderer, WriteBackAuditLog}` with one shared
writeback `ExecutePlan` tail and unknown-option warnings; app row VMs → `ViewModels\Rows\` (V1) and dev
defaults single-sourced via linked `Shared\DevDefaults.cs` + `ResolveOptions.Default` (V2). 121 library
tests (+13); tsm output + app DIAG verified number-identical. **R1 (full cell projection → library) +
M2-prep (TsEditSession, loader interface, app tests) deferred as M2's opening move.**

**▶ SHIPPED 2026-06-10 — mosaic panels are first-class targets** (library `b296d58`→`f11bee5`, host
`8c1b7ee`, app `1191278`). *A panel is a normal target whose key is composite* (user's architectural
principle): the scanner's one walk retains per-panel sub-reports; `target` gains `parent_target_id` (schema
change → Catalog.db deleted/rebuilt); the resolver flows panels through the ONE standard loop — **scope
keys** (anchor within your key-space; no mosaic conditional in matching), token name-validation
(`Panel 01of16` → `P1`), and the **aligned-outranks-unaligned rule** (an unshot panel inside tolerance of
its shot neighbour stays planned instead of becoming a false duplicate — the Witch Head fix). Bulk
write-back auto-writes panels (`ManualReason.Mosaic` retired; manual went 38 mosaic groups → 6 flagged
Rosette `Panel Center` cells + 1 MultiPlan); console rolls panels up under parents (`Reconciler.Merge`);
the grid gains the panel level (target → panel → filter → seconds detail, collapsed by default, labels
"Panel 01of16 · CygnusLoop P1"). Real data: 6 mosaics → 28 matched / 10 planned-only / 7 disk-only panels;
786 rows / 102 groups; 108 library tests. "Rig" (telescope/camera/mode) flagged as a future key dimension —
deliberately deferred. **Pending: user's visual pass on the panel level; `--apply` not yet run post-panels.**
**Closed/superseded:** the panel level was verified through the later mosaic work; the `--apply` era ended —
the CLI was removed 2026-06-11 and write-back became an automatic in-app step 2026-07-06.

**▶ SHIPPED 2026-06-10 — exposure-aware write-back (library `87ae471`, host `ba23f06`).** The write key is now
**(target, filter, purpose, whole-second exposure)** — *the plan's seconds is the spec* (user-decided strict
semantics): each plan receives the disk count at exactly its effective duration (plan exposure ?? template
default), **0 when none match** (flagged decrease — 600 s frames never satisfy a 900 s plan). Same-purpose
plans at different durations auto-resolve (no longer manual); disk buckets no plan targets are
**`UnplannedFrames` notes**, never written, never manual (plan creation is M2's). Surgical `--target` matches
(filter, purpose, bin, seconds) and deliberately never zeroes plans with no matching cell (bulk does — see
ARCHITECTURE). Output: per-row `@900s`, `--target` lists no-ops explicitly, new `unplanned` section + summary
count. **New `tsm-cli.log`** (append-only, `%APPDATA%\TargetSchedulerManager\Logs\`) audits every run's full
decision trail. 91 library tests (+9). Live dry-run verified: Medusa H/S/O @900 → 0, R → 0, B → 2; M17 H
stays MultiPlan (its two plans share 900 s); bulk totals 105 decreases / 154 no-ops / 38 manual (mosaics) /
140 unplanned. **`--apply` not yet run — user's call** (working copy restorable from `schedulerdb - Copy.sqlite`).
**Superseded:** the `--apply` era ended — CLI removed 2026-06-11; write-back is an automatic in-app step
since 2026-07-06 (reviewed at push instead of gated at apply).

**Logging (slice 1, built 2026-06-10, ported from TP):** `tsm.log` under `%APPDATA%\TargetSchedulerManager\Logs\`
(session rotation, WARN/ERROR, `TSM_DIAG` channels) + **Ctrl+N observation window** — modeless always-on-top,
USER_OBS START/END markers, notes + VM ctx snapshot + main-window screenshot into the log stream. Use it during
the M1 pass to annotate findings in place. Slice 1 **verified interactively** (checkpoint / note / cancel /
rotation / clean screenshot). **Slice 2 built + log-verified 2026-06-10:** `DIAG/Load` (per-stage timings — scan
1.77 s of 1.83 s total, the XISF walk is the whole load cost — + report counts) and `DIAG/UI` (filter trail with
row counts), `TSM_DIAG`-gated. Standing M2 rule — the writer logs every TS write. TS read+write remains a **stop-gap** until IS reads `Catalog.db` directly — but the Phase-3 **UI
shell is permanent** (retargets Catalog.db when IS arrives); only the TS data layer is disposable.

Write-back's **manual bucket** (never auto-written — presented with full info to resolve): **dup-folds**
(`M27`/`Dumbell`: two TS targets onto one disk target, plans accumulate) and **identity conflicts** (name-mismatch
/ ambiguous coord match, e.g. `CygnusLoop P3` ↔ `NGC 6995` — auto-writing a false-positive match would zero a real
TS target's counts).
