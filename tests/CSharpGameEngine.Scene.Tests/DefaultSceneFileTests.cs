using CSharpGameEngine.Scene;

namespace CSharpGameEngine.Scene.Tests;

/// <summary>Loads the actual <c>UserGameProject/DefaultScene.scene.json</c> shipped in the repo, so schema drift between it and <see cref="SceneGraph"/> is caught by CI rather than only by the VSIX at runtime.</summary>
public class DefaultSceneFileTests
{
    private static string ResolvePath()
    {
        // tests/CSharpGameEngine.Scene.Tests/bin/Debug/net10.0 -> repo root is 5 levels up.
        string directory = AppContext.BaseDirectory;
        string repoRoot = Path.GetFullPath(Path.Combine(directory, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "UserGameProject", "DefaultScene.scene.json");
    }

    [Fact]
    public void Load_ShippedDefaultScene_HasRootAndTwoCubeChildren()
    {
        string path = ResolvePath();
        Assert.True(File.Exists(path), $"DefaultScene.scene.json not found at {path}");

        SceneGraph scene = SceneGraph.Load(path);
        Assert.Equal(3, scene.Nodes.Count);

        SceneNodeTree tree = scene.BuildTree();
        SceneNodeTreeItem root = Assert.Single(tree.Roots);
        Assert.Equal("Root", root.Node.Name);
        Assert.Equal(2, root.Children.Count);
        Assert.Contains(root.Children, c => c.Node.Name == "Cube_A" && c.Node.MeshAssetPath == "Assets/DefaultScene/Box.glb");
        Assert.Contains(root.Children, c => c.Node.Name == "Cube_B" && c.Node.MeshAssetPath == "Assets/DefaultScene/Box.glb");
    }
}
