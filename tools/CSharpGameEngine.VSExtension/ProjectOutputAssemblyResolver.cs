using System;
using System.IO;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Resolves an <see cref="EnvDTE.Project"/>'s actual built output assembly path - TFM/RID-agnostic by
/// construction (reads the real evaluated MSBuild <c>OutputPath</c>/<c>OutputFileName</c> properties
/// rather than guessing a folder name), which matters because e.g.
/// <c>UserGameProject.csproj</c>'s non-default <c>net10.0-windows10.0.19041.0</c>+<c>win-x64</c> TFM
/// would defeat a folder-name-guessing approach like <see cref="BridgeExecutableLocator"/>'s
/// (ADR-0031). Falls back to a newest-by-write-time glob under <c>bin/</c> if the property read
/// fails for any reason. The EnvDTE-touching part (<see cref="TryResolve"/>) and the pure
/// path-combination/glob logic are deliberately split so the latter is unit-testable without a
/// running VS host, mirroring <see cref="BridgeExecutableLocator"/>'s own split.
/// </summary>
internal static class ProjectOutputAssemblyResolver
{
    internal static string? TryResolve(Project project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string? projectDirectory = Path.GetDirectoryName(project.FullName);
        if (string.IsNullOrEmpty(projectDirectory))
        {
            return null;
        }

        string? viaProperties = TryResolveViaProperties(project, projectDirectory);
        if (viaProperties is not null && File.Exists(viaProperties))
        {
            return viaProperties;
        }

        // None of this repo's projects set an explicit <AssemblyName> (same assumption
        // BridgeExecutableLocator already documents), so the output dll name always matches the
        // project file's own base name.
        string assemblyFileName = Path.GetFileNameWithoutExtension(project.FullName) + ".dll";
        return FindNewestByGlob(projectDirectory, assemblyFileName);
    }

    private static string? TryResolveViaProperties(Project project, string projectDirectory)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            string outputFileName = (string)project.Properties.Item("OutputFileName").Value;
            string outputPath = (string)project.ConfigurationManager.ActiveConfiguration.Properties.Item("OutputPath").Value;
            return CombineOutputPath(projectDirectory, outputPath, outputFileName);
        }
        catch (Exception)
        {
            // Property read can fail for reasons unrelated to whether the project is buildable
            // (project not yet loaded, non-standard project system, etc.) - the glob fallback in
            // TryResolve covers for it.
            return null;
        }
    }

    internal static string CombineOutputPath(string projectDirectory, string outputPath, string outputFileName) =>
        Path.GetFullPath(Path.Combine(projectDirectory, outputPath, outputFileName));

    internal static string? FindNewestByGlob(string projectDirectory, string assemblyFileName)
    {
        string binDirectory = Path.Combine(projectDirectory, "bin");

        if (!Directory.Exists(binDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(binDirectory, assemblyFileName, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
