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

Integration tests start a real PostgreSQL in a container through Testcontainers, so Docker must be running.

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

### Use cases

Each use case lives in its module in `Pfm.Application` as one file holding three types: a command (the
input, normalised in its constructor, for example a trimmed email), its FluentValidation validator, and a
handler deriving from `CommandHandler<TCommand, TOutcome>`. `HandleAsync` runs the validator, then the
handler's `ExecuteAsync` for a valid command, and answers a `Result<TOutcome>`: either the outcome (an
enum when there is more than one, `Done` when there is not) or the errors, keyed by command property.
`AddApplication` registers every handler and validator it finds, so adding a use case is adding its file.
`UseCaseTests` fails when a command has no validator, or more than one.

An endpoint only maps HTTP to a use case: it builds the command from the request, calls the handler, and
turns the outcome into a response. `ValidationProblems.For` turns errors into Problem Details keyed by
the request's field names (`Email` becomes `email`).

What a use case needs from outside, it asks for through an interface in its module (a port, such as
`IUsers`), implemented in `Pfm.Infrastructure`, or by the host when only the host can do it (`ISessions`,
the session cookie). Application never references ASP.NET Core or EF Core.

## Signing in

Users sign in with email and password through ASP.NET Core Identity. The session is an HttpOnly cookie;
the browser never holds a token a script could read.

The use cases (register, verify email, resend verification, sign in, sign out, request a password reset,
reset the password) are in
`Pfm.Application.Identity`, one file each; `IdentityEndpoints` maps them to `/api/v1/auth/...`. They reach
Identity through ports: `IUsers` (users, passwords, email confirmation tokens), `IPasswordPolicy`
and `IIdentityEmails`, implemented in `Pfm.Infrastructure.Identity`, and `ISessions`, implemented by the
API's `CookieSessions` over the sign-in manager. Hosts that sign no one in, such as the worker, get
`NoSessions`. A new identity use case (a password reset, two-factor sign-in) is a new file there, plus a
method on a port when it needs something Identity does, such as a reset token.

- **Everything requires a signed-in user** unless its endpoint opts out with `AllowAnonymous`. Only
  registering, signing in, confirming an email address, resending that link, asking for a password reset,
  resetting the password, the anti-forgery token, health and OpenAPI are open; a test fails when that list
  changes.
- **Password reset:** `POST /api/v1/auth/forgot-password` answers 202 whatever the email, and emails a
  registered user a link to `/reset-password` in the SPA. `POST /api/v1/auth/reset-password` sets the new
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
runs for no user, such as background jobs, sees no user-owned data until it acts for a specific user.

## Conventions

- **Errors** are RFC 9457 Problem Details, for unhandled exceptions and bare status codes alike.
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
