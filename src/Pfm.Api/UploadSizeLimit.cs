using Microsoft.AspNetCore.Http.Metadata;

namespace Pfm.Api;

/// <summary>
/// The largest request an upload endpoint reads at all, and what to say when a request is larger: the server cuts
/// it off with a bare 413 before the endpoint sees it, and <c>AttachmentEndpoints.ExplainTooLarge</c> gives that
/// problem this message, the same as for a file that was read and found too large.
/// </summary>
public sealed class UploadSizeLimit(long maxRequestBodySize, string message) : IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize => maxRequestBodySize;

    public string Message => message;
}
