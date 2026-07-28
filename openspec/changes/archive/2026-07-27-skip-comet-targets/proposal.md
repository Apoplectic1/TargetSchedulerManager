## Why

Comets are captured manually at the telescope, with their own setup and technique, and are **never scheduled in Target Scheduler** — confirmed against the live TS database, which contains no comet target at all. The scan reads them anyway, so `Comet C2023 A3 - Tsuchinshan` currently contributes 254 frames and a set of Disk-only rows whose **Filter column reads `2024-10-18 - Track Comet`**: its capture tree uses date-named session folders where filter directories belong, so the filter parser takes the session name verbatim as a filter code.

The result is grid rows that reconcile against nothing, cannot be planned, and carry a filter vocabulary no other target shares.

## What Changes

- **The scanner skips target directories named for a comet.** All four such directories share the prefix `Comet ` (`Comet 46P - Wirtanen`, `Comet C2020 F3 - Neowise`, `Comet C2022 E3 (ZTF)`, `Comet C2023 A3 - Tsuchinshan`), and nothing else among the 84 targets begins that way, so identification is unambiguous.
- The skip sits beside the existing `Calibration` skip — a directory that is never walked, rather than a result filtered afterwards.
- **BREAKING** for any consumer expecting comet targets in an `ImageLibraryReport`: they no longer appear. Nothing consumes them today.

## Capabilities

### New Capabilities
- `image-library-scan`: what the scan reads and — decisively — what it never reads. Seeded by this change with
  the two exclusions: the calibration tree (masters, not acquired light) and non-sidereal targets. The
  calibration skip has existed since the scanner was written but was never written down, so its first contract
  arrives here alongside the new one.

### Modified Capabilities
<!-- none -->

## Impact

**Sibling repo `..\Library` (`Astronomy.Catalog`)**
- `Scan/ImageLibraryScanner.cs` — a naming predicate plus one guard in `ScanTargetAsync`, which both entry points (`ScanAsync`, `ScanUnitsAsync`) already funnel through.
- `Scan/ImageLibraryScannerTests.cs` — a comet directory is skipped; a target merely containing the word is not.

**This repo (TSM)**
- None. The grid simply stops receiving those rows.

**Measured**
- 254 of 18,904 light frames (1.3%), one target of 84; the other three comet directories hold no lights.
- Zero TS targets match any comet, so nothing on the plan side loses its counterpart.
