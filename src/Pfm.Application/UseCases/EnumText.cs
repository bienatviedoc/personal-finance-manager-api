using System.Text.Json;

namespace Pfm.Application.UseCases;

/// <summary>
/// Reads an enum as it travels in JSON: its name in camelCase (<c>"creditCard"</c>), never a number and never
/// another spelling (README, "Money and settings in JSON").
/// </summary>
public static class EnumText<TEnum>
    where TEnum : struct, Enum
{
    private static readonly IReadOnlyDictionary<string, TEnum> Values =
        Enum.GetValues<TEnum>().ToDictionary(value => JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));

    /// <summary>Every name that can be sent, for a message that lists the choices.</summary>
    public static readonly IReadOnlyCollection<string> Names = [.. Values.Keys];

    /// <summary>Null when the text was left out or is not one of <see cref="Names"/>.</summary>
    public static TEnum? Read(string? text) => text is not null && Values.TryGetValue(text, out var value) ? value : null;
}
