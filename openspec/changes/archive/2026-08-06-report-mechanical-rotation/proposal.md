# Proposal: report-mechanical-rotation

## Why

Mechanical-fallback framings exist today only as the grid's `°(M)` render — scattered, eyeball-found.
Since 2026-08-06 they are *actionable*: XFM's ASTAP solver measures sky rotation for exactly these
frames (the IC405 batch proved the °(M) → sky loop). The ambiguity report should enumerate them so the
solve candidates are a printed list, not a scroll hunt (user request 2026-08-06).

## What Changes

- The report's **Info** section gains one line per in-scope target whose framings express only
  mechanical rotation: target (with project prefix), the folded mechanical angle(s), frame count, and
  the actionable pointer (solve in XFM to measure sky rotation). Info, not an action item — a
  mechanical angle is missing measurement, not a slipped convention (the report's action-item charter).

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `ts-ambiguity-report`: adds the mechanical-rotation informational requirement.

## Impact

App-side only (`AmbiguityReport.BuildFramingInfo` — data already on `ReconciliationCell`);
one new test in `AmbiguityReportTests`.
