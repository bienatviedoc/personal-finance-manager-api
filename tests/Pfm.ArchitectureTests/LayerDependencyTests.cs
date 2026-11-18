namespace Pfm.ArchitectureTests;

/// <summary>Dependencies point inwards: Api and Worker → Infrastructure → Application → Domain.</summary>
public class LayerDependencyTests
{
    /// <summary>
    /// The core runtime assemblies the domain may use. Anything else, including other System.*
    /// assemblies such as System.Text.Json or System.ComponentModel.Annotations, is a framework
    /// concern. Widening this list is a deliberate decision, made in review.
    /// </summary>
    private static readonly string[] DomainAllowedReferences =
    [
        "System.Runtime",
        "System.Collections",
        "System.Linq",
        "System.Memory",
        "System.Runtime.Numerics",

        // The base class library facade. Coverage instrumentation adds it when tests run with coverage.
        "netstandard",
    ];

    [Fact]
    public void Domain_compiles_against_core_runtime_assemblies_only()
    {
        var references = Layout.References(Layout.Domain);

        Assert.All(references, reference =>
            Assert.True(DomainAllowedReferences.Contains(reference), $"Pfm.Domain references {reference}"));
    }

    [Fact]
    public void Domain_project_declares_no_package_or_project_references()
    {
        // Compiled references only show what code uses today; the project file shows what it could use.
        var project = Layout.ProjectFile("Pfm.Domain");

        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("ProjectReference"));
        Assert.Empty(project.Descendants("FrameworkReference"));
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
