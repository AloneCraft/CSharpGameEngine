using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Visual Studio が現在読み込んでいる正確な VSIX アセンブリを識別する。値はアセンブリメタデータであるため、VSIX の配置でリポジトリのチェックアウト外にコピーされた後も正確さを保つ。
/// </summary>
internal static class VsixBuildIdentity {
    private const string VersionKey="CSharpGameEngineVsixVersion";
    private const string RepoRootKey="CSharpGameEngineRepoRoot";
    private const string BuildUtcKey="CSharpGameEngineBuildUtc";
    private const string GitRevisionKey="CSharpGameEngineGitRevision";
    private const string BuildOutputPathKey="CSharpGameEngineBuildOutputPath";

    internal static string Version=>GetMetadata(VersionKey)??"unknown";
    internal static string RepoRoot=>GetMetadata(RepoRootKey)??"unknown";
    internal static string BuildUtc=>GetMetadata(BuildUtcKey)??"unknown";
    internal static string GitRevision=>GetMetadata(GitRevisionKey)??"unknown";
    internal static string LoadedAssemblyPath=>Assembly.GetExecutingAssembly().Location;
    internal static string LoadedAssemblyFileName=>Path.GetFileName(LoadedAssemblyPath);
    internal static string LoadedAssemblyPathTail{
        get {
            string? Directory=Path.GetDirectoryName(LoadedAssemblyPath);
            string? DirectoryName=string.IsNullOrEmpty(Directory)?null:Path.GetFileName(Directory);
            return string.IsNullOrEmpty(DirectoryName)
                ?LoadedAssemblyFileName
                :$"...\\{DirectoryName}\\{LoadedAssemblyFileName}";
        }
    }
    internal static string BuildOutputPath=>GetMetadata(BuildOutputPathKey)??"unknown";
    internal static VsixDeploymentVerification Deployment=>VsixDeploymentVerifier.Verify(
        LoadedAssemblyPath,BuildOutputPath);

    /// <summary>埋め込み UTC ビルドメタデータを、コンパクトな Scene View ヘッダー用に書式化する。</summary>
    internal static string FormatBuildUtc(string Value){
        return DateTimeOffset.TryParse(Value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out DateTimeOffset Parsed)
            ?Parsed.ToUniversalTime().ToString("yyyy-MM-dd HH:mm'Z'",CultureInfo.InvariantCulture)
            :"unknown";
    }

    /// <summary>Scene Designer のヘッダーまたは診断 UI 用のコンパクトなテキスト。</summary>
    internal static string DisplayText{
        get {
            VsixDeploymentVerification Verification=Deployment;
            return $"VSIX {Version} | build {FormatBuildUtc(BuildUtc)} | {Verification.CompactState}";
        }
    }

    internal static string ToolTipText=>VsixText.Format("VsixBuildIdentityToolTip",
        VsixLocalization.UICulture.Name,GitRevision,Deployment.Message,LoadedAssemblyPath,BuildOutputPath,
        Deployment.LoadedHash??"unknown",Deployment.BuildHash??"unknown");

    internal static string ActivityLogText=>
        $"Loaded {DisplayText}; uiCulture='{VsixLocalization.UICulture.Name}'; revision='{GitRevision}'; " +
        $"assembly='{LoadedAssemblyPath}'; buildOutput='{BuildOutputPath}'; deployment='{Deployment.Message}'; " +
        $"loadedSha256='{Deployment.LoadedHash??"unknown"}'; buildSha256='{Deployment.BuildHash??"unknown"}'.";

    private static string?GetMetadata(string Key)=>Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(Attribute=>string.Equals(Attribute.Key,Key,StringComparison.Ordinal))
        ?.Value;
}
