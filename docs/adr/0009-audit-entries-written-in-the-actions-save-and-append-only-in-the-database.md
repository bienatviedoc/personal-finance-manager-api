# Audit entries are written in the save of the action they record, and the database keeps them append-only

Spec section 8 asks for an audit log of sign-ins, imports, bulk edits and deletes, and section 10 makes `AuditEntry`
append only. Two things had to be decided: how the actions, spread over the Identity, Transactions and Imports modules,
write their entries, and how far "append only" is enforced.

**Written in the action's own save.** The audit log belongs to the Identity module (it is the user's own record, read in
their settings, and spec section 9 fixes six modules). Other modules record through a contract,
`Pfm.Application.Contracts.Identity.IAuditLog`, whose methods only add the entry to the unit of work. The use case then
saves it with the change it records, in one database transaction: a bulk delete and its entry are saved together or not
at all, a refused action leaves none, and a use case made again after a concurrent change records again because the
first entry was discarded with everything else. The contract names each action (`ImportCommittedAsync`,
`BulkActionTakenAsync`, ...) rather than taking free text, so the Identity module writes every summary, in the user's
locale, and decides what a summary may hold: never a secret or a statement's text.

**Append only, three times over.** No endpoint changes or deletes an entry. `PfmDbContext` refuses to save a change or a
delete of any `IAppendOnly` entity. And a PostgreSQL trigger on `AuditEntries` refuses every `UPDATE` and `TRUNCATE`,
and every `DELETE` except of the entries of a deactivated user, or of a user already gone. That one exception is the
erasure: spec section 8 erases all of a user's data 30 days after they delete themselves, and audit entries are their
data (the full export has them too). Only erasing a user deletes the entries of a deactivated user.

## Considered options

- **An audit module of its own.** Rejected: the spec fixes six modules, and the log is read and owned by the user
  as part of their identity and security settings, as their sign-ins are.
- **Writing entries from an EF Core interceptor or domain events**, so use cases need not call anything. Rejected: an
  interceptor sees rows change, not what the user did (a bulk delete is hundreds of rows, a sign-in changes a counter or
  nothing), and a summary needs what only the use case knows, such as the category's name.
- **Writing entries after the action, in a save of their own.** Rejected: the action could be saved and its entry lost,
  or the entry saved for an action that then failed.
- **A free-text summary passed by each caller.** Rejected: summaries would drift in wording and language, and nothing
  would stop a caller passing a payee or a statement row.
- **Enforcing append-only with database permissions** (an application role without `UPDATE` or `DELETE` on the table).
  Rejected for now: the API and the worker share one role, and the erasure must delete. The trigger gives the same
  guarantee against the application and keeps the exception for erasure visible in one place.
- **Keeping entries after the user is erased.** Rejected: the spec erases all their data, and an audit log of someone
  who no longer exists serves nobody the app answers to.

## Consequences

- A use case that records must call `IAuditLog` before it saves, inside the code it retries on a concurrent change.
- A summary is written once, in the locale the user had then; changing locale later does not rewrite old entries.
- Removing a user's entries by any route but deactivating them first fails in the database, so a future clean-up job
  that is not erasure needs a decision of its own.
