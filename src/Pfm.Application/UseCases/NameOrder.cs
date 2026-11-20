namespace Pfm.Application.UseCases;

/// <summary>
/// How every list of things a user names (institutions, categories) is ordered: by name, ignoring case,
/// the same on every server whatever its culture; ties by id, so the order never changes between requests.
/// </summary>
public static class NameOrder
{
    public static readonly StringComparer Comparer = StringComparer.InvariantCultureIgnoreCase;

    public static IOrderedEnumerable<T> OrderByName<T>(this IEnumerable<T> items, Func<T, string> name, Func<T, Guid> id) =>
        items.OrderBy(name, Comparer).ThenBy(id);
}
