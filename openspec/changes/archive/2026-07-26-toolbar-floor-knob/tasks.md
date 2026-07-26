## 1. Right-size the two up-downs

- [x] 1.1 `MainWindow.xaml.cs`: rename `DesiredBox_Loaded` → `NarrowNumberBox_Loaded` (logic unchanged — inner `TextBox` centered, `MinWidth = 0`, tight padding) and reword its comment to describe the general narrow-box case rather than the Desired box specifically.
- [x] 1.2 `MainWindow.xaml`: point the grid's Desired `NumberBox` (line ~144) at `NarrowNumberBox_Loaded`.
- [x] 1.3 `MainWindow.xaml`: give `VisibleDuration` `Width="80"` + `Loaded="NarrowNumberBox_Loaded"`, keeping `SpinButtonPlacementMode="Inline"`, `MinWidth="0"`, and its range/defaults.
- [x] 1.4 `MainWindow.xaml`: give the Floor box `Width="70"` + `Loaded="NarrowNumberBox_Loaded"`, same treatment.
- [x] 1.5 Build (`dotnet build`) — expect zero errors; a stale-LSP resolve cascade mid-build is noise, re-run to confirm.

## 2. Rename the knob to Floor (code)

- [x] 2.1 `MainWindow.xaml`: `x:Name="VisibleHorizon"` → `VisibleFloor`; label `"Horizon:"` → `"Floor:"`; reword the box tooltip ("Altitude floor…" → keep the meaning, drop the word "Horizon") and the Tonight button tooltip ("above Horizon tonight" → "above the Floor tonight"); update the group comment (line ~263).
- [x] 2.2 `MainWindow.xaml.cs`: `VisibleHorizon.Value` → `VisibleFloor.Value` in `VisibleTonight_Click`; update the two comment lines (58, 60) to say Floor.
- [x] 2.3 `ViewModels/MainViewModel.Reports.cs`: `horizonAltitudeDeg` → `floorAltitudeDeg` in `RunVisibleTonightAsync`'s signature, `<paramref>` doc, and the `PlanTargets` call.
- [x] 2.4 `Services/VisibleTonightPass.cs`: `horizonAltitudeDeg` → `floorAltitudeDeg` in `PlanTargets`; update the class doc comment ("the toolbar's Duration/Horizon knobs" → Duration/Floor). Leave `using Astronomy.Core.Horizons`, `ScalarHorizonProfile`, `IsAboveHorizonForAtLeast`, and the `altitudeFloor` local untouched.
- [x] 2.5 `Tests/VisibleTonightPassTests.cs`: rename every `horizonAltitudeDeg:` argument to `floorAltitudeDeg:`; rename `HorizonAltitudeFloor_GatesLowTargets` → `AltitudeFloor_GatesLowTargets` and the comment on line ~267 that cites it. Leave the geometric-horizon prose (lines ~60, ~80, ~266) alone.
- [x] 2.6 `Tests/MainViewModelBusyGateTests.cs`: the four `horizonAltitudeDeg: 0` arguments → `floorAltitudeDeg: 0`.
- [x] 2.7 Confirm no knob-scoped "horizon" residue: grep `[Hh]orizon` over `TargetSchedulerManager.App*` and check every remaining hit is a TS column (`usecustomhorizon`/`horizonoffset`), a library symbol, or geometric-horizon prose.
- [x] 2.8 Build + `dotnet test` — all tests green (baseline 230).

## 3. Specs

- [x] 3.1 Sync the two delta specs into `openspec/specs/` (`/opsx:sync` or by hand): `visible-tonight-toggle` (predicate, input contract, applied summary) and `busy-exclusion` (mutual exclusion, disabled surfaces).
- [x] 3.2 Hand-edit the `visible-tonight-toggle` **Purpose** paragraph — deltas carry requirements only, so its "one Find press" and "toolbar's Horizon altitude floor" wording needs the same Tonight/Floor correction.
- [x] 3.3 `openspec validate --change toolbar-floor-knob --strict` (and re-validate the main specs after sync).

## 4. Reference docs

- [x] 4.1 `ARCHITECTURE.md` (~306–336): Horizon → Floor in the Visible-Tonight section (knob name, "Horizon altitude floor", the Duration/Horizon knobs sentence) and **Find** → **Tonight** for the button. Keep the `CoarseVisibility.IsAboveHorizonForAtLeast(... ScalarHorizonProfile(horizonDeg) ...)` call signature accurate to the library — rename only the argument name if the code changed it, and keep TS's own gates list (`custom horizon/offset`) as-is.
- [x] 4.2 `DOMAIN.md`: toolbar map (~211) → "(Duration + Floor up-downs + Tonight)"; extend the integer-edit-box convention (~162–165) with the **inline-spinner case** (spinner block is template-fixed, so budget digits + ~56 px; Duration 80 / Floor 70 as the live example); update the WinUI-gotchas entry (~232–236) and checklist step 6 (~257) to name `NarrowNumberBox_Loaded`.
- [x] 4.3 `VERIFICATION.md` (~27): "(Duration + Horizon up-downs + Find)" → "(Duration + Floor up-downs + Tonight)".
- [x] 4.4 `CHANGELOG.md`: new newest-first entry covering the resize, the Floor rename with its explicit non-scope, and the Find→Tonight doc correction. Do **not** retouch older entries.

## 5. Verify + close

- [x] 5.1 Build + full test run clean; report build/test status separately from visual status.
- [x] 5.2 Commit code + specs + docs together (one commit, per the docs-are-load-bearing rule).
- [x] 5.3 Hand off for the author's visual pass: both boxes narrow with usable spinners, `480` and `89` fully visible, values now centered, label reads "Floor:", Tonight still runs the pass. **Do not run or screenshot the app unprompted.**
- [x] 5.4 After the author confirms visually, archive the change (`/opsx:archive`).
