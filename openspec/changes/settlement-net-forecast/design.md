## Context

Settlement forecasts already use one signed net. The import linker currently distributes payments across open settlements, while recurring forecast matching runs independently. Linked pending entries remain pending after settlement payment.

## Goals / Non-Goals

**Goals:** Use one net payment, close the included pending amounts, support partial inclusion, manual payment linking and signed exact automatic matching.

**Non-Goals:** Modify live data, rewrite paid bank history, create parallel recurring rules, run tests, commit or push without approval.

## Decisions

- Keep bank cash and non-cash settlement coverage separate. Persist allocated and settled amounts on ledger entries and identify lines that reserve a pending entry. The original amount remains available for audit.
- Reduce cash forecasts by allocated amounts. A fully reserved entry is in a settlement; a fully settled entry is paid/collected through that settlement, not another bank movement. A partially included entry retains its unallocated forecast.
- Recompute coverage from settlement lines whenever lines or settlement state change. Already-paid bank entries are not reserved or paid again.
- Automatic matching requires one unambiguous counterparty and one open settlement with the exact signed remaining amount. No automatic excess distribution.
- Explicit payment linking validates ownership, paid bank status and duplicate use. A partial explicitly linked payment only settles the included entries when the settlement reaches zero.
- Settlement-aware import detection takes precedence over generic fixed-rule matching.
- Block direct changes that would invalidate allocated ledger entries; changes go through the settlement.

## Risks / Trade-offs

- Financial double counting: retain bank actual amounts and exclude non-cash coverage from bank sums.
- Existing pending lines: identify these in the cleanup inventory; do not silently migrate historical data.
- Ambiguous names or identical nets: do not auto-link; require explicit selection.
- Data migration: additive default-zero columns only, applied separately with user approval.

## Migration Plan

Build both projects without tests. Present additive migration and exact legacy cleanup inventory before any live data change. Keep paid entries and closed months untouched.
