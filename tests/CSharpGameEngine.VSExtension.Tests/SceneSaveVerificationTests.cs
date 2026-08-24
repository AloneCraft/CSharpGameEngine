using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0051: 書き戻しの往復検証(<see cref="SceneSaveVerification"/>)と、履歴エントリごとの
/// 中間構文木を返す<see cref="SceneSourceEditor.ApplyForSaveSteps"/>を固定する。
///
/// <para>
/// M0049でJSONへ移行した動機は「C#に反映されていない」という実機報告だった。ここで検証するのは
/// 「正しい書き戻しが検証を通ること」と「壊れた書き戻しは検証で止まること」の両方であり、
/// 前者だけだと検証が常にtrueを返す実装でもテストが通ってしまう。
/// </para>
/// </summary>
public sealed class SceneSaveVerificationTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Vector3 { public float R; public float U; public float F; public Vector3(float R,float U,float F){ this.R=R;this.U=U;this.F=F; } }
            public struct Transform {
                public Vector3 Position; public Vector3 Rotation; public Vector3 Scale;
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
                new Node { Name="Sphere" },
            ] };
        }
        """;

    private static readonly SceneKey Key=new("project-a","Game.MainScene");

    /// <summary>正しい書き戻しは検証を通り、しかも手書きコメントが保たれる(最小差分である証拠)。</summary>
    [Fact]
    public void CorrectWriteBackPassesVerificationAndKeepsHandWrittenComments(){
        (var Compilation,var Root,var Projection,var Model,
            var Reconciler)=CreateSeed();
        SceneEditDocument Document=new(Key,new SceneEditSnapshot(Model,Projection),Reconciler);
        var Cube=FindId(Document,"Cube");
        Assert.True(Document.TryApply(
            [new SetTransformOperation(Key,Cube,DocumentVersion:1,
                new SceneTransformValue(new Vector3(9f,0f,0f),Quaternion.Identity,Vector3.One))],
            "移動"));

        var Edited=SceneSourceEditor.ApplyForSave(Root,Key,Projection,Document.History);
        Assert.True(Edited.Succeeded);

        Assert.True(SceneSaveVerification.Verify(
            Edited.Root!,Root,Compilation,Document.Model,Key.ClassFullyQualifiedName,
            CancellationToken.None,out var Error),Error);

        Assert.Contains("手書きの大事なコメント",Edited.Root!.ToFullString());
        Assert.Contains("Position=new global::CSharpGameEngine.Runtime.Windows.Vector3(9f,0f,0f)",Edited.Root.ToFullString());
    }

    /// <summary>
    /// 壊れた書き戻し(意図と違う値が書かれた)を検証が確実に捉えること。これが機能しないと
    /// M0049の「黙って反映されない」が再発する。
    /// </summary>
    [Fact]
    public void WriteBackThatDoesNotMatchTheDocumentFailsVerification(){
        (var Compilation,var Root,var Projection,var Model,
            var Reconciler)=CreateSeed();
        SceneEditDocument Document=new(Key,new SceneEditSnapshot(Model,Projection),Reconciler);
        var Cube=FindId(Document,"Cube");
        Assert.True(Document.TryApply(
            [new SetTransformOperation(Key,Cube,DocumentVersion:1,
                new SceneTransformValue(new Vector3(9f,0f,0f),Quaternion.Identity,Vector3.One))],
            "移動"));

        // Documentは9fを期待しているのに、書き戻し結果は元のまま(1f)という「反映漏れ」を模す。
        Assert.False(SceneSaveVerification.Verify(
            Root,Root,Compilation,Document.Model,Key.ClassFullyQualifiedName,
            CancellationToken.None,out var Error));
        Assert.Contains(SceneModelStructuralSignature.Compute(Document.Model),Error);
    }

    /// <summary>
    /// 履歴3件が、まとめて1つではなく<b>3つの中間構文木</b>として返ること。これが
    /// 「1操作=1 Undo単位」の土台になる。
    /// </summary>
    [Fact]
    public void ApplyForSaveSteps_ReturnsOneTreePerHistoryEntry(){
        (_,var Root,var Projection,var Model,
            var Reconciler)=CreateSeed();
        SceneEditDocument Document=new(Key,new SceneEditSnapshot(Model,Projection),Reconciler);
        var Cube=FindId(Document,"Cube");

        for(var Step=1;Step<=3;Step++){
            Assert.True(Document.TryApply(
                [new SetTransformOperation(Key,Cube,DocumentVersion:Step,
                    new SceneTransformValue(new Vector3(Step,0f,0f),Quaternion.Identity,Vector3.One))],
                $"移動{Step}"));
        }

        var Steps=SceneSourceEditor.ApplyForSaveSteps(Root,Key,Projection,Document.History);

        Assert.True(Steps.Succeeded);
        Assert.Equal(3,Steps.Steps.Count);
        Assert.Equal(["移動1","移動2","移動3"],Steps.Labels);

        // 各ステップが実際にその時点の値を持つ(まとめて最終値になっていない)。
        Assert.Contains("PositionX=1f",Steps.Steps[0].ToFullString());
        Assert.Contains("Position=new global::CSharpGameEngine.Runtime.Windows.Vector3(2f,0f,0f)",Steps.Steps[1].ToFullString());
        Assert.Contains("Position=new global::CSharpGameEngine.Runtime.Windows.Vector3(3f,0f,0f)",Steps.Steps[2].ToFullString());
    }

    /// <summary>ステップ列の最終木は、従来の<see cref="SceneSourceEditor.ApplyForSave"/>の結果と一致する。</summary>
    [Fact]
    public void ApplyForSaveSteps_FinalRootMatchesApplyForSave(){
        (_,var Root,var Projection,var Model,
            var Reconciler)=CreateSeed();
        SceneEditDocument Document=new(Key,new SceneEditSnapshot(Model,Projection),Reconciler);
        var Cube=FindId(Document,"Cube");
        Assert.True(Document.TryApply([new RenameNodeOperation(Key,Cube,DocumentVersion:1,"Renamed")],"名前の変更"));
        Assert.True(Document.TryApply(
            [new SetTransformOperation(Key,Cube,DocumentVersion:2,
                new SceneTransformValue(new Vector3(5f,0f,0f),Quaternion.Identity,Vector3.One))],
            "移動"));

        var Whole=SceneSourceEditor.ApplyForSave(Root,Key,Projection,Document.History);
        var Steps=SceneSourceEditor.ApplyForSaveSteps(Root,Key,Projection,Document.History);

        Assert.True(Whole.Succeeded);
        Assert.True(Steps.Succeeded);
        Assert.Equal(Whole.Root!.ToFullString(),Steps.FinalRoot!.ToFullString());
    }

    /// <summary>
    /// M0051で発見した既存バグ(M0047から存在)の回帰テスト: 同一Nodeに対して
    /// <b>先にRenameしてから移動</b>すると、Renameでテキスト長が変わって
    /// <c>Transform.Location.Span</c>がずれ、保存が
    /// 「Transformのobject creationがSourceに見つかりません」で失敗していた。
    /// 「移動→Rename」「移動→移動」は成功するという非対称な壊れ方をしていたため、
    /// 4つの順序すべてを固定する。
    /// </summary>
    [Theory]
    [InlineData("rename-then-move")]
    [InlineData("move-then-rename")]
    [InlineData("move-twice")]
    [InlineData("rename-move-rename")]
    public void ApplyForSave_SucceedsRegardlessOfOperationOrderOnTheSameNode(string Order){
        (_,var Root,var Projection,var Model,
            var Reconciler)=CreateSeed();
        SceneEditDocument Document=new(Key,new SceneEditSnapshot(Model,Projection),Reconciler);
        var Cube=FindId(Document,"Cube");

        void Move(int Version,float X)=>Assert.True(Document.TryApply(
            [new SetTransformOperation(Key,Cube,Version,
                new SceneTransformValue(new Vector3(X,0f,0f),Quaternion.Identity,Vector3.One))],$"移動{Version}"));
        void Rename(int Version,string Name)=>Assert.True(Document.TryApply(
            [new RenameNodeOperation(Key,Cube,Version,Name)],$"名前の変更{Version}"));

        switch(Order){
            case "rename-then-move": Rename(1,"Renamed"); Move(2,5f); break;
            case "move-then-rename": Move(1,5f); Rename(2,"Renamed"); break;
            case "move-twice": Move(1,5f); Move(2,6f); break;
            case "rename-move-rename": Rename(1,"A"); Move(2,5f); Rename(3,"B"); break;
        }

        var Result=SceneSourceEditor.ApplyForSave(Root,Key,Projection,Document.History);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        Assert.Contains("PositionX",Result.Root!.ToFullString());
    }

    [Fact]
    public void ApplyForSaveSteps_NoHistory_ReturnsNoSteps(){
        (_,var Root,var Projection,_,_)=CreateSeed();

        var Steps=SceneSourceEditor.ApplyForSaveSteps(Root,Key,Projection,History:[]);

        Assert.True(Steps.Succeeded);
        Assert.Empty(Steps.Steps);
        Assert.Null(Steps.FinalRoot);
    }

    private static (CSharpCompilation Compilation,SyntaxNode Root,SceneProjection Projection,
        SceneModel Model,SceneProjectionReconciler Reconciler)CreateSeed(){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(TransformTypes,Source);
        var Root=Compilation.SyntaxTrees
                            .Single(Tree=>Tree.GetRoot().ToFullString().Contains("class MainScene"))
                            .GetRoot();
        var SceneClass=SceneAnalysisTestHelper.FindSceneClass(Compilation,"MainScene")!;
        var Model=SceneDeclarationAnalyzer.Analyze(SceneClass,Compilation,CancellationToken.None);
        SceneProjectionReconciler Reconciler=new();
        return(Compilation,Root,Reconciler.Reconcile(Key,Model,Previous:null),Model,Reconciler);
    }

    private static EditorNodeId FindId(SceneEditDocument Document,string?NodeName)=>
        Assert.Single(Document.Projection.Nodes,Node=>Node.Node.Name==NodeName).Id;
}
