using Microsoft.EntityFrameworkCore;
using Pfm.Application.Accounts;
using Pfm.Domain.Accounts;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Infrastructure.Accounts;

/// <summary>Accounts in the database. The context's user filter keeps every query to the signed-in user's.</summary>
internal sealed class AccountRepository(PfmDbContext db) : IAccountRepository
{
    public async Task<IReadOnlyList<Account>> ListAsync(bool includeArchived, CancellationToken cancellationToken) =>
        await db.Set<Account>().Where(account => includeArchived || !account.IsArchived).ToListAsync(cancellationToken);

    public Task<Account?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Account>().SingleOrDefaultAsync(account => account.Id == id, cancellationToken);

    public void Add(Account account) => db.Set<Account>().Add(account);
}
