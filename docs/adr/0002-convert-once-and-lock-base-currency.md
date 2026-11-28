# Convert once at the transaction-date rate; lock the base currency while a budget exists

Budgets and reports are in the user's base currency, while accounts can be in other currencies. A foreign-currency amount is converted at the stored rate for its transaction date (or the nearest earlier date) and is never revalued when rates move, so a past month's spent and reports do not change on their own. Because budgets are amounts the user typed in the base currency, the base currency can be changed freely only while the user has no budget; once a budget exists it is locked, and it unlocks again when every budget is removed.

## Considered options

- **Revalue at today's rate.** Rejected: last month's budget result would drift every day.
- **Convert existing budgets when the base currency changes.** Rejected: it silently rewrites numbers the user entered, at an arbitrary rate.

## Consequences

- If no rate exists on or before a transaction's date, the transaction is still saved and is flagged as not converted in budgets and reports until a rate arrives.
- Rates are fetched automatically each day; users do not enter them in v1.
- While the base currency can change (no budget), changing it converts every transaction again into the new one, each at the stored rate for its own date, and from then on those amounts are locked as before.
- A user who wants a different base currency after budgeting removes every budget first, then changes it and sets their budgets again in the new one.

## Amendment, 2026-11-28: the lock lifts once every budget is removed

The first version of this record locked the base currency for good after the first budget. It is now locked only while the user has a budget: once the last one is removed, the base currency can be changed again, exactly as before the first budget.

This is safe because the lock protects budgets, not transactions. A budget is an amount typed in the base currency and compared with spent, which is summed from each transaction's amount converted into that same currency; changing the currency under a budget would compare it with amounts in another currency. With no budget left there is nothing to compare, and a change converts every transaction again into the new currency at the stored rate of its own date, the same as a change before the first budget. Reports always read the converted amounts of the current base currency, so nothing is left in the old one.
