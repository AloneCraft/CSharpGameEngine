using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Identifies the exact VSIX assembly currently loaded by Visual Studio.
/// The values are assembly metadata so they remain truthful after VSIX deployment copies the
/// assembly outside the repository checkout.
/// </summary>
internal static class VsixBuildIdentity {
    private const string VersionKey="CSharpGameEngineVsixVersion";
    private const string BuildUtcKey="CSharpGameEngineBuildUtc";
    private const string GitRevisionKey="CSharpGameEngineGitRevision";
    private const string BuildOutputPathKey="CSharpGameEngineBuildOutputPath";

    internal static string Version=>GetMetadata(VersionKey)??"unknown";
    internal static string BuildUtc=>GetMetadata(BuildUtcKey)??"unknown";
    internal static string GitRevision=>GetMetadata(GitRevisionKey)??"unknown";
    internal static string LoadedAssemblyPath=>Assembly.GetExecutingAssembly().Location;
    internal static string LoadedAssemblyFileName=>Path.GetFileName(LoadedAssemblyPath);
    internal static string BuildOutputPath=>GetMetadata(BuildOutputPathKey)??"unknown";

    /// <summary>Compact text intended for the Scene Designer header or diagnostic UI.</summary>
    internal static string DisplayText=>$"VSIX {Version} | {GitRevision} | {BuildUtc}";

    internal static string ActivityLogText=>
        $"Loaded {DisplayText}; assembly='{LoadedAssemblyPath}'; buildOutput='{BuildOutputPath}'.";

    private static string?GetMetadata(string Key)=>Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(Attribute=>string.Equals(Attribute.Key,Key,StringComparison.Ordinal))
        ?.Value;
}
