namespace Pfm.Api;

/// <summary>
/// A response body for a writer that sometimes writes synchronously, which the server refuses. A ZIP archive does
/// when it closes an entry. Synchronous writes are kept in memory and go out, asynchronously, ahead of the next
/// asynchronous write or flush; those are small, so the body is still sent as it is made.
/// </summary>
internal sealed class AsynchronousBody(Stream body) : Stream
{
    private readonly MemoryStream held = new();

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => held.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => held.Write(buffer);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await SendHeldAsync(cancellationToken);
        await body.WriteAsync(buffer, cancellationToken);
    }

    /// <summary>Nothing is sent synchronously: what is held goes with the next asynchronous write or flush.</summary>
    public override void Flush()
    {
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await SendHeldAsync(cancellationToken);
        await body.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            held.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task SendHeldAsync(CancellationToken cancellationToken)
    {
        if (held.Length == 0)
        {
            return;
        }

        held.Position = 0;
        await held.CopyToAsync(body, cancellationToken);
        held.SetLength(0);
    }
}
