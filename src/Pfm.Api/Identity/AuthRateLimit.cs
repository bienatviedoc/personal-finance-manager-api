using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Pfm.Api.Identity;

/// <summary>
/// How many attempts one client gets at the endpoints that take a password, a code or an emailed link, or that
/// send email. Set under <c>RateLimiting:Auth</c>.
/// </summary>
public sealed class AuthRateLimitOptions
{
    public const string Section = "RateLimiting:Auth";

    /// <summary>Attempts allowed per client in each window, across all of those endpoints together.</summary>
    public int PermitLimit { get; set; } = 20;

    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Rate limiting for the auth endpoints (spec section 8): a client that guesses passwords, codes or links, or
/// has the API send email, is answered 429 once it has used up its attempts, until the window is over. Lockout
/// (see <c>AddUsers</c>) guards one user against many clients; this guards every user against one client.
/// </summary>
public static class AuthRateLimit
{
    public const string Policy = "auth";

    public static IServiceCollection AddAuthRateLimit(this IServiceCollection services)
    {
        services.AddOptions<AuthRateLimitOptions>().BindConfiguration(AuthRateLimitOptions.Section)
            .Validate(options => options is { PermitLimit: > 0, WindowSeconds: > 0 }, $"{AuthRateLimitOptions.Section} needs a positive PermitLimit and WindowSeconds.");

        return services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // One budget per client address, shared by every limited endpoint. X-Forwarded-For is not believed
            // unless the deployment says the API is reachable only through a proxy that sets it
            // (ASPNETCORE_FORWARDEDHEADERS_ENABLED=true, see the README): until then this is the address of
            // whoever connected, which behind a proxy is the proxy's, and a client cannot name another.
            limiter.AddPolicy(Policy, context =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = TimeSpan.FromSeconds(options.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

            limiter.OnRejected = async (rejected, _) =>
            {
                var response = rejected.HttpContext.Response;
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await rejected.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = rejected.HttpContext,
                    ProblemDetails = { Status = StatusCodes.Status429TooManyRequests, Title = "Too many attempts. Try again in a little while." },
                });
            };
        });
    }

    /// <summary>Limits how often one client may call these endpoints, and documents the 429 they answer then.</summary>
    public static TBuilder LimitAuthAttempts<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireRateLimiting(Policy).ProducesProblem(StatusCodes.Status429TooManyRequests);
}
