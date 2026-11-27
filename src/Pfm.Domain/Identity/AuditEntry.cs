namespace Pfm.Domain.Identity;

/// <summary>
/// One line of a user's audit log (spec section 8, Auditability): something sensitive they did, what it affected,
/// when, and a short summary in words. Append only: an entry is never changed or deleted, except with everything
/// else of a user's when they are erased.
/// </summary>
public sealed class AuditEntry : IUserOwned, IAppendOnly
{
    /// <summary>The longest summary kept; a longer one is cut short.</summary>
    public const int MaxSummaryLength = 500;

    /// <summary>For persistence, which sets every property itself.</summary>
    private AuditEntry()
    {
        Summary = "";
    }

    private AuditEntry(AuditAction action, AuditEntityType entityType, Guid? entityId, string summary, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        At = now;
        Summary = summary.Length <= MaxSummaryLength ? summary : summary[..(MaxSummaryLength - 1)] + "…";
    }

    /// <summary>A version 7 UUID, made from <see cref="At"/>, so ordering by it is ordering by when.</summary>
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public AuditAction Action { get; private set; }

    /// <summary>The kind of thing the action affected.</summary>
    public AuditEntityType EntityType { get; private set; }

    /// <summary>The thing it affected; null when it affected several, as a bulk edit does.</summary>
    public Guid? EntityId { get; private set; }

    public DateTimeOffset At { get; private set; }

    /// <summary>What happened, in a sentence, in the user's locale when it was written. Never a secret or a statement's text.</summary>
    public string Summary { get; private set; }

    public static AuditEntry Record(AuditAction action, AuditEntityType entityType, Guid? entityId, string summary, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        return new AuditEntry(action, entityType, entityId, summary, now);
    }
}

/// <summary>What an audit entry records.</summary>
public enum AuditAction
{
    /// <summary>The user signed in.</summary>
    SignIn,

    /// <summary>The user committed a statement to import.</summary>
    ImportCommit,

    /// <summary>The user undid an import, removing the transactions it brought in.</summary>
    ImportUndo,

    /// <summary>The user changed many transactions in one bulk action: a category, a tag or an account.</summary>
    BulkEdit,

    /// <summary>The user deleted many transactions in one bulk action.</summary>
    BulkDelete,

    /// <summary>The user deleted one thing: a transaction, a transfer, an attachment, a budget or a rule.</summary>
    Delete,

    /// <summary>The user asked to be deleted: they are deactivated, and erased after 30 days unless they sign in again.</summary>
    DeletionRequest,
}

/// <summary>The kind of thing an audit entry's action affected.</summary>
public enum AuditEntityType
{
    User,
    ImportBatch,
    Transaction,
    Transfer,
    Attachment,
    Budget,
    RecurringRule,
    CategorisationRule,
}

/// <summary>How a user proved who they are when they signed in.</summary>
public enum SignInMethod
{
    Password,

    /// <summary>The password and a code from their authenticator app.</summary>
    AuthenticatorCode,

    /// <summary>The password and one of their recovery codes.</summary>
    RecoveryCode,
}
