# Tasks: filter-rank-row-order

## 1. Filter rank

- [x] 1.1 Add `FilterRank` (H, S, O, L, R, G, B) to `Models/Format.cs` beside the camera alias, with a
      rank-index helper: ranked codes by position, unranked after all ranked (doc comment: user
      re-specifies the list when the filter set changes; obs c73e)
- [x] 1.2 Replace the comparator's `byFilter` `NaturalComparer` step in
      `ReconciliationLoader.BuildRows` with rank comparison; unranked ties fall back to natural order

## 2. Plane ordering

- [x] 2.1 Reorder the rollup `detail` list into disk-backed block (Disk + merged Both lines) then
      plan-only block, seconds ascending within each
- [x] 2.2 Flip the global plane tie-break to disk-before-plan at the comparison site (enum declaration
      unchanged), with a comment naming the "commitments sit under evidence" rule

## 3. Tests

- [x] 3.1 Resync loader-order tests pinning alphabetical filter order; add rank-order coverage
      (H before B; unranked code after every ranked one)
- [x] 3.2 Add detail-order tests: TS line last on a seconds tie; merged Both line stays in the disk
      block; seconds ascend within each block; plane tie-break Disk before TS

## 4. Docs + verify

- [x] 4.1 Update `UI.md` sort-precedence text (filter rank + plane rule + expanded-rollup order) in
      the same commit as the code
- [ ] 4.2 Build + full app test suite green; visual verification by the user (row order), then archive
      on their word
