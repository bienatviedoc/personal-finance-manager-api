namespace Pfm.Application.Abstractions;

/// <summary>
/// Everything one user owns, whichever module it belongs to: what a full export hands them and what erasing
/// them removes (spec section 8, Privacy). It is the data of the signed-in user, or of the user the flow acts
/// for (<see cref="IActForUser"/>), and of nobody when there is neither. Implemented in Infrastructure over
/// every user-owned entity in the model, so a new kind of user data is covered without being named here.
/// </summary>
public interface IUserData
{
    /// <summary>Every row the user owns, a table at a time, soft-deleted rows included.</summary>
    Task<IReadOnlyList<UserDataTable>> ReadEverythingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Erases every row the user owns, for good. The user themselves is left: Identity erases them after
    /// (<c>IUsers.EraseAsync</c>), in the same transaction.
    /// </summary>
    Task EraseEverythingAsync(CancellationToken cancellationToken);
}

/// <summary>The rows a user owns in one table, as they are stored.</summary>
/// <param name="Name">The table's name, such as <c>Transactions</c>.</param>
/// <param name="Columns">The names of the columns, such as <c>BaseCurrencyAmount</c>.</param>
/// <param name="Rows">One value per column for each row; null where the row has none.</param>
public sealed record UserDataTable(string Name, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);
