using Microsoft.Extensions.Options;
using Pfm.Application.Abstractions;
using Pfm.Application.Identity;
using Pfm.Domain;

namespace Pfm.Infrastructure.Identity;

/// <summary>
/// The emails sent about a user's sign-in, in the user's locale. A user who has not chosen one gets
/// English: an email cannot follow their browser.
/// </summary>
internal sealed class IdentityEmails(IEmailSender sender, IOptions<AppOptions> app) : IIdentityEmails
{
    public Task SendVerificationAsync(UserDetails user, string token, CancellationToken cancellationToken)
    {
        var link = $"{app.Value.PublicUrl}/verify-email?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        return SendAsync(
            user,
            english: new(
                "Confirm your email address",
                $"""
                Welcome to Personal Finance Manager.

                Confirm your email address to finish registering:

                {link}

                If you did not register, you can ignore this email.
                """),
            vietnamese: new(
                "Xác nhận địa chỉ email của bạn",
                $"""
                Chào mừng bạn đến với Personal Finance Manager.

                Hãy xác nhận địa chỉ email để hoàn tất đăng ký:

                {link}

                Nếu bạn không đăng ký, bạn có thể bỏ qua email này.
                """),
            cancellationToken);
    }

    public Task SendAlreadyRegisteredAsync(UserDetails user, CancellationToken cancellationToken) =>
        SendAsync(
            user,
            english: new(
                "Someone tried to register with your email address",
                $"""
                Someone tried to register with this email address, but it already has a user.

                If it was you, sign in instead:

                {app.Value.PublicUrl}/login

                If you have forgotten your password, you can reset it:

                {app.Value.PublicUrl}/forgot-password

                If it was not you, you can ignore this email. Nothing has changed.
                """),
            vietnamese: new(
                "Có người đã thử đăng ký bằng địa chỉ email của bạn",
                $"""
                Có người đã thử đăng ký bằng địa chỉ email này, nhưng địa chỉ này đã được đăng ký.

                Nếu đó là bạn, hãy đăng nhập:

                {app.Value.PublicUrl}/login

                Nếu bạn quên mật khẩu, bạn có thể đặt lại:

                {app.Value.PublicUrl}/forgot-password

                Nếu không phải bạn, bạn có thể bỏ qua email này. Không có gì thay đổi.
                """),
            cancellationToken);

    public Task SendPasswordResetAsync(UserDetails user, string token, CancellationToken cancellationToken)
    {
        var link = $"{app.Value.PublicUrl}/reset-password?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        return SendAsync(
            user,
            english: new(
                "Reset your password",
                $"""
                Someone asked to reset the password for this email address.

                If it was you, set a new password here. The link works once:

                {link}

                If it was not you, you can ignore this email. Your password has not changed.
                """),
            vietnamese: new(
                "Đặt lại mật khẩu của bạn",
                $"""
                Có người đã yêu cầu đặt lại mật khẩu cho địa chỉ email này.

                Nếu đó là bạn, hãy đặt mật khẩu mới tại đây. Liên kết chỉ dùng được một lần:

                {link}

                Nếu không phải bạn, bạn có thể bỏ qua email này. Mật khẩu của bạn chưa thay đổi.
                """),
            cancellationToken);
    }

    // Whoever owns this address has no user, so no locale either.
    public Task SendNoUserToResetAsync(string email, CancellationToken cancellationToken) =>
        sender.SendAsync(
            new EmailMessage(
                email,
                "Someone asked to reset a password for your email address",
                $"""
                Someone asked to reset the password for this email address, but it has no user at Personal Finance Manager.

                If it was you, you may have registered with another address. Or you can register with this one:

                {app.Value.PublicUrl}/register

                If it was not you, you can ignore this email.
                """),
            cancellationToken);

    private Task SendAsync(UserDetails user, Text english, Text vietnamese, CancellationToken cancellationToken)
    {
        var text = user.Locale switch
        {
            Locale.Vi => vietnamese,
            Locale.En or null => english,
            _ => throw new ArgumentOutOfRangeException(nameof(user), user.Locale, "No emails in this locale."),
        };

        return sender.SendAsync(new EmailMessage(user.Email, text.Subject, text.Body), cancellationToken);
    }

    private sealed record Text(string Subject, string Body);
}
