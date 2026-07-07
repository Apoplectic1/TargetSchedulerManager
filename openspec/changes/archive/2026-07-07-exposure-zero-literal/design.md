# Design — exposure-zero-literal

## Context

Exposure-0 flows through three TSM paths; after the Library's `d26b75e` adjudication only one diverges:

```
exposure-0 plan
├─ A. Flyout seed          raw column read → box shows 0, "use default" unchecked   ✓ already correct
├─ B. Grid Seconds (load)  EffectiveExposure.Seconds raw-TS overload (< 0 defers)   ✓ always 0-literal
└─ C. Mirror after write   MainWindow:366  v > 0 ? round : null  → null
                           → TsEditGate:116  found && value > 0  → null
                           → Seconds cell stays stale until reload                  ✗ the gap
```

Before `d26b75e`, path C showed the *template default* after writing 0 — actively wrong versus the reload
(path B). Now it shows nothing (stale). This change closes the remaining gap so a committed 0 mirrors as 0,
matching Library `CONSUMERS.md` #19/#20 and the standing rule that a flyout edit reflects in its column at
once. No TSM test pins the old behavior (stubs inject values), so nothing needs unpinning.

## Goals / Non-Goals

**Goals:**
- A verified exposure-0 write mirrors the Seconds cell to 0 immediately (no reload).
- `ReadPlanEffectiveSecondsAsync` returns a resolved 0 instead of discarding it as "unknown".
- The −1 sentinel path is byte-for-byte unchanged (write −1 → resolve template default via the db).

**Non-Goals:**
- No Library changes (its three sites already agree; contract tests pin them).
- No schema change — `exposureplan.exposure` keeps `Min: 0` (0 is TS-legal; TSM renders faithfully).
- Not touching the `effective` resolver's `row.PlanSeconds > 0` guard in `MainWindow.xaml.cs:336`
  (see Decisions).

## Decisions

1. **`TsEditGate.ReadPlanEffectiveSecondsAsync`: `value > 0` → `value >= 0`.** Null now means only
   "unknown" (missing row/template, fault) — never a real resolved value. The Library never returns a
   negative from `ReadPlanEffectiveExposure` (a stored −1 resolves through the template), so `>= 0` accepts
   everything it can legitimately return; keeping the `>= 0` guard (rather than dropping the comparison)
   defends the seam against a stubbed/faulty editor handing back a raw sentinel. Doc comment reworded:
   "missing row/template or a fault", not "non-positive value".

2. **`MainWindow.xaml.cs:366`: `v > 0` → `v >= 0`.** A committed 0 computes its own mirror (0) directly —
   no db round-trip; only the −1 sentinel takes the resolve-via-db path. This alone would fix the visible
   symptom, but decision 1 is still needed so the fallback path is honest (and for any future caller).

3. **Leave `MainWindow.xaml.cs:336` (`row.PlanSeconds > 0`) alone.** That guard feeds the "use default (…)"
   label for a *sentinel-holding* plan; it returns null only when the template default itself is ≤ 0 — a
   pathological template config, and the control degrades gracefully (label without the number). Changing it
   would let a 0 template-default display as "use default (0 s)", which is arguably nicer but expands scope
   for a config that shouldn't exist; skip.

4. **Test at the ViewModel seam, mirroring the existing pattern.** `MainViewModelTests` already exercises
   `SetPlanExposureAsync(row, -1, mirrorSeconds: null)` with a stubbed `EffectiveExposure = (true, 300.0)`.
   Add the 0 case: stub `(true, 0.0)`, call `SetPlanExposureAsync(row, 0, mirrorSeconds: null)`, assert the
   row's plan-seconds applied 0. This pins decision 1 end-to-end through the gate. (Decision 2 short-circuits
   before the gate in production; passing `mirrorSeconds: null` in the test exercises the fallback the way
   the −1 tests do.)

5. **(Discovered during apply) Leave the row model's `PlanSeconds == 0` = "none/unknown" convention alone.**
   `ReconciliationRow` uses 0 as its own no-seconds marker, and the TS-only plane's `SecondsText` renders 0
   as "—" (`ReconciliationRow.cs:253`) — so on a plan-only row a committed 0 mirrors as "—", not "0". That
   is *also* what the next load shows (both paths share `SecondsText`), so the real invariant — mirror ==
   reload — holds on every plane; only the plan+disk plane literally displays "0". De-conflating would mean
   `int?` plan seconds through the row model and reconciliation keying, a refactor this degenerate corner
   doesn't justify. The delta spec scenario states the invariant accordingly.

## Risks / Trade-offs

- [A 0-second plan is degenerate — TS would schedule 0 s subs] → Not TSM's call: the TS planner takes 0
  literally, TSM is a faithful manager. Rendering the truth beats hiding it; the user sees 0 and can fix it.
- [Stubbed editor returns a negative effective value] → the retained `>= 0` guard maps it to null (leave
  cell for reload), same containment as today.

## Migration Plan

None — no persisted format, no back-compat (rule: build for the target state). Requires Library ≥ `d26b75e`,
already on disk via the cross-repo `ProjectReference`.

## Open Questions

None.
