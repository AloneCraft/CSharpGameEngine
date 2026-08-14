using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// SceneModel の解析同一性(NodePath)から Designer session の安定 ID への引き継ぎ規則を固定する。
/// UI や Visual Studio host に依存せず、live な編集列を模した再解析の連続で検証する。
/// </summary>
public sealed class SceneEditorContractsTests {
    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");

    [Fact]
    public void Reconcile_PreservesUniqueNamedSiblingIdsAcrossReorder(){
        SceneProjectionReconciler Reconciler=new();
        SceneProjection First=Reconciler.Reconcile(Key,Analyze("A","B"),Previous:null);
        EditorNodeId A=IdForName(First,"A");
        EditorNodeId B=IdForName(First,"B");

        SceneProjection Reordered=Reconciler.Reconcile(Key,Analyze("B","A"),First);

        Assert.Equal(A,IdForName(Reordered,"A"));
        Assert.Equal(B,IdForName(Reordered,"B"));
        Assert.Equal(NodePath.Root.Append(1),NodeForName(Reordered,"A").Path);
    }

    [Fact]
    public void Reconcile_PreservesIdForAnUnambiguousRename(){
        SceneProjectionReconciler Reconciler=new();
        SceneProjection First=Reconciler.Reconcile(Key,Analyze("Before",TransformX:1f),Previous:null);
        EditorNodeId Before=IdForName(First,"Before");

        SceneProjection Renamed=Reconciler.Reconcile(Key,Analyze("After",TransformX:1f),First);

        Assert.Equal(Before,IdForName(Renamed,"After"));
    }

    [Fact]
    public void Reconcile_DoesNotGuessAnIdentityForIndistinguishableSiblings(){
        const string Source="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node(),new Node()] };
            }
            """;
        SceneProjectionReconciler Reconciler=new();
        SceneProjection First=Reconciler.Reconcile(Key,SceneAnalysisTestHelper.Analyze(Source),Previous:null);
        HashSet<EditorNodeId>OldChildren=First.Nodes.Where(static Node=>Node.ParentId is not null).Select(static Node=>Node.Id).ToHashSet();

        SceneProjection Reparsed=Reconciler.Reconcile(Key,SceneAnalysisTestHelper.Analyze(Source),First);

        Assert.Empty(Reparsed.Nodes.Where(static Node=>Node.ParentId is not null).Select(static Node=>Node.Id).Intersect(OldChildren));
    }

    [Fact]
    public void Diff_ReportsMemberChangeWithoutPretendingTheMeshChanged(){
        SceneProjectionReconciler Reconciler=new();
        SceneProjection Before=Reconciler.Reconcile(Key,Analyze("Cube",TransformX:1f),Previous:null);
        SceneProjection After=Reconciler.Reconcile(Key,Analyze("Cube",TransformX:2f),Before);

        SceneDiff Diff=SceneDiffEngine.Compare(Before,After);
        EditorNodeId Cube=IdForName(After,"Cube");

        Assert.Contains(Diff.Entries,Entry=>Entry.Kind==SceneDiffKind.MemberChanged
            &&Entry.NodeId.Equals(Cube)
            &&Entry.ComponentType=="global::Sample.Transform"
            &&Entry.MemberName=="X");
        Assert.DoesNotContain(Diff.Entries,Entry=>Entry.Kind==SceneDiffKind.ComponentAdded||Entry.Kind==SceneDiffKind.ComponentRemoved);
    }

    [Fact]
    public void Diff_ReportsNodeRemoval(){
        SceneProjectionReconciler Reconciler=new();
        SceneProjection Before=Reconciler.Reconcile(Key,Analyze("A","B"),Previous:null);
        EditorNodeId B=IdForName(Before,"B");
        SceneProjection After=Reconciler.Reconcile(Key,Analyze("A"),Before);

        SceneDiff Diff=SceneDiffEngine.Compare(Before,After);

        Assert.Contains(Diff.Entries,Entry=>Entry.Kind==SceneDiffKind.NodeRemoved&&Entry.NodeId.Equals(B));
    }

    private static SceneModel Analyze(string First,string?Second=null,float TransformX=0f){
        string Children=Second is null
            ?$"new Node {{ Name=\"{First}\",Components=[new Transform {{ X={TransformX}f }}] }}"
            :$"new Node {{ Name=\"{First}\" }},new Node {{ Name=\"{Second}\" }}";
        string Prefix="""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[
            """;
        string Suffix="""
                ] };
            }
            """;
        return SceneAnalysisTestHelper.Analyze(Prefix+Children+Suffix);
    }

    private static EditorNodeId IdForName(SceneProjection Projection,string Name)=>NodeForName(Projection,Name).Id;

    private static SceneProjectionNode NodeForName(SceneProjection Projection,string Name)=>
        Assert.Single(Projection.Nodes,Node=>Node.Node.Name==Name);
}
