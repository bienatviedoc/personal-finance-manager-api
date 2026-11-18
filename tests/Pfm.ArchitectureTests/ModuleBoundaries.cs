using System.Reflection;
using NetArchTest.Rules;

namespace Pfm.ArchitectureTests;

/// <summary>
/// The rule that keeps the monolith modular. A module is a folder of the same name in each
/// layer (<c>{root}.{Layer}.{Module}</c>). Code in one module may use another module only
/// through that module's contracts, which live in <c>{root}.Application.Contracts.{Module}</c>.
/// </summary>
public static class ModuleBoundaries
{
    public static readonly string[] Modules = ["Accounts", "Transactions", "Budgets", "Reports", "Imports", "Identity"];

    private static readonly string[] Layers = ["Domain", "Application", "Infrastructure", "Api", "Worker"];

    /// <summary>Returns one line per type that reaches into another module's internals.</summary>
    public static IReadOnlyList<string> FindViolations(string root, params Assembly[] assemblies)
    {
        var violations = new List<string>();

        foreach (var module in Modules)
        {
            var otherModulesInternals = Modules
                .Where(other => other != module)
                .SelectMany(other => Layers.Select(layer => $"{root}.{layer}.{other}"))
                .ToArray();

            // A module's own code, including its contracts: a contract that exposes another
            // module's internals would hand them to every caller.
            var moduleCode = $@"^{root}\.(({string.Join('|', Layers)})\.{module}|Application\.Contracts\.{module})(\.|$)";

            var result = Types.InAssemblies(assemblies)
                .That().ResideInNamespaceMatching(moduleCode)
                .ShouldNot().HaveDependencyOnAny(otherModulesInternals)
                .GetResult();

            if (!result.IsSuccessful)
            {
                violations.AddRange(result.FailingTypeNames.Select(type => $"{type} (module {module}) uses another module's internals"));
            }
        }

        return violations;
    }
}
