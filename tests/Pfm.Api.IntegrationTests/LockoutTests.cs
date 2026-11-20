using System.Net;

namespace Pfm.Api.IntegrationTests;

/// <summary>Five wrong passwords or two-factor codes in a row lock sign-in for the user for a while.</summary>
[Collection(ApiCollection.Name)]
public class LockoutTests(PfmApi api)
{
    private const int Allowed = 5;

    [Fact]
    public async Task After_five_wrong_passwords_even_the_right_one_is_refused_the_way_a_wrong_one_is()
    {
        var (browser, email) = await api.RegisteredAndVerifiedAsync();
        var wrong = await WrongPasswordsAsync(browser, email, Allowed);

        var right = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });

        // Saying "locked" would tell a stranger the email has a user.
        Assert.Equal(HttpStatusCode.Unauthorized, right.StatusCode);
        Assert.Equal(await wrong[^1].ProblemTitleAsync(), await right.ProblemTitleAsync());
        Assert.Null(await right.ProblemTypeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Four_wrong_passwords_do_not_lock_and_signing_in_starts_the_count_again()
    {
        var (browser, email) = await api.RegisteredAndVerifiedAsync();
        await WrongPasswordsAsync(browser, email, Allowed - 1);

        var signedIn = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });
        await WrongPasswordsAsync(browser, email, Allowed - 1);
        var again = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });

        Assert.Equal(HttpStatusCode.NoContent, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task The_lock_lifts_when_its_time_is_up()
    {
        // Long enough that the locked attempt below lands inside it even on a slow machine.
        var shortLock = PfmApi.CreateBrowser(api.WhereSignInLocksFor(TimeSpan.FromSeconds(3)));
        var (browser, email) = await api.RegisteredAndVerifiedAsync(shortLock);
        await WrongPasswordsAsync(browser, email, Allowed);
        var locked = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });

        await Task.Delay(TimeSpan.FromSeconds(3.2));
        var afterwards = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, afterwards.StatusCode);
    }

    [Fact]
    public async Task Wrong_two_factor_codes_lock_sign_in_too()
    {
        var (signedIn, email) = await api.SignedInAsync();
        var (key, _) = await api.TurnOnTwoFactorAsync(signedIn);
        var browser = api.CreateBrowser();
        for (var attempt = 0; attempt < Allowed; attempt++)
        {
            await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password, twoFactorCode = Totp.WrongCodeFor(key) });
        }

        var right = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password, twoFactorCode = Totp.CodeFor(key) });

        Assert.Equal(HttpStatusCode.Unauthorized, right.StatusCode);
        Assert.Null(await right.ProblemTypeAsync());
    }

    [Fact]
    public async Task Resetting_the_password_lifts_the_lock()
    {
        var (browser, email) = await api.RegisteredAndVerifiedAsync();
        await WrongPasswordsAsync(browser, email, Allowed);
        await browser.PostAsync("/api/v1/auth/forgot-password", new { email });
        var query = System.Web.HttpUtility.ParseQueryString(api.LastLinkTo(email).Query);

        await browser.PostAsync("/api/v1/auth/reset-password", new { userId = query["userId"], token = query["token"], password = "a brand new passphrase" });
        var signIn = await browser.PostAsync("/api/v1/auth/login", new { email, password = "a brand new passphrase" });

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
    }

    [Fact]
    public async Task Wrong_passwords_for_one_user_lock_nobody_else()
    {
        var (browser, email) = await api.RegisteredAndVerifiedAsync();
        var (_, other) = await api.RegisteredAndVerifiedAsync();
        await WrongPasswordsAsync(browser, email, Allowed);

        var signIn = await browser.PostAsync("/api/v1/auth/login", new { email = other, password = PfmApi.Password });

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
    }

    private static async Task<List<HttpResponseMessage>> WrongPasswordsAsync(Browser browser, string email, int times)
    {
        var responses = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < times; attempt++)
        {
            responses.Add(await browser.PostAsync("/api/v1/auth/login", new { email, password = "not the right password" }));
        }

        return responses;
    }
}
