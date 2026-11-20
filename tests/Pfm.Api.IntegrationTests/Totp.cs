using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// What an authenticator app does (RFC 6238): a six-digit code from the key it was set up with and the time.
/// Written apart from the API's own check, so the two can disagree.
/// </summary>
public static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>The code an authenticator set up with this key shows now.</summary>
    public static string CodeFor(string key) => CodeAt(key, DateTimeOffset.UtcNow);

    public static string CodeAt(string key, DateTimeOffset time)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, time.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(FromBase32(key), counter);
        var offset = hash[^1] & 0x0F;
        var binary = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7FFFFFFF;
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A six-digit code that is not the right one now, nor in the steps either side.</summary>
    public static string WrongCodeFor(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var near = Enumerable.Range(-3, 7).Select(step => CodeAt(key, now.AddSeconds(30 * step))).ToHashSet();
        return Enumerable.Range(0, 1000).Select(n => n.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)).First(code => !near.Contains(code));
    }

    private static byte[] FromBase32(string text)
    {
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in text.Replace(" ", "", StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | Base32Alphabet.IndexOf(character, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return [.. bytes];
    }
}
