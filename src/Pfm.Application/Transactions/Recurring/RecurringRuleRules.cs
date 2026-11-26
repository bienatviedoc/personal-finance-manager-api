using FluentValidation;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>
/// What setting up and editing a recurring rule both send. Values arrive as sent and are read once, in
/// <see cref="RecurringRuleFields"/>, so the handler uses exactly what the validator accepted.
/// </summary>
internal interface IRecurringRuleCommand
{
    string? Type { get; }

    Guid AccountId { get; }

    string? Amount { get; }

    Guid CategoryId { get; }

    string? Payee { get; }

    string? Note { get; }

    string? Frequency { get; }

    int? Interval { get; }

    string? Mode { get; }

    string? StartDate { get; }

    string? EndDate { get; }

    RecurringRuleFields Parsed { get; }
}

/// <summary>A recurring rule command's values, read: null where one was left out or is not what it should be.</summary>
/// <param name="Mode">The mode sent, or auto-post when none was.</param>
public sealed record RecurringRuleFields(
    TransactionType? Type, decimal? Amount, RecurringFrequency? Frequency, DateOnly? StartDate, DateOnly? EndDate, RecurringMode? Mode)
{
    public static RecurringRuleFields Read(string? type, string? amount, string? frequency, string? startDate, string? endDate, string? mode) =>
        new(EnumText<TransactionType>.Read(type), AmountText.TryParse(amount, out var parsed) ? parsed : null,
            EnumText<RecurringFrequency>.Read(frequency), DateText.Read(startDate), DateText.Read(endDate),
            mode is null ? RecurringMode.AutoPost : EnumText<RecurringMode>.Read(mode));
}

/// <summary>
/// The rules of a recurring rule, shared by setting one up and editing it and taken from the domain's. The
/// ones that need only the command are the validator's; the ones that need the account or the category are
/// checked while building the rule.
/// </summary>
internal static class RecurringRuleRules
{
    /// <summary>Recurring rules cover income and expenses only, never transfers (spec section 5).</summary>
    private static readonly string[] Types =
        [.. Enum.GetValues<TransactionType>().Where(type => type.IsIncomeOrExpense()).Select(EnumText.Of)];

    public static void RecurringRuleFieldRules<T>(this AbstractValidator<T> validator)
        where T : IRecurringRuleCommand
    {
        validator.RuleFor(command => command.Type)
            .Must((command, _) => command.Parsed.Type?.IsIncomeOrExpense() == true)
            .WithMessage($"Choose one of: {string.Join(", ", Types)}.");
        validator.RuleFor(command => command.AccountId).Required();
        validator.RuleFor(command => command.Amount)
            .Must((command, _) => command.Parsed.Amount is > 0)
            .WithMessage("Enter an amount greater than zero as a decimal string such as \"125000.00\".");
        validator.RuleFor(command => command.CategoryId).Required();
        validator.RuleFor(command => command.Payee).Must(Transaction.IsValidPayee)
            .WithMessage($"Enter a payee of at most {Transaction.MaxPayeeLength} characters.");
        validator.RuleFor(command => command.Note).Must(Transaction.IsValidNote)
            .WithMessage($"Enter a note of at most {Transaction.MaxNoteLength} characters.");
        validator.RuleFor(command => command.Frequency)
            .Must((command, _) => command.Parsed.Frequency is not null)
            .WithMessage($"Choose one of: {string.Join(", ", EnumText<RecurringFrequency>.Names)}.");
        validator.RuleFor(command => command.Interval)
            .Must((command, interval) => RecurringSchedule.IsValidInterval(command.Parsed.Frequency!.Value, interval))
            .When(command => command.Parsed.Frequency is not null)
            .WithMessage($"Send how many days apart, 1 to {RecurringSchedule.MaxInterval}, for everyNDays and leave it out for any other frequency.");
        validator.RuleFor(command => command.Mode)
            .Must((command, _) => command.Parsed.Mode is not null)
            .WithMessage($"Choose one of: {string.Join(", ", EnumText<RecurringMode>.Names)}, or leave it out to post each occurrence.");
        validator.RuleFor(command => command.StartDate)
            .Must((command, _) => command.Parsed.StartDate is not null)
            .WithMessage(DateText.Message);
        validator.RuleFor(command => command.EndDate)
            .Must((command, written) => written is null || command.Parsed.EndDate is not null)
            .WithMessage(DateText.Message);
        validator.RuleFor(command => command.EndDate)
            .Must((command, _) => command.Parsed.EndDate >= command.Parsed.StartDate)
            .When(command => command.Parsed is { StartDate: not null, EndDate: not null })
            .WithMessage("A recurring rule cannot end before it starts.");
    }

    /// <summary>The schedule a valid command asks for.</summary>
    public static RecurringSchedule Schedule(this IRecurringRuleCommand command) =>
        RecurringSchedule.Of(command.Parsed.Frequency!.Value, command.Interval, command.Parsed.StartDate!.Value, command.Parsed.EndDate);

    /// <summary>A broken rule of the domain, as errors keyed by the command property at fault.</summary>
    public static IReadOnlyDictionary<string, string[]> AsErrors(this RecurringRuleViolation violation) =>
        FieldError.On(violation.Property, violation.Message);
}
