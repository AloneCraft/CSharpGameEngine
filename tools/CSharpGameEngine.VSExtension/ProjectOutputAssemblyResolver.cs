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

        string? ProjectDirectory = Path.GetDirectoryName(project.FullName);
        if (string.IsNullOrEmpty(ProjectDirectory))
        {
            return null;
        }

        string? ViaProperties = TryResolveViaProperties(project, ProjectDirectory);
        if (ViaProperties is not null && File.Exists(ViaProperties))
        {
            return ViaProperties;
        }

        // None of this repo's projects set an explicit <AssemblyName> (same assumption
        // BridgeExecutableLocator already documents), so the output dll name always matches the
        // project file's own base name.
        string AssemblyFileName = Path.GetFileNameWithoutExtension(project.FullName) + ".dll";
        return FindNewestByGlob(ProjectDirectory, AssemblyFileName);
    }

    private static string? TryResolveViaProperties(Project project, string ProjectDirectory)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            string OutputFileName = (string)project.Properties.Item("OutputFileName").Value;
            string OutputPath = (string)project.ConfigurationManager.ActiveConfiguration.Properties.Item("OutputPath").Value;
            return CombineOutputPath(ProjectDirectory, OutputPath, OutputFileName);
        }
        catch (Exception)
        {
            // Property read can fail for reasons unrelated to whether the project is buildable
            // (project not yet loaded, non-standard project system, etc.) - the glob fallback in
            // TryResolve covers for it.
            return null;
        }
    }

    internal static string CombineOutputPath(string ProjectDirectory, string OutputPath, string OutputFileName) =>
        Path.GetFullPath(Path.Combine(ProjectDirectory, OutputPath, OutputFileName));

    internal static string? FindNewestByGlob(string ProjectDirectory, string AssemblyFileName)
    {
        string BinDirectory = Path.Combine(ProjectDirectory, "bin");

        if (!Directory.Exists(BinDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(BinDirectory, AssemblyFileName, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
