# Personal Finance Manager API

The ASP.NET Core API and background worker for the Personal Finance Manager. The frontend lives in
[personal-finance-manager-spa](https://github.com/bienatviedoc/personal-finance-manager-spa).

- [Specification](docs/specification.md)
- [Glossary](GLOSSARY.md): the domain language. Use its terms in code, tests and the interface.
- [Decision records](docs/adr/)

## Run it

Requires Docker.

```bash
docker compose up --build
```

This starts PostgreSQL, applies migrations, then starts the API on http://localhost:8080, the worker,
an object storage emulator for attachments (the [Versity S3 Gateway](https://github.com/versity/versitygw), on
http://localhost:7070),
[Mailpit](https://mailpit.axllent.org), which catches every email the API sends: read them at http://localhost:8025,
and the [Aspire Dashboard](https://aspire.dev/dashboard/standalone/), which shows the logs, traces and metrics of
the API and worker: http://localhost:18888.

| URL | What |
| --- | --- |
| `/health/live` | The process is up |
| `/health/ready` | The database is reachable (503 when it is not) |
| `/openapi/v1.json` | The OpenAPI document |
| `/api/v1/...` | The API |

To run the API from source against the Compose database (requires the .NET 10 SDK):

```bash
docker compose up -d db storage
dotnet run --project src/Pfm.Api -- migrate
dotnet run --project src/Pfm.Api
```

### Sample data

`seed` gives a new user a year of sample data, up to today, and exits: two institutions, four accounts (one a
credit card), the default categories, a budget on the main expense categories in every month, and transactions
with pay, bills, transfers, splits and refunds among everyday spending (`SampleDataSeeder`). The user signs in as
`sample@example.com` with the password `sample data password`, their email already confirmed. For a local
environment only: outside the `Development` environment it refuses and exits with code 2.

```bash
docker compose run --rm api seed
# or, from source:
dotnet run --project src/Pfm.Api -- seed [--email <email>] [--password <password>] [--transactions <count>]
```

`--transactions` sets how many the year holds (each leg of a transfer counting as one); left out, a few a day.
Seeding an email that already has a user is refused.

## Test

```bash
dotnet test Pfm.slnx
```

Domain rules that need nothing but the domain type are unit-tested in `Pfm.Domain.UnitTests`; behaviour through
the API is tested over HTTP in `Pfm.Api.IntegrationTests`. Those tests share one API and all call from one
address, so its rate limit is set too high to meet; a test of the limit (or of anything else set at start-up)
runs its own copy of the API from `PfmApi` (`WithAuthRateLimit`, `WhereSignInLocksFor`). Give each test its own
user (`SignedInAsync`): lockout counts per user. `Totp` stands in for an authenticator app. Integration tests start a real PostgreSQL in a container through Testcontainers, so Docker must be running. They start the object storage emulator the same way (`api.Storage`), so a signed URL is fetched for real.

Statement import is tested against golden files (spec section 13) in `tests/statements`: each statement, the mapping
a user chooses for it and the rows it must read as. The Vietcombank and Techcombank files there now are **synthetic
stand-ins**, to be replaced by anonymised real statements; its README says how.

CI runs the same tests through `scripts/test-with-coverage.sh`, which merges coverage from every test
project and fails when line coverage of `Pfm.Domain` and `Pfm.Application` is below 85%. Run it locally
the same way; the report lands in `TestResults/coverage/`.

### End-to-end and accessibility tests

The Playwright journey and the axe checks of spec section 13 live in the SPA repository: `npm run e2e` there starts this
repository's `compose.yaml` with the SPA's image on top, under a Compose project of its own (`pfm-e2e`), and the
SPA's CI runs it against this repository's `main` ([decision record 0010](docs/adr/0010-end-to-end-tests-live-in-the-spa-and-run-against-the-apis-compose-stack.md)).
So keep `compose.yaml` working without its published ports, and keep `seed` and the synthetic statements in
`tests/statements/synthetic-stand-ins`, which the journey imports.

### Load test

`tests/load/load-test.js` is a [k6](https://k6.io) test of the speed targets (spec section 8): every list and
report the app reads, for a user with 50,000 transactions, must answer in under 300 ms (lists) and under 1 s
(reports) at the 95th percentile. Each request is named, so the summary shows each one's own percentile, and
the run fails when any is over its target. Seed the user, then run it against an API built in Release:

```bash
dotnet run --project src/Pfm.Api -c Release -- seed --transactions 50000
dotnet run --project src/Pfm.Api -c Release
docker run --rm --network host -v "$PWD/tests/load:/load:ro" grafana/k6:2.4.0 run /load/load-test.js
```

It reads `PFM_URL` (`http://localhost:8080`), `PFM_EMAIL` and `PFM_PASSWORD` (the seeded user), `VUS`, the
users browsing at once with no pause between requests (10), `DURATION` (`2m`), and `MIN_TRANSACTIONS`
(50000), below which the run refuses to start, as the targets are for a user that size; pass each with `-e`.

Results, 28 November 2026, on a developer laptop (20 cores, WSL 2) shared with other work, PostgreSQL 17 in
Docker, runs of two minutes on the same seeded user: before and after spent was added up in the database (#70).

| Request | Before, 1 user | Before, 10 users | After, 1 user | After, 10 users | Target |
| --- | --- | --- | --- | --- | --- |
| Transactions, first and next page | 20 ms, 19 ms | 83 ms, 81 ms | 21 ms, 21 ms | 66 ms, 56 ms | 300 ms |
| Transactions this month, searched, by amount | 10 ms, 39 ms, 38 ms | 51 ms, 119 ms, 127 ms | 8 ms, 38 ms, 42 ms | 47 ms, 94 ms, 95 ms | 300 ms |
| Transactions in a category | 422 ms | 1.09 s | 114 ms | 293 ms | 300 ms |
| Accounts, institutions, categories, recurring rules | 19 ms or less | 77 ms or less | 21 ms or less | 46 ms or less | 300 ms |
| Budgets (a month) | 58 ms | 208 ms | 21 ms | 60 ms | 300 ms |
| Spending by category, this month | 55 ms | 211 ms | 18 ms | 56 ms | 1 s |
| Spending by category, this year, in a category, 12 months compared | 425 ms, 436 ms, 432 ms | 1.47 s, 1.43 s, 1.45 s | 101 ms, 103 ms, 104 ms | 271 ms, 284 ms, 290 ms | 1 s |
| Spending transactions of a category | 96 ms | 311 ms | 101 ms | 253 ms | 1 s |
| Trend of a category compared, of a payee | 211 ms, 46 ms | 726 ms, 142 ms | 98 ms, 32 ms | 257 ms, 113 ms | 1 s |
| Income vs expenses, this year, last 12 months compared | 423 ms, 438 ms | 1.57 s, 1.52 s | 172 ms, 173 ms | 482 ms, 505 ms | 1 s |
| Net worth, last 12 months | 22 ms | 92 ms | 21 ms | 95 ms | 1 s |
| Budget vs actual, this month, this year | 68 ms, 434 ms | 222 ms, 1.49 s | 23 ms, 120 ms | 98 ms, 343 ms | 1 s |

Before, the transactions of a category, and the reports over a year, read every matching expense with its lines into
memory to add up spent; with ten users and no pause they missed their targets. Spent is now a `SUM` in the database
(see Transactions, Spent), and with ten users every list and report meets its target, the transactions of a category
only just: with no dates, their totals and spent are one pass over every one of the category's lines.

## Layout

A modular monolith. Dependencies point inwards, and the architecture tests fail the build when they do not.

| Project | Responsibility | Depends on |
| --- | --- | --- |
| `Pfm.Domain` | Entities, value objects, domain rules. No framework references | Nothing |
| `Pfm.Application` | Use cases per module (commands, validators, handlers); interfaces for persistence, storage, email, clock | Domain |
| `Pfm.Infrastructure` | EF Core `DbContext` and migrations, the implementations of those interfaces, and the observability setup both hosts share | Application |
| `Pfm.Api` | Endpoints, OpenAPI, problem details | Application, Infrastructure |
| `Pfm.Worker` | Background jobs | Application, Infrastructure |

### Modules

A module (`Accounts`, `Transactions`, `Budgets`, `Reports`, `Imports`, `Identity`) is a folder of that
name in each project, for example `Pfm.Domain.Budgets` and `Pfm.Application.Budgets`.

Code in one module may use another module only through that module's contracts, which live in
`Pfm.Application.Contracts.<Module>`. Reaching into another module's own namespaces fails
`ModuleBoundaryTests`.

Categories belong to `Transactions` (spec section 5), so `Budgets`, `Imports` and `Reports` reach them through
its contracts.

### Use cases

Each use case lives in its module in `Pfm.Application` as one file holding three types: a command (the
input, normalised in its constructor, for example a trimmed email), its FluentValidation validator, and a
handler deriving from `CommandHandler<TCommand, TOutcome>`. `HandleAsync` runs the validator, then the
handler's `ExecuteAsync` for a valid command, and answers a `Result<TOutcome>`: either the outcome (an
enum when there is more than one, `Done` when there is not) or the errors, keyed by command property. An
outcome that carries data is a record: one with a named instance for the case that saved nothing
(`EditAccountOutcome.Stale`, `CreateTransactionOutcome.KeyReused`), or, when the cases carry different data,
an abstract record with one nested record for each (`SetUpTwoFactorOutcome.Ready`, `.AlreadyOn`).
A use case that acts on one of the user's things by id, query or command, answers null when the user has
no such thing, and the endpoint answers 404: `EditInstitution` answers `InstitutionDetails?`,
`ArchiveCategory` answers `Done?`. `UpdateProfile` answers null when nobody is signed in (a 401).
`AddApplication` registers every handler and validator it finds, so adding a use case is adding its file.
`UseCaseTests` fails when a command has no validator, or more than one.

An endpoint only maps HTTP to a use case: it builds the command from the request, calls the handler, and
turns the outcome into a response. Requests and responses are the API's own types (`InstitutionRequest`,
`CategoryResponse`), never Application ones, so the OpenAPI contract changes only when an endpoint does: a use
case answers a `…Details` record (`InstitutionDetails`, `CategoryDetails`, `ProfileDetails`) and the endpoint
maps it to a `…Response`. A list answers an object with its `items`. `ValidationProblems.For` turns errors into Problem Details keyed by
the request's field names (`Email` becomes `email`).

What a use case needs from outside, it asks for through an interface in its module (a port, such as
`IUsers`), implemented in `Pfm.Infrastructure`. Application never references ASP.NET Core or EF Core.

A query is one file too, holding the query record (`ListInstitutions`, `GetProfile`, even when it carries
nothing) and its handler. It has no validator or `Result`: the handler implements `IQueryHandler` and answers
directly, null when the user has no such thing (a 404). Handlers that
change data save through `IUnitOfWork` and read the time from `TimeProvider`. Steps that must all happen or none (registering a user and creating their default categories) share one `IUnitOfWork.BeginTransactionAsync`. A broken domain rule (`CategoryRuleViolation`) comes back as an error keyed by the command property at fault. A validator reuses its
domain type's rules (`Names.IsValid`, `ProfileSettings.IsValidTimeZone`), and the domain type still refuses what
breaks them. Rules several modules share (a name, a colour, a locale) are in `ValidationRules`. A command
reads what travels as text with `EnumText<T>` (an enum's camelCase name), `DateText` and `AmountText`, and a
handler names one field to fix with `FieldError.On`. A command about the signed-in user's own sign-in finds them
with `IUsers.FindSignedInAsync`; a query that only reads it (`GetTwoFactor`) goes by `ICurrentUser.UserId`. An endpoint that edits from a row version reads `If-Match` with
`RowVersions.TryRead`, which also answers the 428 and 412.

**Archive, never delete.** `DELETE` on an institution, account or category archives it: it
leaves lists and pickers (unless they ask for `includeArchived=true`) and stays readable by id, so whatever
already uses it can still show it. An account can be archived at any balance.

**Merging categories** (spec CAT-3). `POST /categories/{id}/merge` with a `targetId` of the same kind folds the
category into the target (`MergeCategory`): every transaction line (of deleted transactions too, so they come back
on the target), recurring rule and budget moves to the target, and where both have a budget in the same month the
amounts are added (`BudgetMerge`, reached through `IUserBudgets.MergeAsync`). Its subcategories move under the
target, and the category is archived. It answers what moved: `transactions` (not deleted; a split counts once) and
`budgets`. `GET /categories/{id}/merge-preview` answers the same counts without changing anything, for the user
to confirm first. A merge that would make three levels (a category with subcategories into a subcategory), budget a
category and its parent in the same month (rule 6), or go into a category of the other kind, an archived one or
one of the merged category's own subcategories is refused as an error on `targetId`, and changes nothing.

### Accounts and balances

- **A balance is never stored.** `Account.BalanceWith` adds what the account's transactions come to onto its
  opening balance, each time it is asked. Accounts asks the Transactions module for that sum through
  `IAccountTransactions` (in `Pfm.Application.Contracts.Transactions`), answered by a `SUM` over the account's
  transactions (`TransactionTotals`). The same contract says whether an account has transactions, which fixes its
  currency. A test pins the columns of the `Accounts` table, so a stored balance cannot slip in.
- **Amount owed.** A credit card or loan sends `amountOwed`, a positive number, instead of `openingBalance`; the
  domain holds it as a negative opening balance, so the account shows a negative balance (spec section 3, rule 9).
  The response carries both.
- **Last 4 digits.** Any account can send `last4`, the last 4 digits of its number, as a label. It is exactly 4
  digits (`Last4Digits`) in a `character(4)` column: a full account or card number is refused by name and has
  nowhere to be stored (spec ACC-8).
- **Credit card fields.** Only a credit card takes `creditLimit` (a decimal string, not negative), `statementDay`
  and `paymentDueDay` (days of the month, 1 to 31), each optional (`CreditCardTerms`); any other type that sends
  one is refused. The response adds `availableCredit`: the credit limit minus the amount owed now, so it is the
  limit plus the (negative) balance, negative over the limit, and null without a limit. Like the balance it is
  computed each time (`Account.AvailableCreditWith`) and never stored. `PUT` replaces every field, so `last4` or
  a credit card field left out is removed.
- **`GET /accounts`** is paged like every list, and also answers `totals`: the balance of each institution in
  each currency, over the whole list whichever page is asked for. Currencies are never added together.
- **`baseCurrencyTotal`**, on the same answer, is those totals converted into the user's base currency at today's
  exchange rates and added up: one balance for everything, and one for each of its `institutions` (spec ACC-4). A
  balance that is not converted (see below) is left out and listed under `notConverted`, on the institution and
  on the whole.

### Exchange rates

- **Nobody enters a rate.** The worker's `ExchangeRateFetchJob` runs `FetchExchangeRates` when it starts and every
  six hours: it asks the source for every day since the latest one stored (a year back the first time) and stores
  the rate of every pair of currencies, in both directions, per day. A stored rate never changes. A day stored
  without every pair (the source had not quoted a currency yet) is asked for again for a week, and only the
  pairs it lacks are filled in. Rates are stored with `INSERT … ON CONFLICT DO NOTHING`, so two workers fetching
  at once store each rate once and neither fails. The job is a plain `BackgroundService` on a timer: the
  specification names Hangfire for background jobs (section 9), which is not in the repository yet. Rates
  belong to no user. The source is [Frankfurter](https://frankfurter.dev), behind `IExchangeRateSource`
  ([decision record 0003](docs/adr/0003-exchange-rates-from-frankfurter.md)); tests fake it and never reach the
  network.
- **Convert through `ICurrencyConversion`** (`Pfm.Application.ExchangeRates`), from any module: it answers the
  amount at the rate of the date asked for, or of the nearest earlier day that has one, with the rate and the day
  it is of. With no rate on or before the date it answers `Conversion.NotConverted`: keep the amount, flag it as
  not converted and leave it out of totals (spec section 3, rule 7). A module reads the user's base currency
  through `IBaseCurrency` (`Pfm.Application.Contracts.Identity`).
- **Exchange rates are not a module.** Like `Currency`, they are shared by all six: `Pfm.Domain.ExchangeRates` and
  `Pfm.Application.ExchangeRates` reach into no module, and any module may use them. The one thing a fetch asks
  of a module goes through a contract: `ITransactionConversion` (see Transactions below).
- **Configuration** (all optional): `ExchangeRates__SourceUrl` (a self-hosted Frankfurter),
  `ExchangeRates__DaysBackWhenEmpty` (365) and `ExchangeRates__FetchEvery` (`06:00:00`).

### Transactions

- **Income or an expense** on one account, with an amount above zero whose sign is its type (spec section 3,
  rule 2), a category of the kind its type takes, and optionally a payee and a note. Its currency is never sent:
  it is the account's, and follows the transaction to another account.
- **Lines and splits** (rule 5, TXN-4). Every transaction has lines (`TransactionLine`, table `TransactionLines`):
  one, with its category and whole amount, or several when it is split. `POST` and `PUT` take `lines` (2 to 50,
  each a `categoryId`, an `amount` and an optional `note`) in place of `categoryId`; an edit without them makes a
  split one category again. The lines must sum exactly to the amount: `Transaction` refuses anything else as an
  error on `lines`, and `Transaction.RemainingToSplit` says how much is left. What is wrong with one line is an
  error on that line (`lines[1].categoryId`). Every answer lists the `lines`, one for a transaction that is not
  split. Lines are the transaction's own (an owned collection): read and saved with it, guarded by its row
  version, and reached in a query through it: `transaction.Lines.Any(line => …)` finds a split through any of
  its lines, which is how a category knows it has transactions. `Transaction.CategoryId` stays as the category of
  the first line, for what shows one category for a transaction; anything that adds up amounts by category
  (budgets, reports) reads the lines. A line has no `UserId` or timestamps: they are its transaction's.
- **A refund** is an expense sent with `isRefund: true`: money coming back from a purchase. It is the one
  exception to the type giving the sign (rule 2): it raises the account's balance, and it lowers spent in its
  category instead of counting as income. Only an expense can be one; income sent with the flag is refused on
  `isRefund`, by `Transaction` and by a check constraint. An edit can set the flag or take it away.
- **Spent** (spec section 6) is `Spent.Of` in `Pfm.Domain.Transactions`: a category's expenses in a month minus
  its refunds, in the base currency, each transaction adding its `SpentAmount`. A split adds each line to its
  own category (`SpentAmountIn`): the lines share the converted amount as they share the amount, the last
  taking what rounding leaves. A refund counts in the month of
  the refund, so spent can go below zero, and nothing clamps it. A deleted transaction and one not converted
  are left out. `GET /budgets` answers it (see Budgets below), through `ICategorySpending`.
- **Spent is added up in the database** (spec sections 8 and 10), by that definition, so lists and reports never read
  a year of transactions to add them up. The view `TransactionLineShares` (`LineShare`) gives each line its share of
  its transaction's converted amount: the whole of it for a line that is not split, and for a split's lines the
  function `SplitLineShare`, rounding each share half away from zero to the base currency's decimals (the function
  `CurrencyDecimals`, which `MigrationTests` holds to `Currencies.DecimalPlaces` for every currency) and leaving the
  rest to the last line, as `SpentAmountIn` does. The queries `SUM` those shares, grouped as each answer needs, refunds
  apart, and `Spent.Signed` gives the sums their sign. The view and the domain are two writings of one rule:
  `SpentTests` holds them to the same answers on the same transactions (converted splits, refunds, transfers, deleted
  and not converted expenses, and a seeded year), so change both together.
- **Rules live in `Transaction`** (`Pfm.Domain.Transactions`). The validator checks what the command alone can
  say (an amount above zero, a date that is one); what needs the account, the category or today's date is checked
  while the transaction is built, and comes back as an error on the field at fault (`TransactionRuleViolation`).
- **Today is the user's.** A date cannot be in the future in the user's time zone (rule 8). Transactions asks
  Identity what today is through `IUserCalendar`, and Accounts for the account's currency through
  `IAccountLookup` (both in `Pfm.Application.Contracts`).
- **Converted once** (rule 7, [decision record 0002](docs/adr/0002-convert-once-and-lock-base-currency.md)).
  Recording a transaction converts its amount into the user's base currency at the exchange rate of its date, or
  of the nearest earlier day that has one, and the transaction keeps the result as one value,
  `BaseCurrencyConversion`, in four columns: `BaseCurrency`, `BaseCurrencyAmount`, `ExchangeRate` and
  `ExchangeRateDate`. Rates that arrive later never change it. An edit
  converts again only when it changes the amount, the date or the account. A transaction in the base currency
  itself needs no rate. Budgets read the converted amount as spent; reports will too.
- **Not converted.** With no rate on or before its date the transaction is saved all the same, with no
  `BaseCurrencyAmount`. Every fetch, whether it stored new rates or not, asks Transactions, through
  `ITransactionConversion` (`Pfm.Application.Contracts.Transactions`), to convert every user's not-converted
  transactions, deleted ones included; any edit of one tries again too. Each user's are saved on their own: when
  a user changes theirs at that moment the conversion reads them again and retries, and if they keep changing
  only that user waits for the next fetch (the worker logs a warning).
- **A new base currency converts everything again.** `PATCH /me` with another `baseCurrency` converts every
  transaction of the user into it, each at the rate of its own date, in the same save as the profile. This can
  only happen while the base currency is unlocked, that is while the user has no budget (rule 12; see Budgets below).
- **A transfer is two linked transactions** (rules 3 and 4, [decision record 0001](docs/adr/0001-transfer-is-two-linked-transactions.md)):
  `POST /transfers` records a `transferOut` leg on the source account and a `transferIn` leg on the destination,
  sharing a `TransferId`, in one save. `Transfer` (`Pfm.Domain.Transactions`) is the only way to record, edit or
  delete a leg, so the two always change together; it is not a table of its own, and the repository pairs the two
  rows back into it (`FindTransferAsync`). A leg has no category and no payee, and counts in its account's balance
  like any transaction, so a transfer moves both balances and is neither income nor spending: whatever adds up
  income or expenses goes by type and never sees a leg. Between accounts in one currency there is one `amount`;
  between currencies the request also carries `toAmount`, what arrived, and the rate between the two is never
  looked up. (Each leg's own amount is still converted into the base currency like any transaction's.)
  `GET`, `PUT` and `DELETE /transfers/{id}` go by the transfer id, which each leg carries as `transferId` in the
  transaction list. The transfer's row version is its Transfer out leg's. `DELETE /transactions/{id}` on a leg
  deletes the whole transfer; `PUT /transactions/{id}` on a leg is refused on `type`. `POST /transfers` takes an
  `Idempotency-Key` like `POST /transactions`, kept on the Transfer out leg: a key stands for one transaction or
  one transfer, never one of each.
- **Mark as transfer** (spec IMP-7). `POST /transfers/mark` joins an `expenseId` on one account and an `incomeId` on
  another, which the user picked, into one transfer (`Transfer.Mark`): the expense becomes the `transferOut` leg and
  the income the `transferIn` leg. Each keeps its id, account, amount, date, note and conversion and loses its lines,
  category and payee, so the two drop out of income, expense and budget totals. They must be on different accounts,
  on the same date and, between accounts in one currency, of the same amount; a refund, a leg of a transfer already,
  and income sent as the expense (or the other way round) are refused on the id at fault. Nothing is matched
  automatically. `POST /transfers/{id}/unmark` turns any transfer back (`Transfer.Unmark`): the `transferOut` leg
  becomes an expense on `expenseCategoryId` and the `transferIn` leg income on `incomeCategoryId`, as a leg has no
  category to go back to. A reconciled transaction cannot be marked, and a transfer with a reconciled leg cannot be
  unmarked, until its reconciliation is reopened (see [Reconciliations](#reconciliations)).
- **Deleting is soft** (rule 10): `DELETE /transactions/{id}` stamps `DeletedAt`. The transaction leaves lists,
  balances and `GET` by id at once. It still counts as a transaction of its account and category, so their
  currency and kind stay fixed: it can be recovered, and would otherwise come back not matching them.
  `GET /transactions/deleted` lists those still held, the last deleted first, and `POST /transactions/{id}/restore`
  brings one back, both legs of a transfer together, until 30 days after it was deleted.
- **A bulk action** (spec TXN-7; see the glossary) is `POST /transactions/bulk` (`BulkEditTransactions`): `setCategory`,
  `addTag`, `removeTag`, `moveToAccount` or `delete`, on the transactions named by `transactionIds` or on every
  one that passes `filter` (the list's filters, so the SPA can select all of a view without loading it), at most
  1000 either way: more ids are a validation problem on `transactionIds`, and a filter that more pass answers 422
  (`urn:pfm:problem:bulk-selection-too-large`, with the `max`) so a client can tell the user to narrow it. It is one save: when any selected transaction cannot take it, the answer is a 409
  (`urn:pfm:problem:bulk-action-refused`) listing each one and why (`BulkRefusal`: `notFound`, `transferLeg`, `split`,
  `categoryKind`, `reconciled`, `otherCurrency`, `tooManyTags`), each with the transaction as it is (`transaction`, null when
  not found), so a client can name one it has not loaded, and nothing changes. A category or account that
  cannot be used at all (archived, not the user's) is a validation problem on its field instead. The rules are the
  transaction's (`Transaction.Recategorise`, `MoveTo`, `AddTag`, `RemoveTag`): a split keeps its lines, a transaction
  moves only to an account in its currency and keeps its converted amount, a reconciled one can be recategorised and
  tagged but not moved or deleted, and deleting a leg deletes its transfer, as `DELETE` does. Done, it answers the `count`
  of transactions changed, the other leg of each transfer deleted included. Like a delete it names
  no row version.
- **`GET /transactions`** finds transactions (spec TXN-2, TXN-3), paged like every list. Filters: `from` and `to`
  (dates, both included), `accountId`, `categoryId` (the category and its subcategories, through any line: a split is found by each of
  its categories), `type`, `min` and `max`
  (amounts, both included, compared in whatever currency each transaction is in) and `q`, text found anywhere in
  the payee, the note or the note of any line whatever its case. `sort` is `date` or `amount` and `order` is `asc` or `desc`: the newest
  date first when left out, and within a date or an amount the last recorded first (first when ascending). One
  the list cannot read is a validation problem naming it; an id the user has nothing under lists nothing.
  A `nextCursor` belongs to the filters and sort order it was answered for.
- **`total`**, on the same answer, is of every transaction that passed the filters, whichever page is asked for:
  their `count`, and for each currency their `income`, `expenses` (minus refunds, so it can go below zero) and
  `net` (each amount with the sign of its type, a refund adding). Currencies are never added together. Filtered by a
  category, a transaction counts once and adds only its lines in that category (and its subcategories), as spent
  does (spec section 6): a split is listed whole, and adds only what it spent there. The legs of a transfer are
  listed and counted, and added to neither income nor expenses (spec section 3, rule 3); `type` takes
  `transferOut` and `transferIn` too. It is a `SUM` in the database (`ITransactionRepository.SumAsync`), over the
  lines when filtered by a category, the type's sign applied by `TransactionTypes.Signed`.
- **Indexes** (spec section 10): the list reads `(UserId, Date DESC, Id DESC)`; search is `ILIKE` over `Payee` and
  `Note`, answered by one trigram (`pg_trgm`) GIN index on the two, and over each line's `Note`, answered by a
  trigram GIN index on `TransactionLines`. A test pins all three.
- **Quick add** (spec TXN-8). `GET /transactions/quick-add` answers what a new transaction starts from:
  `lastAccountId`, the account the user recorded on last (by when it was recorded, not its date), and, given a
  `payee` and optionally a `type`, `suggestedCategoryId`: the category most of their transactions with that
  payee have, in any case but never part of one, and of those used equally often the one used last. Each line of
  a split is a use of its category. Refunds, deleted transactions and archived accounts and categories are passed
  over. It reads through its own port,
  `IQuickAddHistory`. A payee with no such history takes the category of the highest-priority
  [categorisation rule](#categorisation-rules) it matches, of the kind the `type` takes. `suggestedBy` says which
  (`history` or `rule`), so the SPA can say why.
- **Archived accounts and categories** take no new transactions; a transaction already on one can be edited
  and keeps it.
- **A foreign key to another module's table** (a transaction's account) is configured in
  `Persistence/ModuleReferences`, because a module's own configuration cannot name another module's types.
  A line carries its category's kind (`CategoryKind`) for a foreign key like the transaction's below, so the
  kind of a category is as fixed for the second line of a split as for the first.
  Two more are named there and added by their migration alone, because the model cannot hold a foreign key to
  columns that change: a transaction's account and currency point at an account and its currency, and its
  user and base currency at a user and their base currency. A third, within the Transactions module
  (`TransactionConfiguration`), points a transaction's category and the kind its type takes (the stored
  column `CategoryKind`, which the database works out from the type) at a category and its kind. They are
  what keeps an account's currency and a category's kind fixed once it has transactions, and spec rule 7,
  when two requests save at once (see "Optimistic concurrency" below).

### Attachments

- **A receipt** is attached to a transaction with `POST /transactions/{id}/attachments`: one file, as the `file`
  field of a `multipart/form-data` body. `GET` on the same path lists a transaction's attachments, and
  `DELETE /attachments/{id}` removes one, row and file both: an attachment is not kept the way a deleted
  transaction is. A deleted transaction keeps its attachments, out of reach until it is recovered.
  The foreign key from an attachment to its transaction restricts deletes (`AttachmentConfiguration`), so the
  worker's `DeletedTransactionPurgeJob`, which removes transactions deleted more than 30 days ago for good (spec
  rule 10) every hour (`DeletedTransactionPurge__RunEvery`), deletes their attachments first: the files from
  storage, then the rows (`DeletedTransactionPurge`). Deleting the transaction alone fails, and cascading would
  leave the files behind with nothing pointing at them.
- **JPEG, PNG or PDF, at most 5 MB** (spec TXN-6). What a file is, is read from its first bytes
  (`AttachmentFormat`), never from its name or the content type the browser sent, and that is the content type
  it is stored and served with. The rules are in `Attachment` (`Pfm.Domain.Transactions`); a file that breaks
  one is a validation problem on `file` saying what can be attached. A request of over 10 MB is cut off before
  it is read: the server answers 413, a problem whose `detail` is the same message as a file found too large
  (`UploadSizeLimit.ExplainTooLarge`).
- **Files live in private object storage**, behind `IObjectStorage` (`Pfm.Application.Abstractions`): any
  storage that speaks the S3 API ([decision record 0004](docs/adr/0004-attachments-in-s3-compatible-storage.md)).
  The key is `users/{user}/attachments/{attachment}`, with nothing the user wrote in it. The upload goes
  through the API; the browser never writes to storage.
- **A file is served only through a signed URL.** `GET /attachments/{id}` answers `url` and `expiresAt` for the
  signed-in user's own attachment, and 404 for anyone else's. The URL needs no sign-in and works for five
  minutes (`Storage__SignedUrlLifetime`, at most an hour), so the SPA asks for it when the user opens the file
  and never keeps it. It serves the file `inline` under the user's file name.
- **Configuration:** `Storage__Bucket` (required: both hosts refuse to start without it), `Storage__Region`
  (`us-east-1`), and `Storage__AccessKey` with `Storage__SecretKey` (left out, the AWS SDK finds the credentials
  itself, as from a role). For storage other than Amazon S3, `Storage__ServiceUrl` is its address, and
  `Storage__PublicUrl` the address browsers reach it at when that differs (Compose: the API calls
  `http://storage:7070`, the browser `http://localhost:7070`). The bucket must exist and must not be public;
  nothing in the app creates it or sets its policy.

### Recurring rules

- **A recurring rule** (`RecurringRule`, in the Transactions module) is a template for income or an expense,
  never a transfer: a type, an account, an amount, a category, optionally a payee and a note, and a schedule
  (`RecurringSchedule`): `weekly`, `monthly`, `yearly` or `everyNDays` with an `interval` of 1 to 365 days, from a
  `startDate` to an optional `endDate`. Its currency is never sent: it is the account's.
- **Due dates are worked out from the start date**, never from the occurrence before: a monthly rule started on
  the 31st is due on the last day of a shorter month and on the 31st again after it, and a yearly rule started on
  29 February is due on the 28th in a year that has none.
- **The worker posts.** `RecurringPostingJob` runs `RecurringPosting` when the worker starts and every ten
  minutes (`RecurringRules__PostEvery`): each occurrence due on or before today in its user's time zone becomes a
  transaction dated on its due date, with the rule's id as its `recurringRuleId`. Occurrences missed while the
  worker was down are posted by its next run, each with its original due date; so are the ones since a start
  date in the past.
- **Through the same rules as a manual transaction.** An occurrence is recorded by `CreateTransaction`, the use
  case behind `POST /transactions`, so it is converted into the base currency the same way and refused the same
  way. A rule whose account or category has been archived posts nothing and stays due: the worker logs a warning
  with how many such rules there are, and the occurrences are posted once the user edits the rule.
- **Exactly once.** The transaction is saved under an idempotency key made of the rule and the due date, then
  the rule moves on to its next occurrence (`NextRunDate`), guarded by its row version. Two workers, or a worker
  that stops in between, cannot post an occurrence twice. A posted transaction the user deletes does not come back.
- **Nothing is posted for a deactivated user** (`IUserDeactivation`, in `Pfm.Application.Contracts.Identity`). When
  they cancel the deletion by signing in, what was due meanwhile is posted like any other missed occurrence.
- **Pause, resume, edit, delete.** `POST /recurring-rules/{id}/pause` stops a rule posting;
  `POST /recurring-rules/{id}/resume` has it post again from today, skipping what was due meanwhile. `PUT` replaces
  the template and the schedule, from the rule's row version in `If-Match` like every edit (412 when stale, 428
  without); posting an occurrence, pausing and resuming move the row version on too, so an edit read before
  any of them is stale: posted transactions stay as they are, and the new schedule carries on after the
  last occurrence posted or skipped (`SettledThrough`), so nothing is posted twice. `DELETE` removes the rule (it
  is not archived: nothing shows it afterwards); its transactions stay, with no `recurringRuleId`.
- **A rule posts or reminds** (`mode`, REC-2): `autoPost`, when left out of a new rule, has the worker post each
  occurrence (left out of an edit, the rule keeps its mode);
  `remind` has it post nothing, and each occurrence due on or before today is a reminder instead
  (`GET /recurring-rules/reminders`, earliest due first). `POST /recurring-rules/{id}/reminders/{dueDate}/confirm`
  records its transaction through `CreateTransaction`, under the same idempotency key the worker would use, for
  the `amount` sent or else the rule's own; `.../dismiss` records nothing. Either moves the rule on, so a
  rule's reminders are answered earliest first: a later one is refused on `dueDate`. A paused rule has none.
- **The upcoming list** (`GET /recurring-rules/upcoming?days=30`, REC-3, 1 to 90 days) is every occurrence of
  every rule that is not paused, due from today and within the days asked for, earliest first, with its `mode`.
  One due today is listed until the worker posts it, and not at all when it is a reminder already.
  The domain works the dates out (`RecurringRule.Upcoming`); nothing is stored for an occurrence before it is settled.
- Skipping or editing one occurrence (REC-4) is still to come.

### Categorisation rules

A user's rules for picking a transaction's category from its payee, or a statement row's description (spec IMP-4,
`CategorisationRule` in `Pfm.Domain.Transactions`; the spec's data model calls the table `CategoryRule`, a name the
code keeps for a category's own rules, `CategoryRules` and `CategoryRuleViolation`).

- **A rule** is a `matchText` (at most 100 characters), a `matchType` (`contains`, `startsWith` or `exactly`), a
  `categoryId` and a `priority` (0 to 1,000,000). Text is compared as `ComparableText` has it: without case, accents
  or extra spaces, as likely duplicates are, so `Phở Hòa` matches `PHO HOA PASTEUR`.
- **Which wins** (`Categoriser`): of the rules that match, on a category of the kind the transaction takes (an expense
  rule never categorises income) that is not archived, the highest priority; of equal ones, the one set up first.
- **Endpoints**: `GET, POST /category-rules`, `PUT, DELETE /category-rules/{id}`, and `PUT /category-rules/order`
  with every rule's id once, the one to try first first, which gives them priorities from their places (the last 0),
  so a reorder is one save. An edit names the rule's `rowVersion` in `If-Match` like every edit (412 when stale, 428
  when missing), and a reorder sends each rule's `rowVersion` with its id: an order made before a rule was set up,
  edited or deleted answers 412 and saves nothing, so it never puts a rule the user has not seen in a place they did
  not choose. A rule named twice is a validation problem on `rules`. The list is unpaged and in the order rules are tried. A new rule left without a
  `priority` goes one above the highest. Rules are deleted, not archived: they are settings, not money.
- **Archived categories.** A rule is set up only on a category that is not archived. Once its category is archived it
  stays listed and categorises nothing; it can be edited keeping that category, or moved to another. Merging a category
  moves its rules to the target with its transactions (`MergeCategory`).
- **Where rules apply**: quick add, for a payee with no history (see Transactions), and the import preview (see
  Imports). Nothing already recorded is recategorised.

### Integrity check

- **Every night the worker checks that the numbers still add up** (spec sections 4 and 10). `IntegrityCheckJob`
  runs `IntegrityCheck` when the worker starts and every day after that (`IntegrityCheck__RunEvery`,
  `1.00:00:00`). It acts for each user who has transactions in turn and hands everything they have, deleted
  transactions included, to `TransactionIntegrity.Check` (`Pfm.Domain.Transactions`), which answers the drift:
  - **`lines`**: a transaction whose lines do not sum to its amount, split or not;
  - **`transfer`**: a transfer id that is not on exactly two legs, or legs on one account, one deleted and the
    other not, of different amounts between accounts in one currency, or on different dates (`TransferDrift`);
  - **`balance`**: an account whose balance as the account list works it out (the `SUM` behind
    `IAccountTransactions`) is not what its transactions come to added up one at a time.
- **Nothing is corrected.** Each drift is logged as a warning naming the user and the rows to look at (the
  transaction, the transfer and its legs, or the account) with the amounts that disagree. Every run ends with
  one line saying how many transactions of how many users it checked and how much drift it found. A run that
  fails is logged as an error and reported like any failed job; the next one checks everything again.
- **Metrics** (meter `Pfm.Worker.IntegrityCheck`): `pfm.integrity_check.runs`, a counter of runs by `result`
  (`consistent`, `drift` or `failed`); `pfm.integrity_check.last_run`, when the last run of each `result` ended
  (Unix seconds); and `pfm.integrity_check.drift`, how much drift of each `kind` (`lines`, `transfer`, `balance`) the last run that finished found.
  A stale `last_run` for `consistent` and `drift` both means the check has stopped running.
- The domain's rules and the database's constraints keep all of this from happening; tests break the rows in
  the database to see each drift found (`IntegrityCheckTests`).

### Reconciliations

An account is reconciled against a bank statement (spec ACC-7, section 3 rule 11), under
`Pfm.Application.Transactions.Reconciliations`:

- `POST /accounts/{id}/reconciliations` starts one with the statement's closing date and balance. An account has
  one in progress at a time (a unique index on the account where `CompletedAt` is null; another answers 409
  `urn:pfm:problem:reconciliation-in-progress`), and a statement is not dated before the account's last one.
- `GET /reconciliations/{id}/transactions` lists what can be ticked: the account's transactions dated on or before
  the statement date that no reconciliation holds, and those ticked in this one (`Transaction.ReconciliationId`).
  `PUT` and `DELETE /reconciliations/{id}/transactions/{transactionId}` tick and untick (`TickTransaction`,
  `UntickTransaction`, which differ only in what they ask of the domain: `TickHandler`), answering the new
  difference.
- The difference is measured from the account's **reconciled balance**: its opening balance plus every
  transaction reconciled in its other reconciliations (`ReconciliationContext`). So the next statement, which
  carries everything before it, is matched by ticking only what came since.
- `POST /reconciliations/{id}/complete` locks the ticked transactions (`Transaction.IsReconciled`) only at a
  difference of exactly zero; otherwise 409 `urn:pfm:problem:reconciliation-difference-not-zero`. Nothing is
  ever added to make it zero.
- A reconciled transaction refuses a change to its amount, date, account, type or refund flag, each named as the
  field at fault; its payee, note and category still change. Deleting it answers 409
  `urn:pfm:problem:transaction-reconciled`. A transfer is locked as soon as either leg is reconciled, and can
  then be neither marked from a reconciled transaction nor unmarked.
- `POST /reconciliations/{id}/reopen` unlocks its transactions, which stay ticked. Only the account's latest can
  be (409 `urn:pfm:problem:reconciliation-not-latest`), as each later one is measured from it.
  `DELETE /reconciliations/{id}` cancels one in progress, unticking what it held.

### Imports

A user imports a bank statement into one of their accounts (spec IMP-1, IMP-3), in the `Imports` module. No bank's
layout is known to the code (spec section 15): the user maps each file, seeing it, and the golden-file tests check the
Vietcombank and Techcombank layouts through mappings, not parsers.

- **Upload.** `POST /imports` takes a CSV file of at most 5 MB as the `file` field of a form beside `accountId`, an
  account of the user's that is not archived (`UploadStatement`). The file must be text: UTF-8 with or without a
  byte order mark, or UTF-16 with one (`StatementFileText`); anything else (a spreadsheet, an archive) is refused on
  `file`. Nothing is imported: the answer is an import batch (`ImportBatch`, `uploaded`) with a
  `suggestedMapping`, a first guess at the separator and the header row after any preamble (`StatementText`), for
  the user to confirm or change.
- **The file is kept only until its rows are imported**, in the batch's own row (`ImportBatches.Content`), never in
  object storage ([decision record 0006](docs/adr/0006-statement-files-kept-in-the-database-until-imported.md)). A
  statement can hold a full account number, which the app otherwise never stores (spec ACC-8); a check constraint
  holds that a committed or failed batch keeps no file. A statement uploaded and never committed is removed, file and
  all, once it is 24 hours old (`AbandonedUploadSweepJob`, every hour; `AbandonedUploadSweep__KeepFor` sets the
  age, `AbandonedUploadSweep__RunEvery` how often it runs).
- **A mapping** (`StatementMapping`; see the glossary) says how to read the file: the `separator` (`comma`,
  `semicolon`, `tab`, `pipe`), `skipFirstRows` (a preamble), `hasHeaderRow`, `skipLastRows` (a footer, such
  as a total; blank rows at the very end are not counted), the `dateColumn`, the `descriptionColumn` (which becomes
  the payee), and either one signed `amountColumn` (below zero for money going out) or a `debitColumn` and a
  `creditColumn`, all counted from 0; and how dates and amounts are written: `dateFormat` (`dayMonthYear`,
  `monthDayYear`, `yearMonthDay`, whatever separates the parts, a time after the date passed over) and
  `decimalSeparator` (`dot` or `comma`; the other mark, or a space, groups thousands in threes). An amount is one
  well-formed number: one sign before or after it, or brackets round it, and nothing else (no currency, no exponent). `MappingInput` reads one as text, the
  same for the preview and the commit, naming each choice it cannot read.
- **The preview** (`GET /imports/{id}/preview`, the mapping in the query string) reads the file with the mapping
  (`Statement`, in `Pfm.Domain.Imports`) and answers every row, numbered by its place in the file: `skipped`,
  `header`, `blank`, `parsed` (with its date, `income` or `expense`, amount and description), `failed` (with
  its `problem`: no date or one that cannot be read or is after today, no amount or one that cannot be read, is zero
  or has more decimals than the account's currency, a debit and a credit on one row, too few cells), or `unmapped`
  while the date and amount columns are not chosen. A failed row is shown and never imported. It is one answer,
  not a page: the user checks the whole statement before committing it. 5,000 rows upload and preview in 335 ms in
  the tests on a developer laptop, against the 5 seconds the spec allows (`ImportTests` generates the file). A
  statement has at most 10,000 rows (`ImportBatch.MaxRows`, counted as its lines, the most rows any separator
  can make of it), twice the 5,000 the spec expects: a longer one is refused on `file` at upload. That bounds the
  preview, which is not paged (see "Lists" below).
- **Likely duplicates** (IMP-3; `LikelyDuplicates`): a parsed row with the date, amount, direction (money in or out)
  and description of an income or expense already on the account, the description compared without case, accents or
  extra spaces, or with the date, amount and direction of a transfer leg on it (a transfer out is money going out),
  whatever its description, as a leg has no payee, is `likelyDuplicate` and not `selected`. Each existing transaction accounts for one row, so two
  coffees on one day against one recorded flag one. Imports asks Transactions for the account's transactions over the
  statement's dates through `IImportedTransactions` (`Pfm.Application.Contracts.Transactions`).
- **Categories from rules** (IMP-4). The preview gives each parsed row a `suggestedCategoryId`: the category of the
  highest-priority [categorisation rule](#categorisation-rules) its description matches, of the row's kind (Imports
  asks through `ICategorisation`). It is a suggestion: the commit records what it is sent, and applies no rule itself
  ([decision record 0008](docs/adr/0008-categorisation-rules-suggest-and-the-commit-records-each-rows-category.md)).
- **Commit.** `POST /imports/{id}/commit` sends the `mapping` the user previewed with, the `rows` to import (by
  number), `rowCategories` (`[{ row, categoryId }]`: a category of its own for any chosen row, of the kind the row
  takes, such as the preview's suggestion or the one the user changed it to; kept on the batch as `RowCategories`),
  and the `expenseCategoryId` and `incomeCategoryId` the rows left without one are recorded on, each needed only
  when such a row goes that way (`CommitImport`). A row category of the wrong kind, archived, not the user's, given
  twice or for a row not chosen is refused on `rowCategories`. The file is read again with the mapping, so the rows chosen are the rows previewed; a row that is
  not parsed is refused on `rows`, and a batch whose account was archived after the upload on `id`. The batch keeps the mapping, the rows and the counts (`rowCount`, `selectedCount`,
  `duplicateCount`: likely duplicates left out, `failedCount`) and the user's today it was read as of
  (`CommittedOn`), becomes `committing`, and the answer is 202. The worker reads the rows, and records their
  transactions, as of that day, so a row dated today stays importable when the user's time zone moves back a day. A
  batch committed already answers 409 `urn:pfm:problem:import-committed`, and so does its preview, as its file is gone.
  One batch at a time is committed into an account (a unique index on the account of the batches `committing`):
  while another is, a commit answers 409 `urn:pfm:problem:import-in-progress` and changes nothing. Once the first
  is imported, the preview finds its rows as likely duplicates, so the same file committed twice in quick
  succession is not imported twice.
- **The worker imports** (`ImportCommitJob`, every two seconds, `Imports__CommitEvery`): `ImportCommits` takes the
  next 250 chosen rows of each batch being committed and records each as income or an expense through
  `IImportedTransactions`, by the same rules as a transaction a user records (converted into the base currency at
  the rate of its date), with the batch's id as its `importBatchId`. Those transactions and the batch's
  `importedCount` moving on are one save, guarded by the batch's row version: a row is never imported twice, and a
  worker that stops in between leaves nothing half done. `GET /imports/{id}` shows the count growing; the SPA asks
  every second. Once the last row is in, the batch is `committed`, lets its file go and raises the budget
  notifications of every date it imported (`IBudgetNotifications.CheckAsync`, BUD-7). Rows that can no longer be
  recorded stop the batch as `failed`, with a code in `failure` for the SPA to say in the user's language
  (`ImportFailure`): `accountArchived`, `categoryArchived`, `accountCurrencyChanged` or `transactionRefused`
  (`IImportedTransactions.RecordAsync` answers a `RecordImportOutcome`); what it imported stays. A batch the
  worker fails on unexpectedly holds up no other batch or user: the failure is logged, the run carries on, and after
  five failures in a row (`ImportCommits.MaxAttempts`, counted in `FailedAttempts`) the batch is stopped as
  `failed` with `unexpected`. Nothing is imported for a deactivated user. Hangfire is not in the repository yet (see
  Exchange rates), so the batches being committed are the queue (decision record 0006).
- **What the spec leaves as it is.** An imported row is income or an expense on the account, in its currency; a row
  that is one leg of a move between the user's own accounts is joined with the other leg afterwards by
  [mark as transfer](#transactions) (IMP-7: nothing is matched automatically). A row may be dated in a period already
  reconciled, as a transaction a user records may; it is not reconciled itself.
- **The mapping is saved per account** (IMP-2; `ImportMapping`, table `ImportMappings`, one per account). Committing a
  statement saves the mapping it was committed with for the account, in the same save, in place of the one saved
  before. An upload, and `GET /imports/{id}` while the batch is uploaded, answer it as `savedMapping` (null when the
  account has none), beside the `suggestedMapping` guessed from the file: the SPA starts from the saved one, so the
  next statement of the same bank is one click, and the user can still change it in the preview; the change is saved
  when that statement is committed. A commit refused saves nothing. The expense and income categories the statement
  was committed with are saved beside the mapping and answered as `savedExpenseCategoryId` and
  `savedIncomeCategoryId`, for the SPA to start from too (when they are not archived since), so a repeat import needs no
  choices; a commit that sends no category of a kind (its rows needed none) keeps the one saved. Two first commits
  into an account that both save its mapping do not fail: the second to save breaks the one-per-account index, which
  `PfmDbContext` reads as a concurrent change, and is made again on the first's mapping.
- **Past imports.** `GET /imports` lists the batches the user committed, whatever became of them (`committing`,
  `committed`, `failed` or `undone`), the last uploaded first, a page at a time, with `accountId` to list one
  account's (`ListImportBatches`): each with its `fileName`, `createdAt` (uploaded), `committedAt`, `undoneAt` and
  counts. A statement uploaded and not committed is not listed.
- **Undo** (IMP-5; `DELETE /imports/{id}`, `UndoImport`;
  [decision record 0007](docs/adr/0007-undoing-an-import-deletes-its-transactions-in-one-save-with-the-batch.md)). Every
  transaction the batch created and not deleted already, edited or not, is deleted softly
  (`IImportedTransactions.DeleteAsync`): it is listed as recently deleted and restorable for 30 days, one by one, like
  any deleted transaction. One marked as a leg of a transfer since deletes the transfer, the other leg with it, as
  deleting that leg does. The batch becomes `undone` with `undoneAt`, keeps its counts, lets go of its file if it still
  had it, and answers 200. The budget notifications of the dates deleted are checked, as a delete does. While any of
  its transactions, or the other leg of a transfer one of them is a leg of, is reconciled, the undo answers 409
  `urn:pfm:problem:import-reconciled`, with how many in `reconciledCount`, and changes nothing. A statement never
  committed answers 409 `urn:pfm:problem:import-not-committed`; one undone already answers as it is. A batch still
  being committed is undone too, which stops it: the deletes and the batch are one save guarded by its row version,
  so of an undo and the worker saving the next rows at the same moment, one goes first and the other is made again
  (the undo, deleting those rows too) or saves nothing (the worker). The mapping saved for the account stays.
- The batch keeps the mapping it was committed with and every transaction its `importBatchId`; an undo is one call to
  `UndoImportHandler`. Each commit and undo leaves an entry in the [audit log](#audit-log), in its own save.

### Budgets

- **A budget** is a spending limit for one expense category in one budget month, the calendar month from the 1st
  (`BudgetMonth`, written `2026-11`), in the user's base currency. `PUT /budgets/{month}/{categoryId}` sets it:
  a new budget answers 201 and sends no `If-Match`; a new amount for one that exists answers 200 and names its
  row version like every edit (412 when stale, 428 without). `DELETE` on the same address removes it: a budget
  is really gone, it is not archived. An archived category takes no new budget and keeps the ones it has.
- **Copying a month** (BUD-4): `POST /budgets/{month}/copy-from/{sourceMonth}` copies, in one action, each budget
  of the source month into the month with its amount (the SPA copies last month's into the month shown), and
  answers how many it `copied`. A category already budgeted in the month keeps its budget, and a budget its
  category no longer takes there is left out: one on an archived category, or one whose parent or subcategory
  is budgeted in the month (rule 6). A budget set in the month at the same moment makes the copy start again
  on what was saved (`CopyBudgets`).
- **Spent is never stored** (spec section 6). `GET /budgets?month=2026-11` works out each budget's `budgeted`,
  `spent`, `remaining`, `percentSpent` and `state`, and the month's `totals`, every time (`BudgetProgress`,
  `BudgetTotals`): Budgets asks Transactions what was spent in each category that month through
  `ICategorySpending` (`Pfm.Application.Contracts.Transactions`). Transactions answers with its own definition of
  spent (`Transaction.SpentAmountIn`, see Transactions above), added up by the database, the month's expenses as they were
  converted into the base currency at the exchange rate of each one's own date: a refund is taken away, so spent
  can be negative, and a split adds each line to its own category. Only expenses count: income never does, nor
  will the legs of a transfer. A transaction is in the budget month of its date, which is a calendar
  date in the user's time zone; left out, `month` is the month it is now there.
- **The overall budget** (BUD-5) caps all of a month's spending, independent of the category budgets: a budget on
  no category (`Budget.SetOverall`, `IsOverall`), at most one a month (a unique index whose nulls are not distinct).
  `PUT /budgets/{month}/overall` sets it (201 new, 200 changed from its row version, 412 and 428 as above) and
  `DELETE` on the same address removes it, leaving the category budgets. `GET /budgets` answers it as `overall`
  (null when the month has none), against every expense of the month whether or not its category has a budget
  (`BudgetProgress.OfOverall`), with the same states and `notConverted`. It is not one of the `items` nor in
  their `totals`, nor in budget vs actual, which compares category budgets; the dashboard shows it apart (below). It locks the base
  currency like any budget, and copying a month copies it too when the month has none.
- **Pace** (BUD-6): in the current month each budget and the overall budget have a `pace`, `aheadOfPace` when the
  share spent is larger than the share of the month elapsed, today included, in the user's time zone, otherwise
  `onPace` (`BudgetProgress.PaceOn`); the month answers that share as `percentOfMonthElapsed`. A past month,
  and one still to come, has neither (null): it shows its final result.
- **Roll-up** (rule 6): spending in a subcategory counts toward a budget on its parent, so a category and its
  subcategory cannot both have a budget in the same month (`Budget.EnsureCanBeSetOn`); two subcategories of one
  parent can. Two requests that budget a parent and its subcategory at the same moment cannot see each other
  before saving, so `SetBudget` reads the month again after saving and takes its budget back when it broke the
  rule.
- **States** (BUD-3): `over` above 100% of the budget, `warning` at or above the user's threshold, otherwise
  `onTrack`. The threshold is the profile setting `budgetWarningPercent` (`PATCH /me`, 1 to 100, 80 until
  changed). `percentSpent` is rounded down, so it never shows a state the budget is not in yet.
- **Not converted.** An expense with no exchange rate on or before its date is left out of spent and counted in
  `notConverted`, on its budget and on the totals, until a rate arrives. A split is one expense, counted once
  however many of a budget's categories, or of the month's budgets, its lines are in.
- **Drilling down** (BUD-8): the transactions behind a budget are the transaction list filtered by its category
  (which takes in its subcategories) and the first and last days of its month. Filtered by a category, the list's
  `total` also answers `spent`: what those transactions spent there in the base currency, with how many are
  `notConverted`, added up by the database like the budget's, so a split counts only its lines there
  and a refund comes off. The per-currency totals beside it are in each transaction's own currency, so only
  `spent` is sure to equal the budget's figure.
- **The base currency locks once a budget exists** (rule 12,
  [decision record 0002](docs/adr/0002-convert-once-and-lock-base-currency.md)). `GET /me` says so
  (`baseCurrencyLocked`), and `PATCH /me` refuses another `baseCurrency` by name while the user has a budget;
  removing every budget unlocks it. A budget keeps the currency it was entered in, and a foreign key points
  its user and currency at the user and their base currency (`ModuleReferences`), so the database refuses the
  change, or the budget, when the two are saved at the same moment; the use case is then made again.
- **A budgeted category stays an expense category**: changing the kind of a category that has a budget, or whose
  subcategory has one, is refused, and a second foreign key holds that when the two are saved at the same moment.
- **Budget notifications** (BUD-7): after any change that affects spent (a transaction recorded, edited, deleted,
  restored or converted once its rate arrives, a bulk action, a transfer unmarked, a category merged, an occurrence posted) or a budget
  set or copied, the use case calls `IBudgetNotifications.CheckAsync` (`Pfm.Application.Contracts.Budgets`) with
  the dates it touched (a merge, every month the category merged into is budgeted in once merged, as its budget may now
  meet spending it did not, and its subcategories' spending now counts toward it). `BudgetNotifications` reads each of those budget months that is this month or later (a month that has ended
  raises none, so a late edit or a merge never notifies about budgets that went over before notifications existed) as the budget screen does and
  raises a `BudgetNotification` for each category budget (the overall budget raises none: BUD-7 is about a category) that moved into `warning` or `over` and has not raised that
  one in the month (`BudgetNotification.Due`): one warning and one over at most, and no warning after over, even
  when spent drops back and crosses again. A unique index holds that when two requests raise the same one at once.
  A notification keeps `budgeted`, `spent` and `percentSpent` as they stood. Each new one is emailed in the
  user's locale with a link to the budget month, unless `emailBudgetNotifications` (`PATCH /me`, true until
  switched off) is false; an email that cannot be sent is logged and the notification stays in the app.
  `GET /budget-notifications` lists them the newest first, a page at a time, with how many are `unread`;
  `POST /budget-notifications/{id}/read` and `POST /budget-notifications/read` mark one or all read.
  Lowering the warning threshold (`PATCH /me`) moves no spent but can move a budget into warning, so it checks the
  current budget month too.
- Budgets reads categories through `ICategoryLookup` and the threshold through `IBudgetWarningThreshold`;
  Identity and Transactions ask whether budgets exist through `IUserBudgets` (`Pfm.Application.Contracts.Budgets`).

### Reports

- **Every report takes the same filters** (spec section 7): `from` and `to`, the report period (both required,
  both included, `to` on or after `from`, from 1900 on), `accountId`, `categoryId` (the category and its
  subcategories) and `compare`. `ReportParameters` (`Pfm.Api.Reports`) reads them into a `ReportFilter`
  (`Pfm.Application.Reports`), naming each one it cannot read in a validation problem; an id the user has nothing
  under reports nothing. A report added later takes them the same way.
- **The previous period** (`ReportPeriod.Previous`, `Pfm.Domain.Reports`) is as many whole months before a period
  of whole calendar months, so March is compared with February and a year with the year before, and as many days
  before any other.
- **Amounts are in the base currency**, as each was converted at the rate of its own date. What is not converted is
  left out of every total and counted in `notConverted` (spec section 3, rule 7), never dropped silently.
- **Reports own no data.** Reports reads Transactions through its contracts (`ICategorySpending`, `ITransactionTrends`,
  `IIncomeAndSpending`, `ICategoryLookup`, `IRecentActivity`), account balances through `IAccountBalances`,
  `IAccountTotals` and `IAccountLookup` (Accounts), budgets through
  `IBudgetComparison` (Budgets) and the base currency through `IBaseCurrency`; nothing a report shows is stored.
- **Dashboard** (RPT-1). `GET /reports/dashboard` answers the whole dashboard in one request, and takes no filters:
  it is of this calendar month in the user's time zone (`from`, `to`). `balance` is what every account that is not
  archived holds in the base currency, as the accounts list adds it up (`IAccountTotals`: `AccountTotals`, which the accounts list adds up through too),
  with `notConverted` balances named; `thisMonth` is income vs expenses for the month; `budgets` adds up budget vs
  actual for the month, counts the budgets in each state and lists the ones over, then at warning, as
  `needingAttention`, with this month's `overall` budget and its `pace` beside them (`IBudgetComparison.OverallAsync`;
  null when it has none). `budgets` is null only when neither a category budget nor an overall budget is set this
  month; `topCategories` is the first five top-level items of
  spending by category. So each card says what the screen or report behind it says. `recentTransactions` are the
  ten the transaction list starts with, and `upcoming` the first ten occurrences the recurring screen lists for the
  next 30 days (`IRecentActivity`), each with its account and category named. `hasAccounts` is false until the user
  has an account that is not archived, for the screen to show a new user where to start. `GetDashboard` holds the
  counts.
- **Spending by category** (RPT-2). `GET /reports/spending-by-category` answers what each category spent, by the same
  definition of spent as budgets (`Transaction.SpentAmountIn`): a refund lowers its category, each line of a split
  counts in its own, and income and transfers never count. Each item has its `share` of the total, to one
  decimal (none when the total is not above zero), ranked by spent. At the top every top-level category stands for
  itself and its subcategories (`SpendingBreakdown`); with `parentId`, each of that category's subcategories stands
  for itself and the category for what was spent on it directly. `drillsInto` says what choosing an item shows
  next: `subcategories` (ask again with `parentId`) or `transactions`. `GET /reports/spending-by-category/transactions`
  lists the expenses with a line on one category itself, each with what it `spent` there, so they add up to that
  category's spent: a split shows only its part, unlike the transaction list's total, which counts it whole. With
  `compare`, each item has `previousSpent`, and a category that spent only in the period before is listed at zero.
- **Income vs expenses** (RPT-3). `GET /reports/income-vs-expenses` answers the `income`, `expenses` and `net` of
  every calendar month the period touches, in order and empty ones included, each with its own `from` and `to` (a
  period starting or ending inside a month covers only its days there), and the same over the whole period as
  `total` (`IncomeVsExpenses`). Income is what income transactions brought in (`Transaction.IncomeAmount`);
  expenses are spent, by the same definition as spending by category, so a refund lowers them; transfers are
  neither. Net is income minus expenses, and the `savingsRate` is net as a percentage of income, to one decimal,
  none when income is not above zero. Reports reads them through `IIncomeAndSpending`. With `categoryId`, only
  the lines in that category and its subcategories count. With `compare`, `previous` has the period before's
  months and total.
- **Trends** (RPT-7). `GET /reports/trends` follows one category (`categoryId`, with its subcategories) or one payee
  (`payee`, matched whole, trimmed and in any case) over the period: `months` lists every calendar month the period
  touches, cut to it at either end and at zero when nothing happened, each with its `total` and its `change` from
  the month before as an `amount` and a `percent` (of the earlier total's size, to one decimal; none from zero; the first
  month's is from the last month of the period compared with, and none without `compare`). `PeriodChange` and `Trend` (`Pfm.Domain.Reports`) work that out. An expense category
  adds up spent, by the same definition as spending by category; an income category adds up the income received in
  it (`Transaction.IncomeAmountIn`), and `isIncome` says which. A payee alone adds up what was spent with it, or with `type=income` the
  income received from it; with `categoryId` as well, only in that category, whose kind says which (so `type` is
  refused with it). `ReportParameters` reads `payee` and `type` beside the filters. A category the user does not have is not found, as the trend is of
  it. With `compare`, `previous` is the trend over the period before and `change` how the total
  moved from it. `previous.months` has one month for each of `months`, at the same place, so the two pair up by
  position (`ReportPeriod.PreviousMonths`): before whole calendar months, as many calendar months; before any other
  period, each month moved back by the period's days, so 10 March to 20 April is set against 27 January to
  17 February and 18 February to 9 March.
- **Budget vs actual** (RPT-6). `GET /reports/budget-vs-actual` sets each category's budgets against what was spent
  against them over the whole budget months the period touches (`ReportPeriod.WholeMonths`): one month, or several
  summed, `months` saying how many each category has a budget in. Budgets answers it through `IBudgetComparison`, by
  its own rules (`BudgetComparison` in `Pfm.Domain.Budgets`): a budget counts what was spent in its own month, in its
  category and, for a top-level one, its subcategories, and its `state` is worked out as on the budget screen with
  the user's warning threshold. What was spent in a month where no budget covered its category is listed apart
  under `unbudgeted`, most first. The account filter narrows spent, never the budgets; the category filter shows
  only that category and its subcategories. Not compared with the period before.
- **Net worth** (RPT-5). `GET /reports/net-worth` answers, at each month end in the period and on its last day when
  it ends inside a month (`ReportPeriod.MonthEnds`), the `assets` (every account but credit cards and loans), the
  `liabilities` (what is owed on credit cards and loans, as a positive number) and `netWorth`, assets minus
  liabilities (`NetWorthOn`, `Pfm.Domain.Reports`). Accounts answers each account's balance at the end of each day
  through `IAccountBalances`, worked out from its transactions as every balance is (none before it opened); archived
  accounts count (spec section 3, rule 10). Unlike a transaction, a balance is converted at the rate of the day it is
  shown on, or the nearest earlier one, the balances in one currency added up first so a total is rounded once; one
  with no rate is left out and counted in `notConverted`. With `accountId`, only that account counts. Balances
  belong to no category and the report is not compared with the period before, so it takes no `categoryId` or
  `compare`.
- **Cash flow** (RPT-4). `GET /reports/cash-flow` answers the running balance at the end of every day of the period,
  or with `interval=week` at the end of every week (Monday to Sunday) and of the period's last day
  (`ReportPeriod.ClosingDays`). With `accountId`, it is that account's balance in its own currency, as the account
  detail page charts it, and 404 when the user has no such account, as on every route about one account; without, it is every account's, archived ones included, in the base currency. The balances
  come from `IAccountBalances`, as net worth's do (none before an account opened), and across accounts the balances in
  one currency are added up and converted at the rate of the day shown, or the nearest earlier one, all of a period's
  rates read at once (`ICurrencyConversion` with dated amounts); a balance with no rate is left out and counted in
  `notConverted`. Takes no `categoryId` or `compare`.
- **The monthly summary** (spec section 7, export). `GET /reports/monthly-summary.pdf?month=2026-11` answers one
  calendar month as a PDF file to download (`monthly-summary-2026-11.pdf`): income, expenses, net and savings rate,
  spending by top-level category with shares, and budget vs actual with what was spent unbudgeted. `GetMonthlySummary`
  asks the three reports' handlers for that month, so every figure is the one the reports show, and writes what they
  answer in `locale` (`en` or `vi`), or the user's own (`IUserLocale`), or English, with amounts as the SPA writes
  them (`Locales.WriteAmount`, `WritePercent`). `IPdfWriter` lays it out with MigraDoc, in Noto Sans carried in the
  assembly ([decision record 0005](docs/adr/0005-pdf-documents-with-migradoc.md)).

### Export

- **The transaction list as CSV** (spec section 7, Export). `GET /export/transactions.csv` takes the filters and sort
  order of `GET /transactions` (one `TransactionListParameters` reads both) and writes every transaction that
  passes, however many pages the list would take, a batch at a time (`ExportTransactions`). One line each:
  `date`, `type`, `isRefund`, `account` and `category` by name (a split's categories joined by `; `, a leg of a
  transfer none), `payee`, `note`, `tags` (joined by `; `), `amount` (a plain decimal above zero, as JSON answers it) and `currency`.
- **CSV is written one way** (`Csv`, RFC 4180, UTF-8 with a byte order mark so a spreadsheet reads Vietnamese
  as written), the same for this file and for the full export (`GET /export/all.zip`).
- **Reports are exported by the SPA**, from the answer it already shows: a report's CSV holds the numbers of its
  table view and nothing the API did not answer, so no report needs a second endpoint.

## Signing in

Users sign in with email and password through ASP.NET Core Identity. The session is an HttpOnly cookie;
the browser never holds a token a script could read.

- **Registering** takes an optional `locale` (`en` or `vi`): it is saved as the new user's locale (see the
  glossary), and their default categories are created in it, in the same transaction as the user. Left out, the
  user has no locale yet (the SPA follows the browser) and the categories are in English.
- **Emails** about signing in (verification, already registered, password reset) are written in the user's
  locale, and in English for a user who has none (`IdentityEmails`).

The use cases (register, verify email, resend verification, sign in, sign out, request a password reset,
reset the password, change the password, set up, turn on, turn off and get recovery codes for two-factor
sign-in, export the user's data, delete the user, erase deactivated users and list the audit log) are in
`Pfm.Application.Identity`, one file each; `IdentityEndpoints` maps them to `/api/v1/auth/...` and
`SecurityEndpoints` maps the signed-in user's own (`/api/v1/me/password`, `/api/v1/me/two-factor/...`). They reach
Identity through ports: `IUsers` (users, passwords, email confirmation tokens, lockout), `ITwoFactor` (authenticator
keys, codes and recovery codes), `IPasswordPolicy`
and `IIdentityEmails`, and `ISessions`, all implemented in `Pfm.Infrastructure.Identity`: `ISessions` is
`CookieSessions` over the sign-in manager, which a host that signs people in registers with `AddSignIn()` while it
configures the cookie itself (the API's `AuthenticationSetup`). Every Identity specific, the sign-in manager and
token providers included, sits in Infrastructure, which is why it references the ASP.NET Core shared framework.
Hosts that sign no one in, such as the worker, get `NoSessions`. A new identity use case (a password reset, two-factor sign-in) is a new file there, plus a
method on a port when it needs something Identity does, such as a reset token.

- **Everything requires a signed-in user** unless its endpoint opts out with `AllowAnonymous`. Only
  registering, signing in, confirming an email address, resending that link, asking for a password reset,
  resetting the password, the anti-forgery token, health and OpenAPI are open; a test fails when that list
  changes.
- **Password reset:** `POST /api/v1/auth/forgot-password` answers 202 whatever the email, and emails a
  registered user a link to `/reset-password` in the SPA. An address with no user is emailed as well, to say so:
  both answers then do the same work, so their timing does not reveal who is registered. `POST /api/v1/auth/reset-password` sets the new
  password from the link's user id and token. A link works once (resetting changes the security stamp it is
  bound to, which also ends every session) and lasts an hour (`PasswordResetTokenProvider`, separate from the
  day that email confirmation links last). A used, expired or tampered link answers 400 with the problem type
  `urn:pfm:problem:reset-link-invalid`; a password to fix answers a validation problem, and leaves the link
  usable.
- **Two-factor sign-in** is optional, with an authenticator app (TOTP: six digits every thirty seconds).
  `POST /api/v1/me/two-factor/setup` answers the `key` and the same key as an `otpauth://` link
  (`authenticatorUri`) for the SPA to draw as a QR code; nothing changes until `POST /api/v1/me/two-factor/enable`
  proves with a `code` that the app holds the key. That answers ten `recoveryCodes`, the only time they are shown;
  each signs the user in once in place of the authenticator. `POST /api/v1/me/two-factor/recovery-codes` replaces
  them and `POST /api/v1/me/two-factor/disable` turns it off (forgetting the key and the codes); both ask for the
  `password`, because a session left open must not be enough. `GET /api/v1/me/two-factor` says whether it is on
  and how many recovery codes are left.
- **Signing in with two factors** is one request, sent twice: `POST /api/v1/auth/login` with the email and
  password answers 401 with the problem type `urn:pfm:problem:two-factor-required`; the same request with
  `twoFactorCode` (or `recoveryCode`) signs in. A wrong code answers 401 `urn:pfm:problem:two-factor-code-incorrect`.
  Only someone who knows the password gets either answer, so a stranger cannot learn who uses two factors.
- **Changing the password:** `POST /api/v1/me/password` with `currentPassword` and `newPassword`. A wrong
  current password or a new one the policy refuses is a validation problem naming the field.
- **Changes that end sessions:** a new password, and turning two-factor on or off, change the user's security
  stamp. Their other sessions end; the one that made the change is issued its cookie again
  (`ISessions.RefreshAsync`) and carries on.
- **Lockout:** five wrong passwords or two-factor codes in a row lock sign-in for that user for fifteen minutes
  (set in `AddUsers`). While locked, even the right password is answered exactly as a wrong one is, so the answer
  does not reveal that the email has a user; the SPA warns about the lock beside that message. Signing in starts
  the count again, and a password reset lifts the lock, since whoever holds the link owns the email address.
- **Rate limiting:** one client address gets 20 attempts a minute across every endpoint that takes a password, a
  code or an emailed link, or that sends email: all of `/api/v1/auth/...` except the anti-forgery token and
  signing out, plus the password and two-factor changes and deleting the user under `/api/v1/me`. Over that, the answer is 429 Problem
  Details with `Retry-After`, given before a password is hashed or the database asked. Set it with
  `RateLimiting__Auth__PermitLimit` and `RateLimiting__Auth__WindowSeconds`. Mark a new endpoint of that kind with
  `LimitAuthAttempts()`. **Behind a proxy** every request arrives from the proxy's address, so all users would
  share one budget: when the API is reachable only through a proxy that sets `X-Forwarded-For` (the SPA's nginx
  image does), set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on the API in that deployment, so it counts the
  real client. Set this way the API believes the header from whoever connects, so the network must let only that
  proxy reach it. Never set it when clients can reach the API directly: they could then name any address. It is
  off by default and nothing in the code turns it on; `compose.yaml` publishes the API directly, so it is not set
  there, and neither repository has a deployment file that puts the API behind the nginx image yet.
- **Anti-forgery:** every POST, PUT, PATCH or DELETE under `/api` needs the `X-XSRF-TOKEN` header. Get a token
  from `GET /api/v1/auth/antiforgery`, keep it in memory, and fetch a new one after signing in or out, because
  it is tied to who is signed in.
- **Cookies** are `Secure` with the `__Host-` prefix everywhere except the Development environment. The local
  stack runs over plain `http://localhost`, so there they follow the request's scheme instead (see
  `CookiePolicy`). In any other environment the API must sit behind TLS. A TLS-terminating proxy in front of it
  will also need forwarded headers configured; that belongs with the deployment work.
- **Sessions** last 7 days, renewed while in use, and end on their next request once the user's security stamp
  changes (a new or reset password, two-factor turned on or off, deleting the user) or the user is deactivated:
  that request answers 401 and clears the cookie. `SessionCheck` reads the user by id on every request that brings a
  session cookie, uncached so every API instance agrees at once; a use case that then loads the signed-in user
  gets them from the same `DbContext` without a second query.
- **Keys** that sign cookies and emailed links are stored in the database, so sessions and links survive
  restarts and are shared by every API instance.

## Export and deleting a user

Spec section 8 (Privacy): a user can take everything the app holds about them, and delete themselves.

- **Export:** `GET /api/v1/export/all.zip` answers a ZIP archive (`ExportData`): `profile.json` (what `GET /me`
  answers, and whether two-factor sign-in is on), one CSV file for each table of user-owned data
  (`transactions.csv`, `accounts.csv`, ...) and a `README.txt` that says how to read them. CSV is RFC 4180 in
  UTF-8 with a byte order mark, so a spreadsheet reads Vietnamese text right; values are written as the API's
  JSON writes them, a list as its items separated by `; `. Text that starts with `=`, `+`, `-`, `@`, a tab or a
  carriage return (a payee an imported statement brought in, say) is written behind a single quote, so a spreadsheet
  shows it rather than runs it as a formula (OWASP, CSV injection, spec section 13); numbers are not, so a negative
  amount stays a number. Every string is text, even one that looks like a number (a payee typed as `-3`); an amount
  is passed as `Csv.Number` to be written as one (`Csv`, which the transactions export writes with too, and whose
  rule the SPA's `csv.ts` applies to the reports it saves). Transactions the user deleted are included while they are still held. Secrets (the
  password hash, the authenticator key, recovery codes) are not exported. The files the user attached are in
  it too, under `attachments/`, each named by its attachment's id and its own file name: they are fetched from
  object storage one by one (`IAttachmentFiles`, in `Pfm.Application.Contracts.Transactions`), those of deleted
  transactions included. The archive is written to the response as it is made (`AsynchronousBody` takes the few writes a ZIP
  archive makes synchronously), so the attached files are never all in memory at once.
- **Deleting:** `POST /api/v1/me/deletion` with the `password` (`DeleteUser`) makes the user a deactivated user
  (see the glossary): `Users.DeactivatedAt` is stamped and their security stamp changes, so every other session
  they have is refused (401) from its next request, and the session that asked is signed out at once. Nothing is
  erased yet. The worker acts for nobody's session: its jobs leave a deactivated user's data alone
  (`IUserDeactivation`).
- **Cancelling:** signing in within 30 days (`Deactivation.CancellationPeriod`) cancels the deletion.
  `POST /api/v1/auth/login` then answers 200 with `deletionCancelled: true` in place of its 204, so the SPA can
  say so. Only a sign-in that gets all the way through cancels: the password alone is not enough for a user who
  signs in with two factors. After 30 days signing in is refused exactly as a wrong password is, erased yet or
  not.
- **Erasing:** the worker's `UserErasureJob` runs `EraseDeactivatedUsers` when it starts and every hour
  (`UserErasure__RunEvery`): for each user deactivated 30 days ago or more it erases everything they own and
  then the user, in one transaction per user. The email address is then free to register again. The files
  they attached are deleted from object storage first, everything under the user's prefix
  (`users/{user}/attachments/`): storage is no part of the transaction, and should the rows then fail to go, the
  next run finds the user again with no file left to delete. So the worker needs the same `Storage__...`
  settings as the API. A user who cannot be erased holds up no other: the run erases the rest, then fails with
  every user it could not erase, which the job logs, and the next run tries them again.
- **Every kind of user data is covered without being named.** Both the export and the erasure go through
  `IUserData` (`Pfm.Application.Abstractions`), implemented by `Persistence/UserData` over every `IUserOwned`
  entity in the model, inside the user filter: the export reads each table's columns as stored (all but the
  owner, the row version and computed columns), and the erasure deletes each table's rows, a table before the
  tables it references. What an entity owns in a table of its own (a transaction's lines) is exported as its
  own file, read through its owner, and erased with its owner by the database's cascade. So a new user-owned
  entity is exported and erased as soon as it is in the model. Three
  things still need a hand, and tests say when: a table that is not user-owned must be argued for in
  `Every_table_holds_rows_a_user_owns_or_is_known_to_hold_none`; `UserErasureFixtures.OwnSomethingOfEveryKindAsync`
  must create a row of every user-owned table, or the export test fails; and what a user owns outside the
  database (a file in object storage) needs its own step in `ExportDataHandler` and `EraseDeactivatedUsersHandler`,
  as attachments have.

## Audit log

Spec section 8 (Auditability): sensitive actions leave a trace the user can review, and nobody can change.

- **What is recorded:** each sign-in that gets through (saying which second factor, and whether it cancelled the
  user's deletion), each statement committed, each import undone, each bulk action (one entry for all the
  transactions it took, `bulkEdit` or `bulkDelete`), each `delete` of a transaction, a transfer, an attachment, a
  budget (overall or of a category), a recurring rule or a categorisation rule, and the user asking to be deleted
  (`deletionRequest`, saved in the transaction that deactivates them; signing in again cancels it, which the sign-in's
  entry says). An
  `AuditEntry` (`Pfm.Domain.Identity`) keeps the `action`, the `entityType` and `entityId` it affected (none for
  a bulk action), `at` and a short `summary`, written by `AuditSummaries` in the user's locale at the time (English
  when they have none). A summary names a file, a category, a tag, an account, an amount or a count; never a
  password, a code, a token or a statement's text. A refused action records nothing.
- **In the same transaction as the action:** other modules record through `IAuditLog`
  (`Pfm.Application.Contracts.Identity`), whose methods add the entry to the use case's unsaved changes; the use case
  then saves both together. Call it just before `SaveChangesAsync`. A use case made again after a concurrent change
  records again, as `DiscardChanges` dropped the first entry with everything else. Signing in records in the
  transaction that clears the failed attempts and cancels a deletion, before the session cookie is issued.
- **Undoing an import** records with `IAuditLog.ImportUndoneAsync(batchId, fileName, transactions)` in `UndoImport`, in
  the save that deletes the batch's transactions. Undoing a batch undone already records nothing.
- **Reading it:** `GET /api/v1/me/audit-log` answers the signed-in user's entries, the newest first, a page at a time
  (`limit` and `cursor`, as every list). The SPA shows it under Settings.
- **Append only** (decision record 0009): there is no endpoint that changes or deletes an entry. `PfmDbContext`
  refuses to save a change or delete of any `IAppendOnly` entity, and the database's
  `TR_AuditEntries_AppendOnly` trigger refuses an `UPDATE` or `TRUNCATE` of `AuditEntries`, and a `DELETE` while
  the entry's user still has a row in `Users`, deactivated or not. Erasing a user after their 30 days (spec section 8,
  Privacy) leaves their entries out of `IUserData.EraseEverythingAsync` (they are `IAppendOnly`) and deletes the user's
  row last; the `AuditEntries` foreign key to `Users` then deletes their entries with it, on delete cascade, and
  nothing else can. The full export has them as
  `audit-entries.csv`, like all user-owned data.

## Hardening

- **Security headers** go on every answer, errors and refusals included (`SecurityHeaders`):
  `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and a content
  security policy that allows nothing (`default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`),
  because the API serves
  data, never pages. Outside Development it also sends `Strict-Transport-Security` for a year. Answers under
  `/api` are `Cache-Control: no-store`, so financial data does not linger in a cache. The SPA's own headers and
  policy are in its repository.
- **Dependency scanning:** restoring packages checks every one, direct or transitive, against NuGet's known
  vulnerabilities (`Directory.Build.props`). One of high or critical severity fails the restore, locally and in
  CI; low and moderate ones are warnings. Fix it by updating the package in `Directory.Packages.props`.
- **Container scanning:** CI builds the API and worker images on the newest base image and scans them with
  [Trivy](https://trivy.dev): the operating system's packages and the .NET runtime inside. A vulnerability of
  high or critical severity that has a fix fails the job; one with no fix yet does not, since nothing can be done
  about it. The usual fix is a rebuild, which picks up the patched base image. To run the same scan locally:

  ```bash
  docker build --pull -t pfm-api .
  docker run --rm -v /var/run/docker.sock:/var/run/docker.sock aquasec/trivy:0.76.2 image \
    --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 pfm-api
  ```
- Lockout and rate limiting are described under [Signing in](#signing-in).

## Observability

- **Logs** are structured, through Serilog: one JSON object per event on the console (readable text in
  Development), with levels set in the `Serilog` section of `appsettings.json`. Every event carries the trace
  and span it was written in. Each request is logged once, with its method, path (never the query string),
  status and time. Health probes are not traced, and are logged only when they fail.
- **Traces and metrics** go through OpenTelemetry: incoming requests, outgoing HTTP calls and database calls
  (Npgsql), plus runtime metrics. A request joins the trace in its `traceparent` header, which the SPA sends
  with every request, so one trace id runs from the browser through to the database.
- **Where they go:** logs, traces and metrics are sent over OTLP to `OTEL_EXPORTER_OTLP_ENDPOINT` when it is set;
  Compose points it at the Aspire Dashboard. The API run from source can send to the Compose dashboard with
  `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317`.
- **Error reports:** an error logged with an exception (an unhandled request error, or a failed background job)
  is reported to the error tracker (Sentry) when `Sentry__Dsn` is set; without it nothing is sent. The tracker is
  a third party, so a report carries only the exception (type, message, stack), the message template and the
  trace id. It leaves out the request, the user, breadcrumbs, and the values of log properties. The trace id
  finds the rest in our own logs and traces. Keep user data out of exception messages. `ErrorReports` holds these
  rules, and `ObservabilityTests` checks them.

The worker shares all of this through `AddPfmObservability` in `Pfm.Infrastructure`.

## User-owned data

An entity that implements `Pfm.Domain.IUserOwned` belongs to one user, and `PfmDbContext` enforces it:

- every query, including bulk updates and deletes, sees only the signed-in user's rows;
- new rows are stamped with the signed-in user, whatever owner they were given;
- changing or deleting another user's row fails, even for a row attached without loading it;
- a row's owner never changes, whether through a tracked change or a bulk update.

`IgnoreQueryFilters()` and raw SQL step outside all of this; don't use them on user-owned data. There are six
exceptions, and `UserOwnedDataTests` fails when a seventh appears: `TransactionRepository.UsersWithNotConvertedAsync`
ignores the filter to answer which users have transactions flagged not converted,
`TransactionRepository.UsersWithTransactionsAsync` which users have any transaction (for the integrity check),
`TransactionRepository.UsersWithDeletedBeforeAsync` which users have transactions deleted long enough ago to purge,
`RecurringRuleRepository.UsersWithDueRulesAsync` which users have a recurring rule with an occurrence
due, `ImportBatchRepository.UsersWithCommittingAsync` which users have a statement committed and waiting to be
imported, and `ImportBatchRepository.UsersWithUploadedBeforeAsync` which users have a statement uploaded and never
committed for long enough to remove. Each runs for the worker, which acts for no user, and answers user ids only, never a row; the worker then
acts for each of those users in turn, inside the filter. Raw SQL is used only on exchange rates, which belong to
no user, and in migrations, which create what the queries read: the view `TransactionLineShares` and the functions
`SplitLineShare` and `CurrencyDecimals`, which share a split's converted amount among its lines for the reports'
heavy sums, as spec section 9 allows raw SQL for. No query reads them with raw SQL: EF reads the view as the
user-owned entity `LineShare`, so the user filter applies to it as to any table. The view selects each line's user,
and never looks past it to another user's rows.

Nothing to configure per entity; `UserIsolationTests` checks every user-owned entity in the model. That includes `LineShare`, read from the view
`TransactionLineShares` that spent is added up from (see Transactions, Spent): the filter keeps it to the user's lines like any table, and as it is the
user's transactions seen another way, the export and the erasure leave it out. Code that
runs for no user, such as background jobs, sees no user-owned data until it acts for a specific user with
`IActForUser.ActFor(userId)`; until the returned handle is disposed, user-owned data is that user's exactly as
if they were signed in, and so are today's date (`IUserCalendar`) and the base currency (`IBaseCurrency`).
Registration uses it to create the new user's default categories.

## Conventions

- **Errors** are RFC 9457 Problem Details, for unhandled exceptions and bare status codes alike.
- **Lists** answer an object with their `items`, a page at a time (spec section 11): `?limit=` (1 to 100, 50 when
  left out) and `?cursor=`, the `nextCursor` of the page before, which is null on the last page. A wrong `limit`
  or `cursor` answers a validation problem naming it. Lists of things a user names are in `NameOrder`, and
  `Paging.PageByName` cuts their pages. A cursor is opaque Base64Url text, written and read by `CursorText`. These answers are not paged, and answer all of their `items` (the preview, its `rows`) in one go: the category tree
  (`GET /categories`), since a tree cannot be cut into pages and a user has dozens of categories, not thousands; a
  budget month (`GET /budgets`), a report over that tree with its totals, at most one budget per category; and a
  transaction's attachments (`GET /transactions/{id}/attachments`), a handful of receipts; and a statement's preview
  (`GET /imports/{id}/preview`), one parsed file the user checks whole before committing, bounded by the 10,000 rows
  a statement may have (the SPA pages it on the screen); and the categorisation rules (`GET /category-rules`), a few
  dozen, listed and reordered whole, in the order they are tried. Reports
  (`GET /reports/...`) are not lists and are not paged either: each answers one row per category or per month of
  its period, with totals over them.
- **Optimistic concurrency** (spec section 11): the `rowVersion` of an account, a transaction, a transfer, a
  recurring rule, a categorisation rule or a budget is PostgreSQL's `xmin`, also sent as the `ETag` of every response that answers one
  of them. `PUT /accounts/{id}`, `PUT /transactions/{id}`, `PUT /transfers/{id}` and
  `PUT /recurring-rules/{id}` and `PUT /category-rules/{id}` must name it in `If-Match` (`If-Match: "1234"`): a version that is no longer the row's
  answers 412 and saves nothing, and leaving the header out answers 428. `IUnitOfWork.SaveChangesAsync` throws
  `ConcurrentChangeException` when the row changed between reading and saving, which the use case answers as
  stale too. A change that names no row version (deleting a transaction, archiving an account, `PATCH /me`) has
  nothing to call stale: it reads again and is made on what was saved (`RetryOnConcurrentChangeAsync`). The
  same exception is thrown when the database refuses a transaction whose account has changed currency or whose
  user has changed base currency since they were read, or a change of either that a transaction recorded
  meanwhile no longer fits. The use case is made again: the transaction takes the new currency, the base
  currency change converts the new transaction too, and the account's currency change is refused, as it has
  a transaction now. A category's kind goes the same way: the change is refused once a transaction got in
  first, and a transaction is refused by the kind its category has just taken. In
  tests, `api.Saves` (`InterleavedSaves`) has another request get in between a use case's read and its save.
- **Idempotency** (spec section 11): `POST /transactions` takes an `Idempotency-Key` header, a key of up to 200
  characters the client makes up for the transaction it is recording. Sending the request again with the same
  key records nothing and answers the first transaction, with the same 201. The key is kept with the
  transaction under a unique index per user, so two copies of the request arriving together still record one:
  the loser's save throws `AlreadyExistsException` and it answers the winner's transaction. A fingerprint of
  what the request asked for is kept beside the key (`IdempotentRequest`): the same key with a different
  transaction records nothing and answers 422 with the problem type `urn:pfm:problem:idempotency-key-reused`,
  so a corrected transaction needs a new key. The retry answers the transaction as it is now, edited since or
  not, and a deleted transaction's key stays used: its retry answers the deleted transaction and records
  nothing, so a late retry cannot bring back what the user deleted. A transfer unmarked since
  (`POST /transfers/{id}/unmark`) keeps its key on what was its Transfer out leg, so it is not recorded twice
  either; as there is no transfer to answer any more, its retry answers 409 with the problem type
  `urn:pfm:problem:transfer-unmarked` (`CreateTransferOutcome.Unmarked`), whose detail says a new transfer needs a
  new key. The same key with a different transfer still answers 422 `idempotency-key-reused`.
- **Money and settings in JSON:** amounts are decimal strings next to a currency, which travels as its ISO 4217
  code (`"VND"`), with at least two decimals and never more than the currency has (`AmountJson`, `AmountText`, `Amounts`); a locale travels as its language tag (`"vi"`); time zones are IANA ids
  (`"Asia/Ho_Chi_Minh"`). The supported currencies are `Pfm.Domain.Currency`. Every other enum travels as its
  name in camelCase (`"income"`, `"creditCard"`), never as a number; `EnumText` (`Pfm.Application.UseCases`) is the one place
  that says so, for the JSON (`JsonSetup`), the export and the commands that read them. Enums are listed
  in the OpenAPI document, so the SPA's generated types know their values.
- **Migrations** are forward-only and never run at start-up. `Pfm.Api migrate` applies them and exits;
  Compose and deployments run it as a separate step. Add one with:

  ```bash
  dotnet tool restore
  dotnet ef migrations add <Name> -p src/Pfm.Infrastructure -s src/Pfm.Api -o Persistence/Migrations
  ```

  A test fails when the model has changes that no migration covers.
- **OpenAPI**: building `Pfm.Api` rewrites `openapi/pfm-api.json`. Commit it with the change that
  caused it; CI fails when the committed file is out of date.
- **Configuration**: the database connection string is `ConnectionStrings__Pfm`. `App__PublicUrl` is the SPA's
  address, which emailed links point to; the API refuses to start without it. Attachments are stored as
  `Storage__...` says (see [Attachments](#attachments)). Email goes out through
  `Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__Password` and `Smtp__From`. Telemetry and error
  reports are configured as described under [Observability](#observability). STARTTLS is required
  (`Smtp__UseStartTls`, on by default); turn it off only for a local catcher such as Mailpit, never with
  credentials. No secrets in the
  repository; the credentials in `compose.yaml` and `appsettings.Development.json` are for the local
  database only.
- **Build**: nullable reference types are on and warnings are errors, for every project.
