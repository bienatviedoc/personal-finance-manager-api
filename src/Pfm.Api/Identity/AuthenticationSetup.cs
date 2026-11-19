using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pfm.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Pfm.Infrastructure;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Api.Identity;

public static class AuthenticationSetup
{
    /// <summary>
    /// Cookie sign-in for the SPA, served from the same site. Every endpoint requires a signed-in
    /// user unless it opts out with AllowAnonymous.
    /// </summary>
    public static IServiceCollection AddPfmAuthentication(this IServiceCollection services, CookiePolicy cookies)
    {
        services.AddUsers().AddSignInManager().AddDefaultTokenProviders();

        services.AddDataProtection().SetApplicationName("pfm").PersistKeysToDbContext<PfmDbContext>();

        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser, HttpCurrentUser>());

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            // __Host- makes browsers insist on Secure, Path=/ and no Domain, so no subdomain can set it.
            options.Cookie.Name = $"{cookies.NamePrefix}pfm-session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookies.SecurePolicy;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromDays(7);
            options.SlidingExpiration = true;

            // An API answers with status codes; the SPA decides where to send the viewer. Set on the
            // existing events, never by replacing them: Identity's security-stamp check lives there.
            options.Events.OnRedirectToLogin = context => StatusOnly(context, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => StatusOnly(context, StatusCodes.Status403Forbidden);
        });

        // A changed security stamp (password reset, deactivation) ends every session within this time.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(5));

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    private static Task StatusOnly(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
