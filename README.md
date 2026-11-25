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
environment only.

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

CI runs the same tests through `scripts/test-with-coverage.sh`, which merges coverage from every test
project and fails when line coverage of `Pfm.Domain` and `Pfm.Application` is below 85%. Run it locally
the same way; the report lands in `TestResults/coverage/`.

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
users browsing at once with no pause between requests (10), and `DURATION` (`2m`); pass each with `-e`.

Results, 25 November 2026, on a developer laptop (20 cores, WSL 2) that was busy with other builds, PostgreSQL 17
in Docker:

| Request | p95, 1 user | p95, 10 users | Target |
| --- | --- | --- | --- |
| Transactions, first and next page | 44 ms, 35 ms | 271 ms, 215 ms | 300 ms |
| Transactions this month, searched, by amount | 11 ms, 139 ms, 83 ms | 77 ms, 476 ms, 276 ms | 300 ms |
| **Transactions in a category** | **761 ms** | **4.69 s** | 300 ms |
| Accounts, institutions, categories, recurring rules | 37 ms or less | 217 ms or less | 300 ms |
| Budgets (a month) | 92 ms | 958 ms | 300 ms |
| Spending by category, this month | 111 ms | 934 ms | 1 s |
| Spending by category, this year, in a category, 12 months compared | 740 ms, 682 ms, 569 ms | 6.23 s, 4.98 s, 4.99 s | 1 s |
| Spending transactions of a category | 154 ms | 1.31 s | 1 s |
| Trend of a category compared, of a payee | 325 ms, 58 ms | 2.8 s, 427 ms | 1 s |

With one user every report meets its target, and every list but one: the transactions of a category, whose
spent (BUD-8) is added up in memory over every expense in it. With ten users and no pause the reports that
read a year of transactions into memory to add them up (spending by category, trends) fall well behind. Both are
followed up in #70.

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
  only happen until the base currency locks (rule 12; see Budgets below).
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
  category to go back to. Reconciliation does not exist yet; once it does, a reconciled transaction is refused too.
- **Deleting is soft** (rule 10): `DELETE /transactions/{id}` stamps `DeletedAt`. The transaction leaves lists,
  balances and `GET` by id at once. It still counts as a transaction of its account and category, so their
  currency and kind stay fixed: it can be recovered, and would otherwise come back not matching them.
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
  `IQuickAddHistory`. Category rules (spec section 9) will take part when they exist.
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
  job that will purge deleted transactions after 30 days (spec rule 10) must delete their attachments first:
  the files from storage, then the rows. Deleting the transaction alone fails, and cascading would leave the
  files behind with nothing pointing at them.
- **JPEG, PNG or PDF, at most 5 MB** (spec TXN-6). What a file is, is read from its first bytes
  (`AttachmentFormat`), never from its name or the content type the browser sent, and that is the content type
  it is stored and served with. The rules are in `Attachment` (`Pfm.Domain.Transactions`); a file that breaks
  one is a validation problem on `file` saying what can be attached. A request of over 10 MB is cut off before
  it is read: the server answers 413, a problem whose `detail` is the same message as a file found too large
  (`AttachmentEndpoints.ExplainTooLarge`).
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
- Reminders in place of auto-post (REC-2's other mode), the upcoming list (REC-3) and skipping one occurrence
  (REC-4) are still to come.

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
  spent (`Transaction.SpentAmountIn`, see Transactions above), adding up the month's expenses as they were
  converted into the base currency at the exchange rate of each one's own date: a refund is taken away, so spent
  can be negative, and a split adds each line to its own category. Only expenses count: income never does, nor
  will the legs of a transfer. A transaction is in the budget month of its date, which is a calendar
  date in the user's time zone; left out, `month` is the month it is now there.
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
  `notConverted`, added up by `Transaction.SpentAmountIn` like the budget's, so a split counts only its lines there
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
- **Reports own no data.** Reports reads Transactions through its contracts (`ICategorySpending`, `ITransactionTrends`, `ICategoryLookup`)
  and the base currency through `IBaseCurrency`; nothing a report shows is stored.
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
sign-in, export the user's data, delete the user and erase deactivated users) are in
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
- **Sessions** last 7 days, renewed while in use, and end within 5 minutes of the user's security stamp changing
  (a password reset or deactivation).
- **Keys** that sign cookies and emailed links are stored in the database, so sessions and links survive
  restarts and are shared by every API instance.

## Export and deleting a user

Spec section 8 (Privacy): a user can take everything the app holds about them, and delete themselves.

- **Export:** `GET /api/v1/export/all.zip` answers a ZIP archive (`ExportData`): `profile.json` (what `GET /me`
  answers, and whether two-factor sign-in is on), one CSV file for each table of user-owned data
  (`transactions.csv`, `accounts.csv`, ...) and a `README.txt` that says how to read them. CSV is RFC 4180 in
  UTF-8 with a byte order mark, so a spreadsheet reads Vietnamese text right; values are written as the API's
  JSON writes them. Transactions the user deleted are included while they are still held. Secrets (the
  password hash, the authenticator key, recovery codes) are not exported. The files the user attached are in
  it too, under `attachments/`, each named by its attachment's id and its own file name: they are fetched from
  object storage one by one (`IAttachmentFiles`, in `Pfm.Application.Contracts.Transactions`), those of deleted
  transactions included. The archive is written to the response as it is made (`AsynchronousBody` takes the few writes a ZIP
  archive makes synchronously), so the attached files are never all in memory at once.
- **Deleting:** `POST /api/v1/me/deletion` with the `password` (`DeleteUser`) makes the user a deactivated user
  (see the glossary): `Users.DeactivatedAt` is stamped, their security stamp changes, which ends every session
  they have, and the session that asked is signed out at once. Nothing is erased yet.
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

`IgnoreQueryFilters()` and raw SQL step outside all of this; don't use them on user-owned data. There are three
exceptions, and `UserOwnedDataTests` fails when a fourth appears: `TransactionRepository.UsersWithNotConvertedAsync`
ignores the filter to answer which users have transactions flagged not converted,
`TransactionRepository.UsersWithTransactionsAsync` which users have any transaction (for the integrity check), and
`RecurringRuleRepository.UsersWithDueRulesAsync` which users have a recurring rule with an occurrence
due. Each runs for the worker, which acts for no user, and answers user ids only, never a row; the worker then
acts for each of those users in turn, inside the filter. Raw SQL is used only on exchange rates, which belong to
no user.

Nothing to configure per entity; `UserIsolationTests` checks every user-owned entity in the model. Code that
runs for no user, such as background jobs, sees no user-owned data until it acts for a specific user with
`IActForUser.ActFor(userId)`; until the returned handle is disposed, user-owned data is that user's exactly as
if they were signed in, and so are today's date (`IUserCalendar`) and the base currency (`IBaseCurrency`).
Registration uses it to create the new user's default categories.

## Conventions

- **Errors** are RFC 9457 Problem Details, for unhandled exceptions and bare status codes alike.
- **Lists** answer an object with their `items`, a page at a time (spec section 11): `?limit=` (1 to 100, 50 when
  left out) and `?cursor=`, the `nextCursor` of the page before, which is null on the last page. A wrong `limit`
  or `cursor` answers a validation problem naming it. Lists of things a user names are in `NameOrder`, and
  `Paging.PageByName` cuts their pages. Three answers are not paged, and answer all of their `items` in one go: the category tree
  (`GET /categories`), since a tree cannot be cut into pages and a user has dozens of categories, not thousands; a
  budget month (`GET /budgets`), a report over that tree with its totals, at most one budget per category; and a
  transaction's attachments (`GET /transactions/{id}/attachments`), a handful of receipts.
- **Optimistic concurrency** (spec section 11): the `rowVersion` of an account, a transaction, a transfer, a
  recurring rule or a budget is PostgreSQL's `xmin`, also sent as the `ETag` of every response that answers one
  of them. `PUT /accounts/{id}`, `PUT /transactions/{id}`, `PUT /transfers/{id}` and
  `PUT /recurring-rules/{id}` must name it in `If-Match` (`If-Match: "1234"`): a version that is no longer the row's
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
