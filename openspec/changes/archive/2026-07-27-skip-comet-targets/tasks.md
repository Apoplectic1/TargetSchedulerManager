## 1. Library — exclude comets from the scan

- [x] 1.1 Add a public naming predicate to `ImageLibraryScanner` identifying a non-sidereal (comet) target directory by its `Comet ` prefix, documented with why such targets are excluded
- [x] 1.2 Guard `ScanTargetAsync` on it so both entry points (`ScanAsync`, `ScanUnitsAsync`) honour the exclusion from one place
- [x] 1.3 Note the exclusion beside the existing `Calibration` skip in the scanner's directory-layout doc comment

## 2. Library — tests

- [x] 2.1 A comet target directory produces no target report
- [x] 2.2 A directory merely containing the word (e.g. `Cometary Globule`, `NGC 2261 - Comet Nebula`) is still scanned — the trailing space in the prefix is load-bearing
- [x] 2.3 A normal target alongside a comet is unaffected
- [x] 2.4 The calibration tree is not read as light, and a target holding only calibration yields nothing — long-standing behaviour that had no test until it was specified

## 3. Verify

- [x] 3.1 Build the library and app; run every suite
- [x] 3.2 Author-verify in the app: `Comet C2023 A3 - Tsuchinshan` is gone from the grid, and no row anywhere shows a filter like `2024-10-18 - Track Comet`
- [x] 3.3 Seed the `image-library-scan` capability spec: what the scan reads, and the two exclusions (calibration, non-sidereal)
- [x] 3.4 Update `ROADMAP.md` — comets are excluded at the scan, closing the "comets out of scope" note left by `capture-config-keys`
