namespace Pfm.Domain.Identity;

/// <summary>
/// The short summary of each audit entry, in the user's locale. A summary says what happened and to how much, never a
/// password, a code, a token or a statement's text.
/// </summary>
public static class AuditSummaries
{
    public static string SignIn(Locale locale, SignInMethod method, bool deletionCancelled)
    {
        var signedIn = (locale, method) switch
        {
            (Locale.Vi, SignInMethod.Password) => "Đã đăng nhập bằng mật khẩu.",
            (Locale.Vi, SignInMethod.AuthenticatorCode) => "Đã đăng nhập bằng mật khẩu và mã từ ứng dụng xác thực.",
            (Locale.Vi, SignInMethod.RecoveryCode) => "Đã đăng nhập bằng mật khẩu và mã khôi phục.",
            (_, SignInMethod.Password) => "Signed in with a password.",
            (_, SignInMethod.AuthenticatorCode) => "Signed in with a password and an authenticator code.",
            (_, SignInMethod.RecoveryCode) => "Signed in with a password and a recovery code.",
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };
        if (!deletionCancelled)
        {
            return signedIn;
        }

        return signedIn + (locale == Locale.Vi ? " Việc đăng nhập đã hủy yêu cầu xóa người dùng." : " Signing in cancelled the deletion the user had asked for.");
    }
}
