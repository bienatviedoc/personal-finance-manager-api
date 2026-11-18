using System.Reflection;

namespace Pfm.ArchitectureTests;

internal static class Layout
{
    public static readonly Assembly Domain = typeof(Pfm.Domain.AssemblyMarker).Assembly;
    public static readonly Assembly Application = typeof(Pfm.Application.ApplicationServices).Assembly;
    public static readonly Assembly Infrastructure = typeof(Pfm.Infrastructure.InfrastructureServices).Assembly;
    public static readonly Assembly Api = typeof(Program).Assembly;
    public static readonly Assembly Worker = Assembly.Load("Pfm.Worker");

    public static readonly Assembly[] AllAssemblies = [Domain, Application, Infrastructure, Api, Worker];

    public static string[] References(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(reference => reference.Name!)];
}
