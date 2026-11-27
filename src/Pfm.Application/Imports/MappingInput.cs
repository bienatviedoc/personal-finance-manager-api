using FluentValidation;
using Pfm.Application.UseCases;
using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>
/// A mapping as the user sends it, for a preview or a commit: the separator and the date format and decimal
/// separator as text (<see cref="EnumText"/>), the rows to skip and the columns (counted from 0) as numbers.
/// Left out, the date format is <c>dayMonthYear</c> and the decimal separator <c>dot</c>.
/// </summary>
public sealed record MappingInput(
    string? Separator = null, int? SkipFirstRows = null, bool? HasHeaderRow = null, int? SkipLastRows = null, int? DateColumn = null,
    int? DescriptionColumn = null, int? AmountColumn = null, int? DebitColumn = null, int? CreditColumn = null, string? DateFormat = null,
    string? DecimalSeparator = null)
{
    /// <summary>What is wrong with each choice it cannot read, keyed by property; none when it reads as a mapping.</summary>
    public IReadOnlyDictionary<string, string[]> Errors()
    {
        var errors = new Dictionary<string, string[]>();
        if (EnumText<Separator>.Read(Separator) is null)
        {
            errors[nameof(Separator)] = [$"Choose {string.Join(", ", EnumText<Separator>.Names)}."];
        }

        foreach (var (name, rows) in new[] { (nameof(SkipFirstRows), SkipFirstRows), (nameof(SkipLastRows), SkipLastRows) })
        {
            if (!StatementMapping.IsSkipInRange(rows))
            {
                errors[name] = [$"Skip from 0 to {StatementMapping.MaxSkippedRows} rows."];
            }
        }

        foreach (var (name, column) in new[]
                 {
                     (nameof(DateColumn), DateColumn), (nameof(DescriptionColumn), DescriptionColumn), (nameof(AmountColumn), AmountColumn),
                     (nameof(DebitColumn), DebitColumn), (nameof(CreditColumn), CreditColumn),
                 })
        {
            if (!StatementMapping.IsColumnInRange(column))
            {
                errors[name] = [$"Choose a column from 0 to {StatementMapping.MaxColumn}."];
            }
        }

        if (AmountColumn is not null && (DebitColumn is not null || CreditColumn is not null))
        {
            errors.TryAdd(nameof(AmountColumn), ["Map one amount column, or a debit and a credit column, not both."]);
        }

        if (DateFormat is not null && EnumText<Domain.Imports.DateFormat>.Read(DateFormat) is null)
        {
            errors[nameof(DateFormat)] = [$"Choose {string.Join(", ", EnumText<Domain.Imports.DateFormat>.Names)}."];
        }

        if (DecimalSeparator is not null && EnumText<Domain.Imports.DecimalSeparator>.Read(DecimalSeparator) is null)
        {
            errors[nameof(DecimalSeparator)] = [$"Choose {string.Join(", ", EnumText<Domain.Imports.DecimalSeparator>.Names)}."];
        }

        return errors;
    }

    /// <summary>The mapping it says; null when <see cref="Errors"/> names anything.</summary>
    public StatementMapping? Read() =>
        Errors().Count > 0
            ? null
            : new StatementMapping(
                EnumText<Separator>.Read(Separator)!.Value, SkipFirstRows ?? 0, HasHeaderRow ?? false, SkipLastRows ?? 0, DateColumn, DescriptionColumn,
                AmountColumn, DebitColumn, CreditColumn,
                EnumText<Domain.Imports.DateFormat>.Read(DateFormat) ?? Domain.Imports.DateFormat.DayMonthYear,
                EnumText<Domain.Imports.DecimalSeparator>.Read(DecimalSeparator) ?? Domain.Imports.DecimalSeparator.Dot);
}

/// <summary>The rule a preview and a commit share: each choice of a mapping that cannot be read is an error on it.</summary>
public static class MappingRules
{
    /// <param name="under">What goes before each choice's name: the mapping's own, when it is one field of a request.</param>
    public static IRuleBuilderOptionsConditions<T, MappingInput?> MappingInRange<T>(this IRuleBuilder<T, MappingInput?> rule, string under = "") =>
        rule.Custom((mapping, context) =>
        {
            foreach (var (property, messages) in mapping?.Errors() ?? new Dictionary<string, string[]>())
            {
                foreach (var message in messages)
                {
                    context.AddFailure(under + property, message);
                }
            }
        });
}
