using System;
using System.IO;
using System.Linq;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Loads the actual <c>UserGameProject/DefaultScene.scene</c> shipped in the repo, so schema drift
/// between it and the reader is caught by CI rather than only by the VSIX at runtime.
///
/// <para>
/// ADR-0034でこのテストは<c>tests/CSharpGameEngine.Scene.Tests</c>から移設された。旧
/// <c>src/CSharpGameEngine.Scene</c>(JSON方式Sceneの初期実装)は生きた利用者が無く削除されたが、
/// このテストだけは対象が「削除される型」ではなく「出荷される<c>.scene</c>ファイル」だったため、
/// 唯一の読み手として残るVSIX側の<see cref="SceneFile"/>に対する検証として引き継いだ。
/// </para>
///
/// Deliberately does NOT assert an exact total node count (ADR-0021/ADR-0022): the whole point of
/// the shape palette and drag-drop placement features is that this file gets nodes appended to it
/// by real usage (e.g. via a live VS session), so a rigid count would fail the moment someone
/// actually uses the feature against this file - only the two original seed nodes are asserted.
/// </summary>
public sealed class DefaultSceneFileTests
{
    [Fact]
    public void Load_ShippedDefaultScene_HasRootAndTwoCubeChildrenAmongItsNodes()
    {
        string path = Path.Combine(FindRepoRoot(), "UserGameProject", "DefaultScene.scene");
        Assert.True(File.Exists(path), $"DefaultScene.scene not found at {path}");

        SceneFile Scene = SceneFile.Load(path);

        SceneFileNode Root = Assert.Single(Scene.Nodes, N => N.ParentId is null);
        Assert.Equal("Root", Root.Name);

        Assert.Contains(Scene.Nodes, N => N.ParentId == Root.Id && N.Name == "Cube_A" && N.MeshAssetPath == "Assets/DefaultScene/Box.glb");
        Assert.Contains(Scene.Nodes, N => N.ParentId == Root.Id && N.Name == "Cube_B" && N.MeshAssetPath == "Assets/DefaultScene/Box.glb");
    }

    /// <summary>
    /// リポジトリルートは<c>CSharpGameEngine.slnx</c>を辿って探す(相対階層の決め打ちにすると
    /// TFM/RIDのサブディレクトリ差で壊れるため)。<see cref="ExternalScenePreviewLoaderTests"/>と同じ方式。
    /// </summary>
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
}
