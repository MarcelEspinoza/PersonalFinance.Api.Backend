## 1. Settlement accounting

- [x] 1.1 Add allocation and settlement coverage data with an additive migration.
- [x] 1.2 Reserve pending shares and refresh coverage on line changes, closing and reopening.
- [x] 1.3 Use remaining cash amounts in forecasts and prevent invalid direct entry changes.

## 2. Net payment matching

- [x] 2.1 Match an exact signed net without automatic distribution or ambiguous selection.
- [x] 2.2 Give settlement payment matching precedence over fixed forecast matching.
- [x] 2.3 Add explicit bank payment selection and settlement coverage feedback in the frontend.

## 3. Validation and approval

- [x] 3.1 Compile both projects without writing or running tests.
- [ ] 3.2 Review the diff and prepare the exact legacy cleanup inventory without executing it.

The diff review and synthetic UI screenshots are complete. Read-only API calls identified the two November occurrences and current settlement balances. Full future-occurrence enumeration is blocked: the public MySQL URL has no endpoint and Railway SSH times out. Do not use the monthly GET endpoint for inventory because it writes data. The partial cleanup proposal is a session artifact, not an approved execution plan. No live migration, cleanup, commit or push has been performed.
