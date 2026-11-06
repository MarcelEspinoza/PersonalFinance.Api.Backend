## 1. Validation explanations

- [x] 1.1 Return safe, specific rejection reasons from movement normalization and log them.
- [x] 1.2 Preserve valid cards and explain rejected proposals without unsupported month-status claims.

## 2. Validation and publication

- [x] 2.1 Compile and preview controlled rejected and valid December proposals locally without tests or real financial writes.
- [x] 2.2 Show the preview, exact files and commit message and obtain separate commit/push approval.

The local preview exercised the real chat service with a disposable MySQL database and controlled model replies: missing/unavailable concept, unavailable account, invalid date, missing amount, mixed valid/rejected proposals and a valid December 370-euro driving-school proposal. With a synthetic 100-euro account balance, the valid proposal retained its confirmation card. No ledger entries were created. The actual frontend displayed the precise rejection and subsequent valid card; its screenshot uses synthetic data. Compilation and whitespace checks passed.

The user explicitly approved commit and push of the six listed files and the presented commit message after reviewing the synthetic preview.
