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
public class SceneRuntimeInvokerTests {
    [Fact]
    public void Invoke_TestScene_ProducesRootMainCameraPlayerWithCorrectValues(){
        var AssemblyPath=FindOwnAssembly();

        SceneRuntimeInvoker.InvocationResult Result=SceneRuntimeInvoker.Invoke(AssemblyPath,"TestScene");

        Assert.Equal("CSharpGameEngine.SceneAuthoring.Cli.Tests.TestScene",Result.SceneClassFullName);

        IReadOnlyList<ScenePreviewNode>Nodes=WorldInspector.Inspect(Result.Snapshot);
        Assert.Equal(3,Nodes.Count);

        ScenePreviewNode Root=Assert.Single(Nodes,N=>N.EntityId==Result.RootEntityId);
        Assert.Null(Root.ParentEntityId);
        Assert.Contains("SceneNodeTag",Root.ComponentTypeNames);

        ScenePreviewNode MainCamera=Assert.Single(Nodes,N=>N.ComponentTypeNames.Contains("Scale"));
        Assert.Equal(Root.EntityId,MainCamera.ParentEntityId);
        Assert.Equal([0f,2f,-10f],MainCamera.Position);
        Assert.Equal([0f,0f,0f,1f],MainCamera.Rotation);
        Assert.Equal([1f,1f,1f],MainCamera.Scale);

        ScenePreviewNode Player=Assert.Single(Nodes,N=>N.ComponentTypeNames.Contains("Health"));
        Assert.Equal(Root.EntityId,Player.ParentEntityId);
        Assert.Equal([1f,0f,3f],Player.Position);
    }

    [Fact]
    public void Invoke_UnknownClassHint_StillFindsTheSoleSceneDefinitionType(){
        SceneRuntimeInvoker.InvocationResult Result=SceneRuntimeInvoker.Invoke(FindOwnAssembly(),"NoSuchHint");

        Assert.Equal("CSharpGameEngine.SceneAuthoring.Cli.Tests.TestScene",Result.SceneClassFullName);
    }

    [Fact]
    public void Invoke_CalledTwiceInSameProcess_BothSucceedIndependently(){
        var AssemblyPath=FindOwnAssembly();

        SceneRuntimeInvoker.InvocationResult First=SceneRuntimeInvoker.Invoke(AssemblyPath,"TestScene");
        SceneRuntimeInvoker.InvocationResult Second=SceneRuntimeInvoker.Invoke(AssemblyPath,"TestScene");

        Assert.Equal(3,WorldInspector.Inspect(First.Snapshot).Count);
        Assert.Equal(3,WorldInspector.Inspect(Second.Snapshot).Count);
    }

    /// <summary>
    /// 対象は「別のALCへ改めて読み込まれた、このテストアセンブリ自身」。ロード済みのものを流用せず、
    /// 必ずファイルパスから読み直すのが要点(実運用でも対象は常に外部のdll)。
    /// </summary>
    internal static string FindOwnAssembly()=>typeof(SceneRuntimeInvokerTests).Assembly.Location;
}
