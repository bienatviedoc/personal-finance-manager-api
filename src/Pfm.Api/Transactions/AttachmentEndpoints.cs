using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Pfm.Application.Transactions.Attachments;
using Pfm.Domain.Transactions;

namespace Pfm.Api.Transactions;

/// <summary>Attachments over HTTP. The use cases decide everything; these only map requests and answers.</summary>
public static class AttachmentEndpoints
{
    /// <summary>
    /// The largest upload read at all: room for a file somewhat over the 5 MB an attachment may be, so that
    /// one gets the answer that says so. Anything larger is cut off before it is read.
    /// </summary>
    private const long MaxRequestSize = 2 * Attachment.MaxSize;

    /// <summary>
    /// An upload over <see cref="MaxRequestSize"/> is cut off by the server with a bare 413 before the endpoint
    /// reads it: this gives that problem the same message as a file that was read and found too large.
    /// </summary>
    public static void ExplainTooLarge(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ProblemDetails.Status == StatusCodes.Status413PayloadTooLarge
            && context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>() is not null)
        {
            context.ProblemDetails.Detail = Attachment.SizeMessage;
        }
    }

    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder api)
    {
        var ofTransaction = api.MapGroup("/transactions/{id:guid}/attachments").WithTags("Attachments").ProducesProblem(StatusCodes.Status401Unauthorized);
        var attachments = api.MapGroup("/attachments").WithTags("Attachments").ProducesProblem(StatusCodes.Status401Unauthorized);

        ofTransaction.MapGet("/", List).WithName("ListAttachments")
            .WithDescription("The receipts attached to a transaction, the first attached first.");
        ofTransaction.MapPost("/", Add).WithName("AddAttachment")
            .WithDescription("Attaches a receipt to a transaction: one JPEG, PNG or PDF file of at most 5 MB, sent as the file field of a multipart/form-data body. What the file is, is read from the file itself, not from its name.")
            .WithMetadata(new RequestSizeLimitMetadata(MaxRequestSize))
            // The X-XSRF-TOKEN header is checked for every request under /api (UseApiAntiforgery); a form needs no second check of its own.
            .DisableAntiforgery();
        attachments.MapGet("/{id:guid}", GetUrl).WithName("GetAttachmentUrl")
            .WithDescription("A signed URL that opens the attached file. It works for a few minutes and needs no sign-in, so ask for it when the user opens the file, and never keep it.");
        attachments.MapDelete("/{id:guid}", Remove).WithName("RemoveAttachment")
            .WithDescription("Removes the attachment from its transaction and deletes its file.");

        return api;
    }

    private static async Task<Results<Ok<AttachmentListResponse>, NotFound>> List(
        Guid id, ListAttachmentsHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new ListAttachments(id), cancellationToken) is { } attachments
            ? TypedResults.Ok(new AttachmentListResponse([.. attachments.Select(AttachmentResponse.From)]))
            : TypedResults.NotFound();

    /// <param name="file">The receipt: a JPEG, PNG or PDF of at most 5 MB.</param>
    private static async Task<Results<Created<AttachmentResponse>, NotFound, ValidationProblem>> Add(
        Guid id, IFormFile? file, AddAttachmentHandler handler, CancellationToken cancellationToken)
    {
        await using var content = file?.OpenReadStream();
        var result = await handler.HandleAsync(
            new AddAttachmentCommand(id, file is null ? null : new UploadedFile(file.FileName, file.Length, content!)), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is { } attached
            ? TypedResults.Created($"/api/v1/attachments/{attached.Id}", AttachmentResponse.From(attached))
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<AttachmentUrlResponse>, NotFound>> GetUrl(
        Guid id, GetAttachmentUrlHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(new GetAttachmentUrl(id), cancellationToken) is { } signed
            ? TypedResults.Ok(new AttachmentUrlResponse(signed.Url.AbsoluteUri, signed.ExpiresAt))
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Remove(
        Guid id, RemoveAttachmentHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RemoveAttachmentCommand(id), cancellationToken);
        if (!result.IsValid)
        {
            return ValidationProblems.For(result);
        }

        return result.Outcome is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private sealed class RequestSizeLimitMetadata(long maxRequestBodySize) : IRequestSizeLimitMetadata
    {
        public long? MaxRequestBodySize => maxRequestBodySize;
    }
}

/// <summary>A receipt attached to a transaction.</summary>
/// <param name="FileName">What the user's file was called.</param>
/// <param name="ContentType"><c>image/jpeg</c>, <c>image/png</c> or <c>application/pdf</c>.</param>
/// <param name="Size">In bytes.</param>
public sealed record AttachmentResponse(Guid Id, Guid TransactionId, string FileName, string ContentType, int Size)
{
    public static AttachmentResponse From(AttachmentDetails attachment) =>
        new(attachment.Id, attachment.TransactionId, attachment.FileName, attachment.ContentType, checked((int)attachment.Size));
}

/// <summary>The attachments of one transaction: all of them, as a transaction has a handful.</summary>
public sealed record AttachmentListResponse(IReadOnlyList<AttachmentResponse> Items);

/// <summary>Where to open an attached file, for a short while.</summary>
/// <param name="Url">Opens the file with no sign-in until <paramref name="ExpiresAt"/>.</param>
/// <param name="ExpiresAt">When the URL stops working.</param>
public sealed record AttachmentUrlResponse(string Url, DateTimeOffset ExpiresAt);
