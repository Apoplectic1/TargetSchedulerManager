# TSM Code Review — 2026-06-10

> **ARCHIVED 2026-07-26 — executed, and largely about code that no longer exists.** The entire CLI half
> (§1.2, §2.2, §2.3, §3.1, §7.1) concerns the `tsm` console head **removed 2026-06-11**. R1 shipped
> 2026-06-11 (last leak closed 2026-06-26); §5.4's 13-column XAML duplication was solved by `GridColumns.cs`;
> §7.5 `ExpansionState` shipped; §8's migration audit is single-sourced in `ARCHITECTURE.md`. The §9
> sequencing table's one `⏳ deferred` row is **closed or superseded** — `TsEditSession` became `TsEditGate`
> (2026-06-26) and the loader interface never landed (`SetRowsForTest` remains the seam; the seams that did
> land are `ITsEditor`/`ITsWriteBackApplier`). §5.2's view/VM seam contract graduated to `DOMAIN.md`
> checklist step 9, full rules in `CONVENTIONS.md`; §2.1's single-forward-pass property graduated 2026-07-26
> into that same new **`CONVENTIONS.md`**. **Still live:** §7 item 2 — the `CancellationToken` false affordance (the finding names
> `LoadAsync`, since split into `ScanLibraryAsync` + `ResolveAsync`; it is `ResolveAsync` that still ignores
> the token), carried to `ROADMAP.md` → *Carried forward*.

> **Executed status (2026-06-10, "slice 1"):**
>
> | # | Status | Where |
> |---|---|---|
> | R1-prep | ✅ effective-seconds rule single-sourced as `EffectiveExposure` (library); both planners + `BuildRows` consume it | Library `c381a2e` |
> | R2 | ✅ `CatalogBuildReport.IssuesFor/IsIdentityFlagged/AliasMemberCount/IsUnanchoredName` (+`TargetMatchIssues` enum — renamed from `…Flags` for CA1711); planner + loader consume | Library `c381a2e`, `8bf1aef` |
> | R3 | ✅ `Reconciler.MergeFamilies`; `PrintReconciliation` consumes it | Library `c381a2e`, TSM `b3d8b5d` |
> | C1 + §7.1 | ✅ `Program.cs` → `Cli\{CliOptions, BuildCommand, WriteBackCommand, ConsoleRenderer, WriteBackAuditLog, CliLog}` + shared `ExecutePlan` tail; unknown-option/stray-arg warnings | TSM `b3d8b5d` |
> | V1 | ✅ row types → `ViewModels\Rows\` (incl. `RowAggregates` — it consumes `ReconciliationRow`, so leaving it in `Models\` would invert the layering); UI-free `RowSource`/`RowPlane` stay in `Models\RowEnums.cs` with `Format` | TSM `651abb6` |
> | V2 | ✅ `Shared\DevDefaults.cs` linked source file (option 1); VM tolerance from `ResolveOptions.Default` | TSM `b3d8b5d`, `651abb6` |
> | §6 tests | ✅ two test projects (user chose split: Cli tests retire with the transitional CLI): `TargetSchedulerManager.Cli.Tests` (11 — CliOptions) + `TargetSchedulerManager.App.Tests` (34 — `BuildRows` pinned pre-R1, VM pipeline, row/aggregate/format rules; plain test host, brush getters excluded; `SetRowsForTest` internal seam) | TSM `2f74a9f` |
> | R1 (full cell projection), TsEditSession + loader interface, §7.2/§7.5 | ⏳ deferred — agreed as M2's opening move |
>
> Verified after each slice: library tests 108 → 121; `tsm` build/writeback output number-identical; app launch DIAG identical (786 rows / 102 groups / panels 28/10/7).

Scope: the **TargetSchedulerManager repo only** (console host `tsm` + `TargetSchedulerManager.App` WinUI 3, M1).
The sibling `Astronomy.Catalog` library was not directly inspected (separate repo, not mounted this session);
where a recommendation depends on the library's current surface, that's flagged with **[verify vs library]**.

Review emphases requested: separation of concerns, code reuse, straight-line execution pipelines, coordinator
opportunities, method-to-class assignment, MVVM consistency, library-relocation candidates, and confirmation
that no schema-migration code exists.

Files reviewed: `Program.cs`, `CliLog.cs`, both `.csproj`, and under `TargetSchedulerManager.App\`:
`App.xaml.cs`, `MainWindow.xaml(.cs)`, `RowTemplateSelector.cs`, `ViewModels\MainViewModel.cs`,
`Services\ReconciliationLoader.cs`, `Models\*` (ReconciliationRow, TargetGroupRow, PanelGroupRow,
RowAggregates, Format, ThemeBrushes), `Support\Log.cs`, `Support\ObservationWindow.cs`.

---

## Verdict at a glance

| Area | Assessment |
|---|---|
| Library/host SoC | **Strong** — Program.cs and the App genuinely contain no schema/scan/resolve logic; the contract boundary is respected |
| Straight-line pipelines | **Strong** — every flow is a single forward pass; two structural nits (verb routing, writeback tail duplication) |
| MVVM consistency | **Good with one systematic deviation** — view/VM seam is clean; the `Models\` folder is actually item view-models carrying WinUI types |
| Code reuse | **Good in intent, three concrete drift hazards** — effective-seconds rule, report-flag derivation, dev-default paths each exist in two places |
| Migration code | **Clean** — verified, none present (details below) |
| Tests | **Gap is growing** — the "no tests, thin host" rationale no longer fully holds: `BuildRows` and `ApplyFilters` are real logic with zero coverage |

The single highest-leverage change before M2: **move the reconciliation cell/row projection out of
`ReconciliationLoader` into the library** (R1). Almost every other finding either shrinks or disappears
once that's done.

---

## 1. Separation of concerns & method-to-class assignment

### 1.1 The library boundary is respected — good

`Program.cs` orchestrates `CatalogBuilder` / `WriteBackPlanner` / `SingleTargetPlanner` /
`TargetSchedulerWriter` and prints; it computes nothing domain-shaped itself (one exception, §1.3).
`ReconciliationLoader` runs the same scan→read→resolve pipeline in memory. This is exactly the
"library is the contract, apps are thin" shape the architecture calls for.

### 1.2 `Program.cs` is drifting past "thin host" — split by responsibility

At ~465 lines, `Program` now carries four distinct responsibilities in one static class:

1. **Argument parsing** — `ParseArgs`, `IsVerb`, `IsFlag`, `ParseTolerance`, the defaults
2. **Command orchestration** — `BuildAndReport`, `WriteBack`, `WriteBackSingleTarget`, `WriterGuardsFail`
3. **Presentation** — `PrintReport`, `PrintReadBack`, `PrintReconciliation`, `PrintWriteBack`
4. **Audit logging** — `LogWriteBackOutcome`

None of it is wrong, but each verb you add (M2/M3 will add more) compounds the mixing. Recommended
target shape — the classic **command/coordinator** decomposition, still all inside this repo:

```
Cli/CliOptions.cs          // ParseArgs + ParseTolerance + defaults → one immutable options record
Cli/BuildCommand.cs        // BuildAndReport's body: validate → build → render
Cli/WriteBackCommand.cs    // both writeback forms (see §2.2)
Cli/ConsoleRenderer.cs     // the four Print* methods
Cli/WriteBackAuditLog.cs   // LogWriteBackOutcome + CliLog calls
Program.cs                 // Main = parse + route, ~30 lines
```

Why: each verb becomes one class with one straight-line `ExecuteAsync()`, the renderer is reusable
across verbs, and `Program.Main` returns to pure routing. Cost: file count. Pro: M2/M3 verbs slot in
without touching existing ones (open/closed). Con: at exactly today's size this is borderline — but the
writeback duplication (§2.2) tips it to worth doing now.

### 1.3 `PrintReconciliation` does logic inside a print method — relocate

`Program.cs` lines 293–306: the mosaic family fold (group panel reconciliations by `ParentTargetId`,
`Reconciler.Merge` each family under the parent) is a **projection**, not presentation. It's the kind of
shaping every future consumer of `GetReconciliation()` (the Phase-3 grid when it retargets Catalog.db,
TP, IS) will need identically.

Recommendation: lift the fold into the library — e.g. `Reconciler.MergeFamilies(IReadOnlyList<Target>,
IEnumerable<TargetReconciliation>)` or a `CatalogStore.GetReconciliationRollup()` convenience —
**[verify vs library]** that nothing equivalent already exists beside `Reconciler.Merge`. The print method
then consumes an already-shaped list. This follows the shared-library discipline: the fold is contract
behavior ("panels roll up under parents"), not console behavior.

### 1.4 App-side class assignment — mostly right

`ReconciliationLoader` (data layer), `MainViewModel` (state + filter/sort/flatten), `RowAggregates`
(one aggregation rule for both header levels), `RowTemplateSelector` (dispatch only), `Format`,
`ThemeBrushes`, `Log`, `ObservationWindow` — each has one job and a docstring saying what it is. Two
exceptions: the `Models\` naming problem (§5.1) and `BuildRows` living in the app at all (§4.1).

---

## 2. Straight-line execution pipelines

### 2.1 The good news

Every major flow is already a single forward pass with no back-edges:

- `BuildAndReport`: validate inputs → build → print report → print read-back → print reconciliation.
- `ReconciliationLoader.LoadAsync`: scan → TS read → resolve → project rows → log → return.
- `MainViewModel.ApplyFilters`: filter → group → sort → flatten → publish. One pass, no re-entry.
- `WriteBack` (both forms): plan → guard → execute → print → audit → exit code.

This is worth preserving as a stated convention; it's the property that makes the CLI auditable
(the audit log is literally the pipeline's trace).

### 2.2 Writeback tail duplication — extract the shared coordinator

`WriteBack` (lines 144–159) and `WriteBackSingleTarget` (lines 209–225) end in a near-identical
nine-step tail: open writer → `WriterGuardsFail` → print `user_version` → `Execute(plan, apply)` →
`PrintWriteBack` → `LogWriteBackOutcome` → compute rc from `VerifyFailures` → log `writeback end` →
return. The two forms differ **only in how the `WriteBackPlan` is produced** (bulk: rebuild + planner;
surgical: scan one dir + `SingleTargetPlanner`).

That's a textbook **template-method/coordinator** seam:

```csharp
// Cli/WriteBackCommand.cs
private static async Task<int> ExecutePlan(
    WriteBackPlan plan, string tsDb, bool apply, bool listNoOps)
{
    using TargetSchedulerWriter writer = new(tsDb);
    if (WriterGuardsFail(writer)) { CliLog.Line("writeback end rc=1"); return 1; }

    Console.WriteLine($"TS schema user_version {writer.SchemaUserVersion} (validated by column presence)");
    Console.WriteLine();

    WriteBackResult result = writer.Execute(plan, apply);
    ConsoleRenderer.PrintWriteBack(plan, result, apply, listNoOps);
    WriteBackAuditLog.LogOutcome(plan, result);
    int rc = result.VerifyFailures.Count == 0 ? 0 : 1;
    CliLog.Line($"writeback end rc={rc}");
    return rc;
}
```

Both verbs become "make a plan, hand it to `ExecutePlan`". Pro: one place to evolve the guard/verify/audit
contract (M2 will). Con: none of substance — the deliberate bulk/surgical asymmetry (surgical never zeroes
unmatched plans) lives in the *planners*, not the tail, so it's unaffected.

### 2.3 Route the `--target` subform in `Main`

`WriteBack` currently parses options, then mid-function dispatches to `WriteBackSingleTarget`
(line 117). Moving all routing into `Main` (verb → form → command) keeps each command method fully
straight-line and puts the entire dispatch decision in one screenful. Minor, but it's the same principle
the file already applies to verb routing.

---

## 3. Coordinators

### 3.1 CLI

Covered by §1.2/§2.2 — verb command classes *are* the coordinators here; no further machinery needed.
Avoid a generic "command framework": two verbs don't justify one, and `System.CommandLine` would add a
dependency for parsing this app does adequately in 12 lines (see §7.1 for the one parsing gap worth fixing).

### 3.2 App — the M2 seam, flagged now

For M1 (read-only), `MainWindow` → `MainViewModel` → static `ReconciliationLoader` is appropriately
simple; a coordinator today would be ceremony. But M2 adds an edit session with real lifecycle: load →
edit (tiers) → validate → save via `TargetSchedulerEditor` → re-scan → refresh, plus dirty-state and
the `filtercadence`-clear hazard from the roadmap. That lifecycle should **not** accrete into
`MainViewModel` the way state naturally gravitates to a main form.

Recommendation for M2's design: introduce a `TsEditSession` (app `Services\`) owning the
loaded-snapshot + pending-edits + save pipeline, with `MainViewModel` delegating to it. That keeps the
VM as "state the XAML binds" and gives the edit lifecycle a class whose invariants are statable. Also at
that point, put an interface (or injected delegate) in front of `ReconciliationLoader` so the VM/session
become unit-testable without a disk and TS snapshot — today `MainViewModel` is untestable except by
running the app (§6).

---

## 4. Code reuse & library-relocation candidates

### 4.1 R1 (highest value): the cell projection in `ReconciliationLoader.BuildRows` belongs in the library

`BuildRows` (~250 lines including local functions) is a **reconciliation projection engine**: aggregate
plans + inventory per `(filter, purpose, whole-seconds)` cell, pair plan-side and disk-side, derive
rollup-vs-detail (mixed sub-lengths), derive flags from `CatalogBuildReport`, order deterministically.
Only a thin outer layer of it is UI: badge strings, `RowSource`, panel labels.

Why this is the right relocation:

- **It re-implements contract rules.** Line 145: `seconds = round(plan.ExposureSeconds ?? template default)`
  — that is write-back's load-bearing "the plan's whole-second exposure is its spec" rule
  (ARCHITECTURE.md Phase 4), implemented a second time, by hand, in an app. If the rounding or the
  fallback ever changes in `WriteBackPlanner`, the grid silently disagrees with what `tsm writeback` does.
  **[verify vs library]** — if the library doesn't already expose this as a helper, extract one:
  `EffectiveExposure.Seconds(ExposurePlan, ExposureTemplate)`, used by both planners and the projection.
- **M2 needs the same cells.** The roadmap's edit grid is keyed (target, filter, purpose, seconds) —
  identical to these cells. The editor will need the projection *plus* write access; building M2 on an
  app-private projection means porting it later anyway.
- **It's the largest untested logic in the repo** (§6), and the library is where the tests live.

Suggested shape: a pure `Reconcile/CellProjector` (or similar) in the library producing neutral records —
`ReconciledCell { Filter, Purpose, Seconds, Desired, Acquired, Accepted, Disk, PlanCount }` grouped per
target/panel with a `MixedSeconds` rollup notion — and `BuildRows` shrinks to mapping those onto
`ReconciliationRow` (badges, labels, brushes). The deliberate split — neutral math in the library,
presentation vocabulary in the app — also satisfies the no-consumer-terminology rule.

### 4.2 R2: flag-set derivation from `CatalogBuildReport`

`BuildRows` lines 70–77 builds `HashSet`s of alias/dup/mismatch/ambiguous directories and unanchored
names from the report; `WriteBackPlanner` necessarily derives the same "is this target flagged?"
classification for its manual-bucket rule. **[verify vs library]** — if so, give `CatalogBuildReport`
the membership API directly (e.g. `report.IsFlaggedDirectory(dir)`, `report.FlagsFor(target)` returning
a small flags enum). Pro: one definition of "flagged", and the report type stops being a bag of lists
each consumer re-indexes. Con: widens the report's surface slightly — but it's *its own* data.

### 4.3 R3: mosaic family rollup (§1.3) — into the library beside `Reconciler.Merge`.

### 4.4 Items that should **stay** in TSM

- `Format.Hours`, `ThemeBrushes`, `RowAggregates`, the group-row types — display policy.
- `CliLog` / `Support.Log` — see §4.6.
- Dev-default paths — machine-specific, correctly kept out of the contract (but see §4.5).
- `ObservationWindow` — app tooling. (Though note it's a *port* of TP's `UserObservationDialog`; that's
  now two hand-maintained copies of the observation-log pattern across the portfolio, three counting
  `CliLog`'s mini-variant. If a third app wants it, that's the trigger for a small shared
  `Astronomy.Diagnostics` utility assembly — deliberately *not* `Astronomy.Catalog`, which is a schema
  contract, not a grab-bag.)

### 4.5 Dev-default duplication inside this repo

`Program.cs` (lines 23–25) and `MainViewModel` (lines 28–30) each define `DefaultLibrary` / `DefaultTs`
(and the tolerance default exists as `ResolveOptions.Default` in the library *and* `DefaultToleranceDegrees = 0.5`
in the VM). Two copies of machine-specific config drift silently — and the failure mode is nasty
(CLI and app quietly scanning different trees).

Structural cause: there is no TSM-shared assembly between the two apps, and these constants must not go
in the library. Cheapest fixes, in order of preference:

1. A shared **linked source file** (`<Compile Include="..\Shared\DevDefaults.cs" Link="..."/>` in the App
   csproj) — zero new projects, one definition.
2. A `tsm.settings.json` beside the exe both apps read — also the natural seed for the App's future
   settings page.
3. Accept duplication but add a cross-reference comment in both files (currently only the VM has one).

For the tolerance specifically: drop the VM's `DefaultToleranceDegrees` and use
`ResolveOptions.Default.MatchToleranceDegrees` — the library already owns that number.

### 4.6 The two loggers

`CliLog` and `Support.Log` share the same skeleton (static path under
`%APPDATA%\TargetSchedulerManager\Logs`, lock, append, swallow). The divergence is *policy* (append-only
audit vs session-rotated diagnostics with categories and USER_OBS markers), and both are small and
correct. Recommendation: **leave them** — unifying ~30 shared lines behind an abstraction would couple
two deliberately different lifecycles for negligible savings. Revisit only under the
`Astronomy.Diagnostics` trigger in §4.4.

---

## 5. MVVM consistency

### 5.1 The one systematic deviation: `Models\` types are item view-models with WinUI types in them

`ReconciliationRow` exposes `Brush? SecondsBackground / HoursBackground`, `Thickness SourceMargin`,
`Visibility ChevronVisibility`, glyph strings; `TargetGroupRow` / `PanelGroupRow` likewise carry brushes
and glyphs. These aren't models — they're **per-item view-models** (and well-built ones: immutable except
`IsExpanded`, INPC raised precisely). The naming isn't cosmetic; it has two real costs:

- Nothing in `Models\` can be referenced from a non-WinUI context — which blocks both unit-testing the
  row logic and any future extraction (the `Hours` sign convention and `Matches` search rule are logic).
- The folder name misstates the dependency direction for the next person (or the M2 implementer) adding
  a type there.

Options, cheapest first:

1. **Rename the concern**: move row types to `ViewModels\Rows\` (namespace follows), keep `Format`,
   `RowAggregates` (already UI-free) in `Models\`. No behavior change, honest layering. *Recommended.*
2. Strip WinUI types from rows: replace `Brush`/`Visibility`/`Thickness` properties with semantic enums
   (`CellTone.Caution/Success/...`) bound through `x:Bind` static converter functions in the templates.
   Purer (rows become portable + testable), more XAML churn. Worth doing opportunistically for any row
   type R1 wants to relocate; not worth a dedicated pass.

### 5.2 What's consistently right

- `MainWindow.xaml.cs` is genuinely thin: every handler is a one-line forward to the VM; no control
  writes from code-behind; display flows back exclusively through `{x:Bind}` + INPC. The documented
  WinForms-analogy convention is applied uniformly.
- `MainViewModel` has zero `Microsoft.UI.*` references — the VM would survive a UI-framework swap.
- `x:Bind` modes are correct throughout: `OneWay` exactly where state mutates (`Rows`, `IsLoading`,
  texts, `ChevronGlyph`), default `OneTime` for the immutable row properties. No stray classic
  `{Binding}`.
- In-place `ObservableCollection` editing for toggles vs wholesale replacement for filter changes is a
  deliberate, documented, correct trade-off (scroll preservation vs simplicity).

### 5.3 Acceptable deviations, with the nuance stated

- **Event handlers instead of commands/TwoWay binds.** For the search box, note `x:Bind TwoWay` on
  `TextBox.Text` defaults to updating on focus loss; live filtering needs either the current
  `TextChanged` forwarding or `Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` — both are legitimate
  WinUI idioms, so the handler stands. `SourceFilter`/`SortPicker`
  could become `SelectedIndex="{x:Bind ViewModel..., Mode=TwoWay}"` and the Expand/Collapse/Reload
  buttons could bind methods directly (`Click="{x:Bind ViewModel.ExpandAll}"`), retiring four trivial
  handlers. Low value; do it when the file is open anyway.
- **`Row_ItemClick` type-switch in code-behind** duplicates the template selector's dispatch. Fine at
  three cases; if M2 adds more row types, consider a small `IToggleable` on the row types so both
  dispatches collapse.
- **`ObservationWindow` built imperatively** — documented rationale (port fidelity), support tooling,
  outside the MVVM surface. Fine.

### 5.4 XAML duplication nit

The 13-column `ColumnDefinitions` block is repeated four times (3 templates + header) with a
comment acknowledging it. WinUI has no shared ColumnDefinitions; the practical mitigation is extracting
the widths to `<x:Double x:Key="ColW_Source">110</x:Double>` resources so a width change is one edit.
Verbose; only worth it the first time a width change actually bites.

---

## 6. Tests

CLAUDE.md's position — "no tests in this repo, Program.cs is a thin host, logic is tested in the
library" — was true at Phase 2 and is **no longer quite true**: `BuildRows` (cell math, pairing, mixed
detection, ordering comparator), `RowAggregates.Compute` (signed-hours and remaining rules),
`MainViewModel.ApplyFilters`/toggle bookkeeping, and `ReconciliationRow.Hours`' sign convention are all
real logic with zero coverage, exercised only by eyeballing the grid.

R1 resolves the largest piece by moving it where the tests live. For what remains app-side:
`RowAggregates` and `Format` are already UI-free and trivially testable; the VM becomes testable once
the loader is behind an interface (§3.2); the row types become testable under §5.1 option 2. A minimal
`TargetSchedulerManager.App.Tests` project covering aggregates + filter/group/flatten would catch the
regressions M2 editing is most likely to introduce. Suggested sequencing: fold this into the M2
milestone rather than retrofitting now.

---

## 7. Smaller findings

1. **`ParseArgs` silently ignores the unknown.** A typo'd flag (`--tolerence 0.7`) or stray positional
   arg vanishes and defaults apply — in a tool whose verbs write to a database. The dry-run default
   mitigates, but a one-liner that warns on unconsumed tokens / unknown keys would close the gap
   cheaply. Also note duplicate flags are last-wins, undocumented (fine, but say so in the usage text).
2. **`ReconciliationLoader.LoadAsync` accepts a `CancellationToken` but only the scanner observes it**
   — the TS read and resolve run to completion regardless. Harmless at ~1 s totals; either thread the
   token through (library change **[verify vs library]**) or drop the parameter so the signature doesn't
   promise cancellation it can't deliver.
3. **`ThemeBrushes` does a resource lookup per property-get** during item realization. Caching would be
   trivial but *wrong-ish* under a light/dark theme switch mid-session; current behavior is the safer
   default. Leave it; comment already explains the defensive lookup.
4. **`ReconciliationRow`'s constructor takes 23 parameters.** With R1 it naturally regroups (a
   `ReconciledCell` + presentation args). If R1 is deferred, consider bundling the panel triple
   (`panelKey/panelLabel/panelSource`) into one optional record for readability.
5. **Composite string keys** (`target|panel`, `target|panel|filter|purpose`) for expansion state are
   built in three places in `MainViewModel`. A tiny `ExpansionState` helper class (the three sets + key
   builders + `Restore(row)`) would gather the concern and remove the format-string coupling; the M2
   editor will almost certainly grow this state further.
6. **`TargetGroupRow` reads `children[0]`** — safe today because `ApplyFilters` only constructs groups
   from non-empty groupings; worth a `Debug.Assert` or doc line since the ctor is public.

---

## 8. Migration-code audit — clean ✔

Searched the repo (excluding `bin`/`obj`) for `migrat*`, `user_version`, `schema_version`, `PRAGMA`:

- **No migration framework, no version-gated upgrade paths, no legacy-format shims anywhere in TSM.**
  The only matches are documentation *stating* the no-migration rule (ARCHITECTURE.md, ROADMAP.md,
  CLAUDE.md).
- `Program.cs` lines 151/216 print TS's `user_version` **for information only**; compatibility is gated
  on column presence (`writer.HasRequiredColumns`), exactly as the architecture prescribes (TS bumps
  `user_version` every NINA nightly, so exact-version checks would be wrong).
- The `-1`-sentinel normalization noted in `BuildRows` is upstream hardening in the resolver, not
  version migration.

Consistent with the "catalog is fully derived; schema change = delete Catalog.db" invariant.

---

## 9. Recommended sequencing

| # | Change | When | Effort |
|---|---|---|---|
| R1 | Extract cell projection (+ effective-seconds helper) from `BuildRows` into `Astronomy.Catalog` | before M2 design hardens | M |
| R2 | `CatalogBuildReport` flag-membership API; both consumers use it | with R1 | S |
| R3 | Mosaic family rollup → library; `PrintReconciliation` consumes it | with R1 | S |
| C1 | Split `Program.cs` into Cli/ command + renderer + audit classes; shared writeback tail; route `--target` in `Main` | next CLI touch | S–M |
| V1 | Rename `Models\` row types → `ViewModels\Rows\` | anytime, trivial | S |
| V2 | Single source for dev-default paths (linked file or settings file); VM tolerance from `ResolveOptions.Default` | anytime | S |
| M2-prep | `TsEditSession` coordinator + loader interface + App test project | as part of M2 | M |
| Q | §7 items 1, 2, 5 | opportunistic | S |

Items marked **[verify vs library]** (effective-seconds helper, report flag API, family-merge helper,
cancellation threading) need a quick check of `..\Library\Astronomy.Catalog` before implementing — they
were assessed from this repo plus the documented contract, not from the library source.
