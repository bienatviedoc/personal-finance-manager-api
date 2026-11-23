using System.Text.Json;
using System.Text.Json.Serialization;
using Pfm.Domain;

namespace Pfm.Application.UseCases;

/// <summary>
/// How an enum travels as text, in JSON and in the export alike, and the one place that says so (README, "Money and
/// settings in JSON"): its name in camelCase (<c>"creditCard"</c>; a locale as its language tag, <c>"vi"</c>), never
/// a number; except a currency, which is its ISO 4217 code (<c>"VND"</c>).
/// </summary>
public static class EnumText
{
    /// <summary>The value as it travels.</summary>
    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        Of(typeof(TEnum), value.ToString());

    /// <summary>The enum value named <paramref name="name"/> (as C# names it) as it travels.</summary>
    public static string Of(Type enumType, string name)
    {
        ArgumentNullException.ThrowIfNull(enumType);
        return enumType == typeof(Currency) ? name : JsonNamingPolicy.CamelCase.ConvertName(name);
    }

    /// <summary>The JSON converters that write and read enums this way.</summary>
    public static IEnumerable<JsonConverter> JsonConverters() =>
    [
        // The first converter that takes a type wins, so the exception comes before the rule.
        new JsonStringEnumConverter<Currency>(allowIntegerValues: false),
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
    ];
}

/// <summary>Reads an enum as it travels (<see cref="EnumText"/>), never as a number and never in another spelling.</summary>
public static class EnumText<TEnum>
    where TEnum : struct, Enum
{
    private static readonly IReadOnlyDictionary<string, TEnum> Values = Enum.GetValues<TEnum>().ToDictionary(EnumText.Of);

    /// <summary>Every name that can be sent, for a message that lists the choices.</summary>
    public static readonly IReadOnlyCollection<string> Names = [.. Values.Keys];

    /// <summary>Null when the text was left out or is not one of <see cref="Names"/>.</summary>
    public static TEnum? Read(string? text) => text is not null && Values.TryGetValue(text, out var value) ? value : null;
}
