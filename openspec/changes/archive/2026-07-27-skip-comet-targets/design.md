## Context

The image-library scan walks every directory under the library root as a target. One class of directory is already excluded — `Captures/Calibration`, skipped by name at the camera level — because it holds master darks rather than lights.

Comets are a second such class, for a different reason: they are **non-sidereal**. A sidereal scheduler cannot plan an object whose coordinates change nightly, which is why the live TS database contains no comet target and why the user captures them by hand at the telescope.

Their capture trees also break the filter convention. `Comet C2023 A3 - Tsuchinshan` nests `Z183/2024-10-18 - Track Comet/` where `Z183/<Filter>/` is expected, so `ParseFilterDirName` yields the session folder name as a filter code and the grid renders a filter called `2024-10-18 - Track Comet`.

## Goals / Non-Goals

**Goals:**
- Keep comet directories out of the scan entirely, so no consumer has to know about them.
- Identify them by the naming convention the user already maintains, with no configuration.

**Non-Goals:**
- Any general non-sidereal *support* — this is exclusion, not a feature for planning moving objects.
- Reporting comets as skipped. They are policy, not breakage; the `Calibration` skip is silent for the same reason and a report entry would be noise on every load.
- Fixing the session-folder-as-filter-directory shape. Once comets are unread, nothing else in the library uses it.

## Decisions

### Exclude at the walk, not after the scan
A comet directory is never descended, matching how `Calibration` is handled. Filtering after the fact would still pay to read 254 frames and would leave every future consumer to re-implement the same exclusion.

*Alternative rejected:* a scanner option defaulting to on. It is a knob nothing would ever set differently — speculative configuration, which the project's conventions push against.

### Identify by the `Comet ` directory prefix
All four comet directories carry it; none of the other 80 targets begins with it. The space is load-bearing — it prevents a future `Cometary Globule` style name from matching — and the user maintains this directory convention by hand.

*Alternative rejected:* a marker file or an explicit exclusion list. Both add a second thing to keep in step with the directory name that already says it.

### The predicate is public and named for the reason, not the spelling
Exposed so tests and future callers can ask the question directly, and named for *non-sidereal* rather than "comet" so the rule reads as the domain fact it is. The prefix is the current evidence for that fact, not the fact itself.

## Risks / Trade-offs

- **A comet directory not named `Comet …` would still be scanned.** → It would surface as it does today (odd filter rows), which is the current behaviour rather than a regression; the convention is the user's own and consistently applied across all four.
- **A legitimate target whose name starts with `Comet ` would be silently dropped.** → No such object naming exists in practice (a comet-*like* nebula is named for its catalogue designation, e.g. `NGC 2261`), and the trailing space blocks the near-miss cases.
- **The skip is silent.** → Consistent with `Calibration`. If a comet's absence ever looks like a bug, the predicate is public and named to make the reason findable.

## Migration Plan

None. The scan is recomputed from disk on every load; no persisted state carries comet rows.

## Open Questions

- If comet imaging is ever wanted in a *history* view (purpose 2), the exclusion would need to become a display filter rather than a scan skip. Not wanted today — the user's instruction was to ignore them entirely.
