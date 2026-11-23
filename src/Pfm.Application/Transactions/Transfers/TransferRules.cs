using FluentValidation;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Transfers;

/// <summary>
/// What recording and editing a transfer both send. Values arrive as sent and are read once, in
/// <see cref="TransferFields"/>, as a transaction's are.
/// </summary>
internal interface ITransferCommand
{
    Guid FromAccountId { get; }

    Guid ToAccountId { get; }

    string? Amount { get; }

    string? ToAmount { get; }

    string? Date { get; }

    string? Note { get; }

    TransferFields Parsed { get; }
}

/// <summary>A transfer command's values, read: null where one was left out or is not what it should be.</summary>
/// <param name="ToAmount">Null as well when it was left out, which accounts in one currency allow.</param>
public sealed record TransferFields(decimal? Amount, decimal? ToAmount, DateOnly? Date)
{
    public static TransferFields Read(string? amount, string? toAmount, string? date) =>
        new(AmountText.TryParse(amount, out var parsed) ? parsed : null, AmountText.TryParse(toAmount, out var parsedTo) ? parsedTo : null, DateText.Read(date));
}

/// <summary>
/// The rules of a transfer, shared by recording and editing one and taken from the domain's. The ones that
/// need only the command are the validator's; the ones that need the two accounts or today's date are
/// checked while building the transfer.
/// </summary>
internal static class TransferRules
{
    private const string AmountMessage = "Enter an amount greater than zero as a decimal string such as \"125000.00\".";

    public static void TransferFieldRules<T>(this AbstractValidator<T> validator)
        where T : ITransferCommand
    {
        validator.RuleFor(command => command.FromAccountId).Required();
        validator.RuleFor(command => command.ToAccountId).Required();
        validator.RuleFor(command => command.Amount)
            .Must((command, _) => command.Parsed.Amount is > 0)
            .WithMessage(AmountMessage);
        validator.RuleFor(command => command.ToAmount)
            .Must((command, sent) => sent is null || command.Parsed.ToAmount is > 0)
            .WithMessage(AmountMessage);
        validator.RuleFor(command => command.Date)
            .Must((command, _) => command.Parsed.Date is not null)
            .WithMessage(DateText.Message);
        validator.RuleFor(command => command.Note).Must(Transaction.IsValidNote)
            .WithMessage($"Enter a note of at most {Transaction.MaxNoteLength} characters.");
    }

    /// <summary>A broken transfer rule, as errors keyed by the command property at fault.</summary>
    public static IReadOnlyDictionary<string, string[]> AsErrors(this TransferRuleViolation violation) =>
        FieldError.On(violation.Property, violation.Message);
}
