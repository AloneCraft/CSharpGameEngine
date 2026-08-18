using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0047: <see cref="SceneEditDocument"/>のセッション履歴を保存時に一度だけC#構文へ再生する
/// <see cref="SceneSourceEditor.ApplyForSave"/>を、実際の<see cref="SceneEditDocument"/>経由で
/// 蓄積した履歴に対して検証する。最重要は「セッション中に追加したNodeを、同じセッション内で
/// 後続の操作(Rename等)が参照できる」契約(<see cref="ApplyForSave_AddThenRenameSameSessionNode_Succeeds"/>) —
/// これは<see cref="SceneSourceEditor.ApplyBatch"/>単体ではサポートされない。
/// </summary>
public sealed class SceneSourceEditorApplyForSaveTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
        }
        """;

    private const string Source="""
        using CSharpGameEngine.Entities;
        using CSharpGameEngine.Runtime.Windows;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() { Children=[
                new Node {
                    Name="Cube", // 手書きの大事なコメント
                    Components=[new Transform { PositionX=1f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }],
                },
            ] };
        }
        """;

    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");

    [Fact]
    public void ApplyForSave_NoHistory_ReturnsRootUnchanged(){
        (SceneProjection Projection,_,_)=CreateSeed();
        var Root=CSharpSyntaxTree.ParseText(Source).GetRoot();

        SceneSourceEditResult Result=SceneSourceEditor.ApplyForSave(Root,Key,Projection,History:[]);

        Assert.True(Result.Succeeded);
        Assert.Same(Root,Result.Root);
    }

    [Fact]
    public void ApplyForSave_SingleValueEdit_MatchesDirectApply(){
        (SceneProjection Projection,SceneModel Model,SceneProjectionReconciler Reconciler)=CreateSeed();
        SceneEditDocument Document=new(Key,new SceneEditSnapshot(Model,Projection),Reconciler);
        EditorNodeId Cube=FindId(Document,"Cube");
        SetTransformOperation Operation=new(
            Key,Cube,DocumentVersion:1,new SceneTransformValue(new Vector3(9f,0f,0f),Quaternion.Identity,Vector3.One));
        Assert.True(Document.TryApply([Operation],"Move"));

        SceneSourceEditResult Result=SceneSourceEditor.ApplyForSave(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),Key,Projection,Document.History);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Text=Result.Root!.ToFullString();
        Assert.Contains("PositionX=9f",Text);
        Assert.Contains("// 手書きの大事なコメント",Text); // 無関係な部分の書式は保持される
    }

    /// <summary>
    /// 最重要契約: セッション中に追加したNodeを、同じセッション内で後続のRenameが参照できること。
    /// <see cref="SceneSourceEditor.ApplyBatch"/>単体はこれをサポートしない
    /// (新規Nodeが引数の<see cref="SceneProjection"/>に存在しないため)。
    /// </summary>
    [Fact]
    public void ApplyForSave_AddThenRenameSameSessionNode_Succeeds(){
        (SceneProjection Projection,SceneModel Model,SceneProjectionReconciler Reconciler)=CreateSeed();
        SceneEditDocument Document=new(Key,new SceneEditSnapshot(Model,Projection),Reconciler);
        EditorNodeId Root=FindId(Document,null);
        Assert.True(Document.TryApply([new AddNodeOperation(Key,Root,DocumentVersion:1,"Node")],"Add"));
        EditorNodeId NewNode=FindId(Document,"Node");
        Assert.True(Document.TryApply([new RenameNodeOperation(Key,NewNode,DocumentVersion:1,"Wall1")],"Rename"));

        SceneSourceEditResult Result=SceneSourceEditor.ApplyForSave(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),Key,Projection,Document.History);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Text=Result.Root!.ToFullString();
        Assert.Contains("Wall1",Text);
        Assert.DoesNotContain("\"Node\"",Text);

        // 書き戻した結果を再解析すると、Documentの最終状態(Model)と一致するはず。
        SceneModel Truth=SceneAnalysisTestHelper.Analyze([TransformTypes,Text]);
        Assert.Contains(Truth.EnumerateNodes(),static N=>N.Name=="Wall1");
        Assert.Contains(Truth.EnumerateNodes(),static N=>N.Name=="Cube");
    }

    /// <summary>Add→Reparent(同じセッション内で追加したNodeを別の親へ移動)も同様に成立すること。</summary>
    [Fact]
    public void ApplyForSave_AddThenReparentSameSessionNode_Succeeds(){
        const string NestedSource="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[
                    new Node { Name="GroupA" },
                    new Node { Name="GroupB" },
                ] };
            }
            """;
        SceneModel Model=SceneAnalysisTestHelper.Analyze(NestedSource);
        SceneProjectionReconciler Reconciler=new();
        SceneProjection Projection=Reconciler.Reconcile(Key,Model,Previous:null);
        SceneEditDocument Document=new(Key,new SceneEditSnapshot(Model,Projection),Reconciler);
        EditorNodeId GroupA=FindId(Document,"GroupA");
        EditorNodeId GroupB=FindId(Document,"GroupB");
        Assert.True(Document.TryApply([new AddNodeOperation(Key,GroupA,DocumentVersion:1,"Leaf")],"Add"));
        EditorNodeId Leaf=FindId(Document,"Leaf");
        Assert.True(Document.TryApply(
            [new ReparentNodeOperation(Key,Leaf,DocumentVersion:1,GroupB,NewIndex:0)],"Reparent"));

        SceneSourceEditResult Result=SceneSourceEditor.ApplyForSave(
            CSharpSyntaxTree.ParseText(NestedSource).GetRoot(),Key,Projection,Document.History);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        SceneModel Truth=SceneAnalysisTestHelper.Analyze(Result.Root!.ToFullString());
        SceneNode TruthRoot=Truth.Root!;
        SceneNode TruthGroupB=Assert.Single(TruthRoot.Children,static N=>N.Name=="GroupB");
        Assert.Contains(TruthGroupB.Children,static N=>N.Name=="Leaf");
        SceneNode TruthGroupA=Assert.Single(TruthRoot.Children,static N=>N.Name=="GroupA");
        Assert.DoesNotContain(TruthGroupA.Children,static N=>N.Name=="Leaf");
    }

    [Fact]
    public void ApplyForSave_MultipleEntriesAcrossDifferentNodes_AllApply(){
        const string TwoNodeSource="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[
                    new Node { Name="Cube", Components=[new Transform { PositionX=1f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }] },
                    new Node { Name="Sphere", Components=[new Transform { PositionX=5f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }] },
                ] };
            }
            """;
        SceneModel Model=SceneAnalysisTestHelper.Analyze([TransformTypes,TwoNodeSource]);
        SceneProjectionReconciler Reconciler=new();
        SceneProjection Projection=Reconciler.Reconcile(Key,Model,Previous:null);
        SceneEditDocument Document=new(Key,new SceneEditSnapshot(Model,Projection),Reconciler);
        EditorNodeId Cube=FindId(Document,"Cube");
        EditorNodeId Sphere=FindId(Document,"Sphere");
        Assert.True(Document.TryApply(
            [new SetTransformOperation(
                Key,Cube,DocumentVersion:1,new SceneTransformValue(new Vector3(7f,0f,0f),Quaternion.Identity,Vector3.One))],
            "MoveCube"));
        Assert.True(Document.TryApply(
            [new RenameNodeOperation(Key,Sphere,DocumentVersion:1,"Ball")],"RenameSphere"));

        SceneSourceEditResult Result=SceneSourceEditor.ApplyForSave(
            CSharpSyntaxTree.ParseText(TwoNodeSource).GetRoot(),Key,Projection,Document.History);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Text=Result.Root!.ToFullString();
        Assert.Contains("PositionX=7f",Text);
        Assert.Contains("Name=\"Ball\"",Text);
    }

    private static (SceneProjection Projection,SceneModel Model,SceneProjectionReconciler Reconciler)CreateSeed(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze([TransformTypes,Source]);
        SceneProjectionReconciler Reconciler=new();
        return(Reconciler.Reconcile(Key,Model,Previous:null),Model,Reconciler);
    }

    private static EditorNodeId FindId(SceneEditDocument Document,string?NodeName)=>
        Assert.Single(Document.Projection.Nodes,Node=>Node.Node.Name==NodeName).Id;
}
