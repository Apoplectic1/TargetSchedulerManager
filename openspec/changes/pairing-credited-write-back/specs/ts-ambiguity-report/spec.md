# ts-ambiguity-report Delta — pairing-credited-write-back

## ADDED Requirements

### Requirement: Sentinel templates are report action items naming their cause
The report's exposure-templates section SHALL carry one action item per template whose `gain`, `offset`,
or `readoutmode` is the use-camera-default sentinel (`-1`), naming the template (name and TS Id), exactly
which field(s) carry the sentinel, and the plans using it (count plus the owning targets) — so the reader
of the report alone learns both the cause of every grid `sentinel` badge and where the hand fix goes. The
item SHALL state the consequence (plans using the template can never pair and stamp 0 while the sentinel
stands). A zero-use sentinel template is still an item (the error exists in TS regardless of use). The
exempt defer-to-explicit-value sentinels (plan `exposure` `-1`, template `ditherevery` `-1`) SHALL NOT
produce items.

#### Scenario: Sentinel template item names field and blast radius
- **WHEN** the report is built while a template carries gain `-1` and two plans on one target use it
- **THEN** the templates section holds an action item naming the template, "gain", the two plans' targets,
  and the fix (set an explicit value), and the report's action count includes it

#### Scenario: Explicit templates produce no item
- **WHEN** every template expresses gain, offset, and readout mode
- **THEN** the templates section carries no sentinel items (the section's clean marker when nothing else
  fires)
