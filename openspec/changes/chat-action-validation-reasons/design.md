## Context

GlobalChatService normalizes model proposals before returning confirmation cards. A rejected create_expense currently yields only a boolean; all-rejected replies replace the model explanation with unsupported suggestions about paid entries and closed periods.

## Goals / Non-Goals

**Goals:** Specific, safe movement validation reasons; useful partial-failure replies; unchanged confirmation and balance behavior.

**Non-Goals:** Creating the user's driving-school expense, guessing missing category/account/date, changing historical financial data, or rewriting unrelated action validators.

## Decisions

- Return a rejection reason from the existing movement normalizer. Preserve its boolean wrapper for other normalization callers instead of duplicating validation rules.
- Use server-authored messages for missing/nonpositive amount, missing/unavailable/wrong-kind concept, unavailable account and invalid date. Do not copy raw model values into messages or logs.
- Collect rejected reasons during proposal processing. If valid cards remain, append the explanation; if none remain, replace the misleading generic reply. Unknown failures use an honest non-specific explanation rather than attributing them to unverified financial state.
- Do not add balance checks or execute financial writes. Negative month projections remain allowed. User confirmation remains mandatory.
- Validate through compilation and an isolated local service preview with controlled model responses; no tests or production writes.
- When a movement has an unavailable, missing or wrong-kind concept or an unavailable account, request at most one model correction using the same authoritative snapshot and original conversation. Request only the rejected movements in their original order.
- Apply only reference corrections. Reject corrections that change type, amount, description or date, preserve already valid references, and pass every proposal through the existing validators. Valid siblings and unrelated action types are never replaced. Transport, provider and malformed-response failures are logged and retain the original specific rejection behavior.
- Explicitly distinguish repeating a source movement in another month from moving it. Repetition must preserve its source concept and account identifiers and leave the original untouched.

## Risks / Trade-offs

- The original rejected model payload is unavailable, so its exact failed field cannot be reconstructed. The fix explains future rejections without guessing the old cause.
- Unrelated action validators remain boolean-only. Their fallback must state that the specific reason is unavailable rather than falsely blaming a closed month.
- A rejected reference can add one provider round trip. Correction is not guaranteed; unresolved references still produce a specific rejection and never trigger financial writes.
