namespace Pfm.Infrastructure;

/// <summary>Settings under "App".</summary>
public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>The SPA's address, which links in emails point to. No trailing slash.</summary>
    public string PublicUrl { get; set; } = "";
}
