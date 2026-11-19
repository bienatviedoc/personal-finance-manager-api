namespace Pfm.Domain.Transactions;

/// <summary>The categories a new user starts with, in their language. They can reshape them freely afterwards.</summary>
public static class DefaultCategories
{
    private sealed record Entry(string English, string Vietnamese, string Icon, params Entry[] Subcategories);

    private const string ExpenseColor = "#ef4444";
    private const string IncomeColor = "#22c55e";

    private static readonly (Entry Category, CategoryKind Kind, string Color)[] Set =
    [
        (new("Food & drink", "Ăn uống", "utensils",
            new("Groceries", "Đi chợ", "shopping-cart"),
            new("Eating out", "Ăn ngoài", "utensils"),
            new("Coffee", "Cà phê", "coffee")), CategoryKind.Expense, "#f97316"),
        (new("Transport", "Di chuyển", "car",
            new("Fuel", "Xăng xe", "fuel"),
            new("Parking", "Gửi xe", "circle-parking"),
            new("Taxi & ride-hailing", "Taxi & xe công nghệ", "car-taxi-front"),
            new("Public transport", "Phương tiện công cộng", "bus")), CategoryKind.Expense, "#3b82f6"),
        (new("Housing", "Nhà ở", "house",
            new("Rent", "Tiền thuê nhà", "key-round"),
            new("Electricity", "Tiền điện", "zap"),
            new("Water", "Tiền nước", "droplet"),
            new("Internet & phone", "Internet & điện thoại", "wifi")), CategoryKind.Expense, "#8b5cf6"),
        (new("Shopping", "Mua sắm", "shopping-bag",
            new("Clothes", "Quần áo", "shirt"),
            new("Electronics", "Đồ điện tử", "smartphone"),
            new("Household", "Đồ gia dụng", "sofa")), CategoryKind.Expense, "#ec4899"),
        (new("Health", "Sức khỏe", "heart-pulse",
            new("Doctor", "Khám bệnh", "stethoscope"),
            new("Medicine", "Thuốc", "pill"),
            new("Insurance", "Bảo hiểm", "shield")), CategoryKind.Expense, "#14b8a6"),
        (new("Entertainment", "Giải trí", "clapperboard",
            new("Movies & events", "Phim & sự kiện", "ticket"),
            new("Subscriptions", "Dịch vụ đăng ký", "repeat"),
            new("Travel", "Du lịch", "plane")), CategoryKind.Expense, "#eab308"),
        (new("Education", "Giáo dục", "graduation-cap",
            new("Tuition", "Học phí", "school"),
            new("Books & courses", "Sách & khóa học", "book-open")), CategoryKind.Expense, "#6366f1"),
        (new("Family", "Gia đình", "users",
            new("Children", "Con cái", "baby"),
            new("Parents", "Bố mẹ", "heart-handshake"),
            new("Gifts & donations", "Quà tặng & từ thiện", "gift")), CategoryKind.Expense, "#f43f5e"),
        (new("Personal care", "Chăm sóc cá nhân", "sparkles"), CategoryKind.Expense, "#d946ef"),
        (new("Fees & charges", "Phí & lệ phí", "receipt",
            new("Bank fees", "Phí ngân hàng", "landmark"),
            new("Taxes", "Thuế", "file-text")), CategoryKind.Expense, "#64748b"),
        (new("Other expenses", "Chi phí khác", "ellipsis"), CategoryKind.Expense, ExpenseColor),

        (new("Salary", "Lương", "wallet"), CategoryKind.Income, IncomeColor),
        (new("Bonus", "Thưởng", "award"), CategoryKind.Income, "#10b981"),
        (new("Business", "Kinh doanh", "briefcase"), CategoryKind.Income, "#0ea5e9"),
        (new("Investments", "Đầu tư", "trending-up",
            new("Interest", "Lãi tiết kiệm", "piggy-bank"),
            new("Dividends", "Cổ tức", "chart-pie")), CategoryKind.Income, "#84cc16"),
        (new("Gifts received", "Được tặng", "gift"), CategoryKind.Income, "#a855f7"),
        (new("Other income", "Thu nhập khác", "ellipsis"), CategoryKind.Income, IncomeColor),
    ];

    /// <summary>A fresh default set, top-level categories before their subcategories.</summary>
    public static IReadOnlyList<Category> For(Language language)
    {
        var categories = new List<Category>();
        foreach (var (entry, kind, color) in Set)
        {
            var category = Category.Create(NameOf(entry, language), kind, entry.Icon, color);
            categories.Add(category);
            categories.AddRange(entry.Subcategories.Select(sub => Category.Create(NameOf(sub, language), kind, sub.Icon, color, parent: category)));
        }

        return categories;
    }

    private static string NameOf(Entry entry, Language language) => language switch
    {
        Language.English => entry.English,
        Language.Vietnamese => entry.Vietnamese,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "No default categories in this language."),
    };
}
