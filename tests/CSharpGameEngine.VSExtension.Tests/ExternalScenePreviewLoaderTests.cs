using System;
using System.IO;
using System.Linq;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Spawns the real <c>CSharpGameEngine.SceneAuthoring.Cli</c> bridge subprocess against a real,
/// already-built assembly containing a <c>[SceneDefinition]</c> class (ADR-0031) - no VS host needed,
/// same "exercise the real out-of-process bridge from a plain test" pattern already used for the
/// glTF/FBX bridges elsewhere in this project.
///
/// <para>
/// 対象は<c>CSharpGameEngine.SceneAuthoring.Cli.Tests</c>アセンブリ(<c>TestScene</c>を含む)。
/// ADR-0033で<c>samples/SceneAuthoringDemo</c>が削除されたことに伴う変更で、フィクスチャは
/// ブリッジ側のテストと共用している。
/// </para>
/// </summary>
public sealed class ExternalScenePreviewLoaderTests {
    [Fact]
    public void Load_TestScene_ReturnsRootMainCameraPlayer(){
        var RepoRoot=FindRepoRoot();
        var FixtureDirectory=Path.Combine(RepoRoot,"tests","CSharpGameEngine.SceneAuthoring.Cli.Tests");
        var SourcePath=Path.Combine(FixtureDirectory,"TestScene.cs");
        var AssemblyPath=FindNewestAssembly(Path.Combine(FixtureDirectory,"bin"),"CSharpGameEngine.SceneAuthoring.Cli.Tests.dll");

        if(AssemblyPath is null){
            throw new InvalidOperationException(
                "CSharpGameEngine.SceneAuthoring.Cli.Tests.dll が見つかりませんでした。'dotnet build tests/CSharpGameEngine.SceneAuthoring.Cli.Tests' を先に実行してください。");
        }

        ScenePreviewBridgeDocument Document=ExternalScenePreviewLoader.Load(SourcePath,AssemblyPath,RepoRoot);

        Assert.True(Document.Success,Document.ErrorMessage);
        Assert.Equal("CSharpGameEngine.SceneAuthoring.Cli.Tests.TestScene",Document.SceneClassFullName);
        Assert.Equal(3,Document.Nodes.Length);
        Assert.Contains(Document.Nodes,N=>N.Name=="Root"&&N.ParentEntityId==null);
        Assert.Contains(Document.Nodes,N=>N.Name=="MainCamera"&&N.ComponentTypeNames.Contains("Scale"));
        Assert.Contains(Document.Nodes,N=>N.Name=="Player"&&N.ComponentTypeNames.Contains("Health"));
    }

    private static string FindRepoRoot(){
        DirectoryInfo?directory=new(AppContext.BaseDirectory);

        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"CSharpGameEngine.slnx"))){
            directory=directory.Parent;
        }

        return directory?.FullName
            ??throw new InvalidOperationException($"'CSharpGameEngine.slnx' を辿って探索しましたが、リポジトリルートが見つかりませんでした(起点: {AppContext.BaseDirectory})。");
    }

    private static string?FindNewestAssembly(string BinRoot,string AssemblyFileName){
        if(!Directory.Exists(BinRoot)){
            return null;
        }

        return Directory.EnumerateFiles(BinRoot,AssemblyFileName,SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
