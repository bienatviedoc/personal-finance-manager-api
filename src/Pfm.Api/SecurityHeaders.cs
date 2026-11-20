namespace Pfm.Api;

/// <summary>
/// The headers every answer carries so a browser treats it strictly (spec section 8). The API serves data, never
/// pages, so its content security policy allows nothing at all: an answer a browser is tricked into opening as a
/// document can load and run nothing, and cannot be framed.
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>A year, the least browsers treat as a lasting promise to use HTTPS.</summary>
    public const string StrictTransportSecurity = "max-age=31536000";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, IHostEnvironment environment) =>
        app.Use(async (context, next) =>
        {
            // Set as the answer starts, so they are also on answers the exception handler wrote after clearing the response.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers.ContentSecurityPolicy = ContentSecurityPolicy;

                // Development runs over plain http://localhost, where the promise could not be kept.
                if (!environment.IsDevelopment())
                {
                    headers.StrictTransportSecurity = StrictTransportSecurity;
                }

                // A user's financial data must not linger in a browser or proxy cache.
                if (context.Request.Path.StartsWithSegments("/api") && headers.CacheControl.Count == 0)
                {
                    headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });

            await next(context);
        });
}
