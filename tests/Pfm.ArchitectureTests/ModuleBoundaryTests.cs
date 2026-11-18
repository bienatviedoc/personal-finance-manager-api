using Pfm.ArchitectureTests.Fixtures;

namespace Pfm.ArchitectureTests;

public class ModuleBoundaryTests
{
    [Fact]
    public void No_module_reaches_into_another_modules_internals()
    {
        var violations = ModuleBoundaries.FindViolations("Pfm", Layout.AllAssemblies);

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    // The solution starts with no modules, so the rule above would pass even if it were broken.
    // These two tests run the same rule over fixture types to prove it catches a violation.

    [Fact]
    public void The_rule_reports_a_type_that_uses_another_modules_internals()
    {
        var violations = ModuleBoundaries.FindViolations("ArchFixture", typeof(FixtureMarker).Assembly);

        Assert.Contains(violations, violation => violation.Contains("ArchFixture.Domain.Budgets.UsesAccountInternals"));
    }

    [Fact]
    public void The_rule_reports_a_contract_that_exposes_another_modules_internals()
    {
        var violations = ModuleBoundaries.FindViolations("ArchFixture", typeof(FixtureMarker).Assembly);

        Assert.Contains(violations, violation => violation.Contains("ArchFixture.Application.Contracts.Budgets.ILeaksAccountInternals"));
    }

    [Fact]
    public void The_rule_reports_nothing_beyond_the_planted_violations()
    {
        var violations = ModuleBoundaries.FindViolations("ArchFixture", typeof(FixtureMarker).Assembly);

        Assert.Equal(2, violations.Count);
    }

    [Fact]
    public void The_rule_allows_use_of_another_modules_contracts()
    {
        var violations = ModuleBoundaries.FindViolations("ArchFixture", typeof(FixtureMarker).Assembly);

        Assert.DoesNotContain(violations, violation => violation.Contains("UsesAccountContracts"));
    }
}
