using Microsoft.AspNetCore.Http.Metadata;

namespace Pfm.Api;

/// <summary>
/// The largest request an upload endpoint reads at all, and what to say when a request is larger: the server cuts
/// it off with a bare 413 before the endpoint sees it, and <see cref="ExplainTooLarge"/> gives that problem this
/// message, the same as for a file that was read and found too large.
/// </summary>
public sealed class UploadSizeLimit(long maxRequestBodySize, string message) : IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize => maxRequestBodySize;

    public string Message => message;

    /// <summary>
    /// Gives the bare 413 the server answers for an upload over its endpoint's limit the endpoint's own message, the
    /// same as for a file that was read and found too large. Every upload endpoint (attachments, statements) has one.
    /// </summary>
    public static void ExplainTooLarge(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ProblemDetails.Status == StatusCodes.Status413PayloadTooLarge
            && context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<UploadSizeLimit>() is { } limit)
        {
            context.ProblemDetails.Detail = limit.Message;
        }
    }
}
