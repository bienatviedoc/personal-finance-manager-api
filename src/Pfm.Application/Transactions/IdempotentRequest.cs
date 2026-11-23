using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pfm.Application.Transactions.Transfers;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// A request to record a transaction or a transfer that named an idempotency key (spec section 11): the key,
/// and a fingerprint of what it asked for. Two requests with one key are the same request only when
/// their fingerprints match, so a retry is told apart from a key used again for something else.
/// </summary>
/// <param name="Fingerprint">
/// The same for the same transaction however it was written (<c>"250"</c> or <c>"250.00"</c>, a payee with
/// spaces around it), and taken from what was sent, so a later edit of the transaction does not change it.
/// </param>
public sealed record IdempotentRequest(string Key, string Fingerprint)
{
    /// <summary>Null when the command names no key. The command has passed its validator.</summary>
    public static IdempotentRequest? Of(CreateTransactionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.IdempotencyKey is null)
        {
            return null;
        }

        var fields = command.Parsed;
        string?[] asked =
        [
            fields.Type!.Value.ToString(),
            command.AccountId.ToString("N"),
            fields.Amount!.Value.ToString("0.############################", CultureInfo.InvariantCulture),
            fields.Date!.Value.ToString("O", CultureInfo.InvariantCulture),
            command.CategoryId.ToString("N"),
            Transaction.Normalised(command.Payee),
            Transaction.Normalised(command.Note),

            // Only a refund adds to what is hashed, so the fingerprints kept before refunds existed still match.
            .. command.IsRefund ? ["refund"] : Array.Empty<string?>(),
        ];

        return new IdempotentRequest(
            command.IdempotencyKey, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(asked)))));
    }

    /// <summary>
    /// Null when the command names no key. The command has passed its validator. A transfer's fingerprint is
    /// never a transaction's, so a key cannot stand for one of each; and it is the same whether the amount that
    /// arrives was left out or sent equal to the amount that left.
    /// </summary>
    public static IdempotentRequest? Of(CreateTransferCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.IdempotencyKey is null)
        {
            return null;
        }

        var fields = command.Parsed;
        var asked = JsonSerializer.Serialize(new[]
        {
            "Transfer",
            command.FromAccountId.ToString("N"),
            command.ToAccountId.ToString("N"),
            Written(fields.Amount!.Value),
            Written(fields.ToAmount ?? fields.Amount.Value),
            fields.Date!.Value.ToString("O", CultureInfo.InvariantCulture),
            Transaction.Normalised(command.Note),
        });

        return new IdempotentRequest(command.IdempotencyKey, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(asked))));
    }

    private static string Written(decimal amount) => amount.ToString("0.############################", CultureInfo.InvariantCulture);
}

/// <summary>
/// The transaction a request with an idempotency key recorded, and that request's fingerprint. For a
/// transfer it is the Transfer out leg, which keeps the key for both.
/// </summary>
public sealed record RecordedRequest(Transaction Transaction, string? Fingerprint);
