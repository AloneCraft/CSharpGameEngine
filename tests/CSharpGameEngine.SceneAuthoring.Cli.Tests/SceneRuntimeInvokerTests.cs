namespace CSharpGameEngine.SceneAuthoring.Cli.Tests;

/// <summary>
/// Exercises <see cref="SceneRuntimeInvoker"/>+<see cref="WorldInspector"/> together against
/// <c>samples/SceneAuthoringDemo</c>'s real, already-built output (guaranteed present by this test
/// project's own <c>ReferenceOutputAssembly="false"</c> project reference) - the riskiest part of the
/// whole feature (AssemblyLoadContext + reflection), verified against a known-good fixture before any
/// VSIX code exists at all (ADR-0031).
/// </summary>
public class SceneRuntimeInvokerTests
{
    [Fact]
    public void Invoke_SceneAuthoringDemo_ProducesRootMainCameraPlayerWithCorrectValues()
    {
        string assemblyPath = FindSceneAuthoringDemoAssembly();

        SceneRuntimeInvoker.InvocationResult result = SceneRuntimeInvoker.Invoke(assemblyPath, "MainScene");

        Assert.Equal("SceneAuthoringDemo.MainScene", result.SceneClassFullName);

        IReadOnlyList<ScenePreviewNode> nodes = WorldInspector.Inspect(result.World);
        Assert.Equal(3, nodes.Count);

        ScenePreviewNode root = Assert.Single(nodes, n => n.EntityId == result.Root.Id);
        Assert.Null(root.ParentEntityId);
        Assert.Contains("SceneNodeTag", root.ComponentTypeNames);

        ScenePreviewNode mainCamera = Assert.Single(nodes, n => n.ComponentTypeNames.Contains("Camera"));
        Assert.Equal(root.EntityId, mainCamera.ParentEntityId);
        Assert.Equal([0f, 2f, -10f], mainCamera.Position);
        Assert.Equal([0f, 0f, 0f, 1f], mainCamera.Rotation);
        Assert.Equal([1f, 1f, 1f], mainCamera.Scale);

        ScenePreviewNode player = Assert.Single(nodes, n => n.ComponentTypeNames.Contains("PlayerTag"));
        Assert.Equal(root.EntityId, player.ParentEntityId);
        Assert.Equal([0f, 0f, 0f], player.Position);
        Assert.Contains("Health", player.ComponentTypeNames);
    }

    [Fact]
    public void Invoke_UnknownClassHint_StillFindsTheSoleSceneDefinitionType()
    {
        SceneRuntimeInvoker.InvocationResult result = SceneRuntimeInvoker.Invoke(FindSceneAuthoringDemoAssembly(), "NoSuchHint");

        Assert.Equal("SceneAuthoringDemo.MainScene", result.SceneClassFullName);
    }

    [Fact]
    public void Invoke_CalledTwiceInSameProcess_BothSucceedIndependently()
    {
        string assemblyPath = FindSceneAuthoringDemoAssembly();

        SceneRuntimeInvoker.InvocationResult first = SceneRuntimeInvoker.Invoke(assemblyPath, "MainScene");
        SceneRuntimeInvoker.InvocationResult second = SceneRuntimeInvoker.Invoke(assemblyPath, "MainScene");

        Assert.True(first.World.IsAlive(first.Root));
        Assert.True(second.World.IsAlive(second.Root));
    }

    internal static string FindSceneAuthoringDemoAssembly() =>
        RepoBuildOutputLocator.FindProjectAssembly(Path.Combine("samples", "SceneAuthoringDemo"), "SceneAuthoringDemo.dll");
}
