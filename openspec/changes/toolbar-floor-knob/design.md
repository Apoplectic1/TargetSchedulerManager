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

**D2 — Keep `SpinButtonPlacementMode="Inline"`, and shrink the spin buttons per-instance.**
*Superseded once mid-change; the first version's estimate was wrong and `obs-d589` caught it.*

First attempt guessed the spinner block at ~56 px and set `Duration = 80`, `Floor = 70` as eyeball-tuned
numbers. The app clipped a chevron. Measuring `generic.xaml` (WinUI 2.2.1) gave the real geometry: the
NumberBox template root Grid is **col0 `*` · col1 `Auto` (`UpSpinButton`, MinWidth 32 + `Margin="4"` both
sides = 40 px) · col2 `Auto` (`DownSpinButton`, 32 + `Margin="0,4,4,4"` = 36 px)**, with `InputBox`
carrying `Grid.ColumnSpan="3"` — the text sits *underneath* both buttons rather than competing for a
column, which is why a starved box shows roomy centered text beside a missing chevron. **Stock pair = 76
px**, so the no-clip Inline minimum is 104 / 96 — versus the ~110 px we started from. Conclusion the first
pass got wrong: **stock Inline spinners leave nothing to reclaim.**

So the buttons themselves shrink: `NarrowNumberBox_Loaded` sets `MinWidth = 16` and 2 px margins on the
two `RepeatButton`s (pair ≈ 36 px). The user chose this (2026-07-26) over `Compact` (spinners behind a
hover popup) and over full-size Inline at 104/96.

*Second fire (`obs-1fe4`):* the first shrunk version (`Width` 64/56) centered the digits — the grid
convention applied reflexively — and they landed under the chevrons. The template has **no overlap
protection at all**: the spin buttons draw on top of the `InputBox`, and the stock control stays clean
only because its forced 120 px minimum keeps short left-aligned text away from them. A narrow inline box
must therefore supply its own clearance, and centering is structurally wrong for it (center of the *full*
box = under the buttons). Final shape: **left-aligned digits** (the WinForms up-down idiom), 38 px right
padding, **`Duration = 68`, `Floor = 60`** (= digits + 4 left pad + 38). The handler branches on
`SpinButtonPlacementMode` — hidden-spinner grid cells keep their centered convention untouched.

*Why per-instance rather than a style override:* the earlier note that this needs "a `NumberBox` style
override in page resources" was also wrong — shadowing `NumberBoxSpinButtonStyle` in app resources cannot
work, because a `StaticResource` referenced inside a framework `ControlTemplate` resolves within
`generic.xaml`, not against `Application.Resources`. Reaching the realized buttons in the existing
`Loaded` handler is both the working route and the narrow one: the schema-driven editor's boxes
(`SpinButtonPlacementMode.Hidden`, `Width = 110`) keep their stock metrics untouched.

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

- **[Guessed widths clip a spinner]** → **This happened** (`obs-d589`): 80/70 lost a chevron because the
  spinner block was measured at 56 px by eye instead of 76 px from the template. Resolved in D2 by reading
  `generic.xaml` and shrinking the buttons. Lesson for the next narrow-control change: template metrics are
  in the SDK package — measure them, don't estimate from a screenshot.
- **[Convention applied without checking the geometry]** → **Also happened** (`obs-1fe4`): centering — the
  grid's integer-edit-box convention — put digits under the chevrons, because the template's `InputBox`
  spans the buttons with no reservation. A convention written for one template shape (hidden spinners)
  doesn't transfer to another (inline) without re-deriving; the handler now branches on placement mode.
- **[16 px spin buttons are small mouse targets]** → They keep full height, so the target is a tall thin
  strip rather than a square; the author verifies it's comfortable and the value is a one-number edit.
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
