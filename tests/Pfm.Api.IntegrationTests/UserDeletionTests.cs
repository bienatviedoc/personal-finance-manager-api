using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pfm.Domain;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// A user deletes themselves (spec section 8, Privacy): they are signed out and deactivated at once, signing in
/// again within 30 days cancels it, and after 30 days everything of theirs is erased.
/// </summary>
[Collection(ApiCollection.Name)]
public class UserDeletionTests(PfmApi api)
{
    private const string Deletion = "/api/v1/me/deletion";

    [Fact]
    public async Task Deleting_your_user_signs_you_out_at_once()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync(Deletion, new { password = PfmApi.Password });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Deleting_your_user_ends_your_other_sessions_too()
    {
        var (browser, email) = await api.SignedInAsync();
        var elsewhere = api.CreateBrowser();
        (await elsewhere.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password })).EnsureSuccessStatusCode();

        await browser.PostAsync(Deletion, new { password = PfmApi.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, (await elsewhere.GetAsync("/api/v1/me")).StatusCode);
    }

    [Theory]
    [InlineData("not the right password")]
    [InlineData("")]
    public async Task Deleting_your_user_takes_your_password(string password)
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync(Deletion, new { password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["password"], await response.ErrorFieldsAsync());
        // Nothing happened: the user is still signed in, and signing in again cancels nothing.
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SignInAsync(api.CreateBrowser(), await EmailOfAsync(browser))).StatusCode);
    }

    [Fact]
    public async Task Only_a_signed_in_user_can_delete_themselves()
    {
        var response = await api.CreateBrowser().PostAsync(Deletion, new { password = PfmApi.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signing_in_again_within_30_days_cancels_the_deletion_and_says_so()
    {
        var (browser, email) = await api.SignedInAsync();
        var accountId = await BrowserFixtures.AddAccountAsync(browser, "Everyday");
        await browser.PostAsync(Deletion, new { password = PfmApi.Password });
        var later = PfmApi.CreateBrowser(api.After(TimeSpan.FromDays(29)));

        var signIn = await SignInAsync(later, email);

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.True((await signIn.Content.ReadFromJsonAsync<SignedIn>())!.DeletionCancelled);
        // Nothing was erased, and nothing will be: the user is one like any other again.
        Assert.Equal(HttpStatusCode.OK, (await later.GetAsync($"/api/v1/accounts/{accountId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SignInAsync(api.CreateBrowser(), email)).StatusCode);
        await api.EraseDeactivatedUsersAsync(after: TimeSpan.FromDays(31));
        Assert.Equal(HttpStatusCode.NoContent, (await SignInAsync(api.CreateBrowser(), email)).StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_cancels_nothing()
    {
        var (browser, email) = await api.SignedInAsync();
        await browser.PostAsync(Deletion, new { password = PfmApi.Password });

        var signIn = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = "not the right password" });

        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
        await api.EraseDeactivatedUsersAsync(after: TimeSpan.FromDays(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(api.CreateBrowser(), email)).StatusCode);
    }

    [Fact]
    public async Task A_user_who_signs_in_with_two_factors_cancels_only_with_their_code()
    {
        var (browser, email) = await api.SignedInAsync();
        var (key, _) = await api.TurnOnTwoFactorAsync(browser);
        await browser.PostAsync(Deletion, new { password = PfmApi.Password });

        var passwordOnly = await SignInAsync(api.CreateBrowser(), email);
        await api.EraseDeactivatedUsersAsync(after: TimeSpan.FromDays(31));

        // The password alone cancelled nothing, so the user was erased.
        Assert.Equal(HttpStatusCode.Unauthorized, passwordOnly.StatusCode);
        Assert.Equal(Pfm.Api.Identity.IdentityEndpoints.TwoFactorRequired, await passwordOnly.ProblemTypeAsync());
        var withCode = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password, twoFactorCode = Totp.CodeFor(key) });
        Assert.Equal(HttpStatusCode.Unauthorized, withCode.StatusCode);
        Assert.Null(await withCode.ProblemTypeAsync());
    }

    [Fact]
    public async Task After_30_days_signing_in_is_refused_like_a_wrong_password_even_before_the_data_is_erased()
    {
        var (browser, email) = await api.SignedInAsync();
        await browser.PostAsync(Deletion, new { password = PfmApi.Password });
        var later = PfmApi.CreateBrowser(api.After(TimeSpan.FromDays(30) + TimeSpan.FromMinutes(1)));

        var signIn = await SignInAsync(later, email);

        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
        Assert.Null(await signIn.ProblemTypeAsync());
        Assert.Equal("The email or password is incorrect.", await signIn.ProblemTitleAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await later.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task After_30_days_everything_of_a_deactivated_user_is_erased()
    {
        var (browser, email) = await api.SignedInAsync();
        var userId = await IdOfAsync(browser);
        var owned = await UserErasureFixtures.OwnSomethingOfEveryKindAsync(browser);
        Assert.Equal(owned, await api.RowsOwnedByAsync(userId));
        await browser.PostAsync(Deletion, new { password = PfmApi.Password });

        var erased = await api.EraseDeactivatedUsersAsync(after: TimeSpan.FromDays(30) + TimeSpan.FromMinutes(1));

        Assert.True(erased >= 1);
        Assert.Empty(await api.RowsOwnedByAsync(userId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(api.CreateBrowser(), email)).StatusCode);
    }

    [Fact]
    public async Task An_erased_users_email_address_is_free_to_register_again_and_starts_with_nothing()
    {
        var (browser, email) = await api.SignedInAsync();
        await BrowserFixtures.AddAccountAsync(browser, "Everyday");
        await browser.PostAsync(Deletion, new { password = PfmApi.Password });
        await api.EraseDeactivatedUsersAsync(after: TimeSpan.FromDays(31));

        var again = api.CreateBrowser();
        (await again.PostAsync("/api/v1/auth/register", new { email, password = PfmApi.Password })).EnsureSuccessStatusCode();
        (await again.PostAsync("/api/v1/auth/verify-email", PfmApi.VerificationFrom(api.LastLinkTo(email)))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, (await SignInAsync(again, email)).StatusCode);
        Assert.Empty((await again.Http.GetFromJsonAsync<Listed>("/api/v1/accounts"))!.Items);
    }

    [Fact]
    public async Task Until_the_30_days_are_over_nothing_is_erased()
    {
        var (browser, email) = await api.SignedInAsync();
        var userId = await IdOfAsync(browser);
        var owned = await UserErasureFixtures.OwnSomethingOfEveryKindAsync(browser);
        await browser.PostAsync(Deletion, new { password = PfmApi.Password });

        await api.EraseDeactivatedUsersAsync(after: TimeSpan.FromDays(30) - TimeSpan.FromMinutes(1));

        Assert.Equal(owned, await api.RowsOwnedByAsync(userId));
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(api.CreateBrowser(), email)).StatusCode);
    }

    [Fact]
    public async Task Erasing_deactivated_users_leaves_every_other_user_as_they_were()
    {
        var (leaving, _) = await api.SignedInAsync();
        await UserErasureFixtures.OwnSomethingOfEveryKindAsync(leaving);
        var (staying, _) = await api.SignedInAsync();
        var stayingId = await IdOfAsync(staying);
        var owned = await UserErasureFixtures.OwnSomethingOfEveryKindAsync(staying);
        await leaving.PostAsync(Deletion, new { password = PfmApi.Password });

        await api.EraseDeactivatedUsersAsync(after: TimeSpan.FromDays(31));

        Assert.Equal(owned, await api.RowsOwnedByAsync(stayingId));
        Assert.Equal(HttpStatusCode.OK, (await staying.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Every_table_holds_rows_a_user_owns_or_is_known_to_hold_none()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var model = scope.ServiceProvider.GetRequiredService<PfmDbContext>().Model;

        var notUserOwned = model.GetEntityTypes()
            .Where(entity => !typeof(IUserOwned).IsAssignableFrom(entity.ClrType) && !IsOwnedByAUserOwnedEntity(entity))
            .Select(entity => entity.GetTableName()!)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        // A user-owned table is exported and erased without being named anywhere. A table that is not must be
        // argued for here: it holds nothing of one user's, or what it holds leaves another way.
        Assert.Equal(
            [
                "DataProtectionKeys", // The keys that sign cookies and emailed links: the app's, not a user's.
                "ExchangeRates", // Shared by every user.
                "UserClaims", // These three go with their user when the user is erased;
                "UserLogins", // the tokens are the authenticator key and recovery codes, which are secrets
                "UserTokens", // and so not exported.
                "Users", // Exported as profile.json, and erased last.
            ],
            notUserOwned);
    }

    /// <summary>A part of a user-owned entity (a transaction's lines) is the user's through its owner: exported and erased with it.</summary>
    private static bool IsOwnedByAUserOwnedEntity(Microsoft.EntityFrameworkCore.Metadata.IEntityType entity) =>
        entity.FindOwnership()?.PrincipalEntityType is { } owner && typeof(IUserOwned).IsAssignableFrom(owner.ClrType);

    private static Task<HttpResponseMessage> SignInAsync(Browser browser, string email) =>
        browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });

    private static async Task<string> EmailOfAsync(Browser browser) =>
        (await browser.Http.GetFromJsonAsync<Me>("/api/v1/me"))!.Email;

    private static async Task<Guid> IdOfAsync(Browser browser) =>
        (await browser.Http.GetFromJsonAsync<Me>("/api/v1/me"))!.Id;

    private sealed record Me(Guid Id, string Email);

    private sealed record Listed(object[] Items);

    private sealed record SignedIn(bool DeletionCancelled);
}
