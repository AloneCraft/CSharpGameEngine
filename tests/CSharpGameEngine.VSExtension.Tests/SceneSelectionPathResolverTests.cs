using System;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0040: 「今の選択」からソースマーカー用のNodePath集合を解決する純粋関数。ここが崩れると、
/// 編集確定後の再解析着地のたびにソースマーカーが無関係な位置へ再スクロールしてしまう
/// (実機で報告された不具合そのもの)。
/// </summary>
public sealed class SceneSelectionPathResolverTests {
    private const string Source="""
        using CSharpGameEngine.Entities;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() { Children=[
                new Node { Name="Cube" },
                new Node { Name="Sphere" },
            ] };
        }
        """;

    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");
    private static readonly SceneKey OtherKey=new("project-a","global::Game.OtherScene");

    [Fact]
    public void Resolve_NoSelection_ReturnsEmpty(){
        SceneProjection Projection=Analyze();

        Assert.Empty(SceneSelectionPathResolver.Resolve(Selection:null,Key,Projection));
    }

    [Fact]
    public void Resolve_NoProjection_ReturnsEmpty(){
        SceneSelection Selection=new(
            Key,[new EditorNodeId(1)],PrimaryNodeId:null,ComponentType:null,MemberName:null,
            SceneSelectionOrigin.Viewport,Revision:1,CorrelationId:Guid.NewGuid());

        Assert.Empty(SceneSelectionPathResolver.Resolve(Selection,Key,Projection:null));
    }

    [Fact]
    public void Resolve_SceneKeyMismatch_ReturnsEmpty(){
        SceneProjection Projection=Analyze();
        EditorNodeId Cube=FindId(Projection,"Cube");
        // Selectionは別Sceneを切替後の残骸(SelectionのKeyとpin中のKeyが食い違う)を模す。
        SceneSelection Selection=new(
            OtherKey,[Cube],PrimaryNodeId:Cube,ComponentType:null,MemberName:null,
            SceneSelectionOrigin.Viewport,Revision:1,CorrelationId:Guid.NewGuid());

        Assert.Empty(SceneSelectionPathResolver.Resolve(Selection,Key,Projection));
    }

    [Fact]
    public void Resolve_ResolvableNodeIds_ReturnsMatchingPaths(){
        SceneProjection Projection=Analyze();
        EditorNodeId Cube=FindId(Projection,"Cube");
        SceneSelection Selection=new(
            Key,[Cube],PrimaryNodeId:Cube,ComponentType:null,MemberName:null,
            SceneSelectionOrigin.Viewport,Revision:1,CorrelationId:Guid.NewGuid());

        IReadOnlyList<NodePath>Paths=SceneSelectionPathResolver.Resolve(Selection,Key,Projection);

        NodePath Expected=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Path;
        Assert.Equal(Expected,Assert.Single(Paths));
    }

    [Fact]
    public void Resolve_UnresolvableNodeId_IsSkippedNotFailed(){
        SceneProjection Projection=Analyze();
        EditorNodeId Cube=FindId(Projection,"Cube");
        EditorNodeId Unknown=new(999);
        SceneSelection Selection=new(
            Key,[Cube,Unknown],PrimaryNodeId:Cube,ComponentType:null,MemberName:null,
            SceneSelectionOrigin.Viewport,Revision:1,CorrelationId:Guid.NewGuid());

        IReadOnlyList<NodePath>Paths=SceneSelectionPathResolver.Resolve(Selection,Key,Projection);

        Assert.Single(Paths);
    }

    [Fact]
    public void Resolve_MultipleSelection_PreservesDeclarationOrder(){
        SceneProjection Projection=Analyze();
        EditorNodeId Cube=FindId(Projection,"Cube");
        EditorNodeId Sphere=FindId(Projection,"Sphere");
        // NodeIdsの並びを宣言順とは逆にしても、Resolveは渡された順そのままを返す
        // (並べ替えはSceneSourceMarkerPlanner側の責務のため、ここでは入力順を尊重するだけでよい)。
        SceneSelection Selection=new(
            Key,[Sphere,Cube],PrimaryNodeId:Sphere,ComponentType:null,MemberName:null,
            SceneSelectionOrigin.Viewport,Revision:1,CorrelationId:Guid.NewGuid());

        IReadOnlyList<NodePath>Paths=SceneSelectionPathResolver.Resolve(Selection,Key,Projection);

        Assert.Equal(2,Paths.Count);
        Assert.Equal(FindPath(Projection,"Sphere"),Paths[0]);
        Assert.Equal(FindPath(Projection,"Cube"),Paths[1]);
    }

    private static SceneProjection Analyze(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze(Source);
        return new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
    }

    private static EditorNodeId FindId(SceneProjection Projection,string NodeName)=>
        Assert.Single(Projection.Nodes,Node=>Node.Node.Name==NodeName).Id;

    private static NodePath FindPath(SceneProjection Projection,string NodeName)=>
        Assert.Single(Projection.Nodes,Node=>Node.Node.Name==NodeName).Path;
}
