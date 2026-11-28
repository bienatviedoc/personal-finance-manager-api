using System.Reflection;
using Pfm.Application.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Pfm.Application.Abstractions;
using Pfm.Domain;

namespace Pfm.Infrastructure.Persistence;

/// <summary>
/// Everything one user owns, read or erased through the model: every entity that is <see cref="IUserOwned"/>,
/// whichever module it belongs to. Each query goes through the context's user filter, so it reaches the rows
/// of the user the scope is signed in as or acts for, and no others.
/// </summary>
internal sealed class UserData(PfmDbContext db) : IUserData
{
    private static readonly MethodInfo ReadMethod = typeof(UserData).GetMethod(nameof(ReadAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo ReadOwnedMethod = typeof(UserData).GetMethod(nameof(ReadOwnedAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo EraseMethod = typeof(UserData).GetMethod(nameof(EraseAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// The entities whose rows belong to a user, each mapped to a table of its own. One read from a view (a
    /// transaction's lines as spent adds them up) is the user's rows of other tables seen another way, so it is left out.
    /// </summary>
    public static IReadOnlyList<IEntityType> UserOwnedEntities(IModel model) =>
        [.. model.GetEntityTypes().Where(type => typeof(IUserOwned).IsAssignableFrom(type.ClrType) && !type.IsOwned() && type.BaseType is null && type.GetTableName() is not null)];

    /// <summary>
    /// What a user-owned entity keeps in a table of its own for the parts it owns (a transaction's lines). Those
    /// rows are the user's through their owner: read through it, and erased with it by the database.
    /// </summary>
    public static IReadOnlyList<INavigation> OwnedTables(IEntityType entity) =>
        [.. entity.GetNavigations().Where(navigation => navigation.TargetEntityType.IsOwned() && navigation.TargetEntityType.GetTableName() != entity.GetTableName())];

    public async Task<IReadOnlyList<UserDataTable>> ReadEverythingAsync(CancellationToken cancellationToken)
    {
        var tables = new List<UserDataTable>();
        foreach (var entity in UserOwnedEntities(db.Model))
        {
            tables.Add(await (Task<UserDataTable>)ReadMethod.MakeGenericMethod(entity.ClrType).Invoke(this, [entity, cancellationToken])!);
            foreach (var owned in OwnedTables(entity))
            {
                tables.Add(await (Task<UserDataTable>)ReadOwnedMethod.MakeGenericMethod(entity.ClrType, owned.TargetEntityType.ClrType).Invoke(this, [owned, cancellationToken])!);
            }
        }

        return [.. tables.OrderBy(table => table.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Leaves what is append only (the audit log): the database deletes it with the user's own row, which Identity
    /// erases after, and refuses to delete it any other way.
    /// </summary>
    public async Task EraseEverythingAsync(CancellationToken cancellationToken)
    {
        foreach (var entity in ReferencingFirst([.. UserOwnedEntities(db.Model).Where(entity => !typeof(IAppendOnly).IsAssignableFrom(entity.ClrType))]))
        {
            await (Task)EraseMethod.MakeGenericMethod(entity.ClrType).Invoke(this, [cancellationToken])!;
        }
    }

    /// <summary>
    /// The user's rows of one table, oldest first, read as the columns the table stores them in. That takes in
    /// what an entity keeps in columns alone, and leaves nothing to a list of properties that a new one could
    /// be missing from.
    /// </summary>
    private Task<UserDataTable> ReadAsync<TEntity>(IEntityType entity, CancellationToken cancellationToken)
        where TEntity : class =>
        ReadTableAsync(db.Set<TEntity>().AsNoTracking(), entity, cancellationToken);

    /// <summary>The rows the user's entities own in a table of their own, each naming its owner by id.</summary>
    private Task<UserDataTable> ReadOwnedAsync<TOwner, TOwned>(INavigation navigation, CancellationToken cancellationToken)
        where TOwner : class
        where TOwned : class =>
        ReadTableAsync(
            db.Set<TOwner>().AsNoTracking().SelectMany(owner => EF.Property<IEnumerable<TOwned>>(owner, navigation.Name)),
            navigation.TargetEntityType,
            cancellationToken);

    private async Task<UserDataTable> ReadTableAsync<TEntity>(IQueryable<TEntity> query, IEntityType entity, CancellationToken cancellationToken)
        where TEntity : class
    {
        var columns = entity.GetFlattenedProperties().Where(IsTheUsersData).Select(property => (Name: property.GetColumnName(), Property: property)).ToArray();
        var rows = new List<IReadOnlyList<object?>>();
        var key = entity.FindPrimaryKey() is { Properties: [var single] } ? single : null;
        if (key is not null)
        {
            query = query.OrderBy(row => EF.Property<object>(row, key.Name));
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = query.CreateDbCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            // The first column of a name is the table's own: what the query joins in for the parts an entity
            // owns comes after, and repeats the entity's row once for each part, which is read once.
            var ordinals = columns.Select(column => reader.GetOrdinal(column.Name)).ToArray();
            var keyOrdinal = key is null ? -1 : reader.GetOrdinal(key.GetColumnName());
            object? lastKey = null;
            while (await reader.ReadAsync(cancellationToken))
            {
                if (keyOrdinal >= 0)
                {
                    var rowKey = reader.GetValue(keyOrdinal);
                    if (Equals(rowKey, lastKey))
                    {
                        continue;
                    }

                    lastKey = rowKey;
                }

                rows.Add([.. columns.Select((column, index) => reader.IsDBNull(ordinals[index]) ? null : AsItTravels(reader.GetValue(ordinals[index]), column.Property))]);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return new UserDataTable(entity.GetTableName()!, [.. columns.Select(column => column.Name)], rows);
    }

    /// <summary>
    /// Every column but the owner (the export is one user's), the row version (PostgreSQL's own bookkeeping)
    /// and what the database works out from other columns.
    /// </summary>
    private static bool IsTheUsersData(IProperty property) =>
        property.Name != nameof(IUserOwned.UserId)
        && property is not { IsConcurrencyToken: true, ValueGenerated: ValueGenerated.OnAddOrUpdate }
        && property.GetComputedColumnSql() is null;

    /// <summary>An enum is stored under its name; it leaves as it does in the API's JSON (<see cref="EnumText"/>).</summary>
    private static object AsItTravels(object stored, IProperty property) =>
        stored is string name && (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) is { IsEnum: true } type
            ? EnumText.Of(type, name)
            : stored;

    private Task EraseAsync<TEntity>(CancellationToken cancellationToken)
        where TEntity : class =>
        db.Set<TEntity>().ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Orders the entities so that each comes before every entity it references: a user's transactions are
    /// erased before the accounts and categories they are on. A reference to itself (a subcategory's parent) is
    /// no obstacle: one statement erases all of a user's rows of a table.
    /// </summary>
    private static IReadOnlyList<IEntityType> ReferencingFirst(IReadOnlyList<IEntityType> entities)
    {
        var ordered = new List<IEntityType>();
        var left = entities.ToList();
        while (left.Count > 0)
        {
            var free = left.Where(entity => !left.Any(other => other != entity && References(other, entity))).ToList();
            if (free.Count == 0)
            {
                throw new InvalidOperationException($"User-owned entities reference each other in a circle: {string.Join(", ", left.Select(entity => entity.DisplayName()))}.");
            }

            ordered.AddRange(free);
            left.RemoveAll(free.Contains);
        }

        return ordered;
    }

    /// <summary>Whether the entity, or a part it owns in a table of its own, holds a foreign key to the other.</summary>
    private static bool References(IEntityType entity, IEntityType other) =>
        entity.GetForeignKeys().Concat(OwnedTables(entity).SelectMany(owned => owned.TargetEntityType.GetForeignKeys()))
            .Any(key => key.PrincipalEntityType == other);
}
