# Convert once at the transaction-date rate; lock the base currency after the first budget

Budgets and reports are in the user's base currency, while accounts can be in other currencies. A foreign-currency amount is converted at the stored rate for its transaction date (or the nearest earlier date) and is never revalued when rates move, so a past month's spent and reports do not change on their own. Because budgets are amounts the user typed in the base currency, the base currency can be changed freely only until the user's first budget exists; after that it is locked.

## Considered options

- **Revalue at today's rate.** Rejected: last month's budget result would drift every day.
- **Convert existing budgets when the base currency changes.** Rejected: it silently rewrites numbers the user entered, at an arbitrary rate.

## Consequences

- If no rate exists on or before a transaction's date, the transaction is still saved and is flagged as not converted in budgets and reports until a rate arrives.
- Rates are fetched automatically each day; users do not enter them in v1.
- While the base currency can still change (no budget yet), changing it converts every transaction again into the new one, each at the stored rate for its own date, and from then on those amounts are locked as before.
- A user who wants a different base currency after budgeting has no path in v1.
