using System.Linq.Expressions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Pfm.Application.Abstractions;
using Pfm.Domain;
using Pfm.Infrastructure.Identity;

namespace Pfm.Infrastructure.Persistence;

public class PfmDbContext(DbContextOptions<PfmDbContext> options, ICurrentUser currentUser)
    : IdentityUserContext<User, Guid>(options), IDataProtectionKeyContext, IUnitOfWork
{
    /// <summary>
    /// Keys that sign session cookies and emailed links. Kept in the database so they survive
    /// restarts and are shared by every API instance.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Read by every user-owned entity's query filter, per context instance.</summary>
    private Guid? CurrentUserId => currentUser.UserId;

    private static readonly System.Reflection.PropertyInfo CurrentUserIdProperty =
        typeof(PfmDbContext).GetProperty(nameof(CurrentUserId), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(OwnerChangeInterceptor.Instance);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Trigram indexes back free-text search on payee and note.
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PfmDbContext).Assembly);

        IsolateUserOwnedData(modelBuilder);
    }

    Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardUserOwnedRows();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardUserOwnedRows();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Every query on a user-owned entity sees only the signed-in user's rows; with no user, none.</summary>
    private void IsolateUserOwnedData(ModelBuilder modelBuilder)
    {
        var userOwned = modelBuilder.Model.GetEntityTypes()
            .Where(type => typeof(IUserOwned).IsAssignableFrom(type.ClrType) && !type.IsOwned() && type.BaseType is null)
            .ToList();

        foreach (var entityType in userOwned)
        {
            var row = Expression.Parameter(entityType.ClrType, "row");
            var rowUserId = Expression.Convert(Expression.Property(row, nameof(IUserOwned.UserId)), typeof(Guid?));
            var signedInUserId = Expression.Property(Expression.Constant(this, typeof(PfmDbContext)), CurrentUserIdProperty);
            var filter = Expression.Lambda(Expression.Equal(rowUserId, signedInUserId), row);

            var entity = modelBuilder.Entity(entityType.ClrType);
            entity.HasQueryFilter(filter);

            // UPDATE and DELETE also match on the owner, so a row attached without loading it,
            // claiming to be the user's own, changes nothing in the database.
            entity.Property(nameof(IUserOwned.UserId)).IsConcurrencyToken();
        }
    }

    /// <summary>
    /// New rows belong to the signed-in user. Changed and deleted rows must already belong to them,
    /// and nobody can hand a row to someone else.
    /// </summary>
    private void GuardUserOwnedRows()
    {
        foreach (var entry in ChangeTracker.Entries<IUserOwned>())
        {
            var owner = entry.Property(row => row.UserId);
            switch (entry.State)
            {
                case EntityState.Added:
                    owner.CurrentValue = CurrentUserId ?? throw NotYours("added", "no user is signed in");
                    break;

                case EntityState.Modified or EntityState.Deleted when owner.OriginalValue != CurrentUserId:
                    throw NotYours(entry.State == EntityState.Deleted ? "deleted" : "changed", "it belongs to another user");

                case EntityState.Modified when owner.IsModified && owner.CurrentValue != owner.OriginalValue:
                    throw NotYours("changed", "its owner cannot change");
            }
        }
    }

    private static InvalidOperationException NotYours(string action, string reason) =>
        new($"User-owned data cannot be {action}: {reason}.");
}
