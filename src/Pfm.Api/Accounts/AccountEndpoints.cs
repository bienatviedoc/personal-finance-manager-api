using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Pfm.Application.Accounts;
using Pfm.Domain;
using Pfm.Domain.Accounts;

namespace Pfm.Api.Accounts;

/// <summary>Accounts over HTTP. The use cases decide everything; these only map requests and answers.</summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder api)
    {
        var accounts = api.MapGroup("/accounts").WithTags("Accounts").ProducesProblem(StatusCodes.Status401Unauthorized);

        accounts.MapGet("/{id:guid}", Get).WithName("GetAccount")
            .WithDescription("One account, archived or not, with its balance. The ETag header carries its row version.");
        accounts.MapPost("/", Create).WithName("CreateAccount")
            .WithDescription("Adds an account. A credit card or loan sends amountOwed, a positive number, and shows a negative balance; any other type sends openingBalance.");
        accounts.MapPut("/{id:guid}", Edit).WithName("EditAccount")
            .WithDescription("Changes an account; its balance follows a new opening balance at once. Send the row version last read as If-Match: a stale one answers 412, none answers 428.")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return api;
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
            new CreateAccountCommand(request.Name, request.Type, request.InstitutionId, request.Currency, request.OpeningBalance, request.AmountOwed, request.OpeningDate),
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
        if (ifMatch is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status428PreconditionRequired, title: "Send the account's row version as If-Match.");
        }

        if (!TryReadRowVersion(ifMatch, out var rowVersion))
        {
            return Stale();
        }

        var result = await handler.HandleAsync(
            new EditAccountCommand(
                id, rowVersion, request.Name, request.Type, request.InstitutionId, request.Currency, request.OpeningBalance, request.AmountOwed,
                request.OpeningDate),
            cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome switch
        {
            null => TypedResults.NotFound(),
            { Edited: { } edited } => TypedResults.Ok(Versioned(http, AccountResponse.From(edited))),
            _ => Stale(),
        };
    }

    private static ProblemHttpResult Stale() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status412PreconditionFailed, title: "The account has changed since it was read. Read it again, then edit it.");

    /// <summary>A row version as its ETag: the number in double quotes. Anything else is not one the API gave out.</summary>
    private static bool TryReadRowVersion(string ifMatch, out uint rowVersion)
    {
        rowVersion = default;
        return ifMatch is ['"', .. var number, '"']
            && uint.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out rowVersion);
    }

    /// <summary>Sends the account's row version as the response's ETag, for the next edit's <c>If-Match</c>.</summary>
    private static AccountResponse Versioned(HttpContext http, AccountResponse account)
    {
        http.Response.Headers.ETag = $"\"{account.RowVersion}\"";
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
public sealed record AccountRequest(
    string Name, string Type, string Currency, string OpeningDate, Guid? InstitutionId = null, string? OpeningBalance = null, string? AmountOwed = null);

/// <summary>An account with its balance. Amounts are decimal strings in the account's currency.</summary>
/// <param name="OpeningBalance">Negative for an amount owed.</param>
/// <param name="AmountOwed">The amount owed the account opened with, positive; null unless it is a credit card or loan.</param>
/// <param name="Balance">The opening balance plus what the account's transactions come to. Computed, never stored.</param>
/// <param name="RowVersion">Send it as <c>If-Match</c>, in double quotes, when editing the account.</param>
public sealed record AccountResponse(
    Guid Id, Guid? InstitutionId, string Name, AccountType Type, Currency Currency, string OpeningBalance, string? AmountOwed,
    DateOnly OpeningDate, string Balance, bool IsArchived, string RowVersion)
{
    public static AccountResponse From(AccountDetails account) =>
        new(account.Id, account.InstitutionId, account.Name, account.Type, account.Currency, AmountJson.Text(account.OpeningBalance),
            account.AmountOwed is { } owed ? AmountJson.Text(owed) : null, account.OpeningDate, AmountJson.Text(account.Balance),
            account.IsArchived, account.RowVersion.ToString(CultureInfo.InvariantCulture));
}
