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
docker compose up -d db
dotnet run --project src/Pfm.Api -- migrate
dotnet run --project src/Pfm.Api
```

## Test

```bash
dotnet test Pfm.slnx
```

Domain rules that need nothing but the domain type are unit-tested in `Pfm.Domain.UnitTests`; behaviour through
the API is tested over HTTP in `Pfm.Api.IntegrationTests`. Integration tests start a real PostgreSQL in a container through Testcontainers, so Docker must be running.

CI runs the same tests through `scripts/test-with-coverage.sh`, which merges coverage from every test
project and fails when line coverage of `Pfm.Domain` and `Pfm.Application` is below 85%. Run it locally
the same way; the report lands in `TestResults/coverage/`.

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
enum when there is more than one, `Done` when there is not) or the errors, keyed by command property.
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
breaks them. Rules several modules share (a name, a colour, a locale) are in `ValidationRules`.

**Archive, never delete.** `DELETE` on an institution, account or category archives it: it
leaves lists and pickers (unless they ask for `includeArchived=true`) and stays readable by id, so whatever
already uses it can still show it. An account can be archived at any balance.

### Accounts and balances

- **A balance is never stored.** `Account.BalanceWith` adds what the account's transactions come to onto its
  opening balance, each time it is asked. Accounts asks the Transactions module for that sum through
  `IAccountTransactions` (in `Pfm.Application.Contracts.Transactions`), which answers nothing until transactions
  exist (#9, `NoTransactionsYet`). The same contract says whether an account has transactions, which fixes its
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

## Signing in

Users sign in with email and password through ASP.NET Core Identity. The session is an HttpOnly cookie;
the browser never holds a token a script could read.

- **Registering** takes an optional `locale` (`en` or `vi`): it is saved as the new user's locale (see the
  glossary), and their default categories are created in it, in the same transaction as the user. Left out, the
  user has no locale yet (the SPA follows the browser) and the categories are in English.
- **Emails** about signing in (verification, already registered, password reset) are written in the user's
  locale, and in English for a user who has none (`IdentityEmails`).

The use cases (register, verify email, resend verification, sign in, sign out, request a password reset,
reset the password) are in
`Pfm.Application.Identity`, one file each; `IdentityEndpoints` maps them to `/api/v1/auth/...`. They reach
Identity through ports: `IUsers` (users, passwords, email confirmation tokens), `IPasswordPolicy`
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

`IgnoreQueryFilters()` and raw SQL step outside all of this; don't use them on user-owned data.

Nothing to configure per entity; `UserIsolationTests` checks every user-owned entity in the model. Code that
runs for no user, such as background jobs, sees no user-owned data until it acts for a specific user with
`IActForUser.ActFor(userId)`; until the returned handle is disposed, user-owned data is that user's exactly as
if they were signed in. Registration uses it to create the new user's default categories.

## Conventions

- **Errors** are RFC 9457 Problem Details, for unhandled exceptions and bare status codes alike.
- **Lists** answer an object with their `items`, a page at a time (spec section 11): `?limit=` (1 to 100, 50 when
  left out) and `?cursor=`, the `nextCursor` of the page before, which is null on the last page. A wrong `limit`
  or `cursor` answers a validation problem naming it. Lists of things a user names are in `NameOrder`, and
  `Paging.PageByName` cuts their pages. The one exception is the category tree (`GET /categories`), which answers
  all of its `items` in one go: a tree cannot be cut into pages, and a user has dozens of categories, not thousands.
- **Optimistic concurrency** (spec section 11): an account's `rowVersion` is PostgreSQL's `xmin`, also sent as
  the `ETag` of every response that answers one account. `PUT /accounts/{id}` must name it in `If-Match`
  (`If-Match: "1234"`): a version that is no longer the account's answers 412 and saves nothing, and leaving
  the header out answers 428. `IUnitOfWork.SaveChangesAsync` throws `ConcurrentChangeException` when the row
  changed between reading and saving, which the use case answers as stale too.
- **Money and settings in JSON:** amounts are decimal strings next to a currency, which travels as its ISO 4217
  code (`"VND"`), with at least two decimals and never more than the currency has (`AmountJson`, `AmountText`, `Amounts`); a locale travels as its language tag (`"vi"`); time zones are IANA ids
  (`"Asia/Ho_Chi_Minh"`). The supported currencies are `Pfm.Domain.Currency`. Every other enum travels as its
  name in camelCase (`"income"`, `"creditCard"`), never as a number; `JsonSetup` is the one place that says so. Enums are listed
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
  address, which emailed links point to; the API refuses to start without it. Email goes out through
  `Smtp__Host`, `Smtp__Port`, `Smtp__Username`, `Smtp__Password` and `Smtp__From`. Telemetry and error
  reports are configured as described under [Observability](#observability). STARTTLS is required
  (`Smtp__UseStartTls`, on by default); turn it off only for a local catcher such as Mailpit, never with
  credentials. No secrets in the
  repository; the credentials in `compose.yaml` and `appsettings.Development.json` are for the local
  database only.
- **Build**: nullable reference types are on and warnings are errors, for every project.
