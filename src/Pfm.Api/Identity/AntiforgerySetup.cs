using Microsoft.AspNetCore.Antiforgery;

namespace Pfm.Api.Identity;

/// <summary>
/// Anti-forgery for a JSON API used by a same-site SPA. The SPA fetches a request token from
/// <c>GET /api/v1/auth/antiforgery</c>, keeps it in memory and sends it in the X-XSRF-TOKEN header with
/// every request that changes something. The matching half lives in an HttpOnly cookie.
/// </summary>
public static class AntiforgerySetup
{
    public const string HeaderName = "X-XSRF-TOKEN";

    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public static IServiceCollection AddPfmAntiforgery(this IServiceCollection services, CookiePolicy cookies) =>
        services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;
            options.Cookie.Name = $"{cookies.NamePrefix}pfm-antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookies.SecurePolicy;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

    /// <summary>Refuses any request under /api that changes something without a valid token.</summary>
    public static IApplicationBuilder UseApiAntiforgery(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!SafeMethods.Contains(context.Request.Method) && context.Request.Path.StartsWithSegments("/api"))
            {
                var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                if (!await antiforgery.IsRequestValidAsync(context))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context,
                        ProblemDetails = { Status = 400, Title = "The anti-forgery token is missing or invalid." },
                    });
                    return;
                }
            }

            await next(context);
        });
}
