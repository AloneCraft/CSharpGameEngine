using System;
using System.IO;
using System.Linq;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Spawns the real <c>CSharpGameEngine.SceneAuthoring.Cli</c> bridge subprocess against
/// <c>samples/SceneAuthoringDemo</c>'s real, already-built output (ADR-0031) - no VS host needed,
/// same "exercise the real out-of-process bridge from a plain test" pattern already used for the
/// glTF/FBX bridges elsewhere in this project. Skips (rather than fails) if that sample has not been
/// built yet, since building it is this test's responsibility to request, not silently assume.
/// </summary>
public sealed class ExternalScenePreviewLoaderTests
{
    [Fact]
    public void Load_SceneAuthoringDemo_ReturnsRootMainCameraPlayer()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "samples", "SceneAuthoringDemo", "MainScene.cs");
        string? assemblyPath = FindNewestAssembly(Path.Combine(repoRoot, "samples", "SceneAuthoringDemo", "bin"), "SceneAuthoringDemo.dll");

        if (assemblyPath is null)
        {
            throw new InvalidOperationException(
                "SceneAuthoringDemo.dll が見つかりませんでした。'dotnet build samples/SceneAuthoringDemo' を先に実行してください。");
        }

        ScenePreviewBridgeDocument document = ExternalScenePreviewLoader.Load(sourcePath, assemblyPath, repoRoot);

        Assert.True(document.Success);
        Assert.Equal("SceneAuthoringDemo.MainScene", document.SceneClassFullName);
        Assert.Equal(3, document.Nodes.Length);
        Assert.Contains(document.Nodes, n => n.Name == "Root" && n.ParentEntityId == null);
        Assert.Contains(document.Nodes, n => n.Name == "MainCamera" && n.ComponentTypeNames.Contains("Camera"));
        Assert.Contains(document.Nodes, n => n.Name == "Player" && n.ComponentTypeNames.Contains("PlayerTag"));
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
