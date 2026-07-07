# Tasks — exposure-zero-literal

## 1. Code

- [x] 1.1 `TsEditGate.ReadPlanEffectiveSecondsAsync` (`TargetSchedulerManager.App\Shared\TsEditGate.cs:116`):
      change `found && value > 0` to `found && value >= 0`; reword the doc comment so null means only
      "missing row/template or a fault" (drop "non-positive value").
- [x] 1.2 `MainWindow.xaml.cs:366` (exposure commit in `ShowEditFlyoutAsync`): change `v > 0` to `v >= 0`
      so a committed 0 mirrors directly; only the −1 sentinel resolves via the db. Update the adjacent
      "Seconds-cell mirror" comment if its wording implies positive-only.

## 2. Tests

- [x] 2.1 `MainViewModelTests`: add a mirror-at-0 test alongside the existing −1 cases — stub
      `EffectiveExposure = (true, 0.0)`, call `SetPlanExposureAsync(row, 0, mirrorSeconds: null)`, assert
      success and the row's plan seconds applied 0 (Seconds cell mirrors without a reload).
- [x] 2.2 Confirm the existing sentinel tests (`SetPlanExposureAsync(row, -1, …)` with `(true, 300.0)` and
      `(false, null)`) still pass unchanged.

## 3. Verify + docs

- [x] 3.1 `dotnet build` + full TSM test run green (per VERIFICATION.md).
- [x] 3.2 State the feature-correct boundary for the user: visual check = commit 0 in a plan flyout →
      Seconds cell shows 0 at once; reload agrees. (Needs a throwaway exposure-0 plan on the local copy —
      revert or skip the push afterwards.)
- [x] 3.3 CLAUDE.md / ROADMAP touch only if warranted (corner-case fix; a NOTEBOOK.md line is likely
      enough) — same commit as the code either way.
