namespace CSharpGameEngine.SceneAuthoring.Cli.Tests;

/// <summary>
/// Locates another project's already-built assembly under the repo tree from a running test
/// (walking up from <see cref="AppContext.BaseDirectory"/> to the <c>CSharpGameEngine.slnx</c>
/// marker, mirroring <c>BridgeExecutableLocator</c>'s own "newest by write time across
/// Debug/Release" tie-break). Used by <see cref="SceneRuntimeInvokerTests"/> to point at
/// <c>UserGameProject</c>'s real build output - the project reference with
/// <c>ReferenceOutputAssembly="false"</c> in this test project's csproj guarantees that dll exists
/// by the time tests run.
///
/// <para>
/// TFM/RIDのサブディレクトリ名(<c>net10.0-windows10.0.19041.0/win-x64</c>のような)はプロジェクト
/// ごとに異なるため決め打ちにせず、<c>bin/{Configuration}</c>配下を再帰探索して最新のものを選ぶ。
/// </para>
/// </summary>
internal static class RepoBuildOutputLocator {
    internal static string FindProjectAssembly(string ProjectRelativeDirectory,string AssemblyFileName){
        var RepoRoot=FindRepoRoot();
        var BinRoot=Path.Combine(RepoRoot,ProjectRelativeDirectory,"bin");

        if(!Directory.Exists(BinRoot)){
            throw new InvalidOperationException(
                $"'{BinRoot}' が存在しません。'dotnet build {ProjectRelativeDirectory}' を先に実行してください。");
        }

        var Found=Directory
            .EnumerateFiles(BinRoot,AssemblyFileName,SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return Found??throw new InvalidOperationException(
            $"'{AssemblyFileName}' が '{BinRoot}' 配下に見つかりませんでした。'dotnet build {ProjectRelativeDirectory}' を先に実行してください。");
    }

    internal static string FindRepoRoot(){
        DirectoryInfo?directory=new(AppContext.BaseDirectory);

        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"CSharpGameEngine.slnx"))){
            directory=directory.Parent;
        }

        return directory?.FullName
            ??throw new InvalidOperationException($"'CSharpGameEngine.slnx' を辿って探索しましたが、リポジトリルートが見つかりませんでした(起点: {AppContext.BaseDirectory})。");
    }
}
