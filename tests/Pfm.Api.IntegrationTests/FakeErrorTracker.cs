using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// An error tracker that speaks Sentry's protocol on localhost and keeps every event it receives,
/// so tests see exactly what would leave the API.
/// </summary>
public sealed class FakeErrorTracker : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentQueue<Report> _reports = new();
    private readonly Task _serving;

    public FakeErrorTracker()
    {
        var port = FreePort();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        Dsn = $"http://public-key@localhost:{port}/1";
        _serving = Task.Run(ServeAsync);
    }

    /// <summary>The DSN the API reports to.</summary>
    public string Dsn { get; }

    /// <summary>An error report as received: its event, and its envelope's raw text.</summary>
    public sealed record Report(JsonElement Event, string Raw);

    /// <summary>
    /// The reports received so far. Reports are sent in the background, so this waits until
    /// <paramref name="until"/> holds.
    /// </summary>
    public Task<List<Report>> ReportsAsync(Func<List<Report>, bool> until) =>
        Eventually.ReadAsync(() => _reports.ToList(), until);

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (!_listener.IsListening)
            {
                return;
            }

            Receive(context.Request);
            context.Response.StatusCode = 200;
            context.Response.Close();
        }
    }

    private void Receive(HttpListenerRequest request)
    {
        Stream body = request.InputStream;
        if (request.Headers["Content-Encoding"] == "gzip")
        {
            body = new GZipStream(body, CompressionMode.Decompress);
        }

        var raw = new StreamReader(body, Encoding.UTF8).ReadToEnd();

        // An envelope is a header line, then pairs of lines: an item's header, then its payload.
        var lines = raw.Split('\n');
        for (var i = 1; i + 1 < lines.Length; i += 2)
        {
            using var itemHeader = JsonDocument.Parse(lines[i]);
            if (itemHeader.RootElement.GetProperty("type").GetString() == "event")
            {
                _reports.Enqueue(new Report(JsonDocument.Parse(lines[i + 1]).RootElement.Clone(), raw));
            }
        }
    }

    private static int FreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        await _serving;
        _listener.Close();
    }
}
