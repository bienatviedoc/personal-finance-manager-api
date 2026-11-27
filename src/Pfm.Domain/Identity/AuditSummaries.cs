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

        return signedIn + (locale == Locale.Vi ? " Việc đăng nhập đã hủy yêu cầu xóa hồ sơ." : " Signing in cancelled the deletion the user had asked for.");
    }

    public static string ImportCommit(Locale locale, string fileName, int rows) => locale == Locale.Vi
        ? $"Đã xác nhận nhập sao kê {fileName}: {rows} dòng sẽ được nhập."
        : $"Committed the statement {fileName}: {Count(rows, "row")} to import.";

    public static string ImportUndo(Locale locale, string fileName, int transactions) => locale == Locale.Vi
        ? $"Đã hoàn tác lần nhập sao kê {fileName}: đã xóa {transactions} giao dịch."
        : $"Undid the import of the statement {fileName}: {Count(transactions, "transaction")} removed.";

    public static string CategorySet(Locale locale, int transactions, string category) => locale == Locale.Vi
        ? $"Đã chuyển {transactions} giao dịch sang danh mục {category}."
        : $"Put {Count(transactions, "transaction")} on the category {category}.";

    public static string TagAdded(Locale locale, int transactions, string tag) => locale == Locale.Vi
        ? $"Đã gắn nhãn {tag} cho {transactions} giao dịch."
        : $"Added the tag {tag} to {Count(transactions, "transaction")}.";

    public static string TagRemoved(Locale locale, int transactions, string tag) => locale == Locale.Vi
        ? $"Đã gỡ nhãn {tag} khỏi {transactions} giao dịch."
        : $"Removed the tag {tag} from {Count(transactions, "transaction")}.";

    public static string MovedToAccount(Locale locale, int transactions, string account) => locale == Locale.Vi
        ? $"Đã chuyển {transactions} giao dịch sang tài khoản {account}."
        : $"Moved {Count(transactions, "transaction")} to the account {account}.";

    public static string BulkDelete(Locale locale, int transactions) => locale == Locale.Vi
        ? $"Đã xóa {transactions} giao dịch."
        : $"Deleted {Count(transactions, "transaction")}.";

    public static string TransactionDeleted(Locale locale, decimal amount, Currency currency, DateOnly date) => locale == Locale.Vi
        ? $"Đã xóa một giao dịch {locale.WriteAmount(amount, currency)} ngày {Date(date)}."
        : $"Deleted a transaction of {locale.WriteAmount(amount, currency)} dated {Date(date)}.";

    public static string TransferDeleted(Locale locale, decimal amount, Currency currency, DateOnly date) => locale == Locale.Vi
        ? $"Đã xóa một chuyển khoản {locale.WriteAmount(amount, currency)} ngày {Date(date)}."
        : $"Deleted a transfer of {locale.WriteAmount(amount, currency)} dated {Date(date)}.";

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
