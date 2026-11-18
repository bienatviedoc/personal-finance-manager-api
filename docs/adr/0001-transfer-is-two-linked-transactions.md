# A transfer is two linked transactions

Moving money between a user's own accounts is stored as two transactions sharing a transfer id: a Transfer out leg on the source account and a Transfer in leg on the destination. Transaction type therefore has four values (Income, Expense, Transfer out, Transfer in), which keeps the rule that amounts are stored positive and the type alone gives the sign, and lets an account's balance be one sum over its own transactions.

## Considered options

- **One transfer row naming both accounts.** Rejected: every balance and account-history query would need to read a second table, and a cross-currency transfer needs two amounts anyway.
- **A single "Transfer" transaction type.** Rejected: the type could no longer give the sign, so direction would need a second field.

## Consequences

- The two legs are always created, edited and deleted together, and a transfer is locked as soon as either leg is reconciled.
- Transfer legs are excluded from income, expense, budget and report totals.
- A cross-currency transfer stores each leg's own amount; its exchange rate is implied by the two legs, not looked up.
- Imported statements produce unlinked income and expense rows; the user joins a pair with "mark as transfer".
