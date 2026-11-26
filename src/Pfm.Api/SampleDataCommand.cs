using System.Globalization;
using Pfm.Infrastructure.SampleData;

namespace Pfm.Api;

/// <summary>
/// Reads <c>Pfm.Api seed [--email &lt;email&gt;] [--password &lt;password&gt;] [--transactions &lt;count&gt;]</c>: who to seed
/// a year of sample data for, and how many transactions it holds. For a local environment only.
/// </summary>
public static class SampleDataCommand
{
    public const string Name = "seed";

    public const string Usage = "Usage: seed [--email <email>] [--password <password>] [--transactions <count>]";

    /// <summary>The options the arguments after <c>seed</c> name; null, with the reason, when they cannot be read.</summary>
    public static SampleDataOptions? Read(IReadOnlyList<string> arguments, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var options = new SampleDataOptions(SampleDataOptions.DefaultEmail, SampleDataOptions.DefaultPassword);
        problem = null;
        for (var i = 0; i < arguments.Count; i += 2)
        {
            if (i + 1 >= arguments.Count)
            {
                problem = $"{arguments[i]} needs a value. {Usage}";
                return null;
            }

            var value = arguments[i + 1];
            switch (arguments[i])
            {
                case "--email":
                    options = options with { Email = value };
                    break;
                case "--password":
                    options = options with { Password = value };
                    break;
                case "--transactions" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= SampleDataSeeder.FewestTransactions:
                    options = options with { Transactions = count };
                    break;
                case "--transactions":
                    problem = $"--transactions takes a whole number from {SampleDataSeeder.FewestTransactions}.";
                    return null;
                default:
                    problem = $"Unknown option {arguments[i]}. {Usage}";
                    return null;
            }
        }

        return options;
    }
}
