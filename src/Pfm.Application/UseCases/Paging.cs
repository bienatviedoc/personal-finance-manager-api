using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Pfm.Application.UseCases;

/// <summary>One page of a list, and the cursor that asks for the next one: null on the last page (spec section 11).</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Page sizes every paged list accepts.</summary>
public static class Paging
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 100;

    public static bool IsValidLimit(int limit) => limit is >= 1 and <= MaxLimit;

    /// <summary>
    /// The page of <paramref name="items"/>, already in <see cref="NameOrder"/>, that starts after
    /// <paramref name="after"/>. Keyed on name and id, so a page never repeats or skips an item that was
    /// there before, even when others are added or renamed in between.
    /// </summary>
    public static Page<TResult> PageByName<T, TResult>(
        this IOrderedEnumerable<T> items, Func<T, string> name, Func<T, Guid> id, int limit, NameCursor? after, Func<T, TResult> select)
    {
        var rest = after is null ? items : items.Where(item => after.Precedes(name(item), id(item)));
        var page = rest.Take(limit + 1).ToList();
        var next = page.Count > limit ? new NameCursor(name(page[limit - 1]), id(page[limit - 1])).ToString() : null;
        return new Page<TResult>([.. page.Take(limit).Select(select)], next);
    }
}

/// <summary>Where a page of a list in <see cref="NameOrder"/> ends: the name and id of its last item, opaque to clients.</summary>
public sealed record NameCursor(string Name, Guid Id)
{
    /// <summary>Whether the cursor comes before an item with this name and id, which is then on a later page.</summary>
    public bool Precedes(string name, Guid id)
    {
        var byName = NameOrder.Comparer.Compare(Name, name);
        return byName < 0 || (byName == 0 && Id.CompareTo(id) < 0);
    }

    /// <summary>A cursor this list gave out; anything else is not one.</summary>
    public static bool TryParse(string? cursor, [NotNullWhen(true)] out NameCursor? parsed)
    {
        parsed = null;
        if (!CursorText.TryDecode(cursor, out var text) || text.Length < 32 || !Guid.TryParseExact(text[..32], "N", out var id))
        {
            return false;
        }

        parsed = new NameCursor(text[32..], id);
        return true;
    }

    public override string ToString() => CursorText.Encode($"{Id:N}{Name}");
}

/// <summary>What a cursor says, which clients only ever see as opaque Base64Url text.</summary>
public static class CursorText
{
    public static string Encode(string text) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));

    /// <summary>What a cursor says; false when it is not Base64Url at all.</summary>
    public static bool TryDecode(string? cursor, [NotNullWhen(true)] out string? text)
    {
        text = cursor is not null && Base64Url.IsValid(cursor) ? Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)) : null;
        return text is not null;
    }
}
