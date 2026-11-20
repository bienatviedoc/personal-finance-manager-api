using System.Net;
using System.Net.Http.Json;

namespace Pfm.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TwoFactorTests(PfmApi api)
{
    [Fact]
    public void The_test_authenticator_agrees_with_the_RFC_6238_test_vector()
    {
        // RFC 6238 appendix B: the ASCII key "12345678901234567890" at 59 seconds gives 94287082 with eight digits.
        Assert.Equal("287082", Totp.CodeAt("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", DateTimeOffset.FromUnixTimeSeconds(59)));
    }

    [Fact]
    public async Task A_new_user_has_two_factor_sign_in_turned_off()
    {
        var (browser, _) = await api.SignedInAsync();

        var status = await StatusAsync(browser);

        Assert.Equal(new TwoFactor(Enabled: false, RecoveryCodesLeft: 0), status);
    }

    [Fact]
    public async Task Setting_up_answers_a_key_and_the_link_an_authenticator_app_reads_from_a_QR_code()
    {
        var (browser, email) = await api.SignedInAsync();

        var response = await browser.PostAsync("/api/v1/me/two-factor/setup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setup = (await response.Content.ReadFromJsonAsync<Setup>())!;
        var uri = new Uri(setup.AuthenticatorUri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("otpauth", uri.Scheme);
        Assert.Equal("totp", uri.Host);
        Assert.Equal($"/Personal Finance Manager:{email}", Uri.UnescapeDataString(uri.AbsolutePath));
        Assert.Equal(setup.Key, query["secret"]);
        Assert.Equal("Personal Finance Manager", query["issuer"]);
        // Nothing is on until a code from the authenticator confirms it.
        Assert.False((await StatusAsync(browser)).Enabled);
    }

    [Fact]
    public async Task Setting_up_again_before_confirming_shows_the_same_key()
    {
        var (browser, _) = await api.SignedInAsync();

        var first = await SetUpAsync(browser);
        var second = await SetUpAsync(browser);

        Assert.Equal(first.Key, second.Key);
    }

    [Fact]
    public async Task Confirming_with_a_code_from_the_authenticator_turns_it_on_and_answers_recovery_codes()
    {
        var (browser, _) = await api.SignedInAsync();
        var setup = await SetUpAsync(browser);

        var response = await browser.PostAsync("/api/v1/me/two-factor/enable", new { code = Totp.CodeFor(setup.Key) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var codes = (await response.Content.ReadFromJsonAsync<CodesBody>())!.RecoveryCodes;
        Assert.Equal(10, codes.Distinct().Count());
        Assert.Equal(new TwoFactor(Enabled: true, RecoveryCodesLeft: 10), await StatusAsync(browser));
    }

    [Theory]
    [InlineData("123 456")]
    [InlineData("123-456")]
    public async Task A_code_may_be_typed_the_way_authenticator_apps_show_it(string format)
    {
        var (browser, _) = await api.SignedInAsync();
        var setup = await SetUpAsync(browser);
        var code = Totp.CodeFor(setup.Key);
        var typed = format.Replace("123", code[..3], StringComparison.Ordinal).Replace("456", code[3..], StringComparison.Ordinal);

        var response = await browser.PostAsync("/api/v1/me/two-factor/enable", new { code = typed });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_code_is_named_as_the_field_to_fix_and_turns_nothing_on()
    {
        var (browser, _) = await api.SignedInAsync();
        var setup = await SetUpAsync(browser);

        var wrong = await browser.PostAsync("/api/v1/me/two-factor/enable", new { code = Totp.WrongCodeFor(setup.Key) });
        var missing = await browser.PostJsonAsync("/api/v1/me/two-factor/enable", "{}");

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(["code"], await wrong.ErrorFieldsAsync());
        Assert.Equal(["code"], await missing.ErrorFieldsAsync());
        Assert.False((await StatusAsync(browser)).Enabled);
    }

    [Fact]
    public async Task A_code_cannot_confirm_before_anything_was_set_up()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync("/api/v1/me/two-factor/enable", new { code = "123456" });

        Assert.Equal(["code"], await response.ErrorFieldsAsync());
    }

    [Fact]
    public async Task Setting_up_is_refused_while_two_factor_is_on_and_the_authenticator_keeps_working()
    {
        var (browser, email) = await api.SignedInAsync();
        var key = await api.TurnOnTwoFactorAsync(browser);

        var response = await browser.PostAsync("/api/v1/me/two-factor/setup");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var signIn = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password, twoFactorCode = Totp.CodeFor(key.Key) });
        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
    }

    [Fact]
    public async Task Turning_it_on_keeps_this_session_and_ends_the_users_others()
    {
        var (browser, email) = await api.SignedInAsync();
        var other = api.CreateBrowser();
        await other.PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });

        await api.TurnOnTwoFactorAsync(browser);

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Turning_it_off_takes_the_password_and_then_signing_in_needs_no_code()
    {
        var (browser, email) = await api.SignedInAsync();
        await api.TurnOnTwoFactorAsync(browser);

        var wrongPassword = await browser.PostAsync("/api/v1/me/two-factor/disable", new { password = "not the right password" });
        var stillOn = (await StatusAsync(browser)).Enabled;
        var response = await browser.PostAsync("/api/v1/me/two-factor/disable", new { password = PfmApi.Password });

        Assert.Equal(["password"], await wrongPassword.ErrorFieldsAsync());
        Assert.True(stillOn);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new TwoFactor(Enabled: false, RecoveryCodesLeft: 0), await StatusAsync(browser));
        var signIn = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password });
        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
    }

    [Fact]
    public async Task Turning_it_on_again_after_turning_it_off_needs_a_new_key()
    {
        var (browser, _) = await api.SignedInAsync();
        var old = await api.TurnOnTwoFactorAsync(browser);
        await browser.PostAsync("/api/v1/me/two-factor/disable", new { password = PfmApi.Password });

        var setup = await SetUpAsync(browser);
        var withOldKey = await browser.PostAsync("/api/v1/me/two-factor/enable", new { code = Totp.CodeFor(old.Key) });

        Assert.NotEqual(old.Key, setup.Key);
        Assert.Equal(HttpStatusCode.BadRequest, withOldKey.StatusCode);
    }

    [Fact]
    public async Task New_recovery_codes_take_the_password_and_replace_the_old_ones()
    {
        var (browser, email) = await api.SignedInAsync();
        var old = await api.TurnOnTwoFactorAsync(browser);

        var wrongPassword = await browser.PostAsync("/api/v1/me/two-factor/recovery-codes", new { password = "not the right password" });
        var response = await browser.PostAsync("/api/v1/me/two-factor/recovery-codes", new { password = PfmApi.Password });

        Assert.Equal(["password"], await wrongPassword.ErrorFieldsAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fresh = (await response.Content.ReadFromJsonAsync<CodesBody>())!.RecoveryCodes;
        Assert.Equal(10, fresh.Except(old.RecoveryCodes).Count());
        var withOld = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password, recoveryCode = old.RecoveryCodes[0] });
        var withFresh = await api.CreateBrowser().PostAsync("/api/v1/auth/login", new { email, password = PfmApi.Password, recoveryCode = fresh[0] });
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, withFresh.StatusCode);
    }

    [Fact]
    public async Task Recovery_codes_cannot_be_made_while_two_factor_is_off()
    {
        var (browser, _) = await api.SignedInAsync();

        var response = await browser.PostAsync("/api/v1/me/two-factor/recovery-codes", new { password = PfmApi.Password });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/v1/me/two-factor")]
    [InlineData("POST", "/api/v1/me/two-factor/setup")]
    [InlineData("POST", "/api/v1/me/two-factor/enable")]
    [InlineData("POST", "/api/v1/me/two-factor/disable")]
    [InlineData("POST", "/api/v1/me/two-factor/recovery-codes")]
    public async Task Two_factor_settings_need_a_signed_in_user(string method, string path)
    {
        var browser = api.CreateBrowser();

        var response = method == "GET" ? await browser.GetAsync(path) : await browser.PostAsync(path, new { code = "123456", password = PfmApi.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<TwoFactor> StatusAsync(Browser browser) =>
        (await (await browser.GetAsync("/api/v1/me/two-factor")).Content.ReadFromJsonAsync<TwoFactor>())!;

    private static async Task<Setup> SetUpAsync(Browser browser) =>
        (await (await browser.PostAsync("/api/v1/me/two-factor/setup")).Content.ReadFromJsonAsync<Setup>())!;

    private sealed record TwoFactor(bool Enabled, int RecoveryCodesLeft);

    private sealed record Setup(string Key, string AuthenticatorUri);

    private sealed record CodesBody(string[] RecoveryCodes);
}
