using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static Pfm.Api.IntegrationTests.BrowserFixtures;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AttachmentTests(PfmApi api)
{
    /// <summary>The first bytes of a JPEG, a PNG and a PDF, which is how the API tells what a file is.</summary>
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01];
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] Pdf = "%PDF-1.7\n1 0 obj\n"u8.ToArray();

    [Fact]
    public async Task A_user_can_attach_a_receipt_to_a_transaction_and_see_it_listed_there()
    {
        var (browser, _) = await api.SignedInAsync();
        var transaction = await AddTransactionAsync(browser);

        var response = await browser.PostFileAsync(AttachmentsOf(transaction), "lunch.jpg", Jpeg, "image/jpeg");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var attached = (await response.Content.ReadFromJsonAsync<AttachmentBody>())!;
        Assert.Equal(new AttachmentBody(attached.Id, transaction, "lunch.jpg", "image/jpeg", Jpeg.Length), attached);
        Assert.Equal($"/api/v1/attachments/{attached.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal([attached], await ListAsync(browser, transaction));
    }

    [Theory]
    [InlineData("scan.png", "image/png")]
    [InlineData("invoice.pdf", "application/pdf")]
    [InlineData("photo.jpeg", "image/jpeg")]
    public async Task A_JPEG_a_PNG_and_a_PDF_can_be_attached(string fileName, string contentType)
    {
        var (browser, _) = await api.SignedInAsync();
        var transaction = await AddTransactionAsync(browser);

        var attached = await AttachAsync(browser, transaction, fileName, Files[contentType]);

        Assert.Equal((fileName, contentType), (attached.FileName, attached.ContentType));
    }

    [Fact]
    public async Task What_a_file_is_is_read_from_the_file_not_from_its_name_or_what_the_browser_calls_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var transaction = await AddTransactionAsync(browser);

        var disguised = await browser.PostFileAsync(AttachmentsOf(transaction), "receipt.pdf", "<html><script>alert(1)</script>"u8.ToArray(), "application/pdf");
        var misnamed = await browser.PostFileAsync(AttachmentsOf(transaction), "receipt.txt", Png, "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, disguised.StatusCode);
        Assert.Equal("Attach a JPEG, PNG or PDF file.", await disguised.ErrorOnAsync("file"));
        Assert.Equal("image/png", (await misnamed.Content.ReadFromJsonAsync<AttachmentBody>())!.ContentType);
        Assert.Equal(["receipt.txt"], (await ListAsync(browser, transaction)).Select(attachment => attachment.FileName));
    }

    [Theory]
    [InlineData("notes.txt", "text/plain", "just some text")]
    [InlineData("animation.gif", "image/gif", "GIF89a\u0001\u0000\u0001\u0000")]
    [InlineData("archive.zip", "application/zip", "PK\u0003\u0004\u0014\u0000\u0000\u0000")]
    [InlineData("vector.svg", "image/svg+xml", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    public async Task Any_other_type_of_file_is_refused_with_a_message_that_says_what_can_be_attached(string fileName, string contentType, string content)
    {
        var (browser, _) = await api.SignedInAsync();
        var transaction = await AddTransactionAsync(browser);

        var response = await browser.PostFileAsync(AttachmentsOf(transaction), fileName, System.Text.Encoding.Latin1.GetBytes(content), contentType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Attach a JPEG, PNG or PDF file.", await response.ErrorOnAsync("file"));
        Assert.Empty(await ListAsync(browser, transaction));
    }

    [Fact]
    public async Task A_file_of_exactly_5_MB_can_be_attached_and_one_byte_more_is_refused_with_a_message_that_says_so()
    {
        var (browser, _) = await api.SignedInAsync();
        var transaction = await AddTransactionAsync(browser);

        var atTheLimit = await browser.PostFileAsync(AttachmentsOf(transaction), "big.pdf", OfSize(FiveMegabytes), "application/pdf");
        var overTheLimit = await browser.PostFileAsync(AttachmentsOf(transaction), "bigger.pdf", OfSize(FiveMegabytes + 1), "application/pdf");

        Assert.Equal(HttpStatusCode.Created, atTheLimit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, overTheLimit.StatusCode);
        Assert.Equal("Attach a file of at most 5 MB.", await overTheLimit.ErrorOnAsync("file"));
        Assert.Equal([("big.pdf", FiveMegabytes)], (await ListAsync(browser, transaction)).Select(attachment => (attachment.FileName, attachment.Size)));
    }

    /// <summary>
    /// Kestrel cuts off an upload over the endpoint's request limit with a bare 413 before it is read; the test
    /// server sets no such limit, so the problem that 413 becomes is written here the way the server writes it.
    /// </summary>
    [Fact]
    public async Task A_file_too_large_to_be_read_at_all_is_refused_with_the_same_message()
    {
        var addAttachment = api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Single(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "AddAttachment");
        await using var scope = api.Services.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider, Response = { Body = new MemoryStream() } };
        http.SetEndpoint(addAttachment);
        http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;

        await scope.ServiceProvider.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = { Status = StatusCodes.Status413PayloadTooLarge } });

        http.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(http.Response.Body);
        Assert.Equal("Attach a file of at most 5 MB.", problem.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_empty_file_a_request_with_no_file_and_a_file_with_no_name_to_keep_are_refused()
    {
        var (browser, _) = await api.SignedInAsync();
        var transaction = await AddTransactionAsync(browser);

        var empty = await browser.PostFileAsync(AttachmentsOf(transaction), "empty.pdf", [], "application/pdf");
        var noFile = await browser.PostFormAsync(AttachmentsOf(transaction), new() { ["note"] = "no file here" });
        var nameTooLong = await browser.PostFileAsync(AttachmentsOf(transaction), new string('n', 252) + ".pdf", Pdf, "application/pdf");

        Assert.Equal("This file is empty.", await empty.ErrorOnAsync("file"));
        Assert.Equal("Choose a file to attach.", await noFile.ErrorOnAsync("file"));
        Assert.Equal("Give the file a name of at most 255 characters.", await nameTooLong.ErrorOnAsync("file"));
        Assert.Empty(await ListAsync(browser, transaction));
    }

    [Fact]
    public async Task A_file_is_kept_under_its_own_name_without_the_folder_a_browser_may_send_with_it()
    {
        var (browser, _) = await api.SignedInAsync();
        var transaction = await AddTransactionAsync(browser);

        var attached = await AttachAsync(browser, transaction, @"C:\Users\me\Hoá đơn tháng 11.pdf", Pdf);

        Assert.Equal("Hoá đơn tháng 11.pdf", attached.FileName);
    }

    [Fact]
    public async Task An_attachment_opens_through_a_signed_URL_that_needs_no_sign_in_and_serves_the_file_as_what_it_is()
    {
        var (browser, _) = await api.SignedInAsync();
        var attached = await AttachAsync(browser, await AddTransactionAsync(browser), "Hoá đơn.pdf", Pdf);

        var signed = await UrlOfAsync(browser, attached.Id);

        using var anyone = new HttpClient();
        var file = await anyone.GetAsync(signed.Url);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(Pdf, await file.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", file.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", file.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("Hoá đơn.pdf", file.Content.Headers.ContentDisposition?.FileNameStar);
    }

    [Fact]
    public async Task The_storage_is_private_a_file_cannot_be_read_without_the_signature_or_with_one_made_for_another_file()
    {
        var (browser, _) = await api.SignedInAsync();
        var transaction = await AddTransactionAsync(browser);
        var signed = new UriBuilder((await UrlOfAsync(browser, (await AttachAsync(browser, transaction, "one.pdf", Pdf)).Id)).Url);
        var other = new UriBuilder((await UrlOfAsync(browser, (await AttachAsync(browser, transaction, "two.pdf", Pdf)).Id)).Url);

        using var anyone = new HttpClient();
        var unsigned = await anyone.GetAsync(new UriBuilder(signed.Uri) { Query = "" }.Uri);
        var withAnothersSignature = await anyone.GetAsync(new UriBuilder(signed.Uri) { Query = other.Query }.Uri);
        var bucketListing = await anyone.GetAsync(new Uri(api.Storage.Url, signed.Path.Split('/')[1]));

        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withAnothersSignature.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, bucketListing.StatusCode);
    }

    [Fact]
    public async Task A_signed_URL_is_short_lived_five_minutes_unless_the_deployment_says_otherwise()
    {
        var (browser, _) = await api.SignedInAsync();
        var attached = await AttachAsync(browser, await AddTransactionAsync(browser), "lunch.jpg", Jpeg);

        var asked = DateTimeOffset.UtcNow;
        var signed = await UrlOfAsync(browser, attached.Id);

        Assert.InRange(signed.ExpiresAt, asked.AddMinutes(5).AddSeconds(-1), DateTimeOffset.UtcNow.AddMinutes(5).AddSeconds(1));

        // The URL stops working when the answer says. The SDK signs by its own clock, which it corrects for the
        // skew it sees in storage's responses, so X-Amz-Expires alone can be a second or two over 300. X-Amz-Date
        // is cut to the second and X-Amz-Expires rounded, so the two can end up to 1.5 seconds early.
        var query = System.Web.HttpUtility.ParseQueryString(signed.Url.Query);
        var signedAt = DateTimeOffset.ParseExact(query["X-Amz-Date"]!, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var stopsAt = signedAt.AddSeconds(int.Parse(query["X-Amz-Expires"]!, CultureInfo.InvariantCulture));
        Assert.InRange(stopsAt, signed.ExpiresAt.AddSeconds(-2), signed.ExpiresAt.AddSeconds(1));
    }

    [Fact]
    public async Task A_signed_URL_stops_working_when_it_expires()
    {
        var (browser, _) = await api.SignedInAsync(PfmApi.CreateBrowser(api.WhereSignedUrlsLastFor(TimeSpan.FromSeconds(2))));
        var attached = await AttachAsync(browser, await AddTransactionAsync(browser), "lunch.jpg", Jpeg);
        var signed = await UrlOfAsync(browser, attached.Id);
        using var anyone = new HttpClient();
        Assert.Equal(HttpStatusCode.OK, (await anyone.GetAsync(signed.Url)).StatusCode);

        await Task.Delay(signed.ExpiresAt - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(3));

        Assert.Equal(HttpStatusCode.Forbidden, (await anyone.GetAsync(signed.Url)).StatusCode);
    }

    [Fact]
    public async Task Another_user_cannot_obtain_a_URL_for_an_attachment_that_is_not_theirs_nor_list_remove_or_add_to_it()
    {
        var (alice, _) = await api.SignedInAsync();
        var alicesTransaction = await AddTransactionAsync(alice);
        var alicesReceipt = await AttachAsync(alice, alicesTransaction, "lunch.jpg", Jpeg);
        var (bob, _) = await api.SignedInAsync();

        var url = await bob.GetAsync(Attachment(alicesReceipt.Id));
        var list = await bob.GetAsync(AttachmentsOf(alicesTransaction));
        var removal = await bob.DeleteAsync(Attachment(alicesReceipt.Id));
        var addition = await bob.PostFileAsync(AttachmentsOf(alicesTransaction), "bobs.jpg", Jpeg, "image/jpeg");

        Assert.All([url, list, removal, addition], response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.DoesNotContain("X-Amz-Signature", await url.Content.ReadAsStringAsync());
        Assert.Equal([alicesReceipt], await ListAsync(alice, alicesTransaction));
        using var anyone = new HttpClient();
        Assert.Equal(HttpStatusCode.OK, (await anyone.GetAsync((await UrlOfAsync(alice, alicesReceipt.Id)).Url)).StatusCode);
    }

    [Fact]
    public async Task A_visitor_who_is_not_signed_in_gets_no_URL()
    {
        var (browser, _) = await api.SignedInAsync();
        var attached = await AttachAsync(browser, await AddTransactionAsync(browser), "lunch.jpg", Jpeg);

        var response = await api.CreateBrowser().GetAsync(Attachment(attached.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Removing_an_attachment_takes_it_off_the_transaction_and_deletes_its_file()
    {
        var (browser, _) = await api.SignedInAsync();
        var transaction = await AddTransactionAsync(browser);
        var kept = await AttachAsync(browser, transaction, "kept.png", Png);
        var removed = await AttachAsync(browser, transaction, "removed.pdf", Pdf);
        var signedBefore = await UrlOfAsync(browser, removed.Id);

        var response = await browser.DeleteAsync(Attachment(removed.Id));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([kept], await ListAsync(browser, transaction));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(Attachment(removed.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.DeleteAsync(Attachment(removed.Id))).StatusCode);
        using var anyone = new HttpClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync(signedBefore.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anyone.GetAsync((await UrlOfAsync(browser, kept.Id)).Url)).StatusCode);
    }

    [Fact]
    public async Task A_transaction_that_does_not_exist_or_was_deleted_takes_and_shows_no_attachments()
    {
        var (browser, _) = await api.SignedInAsync();
        var deleted = await AddTransactionAsync(browser);
        var attached = await AttachAsync(browser, deleted, "lunch.jpg", Jpeg);
        (await browser.DeleteAsync($"/api/v1/transactions/{deleted}")).EnsureSuccessStatusCode();

        HttpResponseMessage[] responses =
        [
            await browser.GetAsync(AttachmentsOf(Guid.CreateVersion7())),
            await browser.PostFileAsync(AttachmentsOf(Guid.CreateVersion7()), "lunch.jpg", Jpeg, "image/jpeg"),
            await browser.GetAsync(AttachmentsOf(deleted)),
            await browser.PostFileAsync(AttachmentsOf(deleted), "lunch.jpg", Jpeg, "image/jpeg"),
            await browser.GetAsync(Attachment(attached.Id)),
            await browser.DeleteAsync(Attachment(attached.Id)),
            await browser.GetAsync(Attachment(Guid.CreateVersion7())),
        ];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
    }

    [Fact]
    public async Task Attaching_a_file_needs_the_anti_forgery_token_like_every_other_change()
    {
        var (browser, _) = await api.SignedInAsync();
        var transaction = await AddTransactionAsync(browser);
        var file = new ByteArrayContent(Jpeg);

        var response = await browser.Http.PostAsync(AttachmentsOf(transaction), new MultipartFormDataContent { { file, "file", "lunch.jpg" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The anti-forgery token is missing or invalid.", await response.ProblemTitleAsync());
        Assert.Empty(await ListAsync(browser, transaction));
    }

    [Theory]
    [InlineData("Bucket", "", "Storage:Bucket is not configured")]
    [InlineData("ServiceUrl", "storage:7070/", "Storage:ServiceUrl must be the storage's absolute address")]
    [InlineData("PublicUrl", "localhost", "Storage:PublicUrl must be the absolute address browsers reach the storage at")]
    [InlineData("SecretKey", "", "Storage:AccessKey and Storage:SecretKey go together")]
    [InlineData("SignedUrlLifetime", "1.00:00:00", "signed URLs are short-lived")]
    public async Task The_API_refuses_to_start_with_storage_settings_that_cannot_work(string setting, string value, string complaint)
    {
        await using var misconfigured = api.WithStorageSetting(setting, value);

        var refusal = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => misconfigured.CreateClient());

        Assert.Contains(complaint, refusal.Message);
    }

    private const int FiveMegabytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, byte[]> Files = new() { ["image/jpeg"] = Jpeg, ["image/png"] = Png, ["application/pdf"] = Pdf };

    /// <summary>A PDF of exactly this many bytes.</summary>
    private static byte[] OfSize(int bytes)
    {
        var file = new byte[bytes];
        Pdf.CopyTo(file, 0);
        return file;
    }

    private static string Attachment(Guid id) => $"/api/v1/attachments/{id}";

    private static async Task<AttachmentBody> AttachAsync(Browser browser, Guid transactionId, string fileName, byte[] content)
    {
        var response = await browser.PostFileAsync(AttachmentsOf(transactionId), fileName, content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AttachmentBody>())!;
    }

    private static async Task<SignedUrlBody> UrlOfAsync(Browser browser, Guid attachmentId) =>
        (await browser.Http.GetFromJsonAsync<SignedUrlBody>(Attachment(attachmentId)))!;

    private sealed record SignedUrlBody(Uri Url, DateTimeOffset ExpiresAt);

    private static string AttachmentsOf(Guid transactionId) => $"/api/v1/transactions/{transactionId}/attachments";

    private static async Task<List<AttachmentBody>> ListAsync(Browser browser, Guid transactionId) =>
        (await browser.Http.GetFromJsonAsync<AttachmentList>(AttachmentsOf(transactionId)))!.Items;

    /// <summary>An expense of the user's to attach receipts to.</summary>
    private static async Task<Guid> AddTransactionAsync(Browser browser)
    {
        var response = await browser.PostAsync("/api/v1/transactions", new
        {
            type = "expense", accountId = await AddAccountAsync(browser, "Everyday"), amount = "125000", date = "2026-11-02",
            categoryId = await CategoryAsync(browser, "Groceries"),
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    private sealed record Identified(Guid Id);

    private sealed record AttachmentBody(Guid Id, Guid TransactionId, string FileName, string ContentType, long Size);

    private sealed record AttachmentList(List<AttachmentBody> Items);
}
