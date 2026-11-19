using FluentValidation;
using Pfm.Application.UseCases;

namespace Pfm.ArchitectureTests;

/// <summary>Use cases follow one shape: a command, one validator for it, and a handler (spec section 9).</summary>
public class UseCaseTests
{
    private static readonly Type[] ApplicationTypes = Layout.Application.GetTypes();

    private static Type[] Commands() =>
    [
        .. ApplicationTypes
            .Where(type => type is { IsAbstract: false } && BaseTypes(type).Any(IsCommandHandler))
            .Select(handler => BaseTypes(handler).First(IsCommandHandler).GetGenericArguments()[0]),
    ];

    [Fact]
    public void Every_command_has_exactly_one_validator()
    {
        var commands = Commands();
        Assert.NotEmpty(commands);

        Assert.All(commands, command =>
        {
            var validators = ApplicationTypes
                .Where(type => !type.IsAbstract && typeof(IValidator<>).MakeGenericType(command).IsAssignableFrom(type))
                .ToArray();
            Assert.True(validators.Length == 1, $"{command.FullName} has {validators.Length} validators");
        });
    }

    [Fact]
    public void Every_command_has_exactly_one_handler()
    {
        var commands = Commands();

        Assert.Equal(commands.Length, commands.Distinct().Count());
    }

    private static bool IsCommandHandler(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CommandHandler<,>);

    private static IEnumerable<Type> BaseTypes(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }
}
