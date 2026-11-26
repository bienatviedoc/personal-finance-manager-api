using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Tags;

/// <summary>Tag names as a command sends them, read.</summary>
public static class TagNames
{
    /// <summary>The names as tags keep them, each once, in order; one that makes no tag is left out, for the validator to refuse.</summary>
    public static IReadOnlyList<string> Of(IEnumerable<string?>? names) =>
        names is null ? [] : [.. names.Select(Tag.Normalised).OfType<string>().Where(name => name.Length <= Tag.MaxNameLength).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
