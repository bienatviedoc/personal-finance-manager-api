using Pfm.Domain.Identity;

namespace Pfm.Application.Identity;

/// <summary>Where audit entries are kept. Entries are only ever added: there is nothing here to change or remove one.</summary>
public interface IAuditEntryRepository
{
    /// <summary>Adds the entry, saved with the use case's other changes through <see cref="Abstractions.IUnitOfWork"/>.</summary>
    void Add(AuditEntry entry);

    /// <summary>The user's entries, the newest first: at most <paramref name="take"/> of them, those older than <paramref name="after"/> when given.</summary>
    Task<IReadOnlyList<AuditEntry>> ListNewestFirstAsync(int take, Guid? after, CancellationToken cancellationToken);
}
