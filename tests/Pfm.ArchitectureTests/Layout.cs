using System.Reflection;
using System.Xml.Linq;

namespace Pfm.ArchitectureTests;

internal static class Layout
{
    public static readonly Assembly Domain = typeof(Pfm.Domain.AssemblyMarker).Assembly;
    public static readonly Assembly Application = typeof(Pfm.Application.ApplicationServices).Assembly;
    public static readonly Assembly Infrastructure = typeof(Pfm.Infrastructure.InfrastructureServices).Assembly;
    // Not typeof(Program): ASP.NET Core makes every top-level Program public, the worker's too.
    public static readonly Assembly Api = typeof(Pfm.Api.JsonSetup).Assembly;
    public static readonly Assembly Worker = Assembly.Load("Pfm.Worker");

    public static readonly Assembly[] AllAssemblies = [Domain, Application, Infrastructure, Api, Worker];

    public static string[] References(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(reference => reference.Name!)];

    /// <summary>Loads <c>src/{project}/{project}.csproj</c> from the repository.</summary>
    public static XDocument ProjectFile(string project) =>
        XDocument.Load(Path.Combine(RepositoryRoot(), "src", project, $"{project}.csproj"));

    /// <summary>Every C# source file under <c>src</c>, by its path from the repository root, leaving out generated migrations and build output.</summary>
    public static IReadOnlyDictionary<string, string> SourceFiles()
    {
        var root = RepositoryRoot();
        return Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(file => !file.Contains("/Migrations/", StringComparison.Ordinal) && !file.Contains("/obj/", StringComparison.Ordinal) && !file.Contains("/bin/", StringComparison.Ordinal))
            .ToDictionary(file => file, file => File.ReadAllText(Path.Combine(root, file)));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Pfm.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No Pfm.slnx found above {AppContext.BaseDirectory}.");
    }
}
