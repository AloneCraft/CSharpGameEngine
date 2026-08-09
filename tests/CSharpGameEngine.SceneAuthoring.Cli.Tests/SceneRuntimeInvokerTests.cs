using System.Reflection;

namespace CSharpGameEngine.SceneAuthoring.Cli.Tests;

/// <summary>
/// Exercises <see cref="SceneRuntimeInvoker"/>+<see cref="WorldInspector"/> together against a real,
/// already-built assembly - the riskiest part of the whole feature (AssemblyLoadContext + reflection),
/// verified against a known-good fixture (ADR-0031).
///
/// <para>
/// 対象はこのテストアセンブリ自身(<see cref="TestScene"/>)。ADR-0033で
/// <c>CSharpGameEngine.Entities</c>が共有バイナリでなくなり、ブリッジは対象の<c>World</c>/
/// <c>Entity</c>型をコンパイル時に一切知らない前提になったため、「別アセンブリをリフレクションだけで
/// 扱える」ことがまさに検証したい性質になる。
/// </para>
/// </summary>
public class SceneRuntimeInvokerTests
{
    [Fact]
    public void Invoke_TestScene_ProducesRootMainCameraPlayerWithCorrectValues()
    {
        string assemblyPath = FindOwnAssembly();

        SceneRuntimeInvoker.InvocationResult result = SceneRuntimeInvoker.Invoke(assemblyPath, "TestScene");

        Assert.Equal("CSharpGameEngine.SceneAuthoring.Cli.Tests.TestScene", result.SceneClassFullName);

        IReadOnlyList<ScenePreviewNode> nodes = WorldInspector.Inspect(result.Snapshot);
        Assert.Equal(3, nodes.Count);

        ScenePreviewNode root = Assert.Single(nodes, n => n.EntityId == result.RootEntityId);
        Assert.Null(root.ParentEntityId);
        Assert.Contains("SceneNodeTag", root.ComponentTypeNames);

        ScenePreviewNode mainCamera = Assert.Single(nodes, n => n.ComponentTypeNames.Contains("Scale"));
        Assert.Equal(root.EntityId, mainCamera.ParentEntityId);
        Assert.Equal([0f, 2f, -10f], mainCamera.Position);
        Assert.Equal([0f, 0f, 0f, 1f], mainCamera.Rotation);
        Assert.Equal([1f, 1f, 1f], mainCamera.Scale);

        ScenePreviewNode player = Assert.Single(nodes, n => n.ComponentTypeNames.Contains("Health"));
        Assert.Equal(root.EntityId, player.ParentEntityId);
        Assert.Equal([1f, 0f, 3f], player.Position);
    }

    [Fact]
    public void Invoke_UnknownClassHint_StillFindsTheSoleSceneDefinitionType()
    {
        SceneRuntimeInvoker.InvocationResult result = SceneRuntimeInvoker.Invoke(FindOwnAssembly(), "NoSuchHint");

        Assert.Equal("CSharpGameEngine.SceneAuthoring.Cli.Tests.TestScene", result.SceneClassFullName);
    }

    [Fact]
    public void Invoke_CalledTwiceInSameProcess_BothSucceedIndependently()
    {
        string assemblyPath = FindOwnAssembly();

        SceneRuntimeInvoker.InvocationResult first = SceneRuntimeInvoker.Invoke(assemblyPath, "TestScene");
        SceneRuntimeInvoker.InvocationResult second = SceneRuntimeInvoker.Invoke(assemblyPath, "TestScene");

        Assert.Equal(3, WorldInspector.Inspect(first.Snapshot).Count);
        Assert.Equal(3, WorldInspector.Inspect(second.Snapshot).Count);
    }

    /// <summary>
    /// 対象は「別のALCへ改めて読み込まれた、このテストアセンブリ自身」。ロード済みのものを流用せず、
    /// 必ずファイルパスから読み直すのが要点(実運用でも対象は常に外部のdll)。
    /// </summary>
    internal static string FindOwnAssembly() => typeof(SceneRuntimeInvokerTests).Assembly.Location;
}
