using System.IO;
using System.Linq;
using System.Reflection;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Resolves the repo root baked in at build time (<c>CSharpGameEngineRepoRoot</c> assembly
/// metadata, set from <c>$(MSBuildThisFileDirectory)</c>) and validates it still points at a real
/// checkout by requiring the solution marker file to actually exist there - VSIX deployment copies
/// this assembly out of the repo, so a runtime "walk up from my own location" search (the pattern
/// used by Extension/Program.cs's ResolveModelPath) cannot work here. Shared by
/// <see cref="OpenViewerCommand"/> and <see cref="ViewerHwndHost"/> so both launch the viewer the
/// same way.
/// </summary>
internal static class RepoLocator
{
    internal const string RepoMarkerFileName = "CSharpGameEngine.slnx";

    public static string? ResolveRepoRoot()
    {
        string? RepoRoot = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "CSharpGameEngineRepoRoot")
            ?.Value;

        if (string.IsNullOrEmpty(RepoRoot))
        {
            return null;
        }

        return File.Exists(Path.Combine(RepoRoot, RepoMarkerFileName)) ? RepoRoot : null;
    }
}
