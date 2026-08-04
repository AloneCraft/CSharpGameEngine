using CSharpGameEngine.Scene;

namespace CSharpGameEngine.Scene.Tests;

/// <summary>
/// Loads the actual <c>UserGameProject/DefaultScene.scene</c> shipped in the repo, so schema drift
/// between it and <see cref="SceneGraph"/> is caught by CI rather than only by the VSIX at runtime.
/// Deliberately does NOT assert an exact total node count (ADR-0021/ADR-0022): the whole point of
/// the shape palette and drag-drop placement features is that this file gets nodes appended to it
/// by real usage (e.g. via a live VS session), so a rigid count would fail the moment someone
/// actually uses the feature against this file - only the two original seed nodes are asserted.
/// </summary>
public class DefaultSceneFileTests
{
    private static string ResolvePath()
    {
        // tests/CSharpGameEngine.Scene.Tests/bin/Debug/net10.0 -> repo root is 5 levels up.
        string directory = AppContext.BaseDirectory;
        string repoRoot = Path.GetFullPath(Path.Combine(directory, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "UserGameProject", "DefaultScene.scene");
    }

    [Fact]
    public void Load_ShippedDefaultScene_HasRootAndTwoCubeChildrenAmongItsNodes()
    {
        string path = ResolvePath();
        Assert.True(File.Exists(path), $"DefaultScene.scene not found at {path}");

        SceneGraph scene = SceneGraph.Load(path);

        SceneNodeTree tree = scene.BuildTree();
        SceneNodeTreeItem root = Assert.Single(tree.Roots);
        Assert.Equal("Root", root.Node.Name);
        Assert.Contains(root.Children, c => c.Node.Name == "Cube_A" && c.Node.MeshAssetPath == "Assets/DefaultScene/Box.glb");
        Assert.Contains(root.Children, c => c.Node.Name == "Cube_B" && c.Node.MeshAssetPath == "Assets/DefaultScene/Box.glb");
    }
}
