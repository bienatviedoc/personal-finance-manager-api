using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Transactions.Categories;
using Pfm.Application.Transactions.Tags;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>What a bulk action does to every transaction it selects (spec TXN-7).</summary>
public enum BulkAction
{
    /// <summary>Puts each on one category.</summary>
    SetCategory,

    /// <summary>Puts one tag on each.</summary>
    AddTag,

    /// <summary>Takes one tag off each.</summary>
    RemoveTag,

    /// <summary>Moves each to one account.</summary>
    MoveToAccount,

    /// <summary>Deletes each, softly; a leg of a transfer takes its transfer with it.</summary>
    Delete,
}

/// <summary>
/// One action on many of the user's transactions, which all take it or none does (spec TXN-7, section 11): the
/// ones named by <paramref name="TransactionIds"/>, or every one that passes <paramref name="Filter"/>, never both.
/// </summary>
/// <param name="Action">A <see cref="BulkAction"/> as it travels: <c>setCategory</c>, <c>addTag</c>, ...</param>
/// <param name="CategoryId">The category <see cref="BulkAction.SetCategory"/> puts them on.</param>
/// <param name="Tag">The name of the tag <see cref="BulkAction.AddTag"/> and <see cref="BulkAction.RemoveTag"/> put on and take off.</param>
/// <param name="AccountId">The account <see cref="BulkAction.MoveToAccount"/> moves them to.</param>
public sealed record BulkEditTransactionsCommand(
    string? Action, IReadOnlyList<Guid>? TransactionIds, TransactionFilter? Filter, Guid? CategoryId = null, string? Tag = null, Guid? AccountId = null)
{
    /// <summary>The most transactions one bulk action takes.</summary>
    public const int MaxSelected = 1000;

    public BulkAction? ParsedAction { get; } = EnumText<BulkAction>.Read(Action);

    /// <summary>The tag's name as a tag keeps it: trimmed and in lower case.</summary>
    public string? NormalisedTag { get; } = Domain.Transactions.Tag.Normalised(Tag);
}

public sealed class BulkEditTransactionsValidator : AbstractValidator<BulkEditTransactionsCommand>
{
    public BulkEditTransactionsValidator()
    {
        RuleFor(command => command.Action)
            .Must((command, _) => command.ParsedAction is not null)
            .WithMessage($"Choose one of: {string.Join(", ", EnumText<BulkAction>.Names)}.");
        RuleFor(command => command.TransactionIds)
            .Must((command, ids) => (ids is null) != (command.Filter is null))
            .WithMessage("Select transactions by their ids or by a filter, one of the two.")
            .DependentRules(() => RuleFor(command => command.TransactionIds)
                .Must(ids => ids is null || ids.Distinct().Count() is >= 1 and <= BulkEditTransactionsCommand.MaxSelected)
                .WithMessage($"Select 1 to {BulkEditTransactionsCommand.MaxSelected} transactions."));
        RuleFor(command => command.CategoryId).NotNull().Required().When(command => command.ParsedAction == BulkAction.SetCategory);
        RuleFor(command => command.AccountId).NotNull().Required().When(command => command.ParsedAction == BulkAction.MoveToAccount);
        RuleFor(command => command.Tag)
            .Must(Domain.Transactions.Tag.IsValidName)
            .When(command => command.ParsedAction is BulkAction.AddTag or BulkAction.RemoveTag)
            .WithMessage($"Enter a tag of 1 to {Domain.Transactions.Tag.MaxNameLength} characters.");
    }
}

/// <summary>What a bulk action came to: done to every transaction it selected, or refused, with nothing changed.</summary>
public abstract record BulkEditOutcome
{
    private BulkEditOutcome()
    {
    }

    /// <param name="Count">How many transactions took it: every one it selected, and the other leg of each transfer it deleted.</param>
    public sealed record Done(int Count) : BulkEditOutcome;

    /// <summary>More transactions pass the filter than one bulk action takes, so none took it: narrow the filter.</summary>
    /// <param name="Max">The most one bulk action takes.</param>
    public sealed record TooMany(int Max) : BulkEditOutcome;

    /// <summary>Some of the transactions cannot take the action, so none did.</summary>
    /// <param name="Rows">Each transaction refused and why, in the order they were selected.</param>
    public sealed record Refused(IReadOnlyList<RefusedTransaction> Rows) : BulkEditOutcome;
}

/// <summary>A transaction a bulk action selected that cannot take it.</summary>
/// <param name="Message">Why, in words: what the rule it breaks says.</param>
/// <param name="Transaction">The transaction as it is, unchanged, so it can be named wherever it is not on screen; null when not found.</param>
public sealed record RefusedTransaction(Guid TransactionId, BulkRefusal Reason, string Message, TransactionDetails? Transaction)
{
    /// <summary>A transaction the user has no such one of, or deleted.</summary>
    public static RefusedTransaction NotFound(Guid transactionId) =>
        new(transactionId, BulkRefusal.NotFound, "You have no such transaction, or it was deleted.", null);
}

/// <summary>
/// Acts on every selected transaction in one save, or on none. Each transaction takes the change through its own
/// method, which refuses it with the reason a bulk action names (<see cref="TransactionRuleViolation.Refusal"/>); one
/// refused, nothing is saved. A delete of a leg of a transfer deletes the transfer, as deleting one alone does.
/// Like a delete, it names no row version: one that meets a change made at the same moment is made again on what
/// was saved.
/// </summary>
public sealed class BulkEditTransactionsHandler(
    IValidator<BulkEditTransactionsCommand> validator, ITransactionRepository transactions, ICategoryRepository categories, IAccountLookup accounts,
    TransactionTags tags, ITagRepository tagRepository, IBudgetNotifications budgetNotifications, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<BulkEditTransactionsCommand, BulkEditOutcome>(validator)
{
    protected override Task<Result<BulkEditOutcome>> ExecuteAsync(BulkEditTransactionsCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync(() => ActAsync(command, cancellationToken));

    private async Task<Result<BulkEditOutcome>> ActAsync(BulkEditTransactionsCommand command, CancellationToken cancellationToken)
    {
        var refused = new List<RefusedTransaction>();
        IReadOnlyList<Transaction> selected;
        if (command.Filter is { } filter)
        {
            selected = await transactions.ListAsync(filter, TransactionSort.NewestFirst, BulkEditTransactionsCommand.MaxSelected + 1, after: null, cancellationToken);
            if (selected.Count > BulkEditTransactionsCommand.MaxSelected)
            {
                return new BulkEditOutcome.TooMany(BulkEditTransactionsCommand.MaxSelected);
            }
        }
        else
        {
            var ids = command.TransactionIds!.Distinct().ToList();
            var found = (await transactions.FindManyAsync(ids, cancellationToken)).ToDictionary(transaction => transaction.Id);
            refused.AddRange(ids.Where(id => !found.ContainsKey(id)).Select(RefusedTransaction.NotFound));
            selected = [.. ids.Where(found.ContainsKey).Select(id => found[id])];
        }

        var acted = await ActOnEachAsync(command, selected, refused, clock.GetUtcNow(), cancellationToken);
        if (!acted.IsValid)
        {
            return Result<BulkEditOutcome>.Invalid(acted.Errors);
        }

        if (refused.Count > 0)
        {
            // Nothing is saved, so what the others took is dropped with this request.
            var order = (command.TransactionIds ?? [.. selected.Select(transaction => transaction.Id)]).Distinct().ToList();
            return new BulkEditOutcome.Refused([.. refused.OrderBy(row => order.IndexOf(row.TransactionId))]);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (command.ParsedAction is not (BulkAction.AddTag or BulkAction.RemoveTag))
        {
            // A new category, account or a delete moves spent in the months of what was selected.
            await budgetNotifications.CheckAsync([.. selected.Select(transaction => transaction.Date).Distinct()], cancellationToken);
        }

        return new BulkEditOutcome.Done(acted.Outcome);
    }

    /// <summary>
    /// Makes the action's change to each selected transaction, adding to <paramref name="refused"/> each that refuses
    /// it, and says how many transactions took it: a delete of a leg of a transfer deletes the other leg too.
    /// Invalid when what the action names (a category, an account) cannot be used at all.
    /// </summary>
    private async Task<Result<int>> ActOnEachAsync(
        BulkEditTransactionsCommand command, IReadOnlyList<Transaction> selected, List<RefusedTransaction> refused, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        void Change(IEnumerable<Transaction> these, Action<Transaction> change)
        {
            foreach (var transaction in these)
            {
                try
                {
                    change(transaction);
                }
                catch (TransactionRuleViolation violation) when (violation.Refusal is { } reason)
                {
                    refused.Add(new RefusedTransaction(transaction.Id, reason, violation.Message, TransactionDetails.Of(transaction)));
                }
            }
        }

        switch (command.ParsedAction!.Value)
        {
            case BulkAction.SetCategory:
                if (await categories.FindAsync(command.CategoryId!.Value, cancellationToken) is not { IsArchived: false } category)
                {
                    return Result<int>.Invalid(FieldError.On(nameof(command.CategoryId), Choices.NotACategory));
                }

                Change(selected, transaction => transaction.Recategorise(category, now));
                break;

            case BulkAction.AddTag:
                var tag = (await tags.FindOrCreateAsync([command.NormalisedTag!], cancellationToken))[0];
                Change(selected, transaction => transaction.AddTag(tag, now));
                break;

            case BulkAction.RemoveTag:
                var named = await tagRepository.FindByNamesAsync([command.NormalisedTag!], cancellationToken);
                Change(selected, transaction =>
                {
                    foreach (var had in named)
                    {
                        transaction.RemoveTag(had.Id, now);
                    }
                });
                break;

            case BulkAction.MoveToAccount:
                if (await accounts.FindChoosableAsync(command.AccountId!.Value, keeps: null, cancellationToken) is not { } account)
                {
                    return Result<int>.Invalid(FieldError.On(nameof(command.AccountId), Choices.NotAnAccount));
                }

                Change(selected, transaction => transaction.MoveTo(account.Id, account.Currency, now));
                break;

            case BulkAction.Delete:
                var alone = selected.Where(transaction => !transaction.IsTransferLeg).ToList();
                Change(alone, transaction => transaction.Delete(now));
                var transfers = selected.Where(transaction => transaction.IsTransferLeg).GroupBy(transaction => transaction.TransferId!.Value).ToList();
                foreach (var legs in transfers)
                {
                    // The other leg deleted by another request in between fails the save, and the action is made again.
                    var transfer = (await transactions.FindTransferAsync(legs.Key, deletedToo: true, cancellationToken))!;
                    Change(legs, _ => transfer.Delete(now));
                }

                // Both legs of each transfer go, whether one or both were selected.
                return alone.Count + (2 * transfers.Count);

            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.ParsedAction, "Not a bulk action.");
        }

        return selected.Count;
    }
}
