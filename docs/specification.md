# Personal Finance Manager — Product & Technical Specification

Nov 17, 2026 · @Bien Le Cong

## 1. Summary

Personal Finance Manager (PFM) is a web application where a person records income and expenses across their bank accounts, sets monthly budgets, and sees where the money goes. This spec proposes an ASP.NET Core API on PostgreSQL with a React + TypeScript single-page app, built as a modular monolith and delivered in four phases.

The requirements given were five words: incomes, expenses, budget, banks, reports. Everything below was a proposal built on those; the open choices were confirmed on Nov 18, 2026 and are recorded in section 15. Terms are defined in `GLOSSARY.md`.

| Decision | Proposal | Reason |
| --- | --- | --- |
| Users | Multi-user system, each user sees only their own data | Costs little up front and avoids a rewrite if a second person is added |
| Bank data | Manual entry plus CSV statement import in v1; no live bank connection | Bank APIs differ by country and need licensing; import covers most of the value |
| Income and expenses | One `Transaction` entity with a type, not two separate tables | Reports, search and budgets all query one place |
| Transfers | Two linked transactions, excluded from income and expense totals | Moving money between own accounts is neither income nor spending |
| Currency | One currency per account, one base currency per user for reports | Supports foreign-currency accounts without complicating daily entry |
| Budgets | Monthly limit per expense category | The simplest model people actually keep up with |
| Architecture | Modular monolith, one deployable API and one static frontend | Right-sized for the domain; modules can be split later if needed |

## 2. Goals, non-goals and assumptions

The product succeeds if a user can enter a transaction in under 10 seconds and answer "how much did I spend on X this month, and am I within budget?" in one click.

**Goals**

- Record every income and expense against an account, with a category, date and note.
- Keep account balances correct and reconcilable against bank statements.
- Set a budget per category per month and show progress against it during the month.
- Report on spending, income, cash flow and net worth over any date range.
- Let the user get their data in (CSV import) and out (CSV and PDF export) at any time.

**Non-goals for v1**

- Live bank connections (Open Banking, Plaid and similar aggregators).
- Investment tracking: holdings, market prices, capital gains.
- Tax calculation or filing.
- Bill payment or any movement of real money.
- Shared household ledgers with roles and permissions.
- Native mobile apps. The web app is responsive and installable as a PWA instead.

**Assumptions**

- Users are individuals managing their own money, not businesses. No invoicing, VAT or double-entry ledger.
- Tens to low hundreds of users at launch, each with up to about 50,000 transactions over the product's life.
- The app is hosted online, so data is reachable from phone and laptop. A self-hosted single-user deployment stays possible through Docker Compose.
- English and Vietnamese are the first two languages; VND, USD and EUR are the first currencies tested.

## 3. Core concepts and domain rules

Eight concepts carry the whole product; the rules under the table are the ones most often got wrong in finance apps and must be enforced in the domain layer, not the UI.

| Concept | Meaning |
| --- | --- |
| Institution | A bank or wallet provider, e.g. Vietcombank, Techcombank, MoMo. Holds a name and logo only |
| Account | A place money sits: current, savings, credit card, cash, e-wallet or loan. Belongs to one institution (optional for cash) and has one currency |
| Transaction | One movement of money on one account on one date. Type is Income, Expense, Transfer out or Transfer in |
| Transfer | A movement between two of the user's own accounts, made of two linked transactions (legs): one Transfer out, one Transfer in |
| Category | What a transaction was for. Two levels (Food > Groceries). Each category is either an income or an expense category |
| Budget | A spending limit for one expense category in one calendar month |
| Recurring rule | A template that creates income or expense transactions on a schedule (salary, rent, subscriptions). Recurring transfers are after v1 |
| Import batch | One uploaded statement file and the transactions created from it |

**Rules**

1. **Money is never a float.** Amounts use `decimal` in C#, `numeric(19,4)` in PostgreSQL, and are sent as strings in JSON. The frontend formats them and does not do arithmetic on them beyond display.
2. **Amounts are stored positive; the type gives the sign.** Account balance = opening balance + income - expenses + refunds + transfers in - transfers out. The one exception to the type giving the sign is a refund: an expense with the refund flag set adds to the balance.
3. **A transfer is two linked transactions** sharing a `TransferId`: a Transfer out leg on account A and a Transfer in leg on account B. Editing or deleting one updates or deletes the other in the same database transaction. Transfers never appear in income, expense or budget totals. A transfer is locked as soon as either leg is reconciled.
4. **Cross-currency transfers store both amounts.** The exchange rate is implied by the two legs, not looked up.
5. **A transaction can be split** across several categories. The split lines must sum exactly to the transaction amount.
6. **Budgets roll up.** Spending in a subcategory counts toward a budget set on its parent. A budget cannot be set on both a parent and its child for the same month.
7. **Reports convert to the base currency at the rate on the transaction date.** Rates are stored per currency pair per day; a missing rate falls back to the nearest earlier date. Rates are fetched automatically each day from an external source; the user never enters them in v1. An amount is converted once, at its transaction-date rate, and is not revalued when rates move. If no rate exists on or before the date, the transaction is still saved and is flagged as not converted in budgets and reports until a rate arrives.
8. **Transaction date is a calendar date with no time**, interpreted in the user's time zone. Created and updated timestamps are UTC.
9. **Credit cards and loans are accounts with a negative balance.** The user enters the amount owed as a positive number and the account shows it as a negative balance. Paying the card is a transfer from a current account, not an expense, so the spending is not counted twice.
10. **Deletes are soft.** Accounts with history are archived, not deleted. An account can be archived at any balance, with a warning when the balance is not zero, and it stays in net worth and reports. Deleted transactions are recoverable for 30 days.
11. **Reconciled transactions are locked.** Amount, date and account cannot change until the transaction is unreconciled. A reconciliation completes only when the difference is exactly zero; the app never creates an adjustment transaction.
12. **The base currency locks once a budget exists.** It can be changed freely until the user's first budget is created.
13. **A category's kind is fixed once it has transactions.** A subcategory always has the same kind as its parent.
14. **"Account" always means a place money sits.** The sign-in identity is the user.

## 4. Banks and accounts

"Banks" in v1 means the user keeps a list of their institutions and accounts, enters or imports transactions, and reconciles balances against statements. Priority uses MoSCoW: Must and Should are in v1, Could is later.

| ID | Requirement | Priority |
| --- | --- | --- |
| ACC-1 | Create, edit and archive institutions (name, optional logo and colour) | Must |
| ACC-2 | Create an account with name, type, institution, currency, opening balance and opening date | Must |
| ACC-3 | Account types: current, savings, credit card, cash, e-wallet, loan | Must |
| ACC-4 | Show current balance per account and total balance per institution, in account currency and base currency | Must |
| ACC-5 | Transfer money between two accounts, including accounts in different currencies | Must |
| ACC-6 | Archive an account: hidden from pickers, kept in history and reports | Must |
| ACC-7 | Reconcile: enter the statement closing balance and date, tick off matching transactions, see the difference reach zero, then lock | Should |
| ACC-8 | Store the last 4 digits of an account number as a label. Full account or card numbers are never stored | Should |
| ACC-9 | Credit card fields: credit limit, statement day, payment due day, with available credit shown | Should |
| ACC-10 | Savings fields: interest rate and maturity date, with a reminder before maturity | Could |
| ACC-11 | Live bank sync through an aggregator, behind an `IBankFeedProvider` interface | Could |

**Acceptance notes**

- Changing the opening balance recalculates the current balance immediately.
- For credit card and loan accounts the form asks for the amount owed instead of an opening balance.
- An account with transactions cannot change currency.
- Balance shown on the account list always equals the sum computed from transactions; a nightly job verifies this and logs any drift.

## 5. Income and expenses

Income and expenses share one transaction screen and one data model; the type toggle at the top of the form is the only difference the user sees.

**Transactions**

| ID | Requirement | Priority |
| --- | --- | --- |
| TXN-1 | Add, edit and delete a transaction: type, account, amount, date, category, payee, note | Must |
| TXN-2 | Transaction list with filters (date range, account, category, type, amount range, tag) and free-text search on payee and note | Must |
| TXN-3 | Sort by date or amount; server-side paging with a running total of the filtered set | Must |
| TXN-4 | Split one transaction across several categories | Should |
| TXN-5 | Tags: free-form labels across categories, e.g. `tet-2027`, `japan-trip` | Should |
| TXN-6 | Attach receipts (JPEG, PNG, PDF, up to 5 MB each) | Should |
| TXN-7 | Bulk edit: change category, tag or account for selected rows; bulk delete | Should |
| TXN-8 | Quick add: the form remembers the last account and suggests a category from the payee's history | Should |
| TXN-9 | Duplicate an existing transaction | Could |

**Categories**

| ID | Requirement | Priority |
| --- | --- | --- |
| CAT-1 | Default category set created on sign-up, in the locale the user registers in | Must |
| CAT-2 | Create, rename, recolour, re-icon and archive categories, two levels deep | Must |
| CAT-3 | Merge category A into B, moving all transactions and budgets. Where both have a budget in the same month, the amounts are added | Should |

**Recurring items**

| ID | Requirement | Priority |
| --- | --- | --- |
| REC-1 | Define a recurring income or expense: amount, account, category, frequency (weekly, monthly, yearly, every N days), start and optional end date | Should |
| REC-2 | Mode per rule: auto-post on the due date, or create a reminder the user confirms | Should |
| REC-3 | Upcoming list for the next 30 days, feeding the cash-flow forecast | Should |
| REC-4 | Skip or edit one occurrence without changing the rule | Could |

**Statement import**

| ID | Requirement | Priority |
| --- | --- | --- |
| IMP-1 | Upload a CSV, map its columns (date, amount or debit/credit, description) and preview before saving | Must |
| IMP-2 | Save the column mapping per account so the next import is one click | Should |
| IMP-3 | Detect duplicates by account, date, amount and normalised description; flag them, never silently drop | Must |
| IMP-4 | Auto-categorise using user rules ("description contains GRAB -> Transport") | Should |
| IMP-5 | Undo an entire import batch | Should |
| IMP-6 | Import OFX/QFX and Excel files | Could |
| IMP-7 | Mark as transfer: pick two transactions on different accounts and turn them into the two legs of one transfer. No automatic matching in v1 | Should |

**Acceptance notes**

- Amount must be greater than zero. Date may be in the future only for recurring previews.
- An income transaction accepts only income categories, and likewise for expenses. Refunds use the expense type with a refund flag (priority: Should).
- An import of 5,000 rows completes preview in under 5 seconds and runs as a background job with progress.
- In the import preview a likely duplicate is shown, marked and unticked, so it is skipped unless the user ticks it. The number of skipped duplicates is shown before commit.
- Undoing an import batch is refused while any of its transactions is reconciled. Otherwise it removes every transaction in the batch, edited or not; they stay recoverable for 30 days.
- Vietcombank and Techcombank statement files must import cleanly at launch, tested against real sample files.
- Recurring rules cover income and expenses only. If the worker was down on a due date, every missed occurrence is posted afterwards with its original due date.

## 6. Budgets

A budget is a monthly spending limit on an expense category, tracked in the user's base currency. Spent is never stored; it is always computed from transactions, so a late edit or import corrects the budget automatically.

| ID | Requirement | Priority |
| --- | --- | --- |
| BUD-1 | Set a budget amount per expense category for a given month | Must |
| BUD-2 | Budget screen per month: budgeted, spent, remaining and a progress bar per category, with totals | Must |
| BUD-3 | Three states per category: on track, warning (at or above 80%), over (above 100%). The warning threshold is a user setting | Must |
| BUD-4 | Copy last month's budgets into a new month in one action | Must |
| BUD-5 | Overall monthly budget: a cap on total spending, independent of category budgets | Should |
| BUD-6 | Pace indicator: compares spent so far against the share of the month elapsed | Should |
| BUD-7 | Notifications in-app and by email when a category crosses warning or over | Should |
| BUD-8 | Drill down from a budget row to the transactions behind it | Should |
| BUD-9 | Rollover: carry unspent or overspent amounts into the next month, per category | Could |
| BUD-10 | Savings goals: a target amount and date, funded by transfers to a savings account | Could |

**Calculation**

```latex
\text{spent}(c, m) = \sum_{t \in \text{expenses},\ \text{month}(t) = m,\ \text{cat}(t) \in \{c\} \cup \text{children}(c)} \text{amount}_{\text{base}}(t)
```

- Transfers are excluded. A refund is an expense with the refund flag set: it adds to the account balance and reduces spent in its category, rather than inflating income.
- Split transactions contribute each line to its own category.
- Spent can be negative, for example when a refund lands in a month with no other spending in its category. It is shown as it is, never clamped to zero.
- Each category sends at most one warning notification and one over notification per month, even if spent drops back and crosses the threshold again.
- A budget month is the calendar month from the 1st, and its boundary follows the user's time zone. A configurable month start day (e.g. the 25th, for salary cycles) is a Could.

## 7. Reports and dashboard

Reports answer five questions: where did the money go, where did it come from, am I saving, what am I worth, and am I within budget. Every report takes a date range, an account filter and a category filter, and shows amounts in the base currency.

| ID | Report | Shows | Chart | Priority |
| --- | --- | --- | --- | --- |
| RPT-1 | Dashboard | Total balance, this month's income, expenses and net, budget status, top 5 categories, last 10 transactions, upcoming recurring items | Cards plus small charts | Must |
| RPT-2 | Spending by category | Expense total and share per category, drill down to subcategory then transactions | Donut plus ranked table | Must |
| RPT-3 | Income vs expenses | Income, expenses and net per month, with savings rate | Grouped bars with net line | Must |
| RPT-4 | Cash flow over time | Daily or weekly running balance per account or across all | Line | Should |
| RPT-5 | Net worth | Assets minus liabilities at each month end | Stacked area | Should |
| RPT-6 | Budget vs actual | Budgeted against spent per category, per month or across a range | Horizontal bars | Should |
| RPT-7 | Trends | One category or payee over time, with period-over-period change | Line | Should |
| RPT-8 | Top payees | Where spending concentrates, by payee | Ranked table | Could |
| RPT-9 | Cash-flow forecast | Projected balance for the next 30 to 90 days from recurring rules | Line with projection | Could |

**Common behaviour**

- Date presets: this month, last month, last 3, 6 and 12 months, this year, last year, custom.
- Compare with the previous period on RPT-2, RPT-3 and RPT-7.
- Every chart has a table view of the same numbers, for accessibility and copy-out.
- Export: CSV for any report or transaction list (Must); PDF for the monthly summary (Should).
- Filters live in the URL, so a report view can be bookmarked.
- Monthly summary email, opt-in (Could).

## 8. Non-functional requirements

Financial data is sensitive even without real money moving, so security and data isolation are treated as functional correctness, not polish.

| Area | Requirement |
| --- | --- |
| Authentication | Email and password with ASP.NET Core Identity; email verification, required before the first sign-in; password reset; optional TOTP two-factor. Google sign-in is a Could |
| Session | HttpOnly, Secure, SameSite cookie issued by the API. No tokens in `localStorage`. Anti-forgery token on state-changing requests |
| Data isolation | Every table carries `UserId`. An EF Core global query filter applies it to every query, and integration tests prove user A cannot read or write user B's rows |
| Transport and storage | TLS everywhere; database and backups encrypted at rest; receipts in private object storage served through short-lived signed URLs |
| Secrets | No secrets in the repository. Environment variables or a secret manager in every environment |
| Hardening | Rate limiting on auth endpoints, account lockout, security headers and CSP, upload type and size validation, dependency scanning in CI |
| Privacy | User can export all their data and delete their user. On deletion the user is signed out and deactivated at once; signing in again within 30 days cancels it, and after 30 days all data is erased. No third-party analytics on financial content |
| Performance | API p95 under 300 ms for lists and under 1 s for reports at 50,000 transactions; first load under 2.5 s on 4G; transaction list virtualised |
| Reliability | Daily automated backups with point-in-time recovery, restore tested quarterly; 99.5% availability target |
| Auditability | Created and updated timestamps on every row; an audit log for sign-ins, imports, bulk edits and deletes |
| Accessibility | WCAG 2.1 AA: keyboard operable, screen-reader labels, colour never the only signal for budget state |
| Internationalisation | English and Vietnamese; locale-aware number, currency and date formats (VND shows no decimals) |
| Responsiveness | Usable from 360 px width up; installable PWA. Offline entry is out of scope for v1 |
| Observability | Structured logs, traces and metrics through OpenTelemetry; health endpoints; error tracking on both tiers |
| Browsers | Latest two versions of Chrome, Edge, Safari and Firefox |

## 9. Architecture and tech stack

The system is a modular monolith: one ASP.NET Core API that owns every business rule, one React SPA that talks only to that API, and one PostgreSQL database.

&#91;embedded content: system architecture · SPA, API with six modules, worker, three stores\]

The browser never touches the database or storage directly, and the worker shares the API's domain code and database, so a recurring item is posted by the same rules as a manual one.

**Backend solution layout**

| Project | Responsibility | Depends on |
| --- | --- | --- |
| `Pfm.Domain` | Entities, value objects (`Money`, `DateRange`), domain rules. No framework references | Nothing |
| `Pfm.Application` | Use cases per module (commands, queries, validators), interfaces for persistence, storage, email, clock | Domain |
| `Pfm.Infrastructure` | EF Core `DbContext` and migrations, Identity, storage, email, CSV parsing, exchange-rate provider | Application |
| `Pfm.Api` | Endpoints, auth, OpenAPI, rate limiting, problem details | Application, Infrastructure |
| `Pfm.Worker` | Hosted background jobs | Application, Infrastructure |

Modules are folders inside each project (`Accounts`, `Transactions`, `Budgets`, `Reports`, `Imports`, `Identity`). A module calls another only through its Application-layer interface, and an architecture test enforces this.

**Stack**

| Concern | Choice | Note |
| --- | --- | --- |
| Runtime | .NET 10 (LTS), C# | ASP.NET Core Minimal APIs grouped per module |
| ORM | EF Core with Npgsql | LINQ for CRUD; raw SQL or Dapper allowed for heavy report queries |
| Database | PostgreSQL 17 or later | `numeric` for money, `pg_trgm` for search |
| Auth | ASP.NET Core Identity, cookie auth | Same-site deployment, so no JWT handling in the browser |
| Validation | FluentValidation | One validator per command |
| Background jobs | Hangfire on PostgreSQL | Recurring posts, imports, emails, integrity check |
| API contract | Built-in OpenAPI generation | TypeScript client generated in CI |
| Observability | Serilog, OpenTelemetry |  |
| Frontend | React 19, TypeScript (strict), Vite |  |
| Routing and data | React Router, TanStack Query |  |
| Forms | React Hook Form, Zod |  |
| UI | Tailwind CSS with shadcn/ui components | Accessible primitives, owned in the repository |
| Charts | Recharts | Behind a wrapper |
| i18n | react-i18next, `Intl` APIs |  |
| Hosting | Docker images; API and worker on a container service, SPA on static hosting behind the same domain | Cloud provider is an open question; Docker Compose for self-hosting |

**Alternatives considered**

- **Microservices:** rejected. The modules share one transactional core, and the team and load do not justify the operational cost.
- **SQL Server:** workable and familiar to .NET teams. PostgreSQL is proposed for zero licence cost and easier self-hosting; EF Core keeps a later switch feasible.
- **Next.js or server-rendered React:** rejected. The app sits entirely behind a login, so SEO and server rendering add no value.
- **JWT in the browser:** rejected in favour of cookies, which keep tokens out of reach of scripts.

## 10. Data model

The schema centres on `Transaction`; balances, budgets and reports are all derived from it rather than stored. Every table has `Id` (UUID v7), `UserId`, `CreatedAt`, `UpdatedAt`, and `DeletedAt` where soft delete applies.

| Entity | Key fields | Notes |
| --- | --- | --- |
| `User` | Email, BaseCurrency, TimeZone, Locale, BudgetWarningPct | ASP.NET Core Identity user, extended |
| `Institution` | Name, LogoKey, Color, IsArchived |  |
| `Account` | InstitutionId?, Name, Type, Currency, OpeningBalance, OpeningDate, CreditLimit?, Last4?, IsArchived | Currency immutable once transactions exist |
| `Category` | ParentId?, Name, Kind (Income or Expense), Icon, Color, IsArchived | Max depth 2, enforced in domain |
| `Transaction` | AccountId, Type, Amount, Currency, Date, Payee, Note, IsRefund, TransferId?, RecurringRuleId?, ImportBatchId?, ReconciliationId?, RowVersion | Amount > 0. Currency copied from account |
| `TransactionLine` | TransactionId, CategoryId, Amount, Note | One line normally, several for a split. Lines sum to the transaction amount |
| `Tag`, `TransactionTag` | Name; TransactionId + TagId | Many to many |
| `Attachment` | TransactionId, StorageKey, FileName, ContentType, Size | File itself lives in object storage |
| `Budget` | CategoryId?, Month (first day), Amount | Null category means the overall monthly budget. Unique on (UserId, CategoryId, Month) |
| `RecurringRule` | Template fields, Frequency, Interval, StartDate, EndDate?, NextRunDate, Mode (AutoPost or Remind) |  |
| `ImportBatch` | AccountId, FileName, Status, RowCount, ImportedCount, DuplicateCount | Undo deletes by ImportBatchId |
| `ImportMapping` | AccountId, ColumnMap (JSON), DateFormat, DecimalSeparator |  |
| `CategoryRule` | MatchText, MatchType, CategoryId, Priority | Used by import and quick add |
| `Reconciliation` | AccountId, StatementDate, StatementBalance, CompletedAt |  |
| `ExchangeRate` | BaseCurrency, QuoteCurrency, Date, Rate | Shared across users; no UserId |
| `AuditEntry` | Action, EntityType, EntityId, At, Summary | Append only |

**Indexes and integrity**

- `Transaction (UserId, Date DESC, Id)` for the list; `(UserId, AccountId, Date)` for balances; `(TransferId)` for linked legs.
- `TransactionLine (CategoryId, TransactionId)` for budget and category reports.
- Trigram index on `Transaction.Payee` and `Note` for search.
- Check constraints: `Amount > 0`; `Budget.Amount >= 0`. Split-sum equality is checked in the domain and re-verified by the nightly integrity job.
- Optimistic concurrency through `RowVersion` on `Transaction`, `Account` and `Budget`.
- Balances are computed with a `SUM` query. If that becomes slow, add a monthly `AccountBalanceSnapshot` table; do not add a mutable `CurrentBalance` column.

## 11. REST API

The API is JSON over HTTPS under `/api/v1`, described by an OpenAPI document from which the TypeScript client is generated, so the frontend never hand-writes request types.

**Conventions**

- Resource-oriented routes, plural nouns, UUIDs in paths.
- Errors use RFC 9457 Problem Details; validation errors list fields under `errors`.
- Money fields are decimal strings (`"125000.00"`) alongside a `currency` code. Dates are `YYYY-MM-DD`; timestamps are ISO 8601 UTC.
- Lists use cursor paging: `?limit=50&cursor=...`, returning `items` and `nextCursor`. The category tree is the exception: `GET /categories` returns all of its `items` at once, because a tree cannot be cut into pages and a user has dozens of categories.
- Updates send `If-Match` with the row version; a stale version returns `412`.
- `POST` endpoints that create money records accept an `Idempotency-Key` header, so a retried request cannot create a duplicate.
- All endpoints require authentication except register, login and password reset.

**Endpoints**

| Area | Method and path | Purpose |
| --- | --- | --- |
| Auth | `POST /auth/register`, `/auth/login`, `/auth/logout`, `/auth/forgot-password`, `/auth/reset-password` | Account lifecycle |
| Auth | `GET /me`, `PATCH /me` | Profile, base currency, time zone, locale |
| Institutions | `GET, POST /institutions`; `GET, PUT, DELETE /institutions/{id}` | CRUD; delete archives |
| Accounts | `GET, POST /accounts`; `GET, PUT, DELETE /accounts/{id}` | CRUD with computed balance |
| Accounts | `POST /accounts/{id}/reconciliations`; `POST /reconciliations/{id}/complete` | Start and lock a reconciliation |
| Transactions | `GET /transactions?from&to&accountId&categoryId&type&tag&q&min&max` | Filtered, paged list with totals |
| Transactions | `POST /transactions`; `GET, PUT, DELETE /transactions/{id}` | Income and expense CRUD, including split lines |
| Transactions | `POST /transactions/bulk` | Bulk recategorise, tag or delete |
| Transfers | `POST /transfers`; `PUT, DELETE /transfers/{id}` | Creates and maintains both legs |
| Attachments | `POST /transactions/{id}/attachments`; `GET, DELETE /attachments/{id}` | Receipt upload and signed download |
| Categories | `GET, POST /categories`; `PUT, DELETE /categories/{id}`; `POST /categories/{id}/merge` | Tree CRUD and merge |
| Tags | `GET /tags` | Autocomplete |
| Budgets | `GET /budgets?month=2026-11` | Budgets with spent, remaining and state |
| Budgets | `PUT /budgets/{month}/{categoryId}`; `DELETE` same; `POST /budgets/{month}/copy-from/{sourceMonth}` | Upsert, remove, copy |
| Recurring | `GET, POST /recurring-rules`; `PUT, DELETE /recurring-rules/{id}`; `GET /recurring-rules/upcoming?days=30` | Rules and upcoming occurrences |
| Imports | `POST /imports` (upload); `GET /imports/{id}/preview`; `POST /imports/{id}/commit`; `DELETE /imports/{id}` | Upload, preview, commit, undo |
| Rules | `GET, POST /category-rules`; `PUT, DELETE /category-rules/{id}` | Auto-categorisation rules |
| Reports | `GET /reports/dashboard`, `/spending-by-category`, `/income-vs-expense`, `/cash-flow`, `/net-worth`, `/budget-vs-actual`, `/trends` | Aggregates; all take `from`, `to` and filters |
| Export | `GET /export/transactions.csv`; `GET /export/all.zip`; `GET /reports/monthly-summary.pdf` | Data out |
| System | `GET /health/live`, `/health/ready` | Probes |

## 12. Frontend

The frontend is a React single-page app in strict TypeScript, organised by feature, with all server data held in TanStack Query and almost no global client state.

**Screens**

| Route | Screen | Key elements |
| --- | --- | --- |
| `/login`, `/register`, `/check-email`, `/verify-email`, `/forgot-password`, `/reset-password` | Auth | Forms with inline validation |
| `/` | Dashboard | Balance, month summary, budget status, top categories, recent and upcoming |
| `/transactions` | Transactions | Filter bar, virtualised table, bulk actions, add and edit in a side drawer |
| `/accounts` | Accounts | Grouped by institution, balances, add account |
| `/accounts/:id` | Account detail | Balance chart, transactions for the account, reconcile |
| `/budgets/:month?` | Budgets | Month switcher, category rows with progress, inline amount edit, copy last month |
| `/reports/:report` | Reports | Shared filter bar, chart and table toggle, export |
| `/recurring` | Recurring | Rules list, upcoming 30 days |
| `/import` | Import wizard | Upload, map columns, preview with duplicates flagged, commit |
| `/settings/*` | Settings | Profile, categories, rules, security, data export and account deletion |

**Design decisions**

- **Structure:** `src/features/<feature>/{api,components,hooks,routes}` plus `src/shared` for UI primitives, formatting and the generated API client. Features do not import from each other's internals.
- **Server state:** TanStack Query with query keys per resource and filter. Mutations invalidate the affected lists, account balances and budget queries. Optimistic updates on add and edit transaction only.
- **Client state:** URL search params for filters and paging; React state for local UI; a small context for user settings. No Redux.
- **Forms:** React Hook Form with Zod schemas. The same rules are enforced again on the server; the client copy is for speed of feedback only.
- **Money:** a single `<Money>` component and `formatMoney(amount, currency, locale)` using `Intl.NumberFormat`. Amount inputs parse locale separators and hold a decimal string, never a JS number for anything above display.
- **Entry speed:** global `N` shortcut opens the add drawer; Enter saves and keeps the drawer open for the next entry; category picker is type-ahead.
- **Charts:** one wrapper per chart type over the charting library, so the library can be swapped without touching feature code.
- **Loading and errors:** skeletons for lists, an error boundary per route, toast on mutation failure with retry.
- **Layout:** sidebar navigation on desktop, bottom tab bar on mobile, light and dark themes.

## 13. Testing, quality and delivery

Money logic is tested hardest at the domain and database level, because that is where a wrong number becomes a lost user.

| Layer | Tooling | What it covers |
| --- | --- | --- |
| Domain unit tests | xUnit | Balance maths, split sums, transfer pairing, budget roll-up, recurrence dates, rounding per currency |
| API integration tests | xUnit, `WebApplicationFactory`, Testcontainers with real PostgreSQL | Every endpoint, validation, concurrency, and cross-user isolation |
| Import tests | Golden CSV files per bank format | Parsing, duplicate detection, undo |
| Frontend unit and component | Vitest, React Testing Library, MSW | Forms, money formatting, filter-to-URL mapping |
| End to end | Playwright | Sign up, add income and expense, transfer, set budget, view report, import CSV |
| Non-functional | k6 load test at 50,000 transactions; axe accessibility checks in CI | Section 8 targets |

**Quality gates on every pull request**

- Build with warnings as errors and nullable reference types on; ESLint and `tsc --noEmit` clean.
- All tests green; coverage at least 85% on the Domain and Application projects.
- OpenAPI document regenerated and the TypeScript client in sync; CI fails on drift.
- EF Core migration included for any model change; migrations are forward-only and reviewed.
- Dependency and container vulnerability scan with no high-severity findings.

**Environments and release**

- Local: Docker Compose starts PostgreSQL, object storage emulator, mail catcher, API and frontend with one command, plus a seed script with a year of sample data.
- CI/CD: build and test on pull request, producing Docker images. Staging and production deployment (merge to `main` deploys to staging; production is a manual approval of the same image) is deferred until a cloud provider is chosen.
- Trunk-based development with short-lived branches and feature flags for unfinished work.
- Database migrations run as a separate deploy step before the new API version starts.

## 14. Delivery plan

v1 is delivered in four phases of about three weeks each, and every phase ends with a deployable increment a real person can use. The durations were a planning estimate for two full-stack developers and one frontend developer. The build is now one developer working with AI agents and no committed date, so the phases are an ordering, not a schedule.

&#91;embedded content: delivery roadmap · 4 phases, 4 gates\]

The diamond under each phase is its exit gate. Should items ship in the phase of their feature (splits, tags and attachments in Phase 2; pace and overall budget in Phase 3); if a phase runs late, Should items move out before the gate does.

**After v1 (Could items, unordered)**

- Budget rollover and savings goals.
- Cash-flow forecast and top payees report.
- OFX and Excel import; Google sign-in.
- Shared household ledger.
- Live bank sync through an aggregator.
- Offline entry in the PWA.

## 15. Risks and open questions

The largest risk is scope: each of the five features has an easy 80% and an expensive last 20%, so the Must/Should/Could split in sections 4 to 7 is the main control.

| Risk | Effect | Mitigation |
| --- | --- | --- |
| Bank CSV formats vary widely and change without notice | Import fails or mis-parses, user loses trust | User-driven column mapping with preview rather than hard-coded bank parsers; golden-file tests for the formats we see |
| Rounding and currency conversion errors | Reports do not add up | Decimal everywhere, per-currency rounding rules, nightly integrity job, property-based tests on totals |
| Duplicate entries from import plus manual entry | Overstated spending | Duplicate detection at import, merge action in the transaction list |
| Reports slow as data grows | Dashboard feels sluggish | Indexes from section 10, load test in Phase 3, monthly snapshots only if measured need |
| Daily entry is tedious and users drop off | Product unused | Quick add, recurring rules, import and auto-categorisation ship in v1, not later |
| Data breach | Severe, given the content | Section 8 controls, no full account numbers stored, external penetration test before public launch |

**Decisions (confirmed Nov 18, 2026)**

| Question | Decision |
| --- | --- |
| Who is this for? | Anyone can register; each user sees only their own data. Shared household ledgers and billing stay out of v1 |
| Hosted or self-hosted? | Hosted online is the target; Docker Compose keeps a single-user self-host possible. Email, object storage and hardening stay in scope |
| Multi-currency from day one? | Yes: one currency per account, one base currency per user |
| Which banks must import at launch? | Vietcombank and Techcombank, tested against real sample statements |
| Is live bank sync a firm later requirement? | No. The account model is not shaped around a sync provider |
| Month start? | The 1st. A configurable start day stays a Could |
| Cash and e-wallets? | Tracked as accounts |
| Offline entry? | No. v1 needs a connection |
| Team, date and hosting budget? | One developer with AI agents, no committed date. Cloud provider not yet chosen, so deployment beyond Docker images and CI is deferred |

**Scope of v1 tickets:** every Must and Should item; Could items are not ticketed.
