using Pfm.Application.Contracts.Transactions;
using Pfm.Application.Reports;
using Pfm.Application.UseCases;
using Pfm.Domain.Reports;

namespace Pfm.Api.Reports;

/// <summary>
/// Reads what every report takes from its query string (spec section 7): <c>from</c> and <c>to</c>, both
/// required; <c>accountId</c> and <c>categoryId</c>, the filters; and <c>compare</c>. Keeps a message for each one
/// it cannot read, under the parameter's name, as a validation problem names it.
/// </summary>
internal sealed class ReportParameters
{
    public Dictionary<string, string[]> Refused { get; } = [];

    /// <summary>The filter, or null when anything could not be read (<see cref="Refused"/> says what).</summary>
    public ReportFilter? Filter(string? from, string? to, string? accountId, string? categoryId, bool compare)
    {
        var first = Date(nameof(from), from);
        var last = Date(nameof(to), to);
        if (first is { } start && last is { } end && end < start)
        {
            Refused[nameof(to)] = ["End the period on or after the day it starts."];
        }

        var account = Id(nameof(accountId), accountId);
        var category = Id(nameof(categoryId), categoryId);
        return Refused.Count > 0 ? null : new ReportFilter(new ReportPeriod(first!.Value, last!.Value), account, category, compare);
    }

    /// <summary>
    /// What a trend follows besides the filter (spec RPT-7): a payee, trimmed, which with no category can be followed
    /// by its expenses or its income (`type`). A category or a payee is needed; a category's own kind says what it
    /// adds up, so `type` goes with a payee alone.
    /// </summary>
    public (string? Payee, TrendType? Type) Followed(string? categoryId, string? payee, string? type)
    {
        var followed = string.IsNullOrWhiteSpace(payee) ? null : payee.Trim();
        if (followed is { Length: > Payees.MaxLength })
        {
            Refused[nameof(payee)] = [$"Enter a payee of at most {Payees.MaxLength} characters."];
        }

        if (categoryId is null && followed is null)
        {
            Refused[nameof(categoryId)] = ["Choose a category or a payee."];
        }

        var read = EnumText<TrendType>.Read(type);
        if (type is not null && read is null)
        {
            Refused[nameof(type)] = [$"Choose one of: {string.Join(", ", EnumText<TrendType>.Names)}."];
        }
        else if (type is not null && categoryId is not null)
        {
            Refused[nameof(type)] = ["Leave out type with a category: the category's kind says what it adds up."];
        }

        return (followed, read);
    }

    /// <summary>An optional id; null when left out.</summary>
    public Guid? Id(string name, string? text)
    {
        if (text is null)
        {
            return null;
        }

        if (!Guid.TryParse(text, out var id))
        {
            Refused[name] = ["Send an id."];
            return null;
        }

        return id;
    }

    private DateOnly? Date(string name, string? text)
    {
        var date = DateText.Read(text);
        if (date is null)
        {
            Refused[name] = [DateText.Message];
        }
        else if (date < ReportPeriod.Earliest)
        {
            Refused[name] = [$"Enter a date from {DateText.Of(ReportPeriod.Earliest)} on."];
            return null;
        }

        return date;
    }
}
