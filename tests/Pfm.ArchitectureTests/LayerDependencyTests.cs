namespace Pfm.ArchitectureTests;

/// <summary>Dependencies point inwards: Api and Worker → Infrastructure → Application → Domain.</summary>
public class LayerDependencyTests
{
    [Fact]
    public void Domain_references_nothing_but_the_base_class_library()
    {
        var references = Layout.References(Layout.Domain);

        Assert.All(references, reference =>
            Assert.True(reference.StartsWith("System.", StringComparison.Ordinal), $"Pfm.Domain references {reference}"));
    }

    [Fact]
    public void Application_depends_on_Domain_and_no_outer_layer()
    {
        var references = Layout.References(Layout.Application);

        Assert.DoesNotContain("Pfm.Infrastructure", references);
        Assert.DoesNotContain("Pfm.Api", references);
        Assert.DoesNotContain("Pfm.Worker", references);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Npgsql")]
    public void Application_does_not_reference_persistence_or_web_frameworks(string framework)
    {
        var references = Layout.References(Layout.Application);

        Assert.DoesNotContain(references, reference => reference.StartsWith(framework, StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_a_host()
    {
        var references = Layout.References(Layout.Infrastructure);

        Assert.DoesNotContain("Pfm.Api", references);
        Assert.DoesNotContain("Pfm.Worker", references);
    }

    [Fact]
    public void The_hosts_do_not_depend_on_each_other()
    {
        Assert.DoesNotContain("Pfm.Worker", Layout.References(Layout.Api));
        Assert.DoesNotContain("Pfm.Api", Layout.References(Layout.Worker));
    }
}
