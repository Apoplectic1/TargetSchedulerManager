## ADDED Requirements

### Requirement: Row insertion is a guarded library primitive
The library editor SHALL expose a guarded insert primitive for `target` and `exposureplan` rows: the
caller supplies the full column payload including a minted guid; the primitive applies the existing guard
order (schema compatibility, read-only, open sidecar, column presence) before writing, executes the
INSERT in a single transaction, and read-back-verifies the inserted row. Refusals are structured, never
throws for guardable conditions. The primitive SHALL NOT invent or default contract-relevant values — a
missing required payload column is a caller bug and refuses loudly.

#### Scenario: Guards precede the insert
- **WHEN** an insert is requested while the db has an open `-wal` sidecar
- **THEN** the call returns the existing sidecar refusal and no row is written

#### Scenario: Verified insert
- **WHEN** a plan insert commits
- **THEN** the row reads back with the supplied payload (guid included) and the result reports success

### Requirement: Plan insertion is a cadence-affecting target-scope operation
Inserting an `exposureplan` row SHALL delete the parent target's `filtercadenceitem` rows in the same
transaction as the INSERT (both applied or neither) — a new plan changes the target's filter rotation
exactly like enabling one. A plan insert SHALL be refused with the existing override-order refusal when
the parent target has `overrideexposureorderitem` rows (no write, no deletion). Target insertion clears
nothing (a new target has no cadence rows) and is never refused for override-order.

#### Scenario: Plan insert clears its target's cadence
- **WHEN** a plan is inserted under a target holding `filtercadenceitem` rows
- **THEN** the plan row exists and the target has zero `filtercadenceitem` rows, other targets untouched

#### Scenario: OEO target refuses the insert
- **WHEN** a plan insert is requested under a target with `overrideexposureorderitem` rows
- **THEN** the call returns the override-order refusal and neither the plan nor any deletion is applied

#### Scenario: Failure applies nothing
- **WHEN** the transaction cannot commit mid-insert
- **THEN** no plan row exists and the `filtercadenceitem` rows remain intact
