using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pfm.Application.Accounts;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Accounts;

namespace Pfm.Api.Accounts;

/// <summary>Accounts over HTTP. The use cases decide everything; these only map requests and answers.</summary>
public static class AccountEndpoints
{
    /// <summary>What these endpoints edit, as a problem about its row version names it.</summary>
    private const string What = "account";

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder api)
    {
        var accounts = api.MapGroup("/accounts").WithTags("Accounts").ProducesProblem(StatusCodes.Status401Unauthorized);

        accounts.MapGet("/", List).WithName("ListAccounts")
            .WithDescription("The user's accounts by name, each with its balance, the total balance of each institution in each currency, and the totals of each institution and of everything in the user's base currency, at today's exchange rates. A balance with no rate to convert it is left out of those and listed as not converted. The totals cover the whole list, whichever page is asked for. Archived accounts, which pickers never offer, only with includeArchived=true.");
        accounts.MapGet("/{id:guid}", Get).WithName("GetAccount")
            .WithDescription("One account, archived or not, with its balance. The ETag header carries its row version.");
        accounts.MapPost("/", Create).WithName("CreateAccount")
            .WithDescription("Adds an account. A credit card or loan sends amountOwed, a positive number, and shows a negative balance; any other type sends openingBalance. Any account can be labelled with the last 4 digits of its number, never more of it. Only a credit card takes creditLimit, statementDay and paymentDueDay.");
        accounts.MapPut("/{id:guid}", Edit).WithName("EditAccount")
            .WithDescription("Changes an account; its balance follows a new opening balance at once, and a credit card's available credit a new credit limit. Every field is replaced: last4 or a credit card field left out is removed. Send the row version last read as If-Match: a stale one answers 412, none answers 428.")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
        accounts.MapDelete("/{id:guid}", Archive).WithName("ArchiveAccount")
            .WithDescription("Archives the account, at any balance: it leaves lists and pickers and stays in history.");

        return api;
    }

    /// <param name="limit">How many accounts a page holds: 1 to 100, 50 when left out.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the page before; left out for the first page.</param>
    private static async Task<Results<Ok<AccountListResponse>, ValidationProblem>> List(
        ListAccountsHandler handler, CancellationToken cancellationToken,
        bool includeArchived = false, int limit = Paging.DefaultLimit, string? cursor = null)
    {
        if (!Paging.IsValidLimit(limit))
        {
            return PagingProblems.For("limit", $"Ask for 1 to {Paging.MaxLimit} at a time.");
        }

        NameCursor? after = null;
        if (cursor is not null && !NameCursor.TryParse(cursor, out after))
        {
            return PagingProblems.For("cursor", "Use the nextCursor of the page before.");
        }

        var list = await handler.HandleAsync(new ListAccounts(includeArchived, limit, after), cancellationToken);
        return TypedResults.Ok(new AccountListResponse(
            [.. list.Page.Items.Select(AccountResponse.From)], list.Page.NextCursor, [.. list.Totals.Select(InstitutionTotalResponse.From)],
            BaseCurrencyTotalResponse.From(list.BaseCurrencyTotal)));
    }

    private static async Task<Results<Ok<AccountResponse>, NotFound>> Get(
        Guid id, GetAccountHandler handler, HttpContext http, CancellationToken cancellationToken)
    {
        if (await handler.HandleAsync(new GetAccount(id), cancellationToken) is not { } account)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(Versioned(http, AccountResponse.From(account)));
    }

    private static async Task<Results<Created<AccountResponse>, ValidationProblem>> Create(
        AccountRequest request, CreateAccountHandler handler, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CreateAccountCommand(
                request.Name, request.Type, request.InstitutionId, request.Currency, request.OpeningBalance, request.AmountOwed, request.OpeningDate,
                request.Last4, request.CreditLimit, request.StatementDay, request.PaymentDueDay),
            cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return TypedResults.Created($"/api/v1/accounts/{result.Outcome.Id}", Versioned(http, AccountResponse.From(result.Outcome)));
    }

    /// <param name="ifMatch">The account's row version as last read, in double quotes: its ETag.</param>
    private static async Task<Results<Ok<AccountResponse>, NotFound, ValidationProblem, ProblemHttpResult>> Edit(
        Guid id, AccountRequest request, [FromHeader(Name = "If-Match")] string? ifMatch, EditAccountHandler handler, HttpContext http,
        CancellationToken cancellationToken)
    {
        if (!RowVersions.TryRead(ifMatch, What, out var rowVersion, out var refused))
        {
            return refused;
        }

        var result = await handler.HandleAsync(
            new EditAccountCommand(
                id, rowVersion, request.Name, request.Type, request.InstitutionId, request.Currency, request.OpeningBalance, request.AmountOwed,
                request.OpeningDate, request.Last4, request.CreditLimit, request.StatementDay, request.PaymentDueDay),
            cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            { Edited: { } edited } => TypedResults.Ok(Versioned(http, AccountResponse.From(edited))),
            _ => RowVersions.Stale(What),
        };
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Archive(
        Guid id, ArchiveAccountHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ArchiveAccountCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    /// <summary>Sends the account's row version as the response's ETag, for the next edit's <c>If-Match</c>.</summary>
    private static AccountResponse Versioned(HttpContext http, AccountResponse account)
    {
        http.SendAsETag(account.RowVersion);
        return account;
    }
}

/// <summary>What creating or editing an account sends.</summary>
/// <param name="Type">One of <c>current</c>, <c>savings</c>, <c>creditCard</c>, <c>cash</c>, <c>eWallet</c>, <c>loan</c>.</param>
/// <param name="Currency">An ISO 4217 code the app supports, such as <c>VND</c>.</param>
/// <param name="OpeningDate">A calendar date, <c>YYYY-MM-DD</c>.</param>
/// <param name="InstitutionId">One of the user's institutions; left out for an account held with none, such as cash.</param>
/// <param name="OpeningBalance">A decimal string such as <c>"125000.00"</c>. For every type but a credit card or loan.</param>
/// <param name="AmountOwed">A decimal string, positive. For a credit card or loan, instead of an opening balance.</param>
/// <param name="Last4">The last 4 digits of the account or card number, as a label: exactly 4 digits. A full number is refused and never stored.</param>
/// <param name="CreditLimit">A decimal string, not negative. For a credit card only.</param>
/// <param name="StatementDay">The day of the month the statement closes, 1 to 31. For a credit card only.</param>
/// <param name="PaymentDueDay">The day of the month the payment is due, 1 to 31. For a credit card only.</param>
public sealed record AccountRequest(
    string Name, string Type, string Currency, string OpeningDate, Guid? InstitutionId = null, string? OpeningBalance = null, string? AmountOwed = null,
    string? Last4 = null, string? CreditLimit = null, int? StatementDay = null, int? PaymentDueDay = null);

/// <summary>An account with its balance. Amounts are decimal strings in the account's currency.</summary>
/// <param name="OpeningBalance">Negative for an amount owed.</param>
/// <param name="AmountOwed">The amount owed the account opened with, positive; null unless it is a credit card or loan.</param>
/// <param name="Balance">The opening balance plus what the account's transactions come to. Computed, never stored.</param>
/// <param name="RowVersion">Send it as <c>If-Match</c>, in double quotes, when editing the account.</param>
/// <param name="Last4">The last 4 digits of the account or card number, or null.</param>
/// <param name="CreditLimit">A credit card's limit; null when it has none, and for every other type.</param>
/// <param name="StatementDay">The day of the month a credit card's statement closes, or null.</param>
/// <param name="PaymentDueDay">The day of the month a credit card's payment is due, or null.</param>
/// <param name="AvailableCredit">The credit limit minus the amount owed now: negative over the limit. Computed, never stored; null for an account with no credit limit.</param>
public sealed record AccountResponse(
    Guid Id, Guid? InstitutionId, string Name, AccountType Type, Currency Currency, string OpeningBalance, string? AmountOwed,
    DateOnly OpeningDate, string Balance, bool IsArchived, string RowVersion, string? Last4, string? CreditLimit, int? StatementDay,
    int? PaymentDueDay, string? AvailableCredit)
{
    public static AccountResponse From(AccountDetails account) =>
        new(account.Id, account.InstitutionId, account.Name, account.Type, account.Currency, AmountJson.Text(account.OpeningBalance),
            account.AmountOwed is { } owed ? AmountJson.Text(owed) : null, account.OpeningDate, AmountJson.Text(account.Balance),
            account.IsArchived, account.RowVersion.ToString(CultureInfo.InvariantCulture), account.Last4,
            account.CreditLimit is { } limit ? AmountJson.Text(limit) : null, account.StatementDay, account.PaymentDueDay,
            account.AvailableCredit is { } available ? AmountJson.Text(available) : null);
}

/// <summary>
/// A page of accounts. <c>nextCursor</c> asks for the next page; it is null on the last. <c>totals</c> are
/// of every account the list holds, on every page, and so is <c>baseCurrencyTotal</c>.
/// </summary>
public sealed record AccountListResponse(
    IReadOnlyList<AccountResponse> Items, string? NextCursor, IReadOnlyList<InstitutionTotalResponse> Totals, BaseCurrencyTotalResponse BaseCurrencyTotal);

/// <summary>The total balance of the listed accounts at one institution in one currency.</summary>
/// <param name="InstitutionId">Null for the accounts held with no institution.</param>
/// <param name="Balance">A decimal string in <paramref name="Currency"/>.</param>
public sealed record InstitutionTotalResponse(Guid? InstitutionId, Currency Currency, string Balance)
{
    public static InstitutionTotalResponse From(InstitutionTotal total) => new(total.InstitutionId, total.Currency, AmountJson.Text(total.Balance));
}

/// <summary>
/// The balances of the listed accounts in the user's base currency, at today's exchange rates (or the nearest
/// earlier ones): all of them, and those of each institution.
/// </summary>
/// <param name="Currency">The user's base currency.</param>
/// <param name="Balance">A decimal string in <paramref name="Currency"/>: what the institutions' balances come to.</param>
/// <param name="NotConverted">Balances left out of <paramref name="Balance"/> because no exchange rate exists for them, added up by currency. Empty when the total is complete.</param>
public sealed record BaseCurrencyTotalResponse(
    Currency Currency, string Balance, IReadOnlyList<NotConvertedAmountResponse> NotConverted, IReadOnlyList<InstitutionBaseCurrencyTotalResponse> Institutions)
{
    public static BaseCurrencyTotalResponse From(BaseCurrencyTotal total) =>
        new(total.Currency, AmountJson.Text(total.Balance), [.. total.NotConverted.Select(NotConvertedAmountResponse.From)],
            [.. total.Institutions.Select(InstitutionBaseCurrencyTotalResponse.From)]);
}

/// <summary>The total balance of the listed accounts at one institution, in the user's base currency.</summary>
/// <param name="InstitutionId">Null for the accounts held with no institution.</param>
/// <param name="Balance">A decimal string in the base currency.</param>
/// <param name="NotConverted">Balances left out of <paramref name="Balance"/> because no exchange rate exists for them. Empty when the total is complete.</param>
public sealed record InstitutionBaseCurrencyTotalResponse(Guid? InstitutionId, string Balance, IReadOnlyList<NotConvertedAmountResponse> NotConverted)
{
    public static InstitutionBaseCurrencyTotalResponse From(InstitutionBaseCurrencyTotal total) =>
        new(total.InstitutionId, AmountJson.Text(total.Balance), [.. total.NotConverted.Select(NotConvertedAmountResponse.From)]);
}

/// <summary>A balance that is not converted: it stays in its own currency and out of the base-currency total.</summary>
/// <param name="Balance">A decimal string in <paramref name="Currency"/>.</param>
public sealed record NotConvertedAmountResponse(Currency Currency, string Balance)
{
    public static NotConvertedAmountResponse From(NotConvertedAmount amount) => new(amount.Currency, AmountJson.Text(amount.Balance));
}
