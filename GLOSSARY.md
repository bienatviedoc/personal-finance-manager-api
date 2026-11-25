# Personal Finance Manager

The language of a web application where a person records income and expenses across their accounts, sets monthly budgets, and sees where their money goes.

## Language

**User**:
A person who signs in and owns their own separate set of financial data.
_Avoid_: Account (for the sign-in identity), member, customer

**Institution**:
A bank or wallet provider that a user holds accounts with.
_Avoid_: Bank, provider

**Account**:
A place a user's money sits: current, savings, credit card, cash, e-wallet or loan. It has exactly one currency and optionally belongs to an institution.
_Avoid_: Wallet, bank account (as the general term), user account

**Transaction**:
One movement of money on one account on one calendar date. Its type is Income, Expense, Transfer out or Transfer in.
_Avoid_: Entry, record, payment

**Split transaction**:
An income or expense transaction divided across several categories, such as a supermarket bill that is part groceries and part household. Its lines sum exactly to its amount.
_Avoid_: Itemised transaction, multi-category transaction

**Line**:
One part of a transaction's amount, with the category it was for and an optional note. A transaction that is not split has one line, with its whole amount.
_Avoid_: Item, split (for one part), sub-transaction

**Transfer**:
A movement of a user's own money between two of their accounts, made of two linked transactions: a Transfer out leg and a Transfer in leg. It is neither income nor spending.
_Avoid_: Transfer transaction, internal payment, move

**Leg**:
One of the two transactions that make up a transfer.
_Avoid_: Side, half

**Mark as transfer**:
To join an expense on one account and income on another, both picked by the user, into the two legs of one transfer, such as the two rows imported statements show for paying a card. Unmarking turns a transfer back into an expense and income.
_Avoid_: Match (nothing is matched automatically), link, merge, split (for unmarking)

**Refund**:
An expense transaction flagged as money coming back, which raises the account balance and lowers spent in its category.
_Avoid_: Negative expense, reversal, income (for money returned)

**Spent**:
The total of a category's expenses in a budget month (or a report period), minus its refunds, in the base currency. It is always derived from transactions and can be negative.
_Avoid_: Actual, used, consumed

**Attachment**:
A receipt kept with a transaction: a JPEG, PNG or PDF file of at most 5 MB that the user can open again later. Removing it deletes the file.
_Avoid_: Document, upload, file (for the thing attached)

**Category**:
What a transaction was for, at most two levels deep (Food > Groceries). Its kind is either income or expense, shared with its parent and fixed once it has transactions.
_Avoid_: Type (for the kind), group, label

**Budget**:
A spending limit for one expense category in one budget month, in the base currency. Spending in a subcategory counts toward a budget on its parent.
_Avoid_: Limit, allowance, envelope

**Archive**:
To hide an institution, account or category from pickers while keeping its history in balances, net worth and reports.
_Avoid_: Delete, disable, close, deactivate

**Amount owed**:
What a user owes on a credit card or loan account, entered as a positive number and held as a negative balance.
_Avoid_: Debt, negative balance (in the interface)

**Recurring rule**:
A template that produces income or expense transactions on a schedule, each dated on its due date.
_Avoid_: Subscription, schedule, repeating transaction

**Import batch**:
One uploaded statement file together with the transactions created from it.
_Avoid_: Import, upload, statement

**Likely duplicate**:
A row in an import batch that matches an existing transaction on account, date, amount and description, and is skipped unless the user chooses to keep it.
_Avoid_: Dupe, conflict

**Reconciliation**:
Matching an account's transactions against a statement's closing balance until the difference is exactly zero, which then locks those transactions.
_Avoid_: Balancing, verification, closing

**Two-factor sign-in**:
Signing in with a password and, as well, a code from an authenticator app on the user's phone. A user turns it on and off; it is off until they do.
_Avoid_: 2FA, MFA, two-step verification (in the interface)

**Recovery code**:
One of ten codes a user is shown once when they turn two-factor sign-in on. Each signs them in one time in place of a code from the authenticator app.
_Avoid_: Backup code, scratch code

**Deactivated user**:
A user who has asked to be deleted and is within the 30 days before permanent erasure; signing in again cancels the deletion.
_Avoid_: Deleted user, suspended user

**Base currency**:
The single currency a user chooses for totals, budgets and reports across accounts in different currencies.
_Avoid_: Home currency, default currency, reporting currency

**Exchange rate**:
What one unit of a currency was worth in another on one calendar day. Rates are fetched each day, shared by every user and never entered by one.
_Avoid_: FX rate, conversion rate, price

**Not converted**:
Said of an amount that stays in its own currency because no exchange rate exists on or before its date. It is flagged and left out of base-currency totals until a rate arrives.
_Avoid_: Unconverted, pending, missing rate (for the amount)

**Locale**:
The language a user uses the app in, English or Vietnamese: the interface, how amounts and dates are written, and the names of the default categories they start with. A user registers in a locale and can change it later; one who has never chosen one sees their browser's.
_Avoid_: Language (as a separate setting), culture

**Budget month**:
The calendar month, from the 1st, that a budget applies to.
_Avoid_: Period, cycle, salary month

**Report period**:
The dates a report covers, from one calendar date to another, both included. It is compared with the previous period: as many whole months before it when it is whole calendar months, otherwise as many days.
_Avoid_: Range (in the interface), window, budget month (for a report)

**Integrity check**:
The nightly job that verifies a user's saved transactions still add up: each transaction's lines sum to its amount, each transfer has its two matching legs, and each account's balance is what its transactions come to. It reports what it finds and corrects nothing.
_Avoid_: Reconciliation (which the user does against a statement), audit, validation

**Drift**:
Something the integrity check finds that does not add up in what was saved, such as a transfer missing a leg. It is logged for a person to look into, never corrected automatically.
_Avoid_: Inconsistency, corruption, error
