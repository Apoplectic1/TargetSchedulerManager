# TCM Code Review — Round 2 (2026-06-10)

> **Executed status (2026-06-10):** B1 ✅ (`--apply <value>` warns + stays dry-run; '=' form was already
> safe as an unknown key; Cli tests 11 → 13) and B2 ✅ (unknown key + its value warned AND ignored) in TCM
> `b67ddfa`; B3 ✅ (single-thread/equality doc line on the report's lazy indexes) in Library `7ce569d`;
> B4 ✅ (console-capture comment) in `b67ddfa`. B5/B6 accepted as written. **Deferred with the M2 work, as
> agreed: §7.2 cancellation threading, R1, TsEditSession + loader seam, §7.5 ExpansionState.**

Purpose: **independent verification of the slice-1 work** executed against `CODE-REVIEW-2026-06-10.md`
(commits `b3d8b5d`, `651abb6`, `2f74a9f`, `603f3a9` here; `c381a2e`, `8bf1aef` in the Library), plus a
fresh pass with the new emphasis: **maintainability and ease of reasoning**.

Differences from round 1: the Library repo (`..\Library`) was mounted and inspected directly this time,
so every round-1 **[verify vs library]** caveat is now resolved. What could not be done: executing the
test suites (no .NET SDK in this review environment) — test *code* was reviewed line-by-line; the
run results (45 TCM / 121 library, green) rest on the commit-message records of local runs.

---

## A. Verification of the executed slices

Every claim in the annotated review header was checked against the actual code. **All verified.**
Evidence per claim:

| Claim | Verdict | Evidence |
|---|---|---|
| C1 — `Program.cs` → `Cli\` split | ✅ Verified | `Program.cs` is now 46 lines of parse+route; `Cli\{CliOptions, BuildCommand, WriteBackCommand, ConsoleRenderer, WriteBackAuditLog, CliLog}` match the review's sketch. `WriteBackCommand.ExecutePlan` (lines 111–130) is the shared tail exactly as proposed; the bulk/surgical asymmetry stayed in the planners, untouched. Renderer output strings are character-identical to the originals — behavior preservation is verifiable by inspection. |
| §7.1 — unknown-option / stray-arg warnings | ✅ Verified | `CliOptions.Parse` warns on both (lines 29–36); contract pinned by `Parse_UnknownOption_WarnsButStillParses` and `Parse_StrayPositional_Warns`, which capture `Console.Error` as part of the tested contract — nice touch. |
| R1-prep — `EffectiveExposure` single-sourced | ✅ Verified | Library `TargetScheduler\EffectiveExposure.cs` with *both* overloads (catalog-side null-fallback, raw-TS-side −1 sentinel) and a docstring that states the invariant ("one definition, no re-implementations"). Consumed by `WriteBackPlanner` (lines 57, 103), `SingleTargetPlanner` (line 131), and the app loader (`BuildRows` line 136). The round-1 drift hazard is gone. 7 theory cases cover both overloads incl. rounding and the 0 fallback. |
| R2 — `CatalogBuildReport` issue API | ✅ Verified | `IssuesFor`/`IsIdentityFlagged`/`AliasMemberCount`/`IsUnanchoredName` + `[Flags] TargetMatchIssues`, lazily indexed, `OrdinalIgnoreCase` (matching the old hand-built sets). Loader's five hand-rolled `HashSet`s are gone (now lines 123–128); `WriteBackPlanner` consumes the same definitions (lines 75/84/96) — "flagged" now has exactly one meaning across writer and grid. |
| R3 — `Reconciler.MergeFamilies` | ✅ Verified | Library `Reconcile\Reconciler.cs` lines 119–152; `ConsoleRenderer.PrintReconciliation` consumes it. Strictly better than the loop it replaced: it also absorbs the parent's own (empty) reconciliation and documents result ordering. |
| V1 — row VMs → `ViewModels\Rows\` | ✅ Verified | Five types moved (incl. `RowAggregates` — correct call: it consumes `ReconciliationRow`, so leaving it in `Models\` would have inverted the layering, a judgment call the CLI got right). UI-free `RowSource`/`RowPlane` extracted to `Models\RowEnums.cs` beside `Format`. The `Models\` folder is now honestly named: nothing in it touches WinUI. |
| V2 — dev defaults single-sourced | ✅ Verified | `Shared\DevDefaults.cs`; console head compiles it via root glob, App links it (`Compile Include` line 42 of the App csproj). VM tolerance now `ResolveOptions.Default.MatchToleranceDegrees` — the third copy of 0.5 is gone. Both csprojs' glob exclusions updated correctly. |
| §6 — test projects | ✅ Verified (read, not run) | `Cli.Tests` (11 facts on `CliOptions` via `InternalsVisibleTo`) + `App.Tests` (BuildRows / VM pipeline / row rules; `SetRowsForTest` internal seam). Quality assessed in §B below. |
| Migration code | ✅ Still clean | Re-checked post-refactor: no migration/upgrade/compat code anywhere; TS `user_version` still print-only, compatibility still gated on column presence. |

Also verified, beyond the claims: docs-as-memory held up — CLAUDE.md / ARCHITECTURE.md / ROADMAP.md all
describe the post-slice-1 reality (two-test-project layout, `Cli\` structure, `Shared\DevDefaults.cs`,
local-time logging), so a fresh reader's mental model from the docs matches the code. That discipline is
itself a maintainability asset; keep it.

## B. Assessment of the CLI's work quality

**Overall: faithful, behavior-preserving, and well-sequenced.** Three things deserve explicit credit
because they are exactly the habits that keep a codebase reasonable:

1. **Tests were written to pin `BuildRows`' contract *before* the R1 relocation** (the test file's own
   docstring says so). Refactor-then-test would have proven nothing; this ordering makes the M2 move
   safe.
2. **Behavior preservation was treated as a verification obligation**, not an assumption — renderer
   strings kept identical, writeback output "number-identical", app DIAG line compared against the known
   786 rows / 102 groups / 28-10-7 panels baseline.
3. **Small acts of care:** `TestEnv`'s `[ModuleInitializer]` blanks `TCM_DIAG` so VM tests can't append
   to the user's real session log; the `CliLog`/`Log` UTC→local-with-offset fix addresses a real
   reasoning bug (evening sessions logged under tomorrow's date); `TargetMatchFlags`→`TargetMatchIssues`
   for CA1711 shows the analyzers are being heard.

Issues found during verification — all minor, none blocking:

- **B1. `--apply=false` sets `Apply = true`.** Round 1's `--apply` detection was an exact-token match;
  `CliOptions` now uses `opts.ContainsKey("apply")`, so an explicit `=false` (or `--apply true`-style
  stray value) still arms apply. Edge-of-the-edge case, but this flag commits writes to a DB: one line
  (`Apply: opts.TryGetValue("apply", out string? av) && av is ""`) — or a warning on any `--apply`
  value — would close it. Add a test either way.
- **B2. Unknown keys warn but are still stored** in the options dictionary (`opts[key] = …` runs after
  the warning). Harmless today because nothing reads unknown keys, but `continue` after warning would
  make the contract "warn *and ignore*" true by construction.
- **B3. `CatalogBuildReport`'s lazy indexes are not thread-safe** (`??=` on three private fields).
  Fine under the current single-threaded build/report flow; worth one doc line stating that assumption,
  since reports now travel into the app layer where someone could conceivably touch one from a
  background load. (Private fields also sit outside record equality — irrelevant in practice, but it's
  the kind of subtlety a comment preempts.)
- **B4. Console capture in `CliOptionsTests` is process-global.** `Console.SetError` swaps shared
  state; safe now (xunit runs facts within one class sequentially, and it's the only capturing class),
  but the second class that captures console output will need an xunit collection to serialize them.
  A one-line comment on `ParseCapturing` would warn the future author.
- **B5. `BuildRowsTests` builders duplicate the library tests' builders** — acknowledged in-file as
  deliberate ("mirroring"), and they retire with R1. Accept; don't share test infrastructure across
  repos for a temporary seam.
- **B6. `App.Tests` runs WinUI-projected types in a plain host** — `Thickness`/`Visibility` in the
  `SourceMargin`/chevron tests work because they're projection structs, while the `Brush` getters
  (which need `Application.Current`) are excluded by convention, documented in the csproj. The
  convention is one comment away from being violated by a future test; the csproj comment covers it —
  just keep that discipline when M2 adds tests.

## C. Maintainability & ease-of-reasoning pass

The question asked of every file this round: *can a competent stranger predict where something lives,
and trust what they read?*

### C1. What now actively helps (preserve these as conventions)

- **Predictable placement.** Parse → `CliOptions`; orchestrate → `*Command`; print → `ConsoleRenderer`;
  audit → `WriteBackAuditLog`; contract rules → library; per-item display → `ViewModels\Rows`;
  UI-free display policy → `Models`. Round 1's "four responsibilities in one static class" is gone;
  there is now exactly one plausible home for each kind of change, which is the property that keeps
  diffs small and reviews easy.
- **Invariants stated where they're enforced.** `EffectiveExposure`'s "THE … rule / MUST agree" docstring,
  `IssuesFor`'s "THE definition every consumer derives from", `ExecutePlan`'s "guards → execute → render
  → audit" — load-bearing rules are written at the single point that owns them, not (only) in distant
  docs. This is the cheapest form of architectural enforcement available and it's being used well.
- **Straight-line pipelines, unchanged.** Every flow still reads top-to-bottom; the split didn't
  introduce indirection for its own sake (no interfaces, no DI container, no command framework —
  static classes were the right weight for two verbs).
- **The audit log mirrors the pipeline**, so runtime behavior remains explainable after the fact.

### C2. Remaining reasoning hot-spots (ranked)

1. **`ReconciliationLoader.BuildRows` is still the densest code in either head** (~200 lines: closure
   over `panelOrdinal`, a `void EmitRows` local with three nested row-factory locals capturing
   `badge`/`flagged`, pairing + mixed detection + a 6-key sort comparator). It is now *pinned* by seven
   tests, which converts it from "risky" to merely "dense" — but it remains the file a newcomer will
   struggle with. The agreed disposition stands: **R1 (move the cell projection into the library) is
   M2's opening move**, and the pinned tests make it nearly mechanical. Resist the temptation to
   prettify it in place before then; touching it twice buys nothing.
2. **`MainViewModel`'s expansion bookkeeping** (three `HashSet`s, stringly composite keys built in three
   places, restore logic split between `ApplyFilters` and group construction). Two of the new tests
   (`Expansion_SurvivesFilterRoundTrip`, `RollupExpansion_RestoredAcrossRebuilds`) now guard the
   behavior, which removes the urgency. Fold the §7.5 `ExpansionState` extraction into M2, when edit
   state will force this area open anyway.
3. **`ReconciliationRow`'s 23-parameter constructor** — `Make.cs` now exists partly to hide it, which
   is the test suite telling you what production callers feel. Regroups naturally under R1; no action
   before then.
4. **`LoadAsync`'s `CancellationToken` still promises more than it delivers** (§7.2, only the scanner
   observes it). Now that the library is in scope: thread it through `TargetSchedulerReader.ReadPlanData`
   and `TargetResolver.Resolve`, or drop the parameter. Either resolves the false affordance; threading
   it is preferable with the M2 editor coming (a reload-during-edit will want real cancellation).
5. **`Row_ItemClick`'s type-switch + `RowTemplateSelector`** still encode the same dispatch twice —
   carried from round 1, still three cases, still fine; revisit only if M2 adds row types.

### C3. Verdict

Slice 1 measurably improved both review axes. Concretely: the three cross-cutting drift hazards
(effective-seconds, flag derivation, dev defaults) are now zero; `Program.cs` went from 465 lines / 4
responsibilities to 46 / 1; TCM-side logic went from 0 tests to 45 (with the library at 121); and the
one dishonest folder name (`Models\`) now tells the truth. The codebase's reasoning story is currently
limited by exactly one file — `BuildRows` — and that has a funded plan (R1 at M2) plus a safety net.
No regressions were found; the two real (if small) findings are B1 and the §7.2 token.

## D. Updated backlog

| # | Item | When | Size |
|---|---|---|---|
| B1 | `--apply=false` arms apply — tighten or warn, + test | next CLI touch | XS |
| §7.2 | Thread `CancellationToken` through reader/resolver (library) or drop the parameter | with M2 reload/edit work | S |
| R1 | Cell projection → library (`ReconciledCell` + projector); `BuildRows` becomes a thin mapper; row ctor regroups | **M2 opening move** (agreed) | M |
| M2-prep | `TsEditSession` coordinator + injectable loader seam (replaces `SetRowsForTest` as the long-term seam) | M2 | M |
| §7.5 | `ExpansionState` helper for the VM's expansion sets/keys | M2, opportunistic | S |
| B2–B4 | `continue` after unknown-key warn; thread-safety doc line on report indexes; comment on console-capture test pattern | opportunistic | XS |

Round-1 items now closed: C1, §7.1, R1-prep, R2, R3, V1, V2, §6 (initial coverage), §4.5, §4.6 (kept,
per review), §5.1 (option 1). Migration audit: clean in both rounds.
