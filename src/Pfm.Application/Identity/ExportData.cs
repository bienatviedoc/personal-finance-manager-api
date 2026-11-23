using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain;

namespace Pfm.Application.Identity;

/// <summary>Everything the app holds about the signed-in user, to download (spec section 8, Privacy; section 11, Export).</summary>
public sealed record ExportData;

/// <param name="FileName">The name to save the download under, with the user's date of the export.</param>
/// <param name="WriteZipAsync">
/// Writes a ZIP archive of open formats, <c>profile.json</c> and one CSV file for each kind of data, to the stream
/// as it goes: the attached files can come to far more than is wise to hold in memory, so none is held whole.
/// </param>
public sealed record DataExport(string FileName, Func<Stream, CancellationToken, Task> WriteZipAsync);

/// <summary>
/// The export is the user's profile and every row they own (<see cref="IUserData"/>), a file for each table:
/// <c>Transactions</c> becomes <c>transactions.csv</c> and its column <c>BaseCurrencyAmount</c> becomes
/// <c>baseCurrencyAmount</c>. A table the user has nothing in is still there, with its column names alone. A
/// new kind of user data is in the export once it is in the model.
/// The files a user attached live in object storage, not in a table, so they are fetched one by one into
/// <c>attachments/</c>. Passwords, authenticator keys and recovery
/// codes are no part of it: they are secrets, kept hashed or encrypted, and of no use outside the app.
/// </summary>
public sealed class ExportDataHandler(
    ICurrentUser currentUser, IUsers users, IUserProfiles profiles, IUserData userData, IAttachmentFiles attachmentFiles, IUserCalendar calendar,
    TimeProvider clock)
    : IQueryHandler
{
    /// <summary>
    /// UTF-8 with a byte order mark, which is what tells a spreadsheet that opens a CSV file that its
    /// Vietnamese text is not in the computer's own code page.
    /// </summary>
    private static readonly UTF8Encoding CsvEncoding = new(encoderShouldEmitUTF8Identifier: true);

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private const string ReadMe =
        """
        Personal Finance Manager: everything the app holds about you.

        profile.json    Who you are in the app and your settings.
        *.csv           One file for each kind of data, one line for each thing, under a first line of
                        column names. Comma-separated (RFC 4180), UTF-8. Transactions you deleted are
                        included for as long as the app still holds them (deletedAt says when).
        attachments/    The files you attached to transactions, as you uploaded them. Each is named
                        by the id of its line in attachments.csv, then its own name.

        Amounts are plain numbers with a decimal point, next to their currency's ISO 4217 code.
        Dates are YYYY-MM-DD; moments are ISO 8601 in UTC. A column ending in "Id" names a line of
        another file by its id.

        """;

    /// <summary>The signed-in user's export, or null when nobody is signed in (or their user is gone).</summary>
    public async Task<DataExport?> HandleAsync(ExportData query, CancellationToken cancellationToken = default)
    {
        if (await users.FindSignedInAsync(currentUser) is not { } user || await profiles.FindAsync(user.Id, cancellationToken) is not { } profile)
        {
            return null;
        }

        var today = await calendar.TodayAsync(cancellationToken);
        return new DataExport(
            $"personal-finance-manager-{today:yyyy-MM-dd}.zip",
            (target, cancel) => WriteZipAsync(target, user, profile, cancel));
    }

    private async Task WriteZipAsync(Stream target, UserDetails user, ProfileDetails profile, CancellationToken cancellationToken)
    {
        var tables = await userData.ReadEverythingAsync(cancellationToken);
        var now = clock.GetUtcNow();

        await using var zip = await ZipArchive.CreateAsync(target, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        await AddAsync(zip, "README.txt", now, writer => writer.Write(ReadMe.ReplaceLineEndings("\r\n")), cancellationToken);
        await AddAsync(zip, "profile.json", now, writer => writer.Write(JsonSerializer.Serialize(Profile(user, profile), Indented)), cancellationToken);
        foreach (var table in tables)
        {
            await AddAsync(
                zip, $"{KebabCase(table.Name)}.csv", now, writer => Csv.Write(writer, [.. table.Columns.Select(CamelCase)], table.Rows), cancellationToken,
                CsvEncoding);
        }

        foreach (var file in await attachmentFiles.ListAsync(cancellationToken))
        {
            // A file storage has lost keeps its line in attachments.csv and is left out here.
            await using var content = await attachmentFiles.OpenAsync(file, cancellationToken);
            if (content is null)
            {
                continue;
            }

            // The id keeps two files of one name apart. JPEG, PNG and PDF are compressed already.
            var entry = zip.CreateEntry($"attachments/{file.Id}-{file.FileName}", CompressionLevel.NoCompression);
            entry.LastWriteTime = now;
            await using var entryStream = await entry.OpenAsync(cancellationToken);
            await content.CopyToAsync(entryStream, cancellationToken);
        }
    }

    /// <summary>The names and values of the settings <c>GET /me</c> answers, and whether two-factor sign-in is on.</summary>
    private static Dictionary<string, object?> Profile(UserDetails user, ProfileDetails profile) => new()
    {
        ["id"] = profile.Id,
        ["email"] = profile.Email,
        ["baseCurrency"] = profile.BaseCurrency.ToString(),
        ["timeZone"] = profile.TimeZone,
        ["locale"] = profile.Locale?.Tag(),
        ["budgetWarningPercent"] = profile.BudgetWarningPercent,
        ["twoFactorSignIn"] = user.TwoFactorEnabled,
    };

    private static async Task AddAsync(
        ZipArchive zip, string name, DateTimeOffset now, Action<TextWriter> write, CancellationToken cancellationToken, Encoding? encoding = null)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = now;
        await using var writer = new StreamWriter(await entry.OpenAsync(cancellationToken), encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        write(writer);
    }

    private static string CamelCase(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static string KebabCase(string name) => JsonNamingPolicy.KebabCaseLower.ConvertName(name);
}
