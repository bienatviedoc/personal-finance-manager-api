using Pfm.Application.Contracts.Identity;
using Pfm.Domain;
using Pfm.Domain.Identity;

namespace Pfm.Application.Identity;

/// <summary>
/// Writes the audit entries of the user a request is signed in as, or a flow acts for (spec section 8,
/// Auditability). Each is added to the use case's unsaved changes, so it is saved in the same transaction as the change
/// it records, or not at all: a use case records before it saves. The summary is written in the user's locale, in
/// English for a user who has none.
/// </summary>
public sealed class AuditLog(IAuditEntryRepository entries, IUserLocale locale, TimeProvider clock)
{
    public Task SignedInAsync(Guid userId, SignInMethod method, bool deletionCancelled, CancellationToken cancellationToken) =>
        RecordAsync(AuditAction.SignIn, AuditEntityType.User, userId, language => AuditSummaries.SignIn(language, method, deletionCancelled), cancellationToken);

    private async Task RecordAsync(AuditAction action, AuditEntityType entityType, Guid? entityId, Func<Locale, string> summary, CancellationToken cancellationToken)
    {
        var language = await locale.OfCurrentUserAsync(cancellationToken) ?? Locale.En;
        entries.Add(AuditEntry.Record(action, entityType, entityId, summary(language), clock.GetUtcNow()));
    }
}
