namespace Pfm.Api.Identity;

/// <summary>
/// How the API's cookies are marked. Everywhere but development the API sits behind TLS, so cookies
/// are Secure and use the __Host- prefix. The local stack runs over plain http://localhost, where a
/// Secure-only policy would refuse every request, so cookies follow the request's scheme instead and
/// drop the prefix, which browsers only accept on Secure cookies.
/// </summary>
public sealed record CookiePolicy(CookieSecurePolicy SecurePolicy, string NamePrefix)
{
    public static CookiePolicy For(IHostEnvironment environment) =>
        environment.IsDevelopment()
            ? new CookiePolicy(CookieSecurePolicy.SameAsRequest, "")
            : new CookiePolicy(CookieSecurePolicy.Always, "__Host-");
}
