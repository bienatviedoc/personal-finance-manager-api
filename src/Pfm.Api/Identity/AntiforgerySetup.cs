using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

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

    /// <summary>
    /// Documents, in OpenAPI, the 400 that every endpoint changing something can answer when the
    /// token is missing or invalid, unless the endpoint already documents a 400 of its own.
    /// </summary>
    public static TBuilder DocumentAntiforgeryRefusals<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint =>
        {
            var methods = endpoint.Metadata.OfType<HttpMethodMetadata>().SelectMany(metadata => metadata.HttpMethods);
            var documentsBadRequest = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().Any(metadata => metadata.StatusCode == StatusCodes.Status400BadRequest);
            if (methods.Any(method => !SafeMethods.Contains(method)) && !documentsBadRequest)
            {
                endpoint.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(ProblemDetails), ["application/problem+json"]));
            }
        });
        return builder;
    }

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
