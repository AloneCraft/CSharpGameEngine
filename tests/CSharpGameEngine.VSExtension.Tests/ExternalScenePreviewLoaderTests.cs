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
public sealed class ExternalScenePreviewLoaderTests
{
    [Fact]
    public void Load_TestScene_ReturnsRootMainCameraPlayer()
    {
        string repoRoot = FindRepoRoot();
        string fixtureDirectory = Path.Combine(repoRoot, "tests", "CSharpGameEngine.SceneAuthoring.Cli.Tests");
        string sourcePath = Path.Combine(fixtureDirectory, "TestScene.cs");
        string? assemblyPath = FindNewestAssembly(Path.Combine(fixtureDirectory, "bin"), "CSharpGameEngine.SceneAuthoring.Cli.Tests.dll");

        if (assemblyPath is null)
        {
            throw new InvalidOperationException(
                "CSharpGameEngine.SceneAuthoring.Cli.Tests.dll が見つかりませんでした。'dotnet build tests/CSharpGameEngine.SceneAuthoring.Cli.Tests' を先に実行してください。");
        }

        ScenePreviewBridgeDocument document = ExternalScenePreviewLoader.Load(sourcePath, assemblyPath, repoRoot);

        Assert.True(document.Success, document.ErrorMessage);
        Assert.Equal("CSharpGameEngine.SceneAuthoring.Cli.Tests.TestScene", document.SceneClassFullName);
        Assert.Equal(3, document.Nodes.Length);
        Assert.Contains(document.Nodes, n => n.Name == "Root" && n.ParentEntityId == null);
        Assert.Contains(document.Nodes, n => n.Name == "MainCamera" && n.ComponentTypeNames.Contains("Scale"));
        Assert.Contains(document.Nodes, n => n.Name == "Player" && n.ComponentTypeNames.Contains("Health"));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpGameEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"'CSharpGameEngine.slnx' を辿って探索しましたが、リポジトリルートが見つかりませんでした(起点: {AppContext.BaseDirectory})。");
    }

    private static string? FindNewestAssembly(string binRoot, string assemblyFileName)
    {
        if (!Directory.Exists(binRoot))
        {
            return null;
        }

        return Directory.EnumerateFiles(binRoot, assemblyFileName, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
