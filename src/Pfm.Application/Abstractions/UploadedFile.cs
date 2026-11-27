namespace Pfm.Application.Abstractions;

/// <summary>A file as it was uploaded: a receipt, a statement. Nothing here is believed until it is checked.</summary>
/// <param name="Name">What the browser called it.</param>
/// <param name="Size">In bytes.</param>
/// <param name="Content">The bytes, readable from the start more than once.</param>
public sealed record UploadedFile(string? Name, long Size, Stream Content);
