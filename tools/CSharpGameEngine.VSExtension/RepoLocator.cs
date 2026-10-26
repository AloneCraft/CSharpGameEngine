using System.IO;
using System.Linq;
using System.Reflection;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// ビルド時に埋め込まれたリポジトリルート（<c>CSharpGameEngineRepoRoot</c> assembly metadata。<c>$(MSBuildThisFileDirectory)</c> から設定）を解決し、ソリューション marker file が実在することを確認して checkout が有効か検証します。VSIX の配置時には assembly がリポジトリ外へコピーされるため、自身の場所から上位ディレクトリをたどる実行時検索（Extension/Program.cs の ResolveModelPath が使う方式）は使えません。<see cref="OpenViewerCommand"/> と <see cref="ViewerHwndHost"/> で共有し、viewer を同じ方法で起動します。
/// </summary>
internal static class RepoLocator {
    internal const string RepoMarkerFileName="CSharpGameEngine.slnx";

    public static string?ResolveRepoRoot(){
        var RepoRoot=Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(Attribute=>Attribute.Key=="CSharpGameEngineRepoRoot")
            ?.Value;

        if(string.IsNullOrEmpty(RepoRoot)){
            return null;
        }

        return File.Exists(Path.Combine(RepoRoot,RepoMarkerFileName))?RepoRoot:null;
    }
}
