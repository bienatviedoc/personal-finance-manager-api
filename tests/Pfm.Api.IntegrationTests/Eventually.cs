namespace Pfm.Api.IntegrationTests;

/// <summary>For what the API does in the background, such as exporting a span or sending an error report.</summary>
public static class Eventually
{
    /// <summary>Reads until <paramref name="until"/> holds or a few seconds pass, then returns the last read.</summary>
    public static async Task<T> ReadAsync<T>(Func<T> read, Func<T, bool> until)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var value = read();
            if (until(value) || DateTime.UtcNow > deadline)
            {
                return value;
            }

            await Task.Delay(20);
        }
    }
}
