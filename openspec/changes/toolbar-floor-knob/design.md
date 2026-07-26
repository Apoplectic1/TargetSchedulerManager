## Context

The Visible-Tonight toolbar group is `MainWindow.xaml` lines ~262–282: a `"Visible Tonight:"` caption, a
`Duration` `NumberBox` (15–480, default 30), a `Horizon` `NumberBox` (0–89, default 30), and the button
that runs the pass. Both boxes declare `MinWidth="0" Padding="4,2"` but **no `Width`**, so each measures to
its template content and lands near 110 px — most of it the inner `TextBox`'s default `MinWidth`
(`TextControlThemeMinWidth` = 64 px) plus the inline spin-button block.

The repo already solved narrow-`NumberBox` sizing once, for the grid's inline **Desired** box
(`MainWindow.xaml:138–144` + `DesiredBox_Loaded` in `MainWindow.xaml.cs:153`), and hardened it into a
DOMAIN convention: *"Integer edit boxes are ~3 characters wide … Fixed `Width` (~40 px) + trimmed inner
padding; the text is centered in code-behind."* That case sets `SpinButtonPlacementMode="Hidden"`, so its
40 px is all digits — the toolbar knobs keep their spinners and therefore need a different number.

Separately: `Content="Find"` on the pass button was changed to `"Tonight"` in the working tree and never
committed, leaving eight "Find" references across two live specs and three reference docs. The user
confirmed (2026-07-26) to keep **Tonight** and correct the docs.

## Goals / Non-Goals

**Goals:**
- Each up-down occupies only what its value range needs: Duration 3 digits, Floor 2 digits.
- The knob's user-facing and internal vocabulary is **Floor**, end to end, with no residue.
- The DOMAIN integer-edit-box convention grows a second, explicit case: *with inline spin buttons*.
- Live specs and reference docs name the button **Tonight**.

**Non-Goals:**
- No change to the visibility predicate, ranges, defaults, busy exclusion, journaling, or summary.
- No renaming of TS schema columns (`usecustomhorizon`, `horizonoffset`) or of `Astronomy.Core`'s
  `Horizons` namespace / `ScalarHorizonProfile` / `IsAboveHorizonForAtLeast`. Three different concepts
  share the word "horizon"; only the toolbar knob is in scope.
- No `..\Library` edits at all — this is a TSM-local change.
- No rewriting of `CHANGELOG.md`, dated `docs/*.md`, or archived openspec changes: those record what
  shipped under the old name and stay accurate as history.
- No custom `NumberBox` template / spin-button restyle (see D2).

## Decisions

**D1 — Explicit `Width` + a shared narrow-box `Loaded` handler.**
Setting `Width` alone does not shrink a `NumberBox`: the template-internal `TextBox` carries its own
`MinWidth` and simply overflows. The fix is the one already proven for the Desired box — walk to the inner
`TextBox` on `Loaded` and set `MinWidth = 0` plus tight `Padding`. Rather than copy that handler,
**generalize `DesiredBox_Loaded` into `NarrowNumberBox_Loaded`** and point all three boxes at it (the
handler is already idempotent and container-realization safe, which the virtualized grid required).
Centering comes along with it, which is what the DOMAIN convention prescribes for integer edit boxes — a
**visible change** to the toolbar values (currently left-aligned) for the author to eyeball.

*Alternative considered:* per-box inline `Loaded` lambdas, or a `Style` with a `Setter` on the inner
TextBox. A `Style` cannot reach template internals without a full template override (see D2), and three
copies of the same walk is exactly the duplication the presentation lane spent P1–P5 removing.

**D2 — Keep `SpinButtonPlacementMode="Inline"`; do not restyle the spinners.**
The inline spin-button block is template-fixed (~2 × 28 px) and no `Width` shrinks it, so an Inline box has
a floor near 80 px however few digits it holds. The user chose (2026-07-26) to keep the spinners always
visible and clickable rather than take `Compact` (spinners hidden behind hover, box ≈ 40 px) — the toolbar
gains less width, but the up/down targets stay real. A local template override to squeeze the buttons was
offered and declined as not worth the XAML.

**Starting widths: `Duration = 80`, `Floor = 70`** — spinner block plus a centered 3- / 2-digit text area.
These are eyeball-tuned numbers, not derived: the author runs the app and we adjust by a few px if `480`
crowds its box. Anything that clips is a bug in this change, since both maxima are inside the digit budget.

**D3 — Rename depth: the knob and everything that only ever meant the knob.**
`VisibleHorizon` → `VisibleFloor`; label `"Horizon:"` → `"Floor:"`; `horizonAltitudeDeg` →
`floorAltitudeDeg` across `MainViewModel.RunVisibleTonightAsync`, `VisibleTonightPass.PlanTargets`, and
every call site and test argument; test `HorizonAltitudeFloor_GatesLowTargets` →
`AltitudeFloor_GatesLowTargets`. The local `ScalarHorizonProfile altitudeFloor` variable already reads
correctly and stays. Prose describing the *geometric* horizon (the 0°-pinned scenario tests, the
"above-horizon arc" comment) describes the sky, not the knob, and stays.

*Why "Floor" is the right word:* the spec and the implementation both already call this an *altitude
floor*; "Horizon" was the label that drifted from the concept, and it collided with two unrelated
"horizon"s a reader meets in the same files.

**D4 — Width lives in DOMAIN.md, not in the spec.**
The spec requirement gains only a behavioral clause (each knob is sized to its digit budget with spinners
visible without hovering); the px numbers and the `Loaded`-handler mechanism belong to the UI design
language in `DOMAIN.md` — extending the existing integer-edit-box convention with the inline-spinner case
and updating WinUI-gotchas + checklist step 6 to name the generalized handler.

## Risks / Trade-offs

- **[80 / 70 px may still crowd `480`]** → The author verifies visually on the first run (the whole point
  of observation-driven UI work here); adjustment is a one-number edit with no structural consequence.
- **[Toolbar values become centered, which nobody asked for]** → It is the standing DOMAIN convention for
  integer edit boxes and arrives free with the shared handler; called out explicitly for the visual pass,
  and trivially droppable by giving the toolbar boxes their own handler if disliked.
- **[Renaming `DesiredBox_Loaded` touches the grid's hot path]** → Pure rename, no logic change; the grid
  tests plus a run over an 783-row load cover it.
- **[Grepping "horizon" hits three unrelated concepts]** → The rename is done per-file against the D3 list,
  not by global find-replace; the TS-column and library cases are enumerated in the proposal as
  out-of-scope so a later reader does not "finish" the rename.
- **[Delta specs `MODIFIED` a requirement whose behavior did not change]** → Deliberate: the requirement
  text *names* the controls, so the vocabulary change is a spec-text change. Full requirement bodies are
  copied so nothing is lost at archive.
