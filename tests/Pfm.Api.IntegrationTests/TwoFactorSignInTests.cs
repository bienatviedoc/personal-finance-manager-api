using System.Net;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TwoFactorSignInTests(PfmApi api)
{
    private const string TwoFactorRequired = "urn:pfm:problem:two-factor-required";
    private const string TwoFactorCodeIncorrect = "urn:pfm:problem:two-factor-code-incorrect";

    [Fact]
    public async Task With_two_factor_on_the_password_alone_does_not_sign_in_and_a_code_is_asked_for()
    {
        var (_, email, _) = await UserWithTwoFactorAsync();
        var browser = api.CreateBrowser();

        var response = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(TwoFactorRequired, await response.ProblemTypeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task The_password_and_a_code_from_the_authenticator_sign_in()
    {
        var (key, email, _) = await UserWithTwoFactorAsync();
        var browser = api.CreateBrowser();

        var response = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password, twoFactorCode = Totp.CodeFor(key) });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task A_wrong_code_does_not_sign_in()
    {
        var (key, email, _) = await UserWithTwoFactorAsync();
        var browser = api.CreateBrowser();

        var response = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password, twoFactorCode = Totp.WrongCodeFor(key) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(TwoFactorCodeIncorrect, await response.ProblemTypeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_the_same_way_whether_or_not_a_code_is_sent_or_two_factor_is_on()
    {
        var (key, withTwoFactor, _) = await UserWithTwoFactorAsync();
        var (_, without) = await api.RegisteredAndVerifiedAsync();
        var browser = api.CreateBrowser();

        var responses = new[]
        {
            await browser.PostAsync("/api/v1/auth/login", new { email = withTwoFactor, password = "not the right password" }),
            await browser.PostAsync("/api/v1/auth/login", new { email = withTwoFactor, password = "not the right password", twoFactorCode = Totp.CodeFor(key) }),
            await browser.PostAsync("/api/v1/auth/login", new { email = without, password = "not the right password" }),
        };

        // Only someone who knows the password learns that the user signs in with two factors.
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.Single((await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()))).Select(WithoutTraceId).Distinct());
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once_in_place_of_the_authenticator()
    {
        var (_, email, recoveryCodes) = await UserWithTwoFactorAsync();
        var first = api.CreateBrowser();

        var signedIn = await first.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password, recoveryCode = $" {recoveryCodes[0]} " });
        var again = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password, recoveryCode = recoveryCodes[0] });

        Assert.Equal(HttpStatusCode.NoContent, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        Assert.Equal(TwoFactorCodeIncorrect, await again.ProblemTypeAsync());
        var status = await (await first.GetAsync("/api/v1/me/two-factor")).Content.ReadAsStringAsync();
        Assert.Contains("\"recoveryCodesLeft\":9", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_code_sent_by_a_user_without_two_factor_changes_nothing()
    {
        var (browser, email) = await api.RegisteredAndVerifiedAsync();

        var response = await browser.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password, twoFactorCode = "123456" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<(string Key, string Email, string[] RecoveryCodes)> UserWithTwoFactorAsync()
    {
        var (browser, email) = await api.SignedInAsync();
        var (key, recoveryCodes) = await api.TurnOnTwoFactorAsync(browser);
        return (key, email, recoveryCodes);
    }

    private static string WithoutTraceId(string problem) =>
        System.Text.RegularExpressions.Regex.Replace(problem, "\"traceId\":\"[^\"]*\"", "");
}
