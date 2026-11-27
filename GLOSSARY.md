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
_Avoid_: Actual, used, consumed (the budget vs actual report keeps the name the specification gives it, RPT-6; within it, say spent)

**Attachment**:
A receipt kept with a transaction: a JPEG, PNG or PDF file of at most 5 MB that the user can open again later. Removing it deletes the file.
_Avoid_: Document, upload, file (for the thing attached)

**Category**:
What a transaction was for, at most two levels deep (Food > Groceries). Its kind is either income or expense, shared with its parent and fixed once it has transactions.
_Avoid_: Type (for the kind), group, label

**Tag**:
A free-form label a user puts on transactions across categories, such as `tet-2027` or `japan-trip`. A tag is the user's own, kept in lower case, and created the first time they give a transaction it.
_Avoid_: Label, keyword, category (for a tag)

**Bulk action**:
One change made to many transactions the user selects at once, one by one or every one that passes the list's filters: setting their category, adding or removing a tag, moving them to another account, or deleting them. Every one takes it or none does; the ones that cannot are named, with why.
_Avoid_: Batch edit, mass update, import batch (which is something else)

**Budget**:
A spending limit for one expense category in one budget month, in the base currency. Spending in a subcategory counts toward a budget on its parent.
_Avoid_: Limit, allowance, envelope

**Overall budget**:
A spending limit on all of a budget month's expenses together, in the base currency, whether or not their category has a budget of its own. A month has at most one; it is not a category budget and is not added into the category budgets' totals.
_Avoid_: Total budget, monthly cap, global budget

**Pace**:
Whether a budget in the current budget month is being spent faster than the month is passing: ahead of pace when the share of the budget spent is larger than the share of the month elapsed, today included; otherwise on pace. A past month shows only its final result and has no pace.
_Avoid_: Burn rate, forecast, trajectory

**Budget notification**:
A message, shown in the app and emailed, that a category's budget moved into warning or over in a budget month. The overall budget raises none, and neither does a month that has ended. Each category raises at most one warning and one over a budget month, and keeps what the budget stood at when it was raised. Email can be switched off in settings; the app always shows it.
_Avoid_: Alert, reminder (which is a recurring rule's), budget email

**Archive**:
To hide an institution, account or category from pickers while keeping its history in balances, net worth and reports.
_Avoid_: Delete, disable, close, deactivate

**Merge**:
To fold one category into another of the same kind: its transactions, budgets and subcategories move to the other, budgets in the same month are added together, and it is archived.
_Avoid_: Combine, join, move (for a whole category)

**Amount owed**:
What a user owes on a credit card or loan account, entered as a positive number and held as a negative balance.
_Avoid_: Debt, negative balance (in the interface)

**Recurring rule**:
A template that produces income or expense transactions on a schedule, each dated on its due date.
_Avoid_: Subscription, schedule, repeating transaction

**Reminder**:
An occurrence of a recurring rule set to remind, due on or before today, waiting for the user to confirm it (recording its transaction, optionally for another amount) or dismiss it (recording nothing).
_Avoid_: Notification, alert, pending transaction

**Import batch**:
One uploaded statement file together with the transactions created from it. The file is kept only until its rows are imported; each transaction created belongs to the batch.
_Avoid_: Import, upload, statement (for the batch)

**Statement file**:
A bank's export of an account's transactions, as a CSV file the user uploads to import. Its rows may be preceded by a preamble and followed by a footer, such as a total.
_Avoid_: Bank file, export (which is the app's own file out), CSV (for the thing itself)

**Mapping**:
How the user says a statement file is read: what separates its cells, the rows to skip before and after the transactions, whether a header row names the columns, which column holds the date, the description, and either one signed amount or a debit and a credit, and how dates and amounts are written. No bank's layout is built in; each is mapped.
_Avoid_: Parser, format, template, column map

**Saved mapping**:
The mapping an account's last statement was committed with, kept for the account and offered for its next statement, which the user can still change before committing; the change is then saved in its place. One per account.
_Avoid_: Template, profile, preset

**Undo** (of an import batch):
Deleting every transaction an import batch created, edited or not, in one action. They stay recoverable for 30 days like any deleted transaction; the batch stays, undone, in the list of past imports. Refused while any of them is reconciled; a batch still being imported is stopped.
_Avoid_: Rollback, revert, delete (for the batch)

**Preview**:
A statement file read with a mapping before anything is saved: every row, the transaction each says, the rows that cannot be read and why, and the likely duplicates, unticked. The user chooses there which rows to commit.
_Avoid_: Dry run, draft

**Likely duplicate**:
A row of a statement file that matches a transaction already on the account on date, amount, which way the money went, and description compared without case, accents or extra spaces; or a transfer leg on the account on date, amount and which way the money went, as a leg has no description. Each existing transaction matches one row at most. It is shown, unticked, and skipped unless the user ticks it; never dropped silently.
_Avoid_: Dupe, conflict

**Categorisation rule**:
A user's rule that a payee or statement row description containing, starting with or exactly matching some text takes a category, compared without case, accents or extra spaces. It categorises only transactions of its category's kind, and nothing while that category is archived. Of several that match, the one of highest priority wins. It suggests: the import preview and quick add start from its category, and the user may change it.
_Avoid_: Category rule (a rule a category itself keeps, such as its depth), auto-category, filter

**Priority**:
A categorisation rule's rank: of several rules that match, the highest wins, and of equal ones the one set up first. Reordering the rules sets every rule's priority from its place.
_Avoid_: Order number, weight

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

**Audit log**:
The record of the sensitive things a user did, read in their settings, the newest first: each sign-in, statement committed, import undone, bulk action and delete of a transaction or transfer. Only the app writes to it, and nothing in it is ever changed or removed, until the user is erased with everything else of theirs.
_Avoid_: History, activity (the dashboard's recent activity is something else), event log

**Audit entry**:
One line of the audit log: what the user did, the kind of thing and the one thing it affected (none for a bulk action), when, and a short summary in words, written in the user's locale at the time. A summary never holds a password, a code or a statement's text.
_Avoid_: Event, log line, record

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

**Trend**:
One category (with its subcategories) or one payee followed over a report period: what it came to in each month, and its period-over-period change, the amount and percentage by which each month's total differs from the month before.
_Avoid_: History, timeline, family (for a category and its subcategories)

**Dashboard**:
The screen a user lands on: their total balance, this month's income, expenses and net, budget status, top five spending categories, latest transactions and what is coming up from recurring rules, each linking to the screen or report behind it.
_Avoid_: Home, overview, summary (which is the monthly summary)

**Net worth**:
What a user is worth on a day, in the base currency: the balances of their assets (every account but credit cards and loans) minus the amount owed on their liabilities (credit cards and loans), archived accounts included.
_Avoid_: Wealth, equity, balance (for the total)

**Cash flow**:
How a balance moved over a report period: the running balance of one account, in its own currency, or of all of a user's accounts, archived ones included, in the base currency, at the end of every day or of every week.
_Avoid_: Cash flow statement, balance history, income vs expenses (which is money in and out, not a balance)

**Running balance**:
An account's balance at the end of a day: its opening balance plus every transaction dated on or before that day, and nothing before the account opened.
_Avoid_: Cumulative balance, end-of-day total

**Savings rate**:
Income minus expenses over some dates, as a percentage of that income. There is none when there was no income.
_Avoid_: Saving ratio, margin

**Unbudgeted**:
Spent in a budget month where no budget covered its category: none on the category, nor on its parent. The overall budget covers no category.
_Avoid_: Over budget (which a budgeted category is), uncategorized

**Export**:
A file a user downloads to take their numbers elsewhere. A CSV export holds what one transaction list or one report shows: every transaction that passes the list's filters, not only the page loaded, or the numbers of the report's table view, with amounts as plain decimals beside their currency and dates as `YYYY-MM-DD`. The full export is everything the user owns, in one ZIP file.
_Avoid_: Download (for the file itself), backup, report (for the file)

**Integrity check**:
The nightly job that verifies a user's saved transactions still add up: each transaction's lines sum to its amount, each transfer has its two matching legs, and each account's balance is what its transactions come to. It reports what it finds and corrects nothing.
_Avoid_: Reconciliation (which the user does against a statement), audit (the audit log is something else), validation

**Drift**:
Something the integrity check finds that does not add up in what was saved, such as a transfer missing a leg. It is logged for a person to look into, never corrected automatically.
_Avoid_: Inconsistency, corruption, error
