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

The original implementation was published with approval. The full legacy-rule cleanup inventory remains blocked and no cleanup was executed.

## 4. Existing manual line repair

- [x] 4.1 Add explicit source attachment with coverage refresh and unchanged financial totals, including closed settlements and passthrough obligations.
- [x] 4.2 Expose existing line attachment in the settlement UI and assistant confirmation cards.
- [x] 4.3 Build without tests, capture the repair UI and apply the specifically approved November source link after deployment approval.

## 5. Dashboard cash warnings

- [x] 5.1 Separate variable budget reserves from dated payment alerts and calculate uncovered shortfall on the combined daily account timeline.
- [x] 5.2 Build without tests and preview the revised warning with the November snapshot.
- [x] 5.3 Apply the explicitly approved gas forecast corrections (42.37 and 50.20), preserving pending status and actual bank balances.
- [x] 5.4 Review and approve the exact November Pasanaco compensation: 2000 gross less 600 own contribution and 200 already received leaves 1200 pending on November 5. Preserve December forecasts and the mother's closed settlement net.

Both approved application commits were deployed successfully. The approved live repairs were applied and verified: the mother's net remains 269.50, the Pasanaco compensation is closed at zero, 1200 remains pending on November 5, December's 600 quota is unchanged, and actual bank balances did not change.
