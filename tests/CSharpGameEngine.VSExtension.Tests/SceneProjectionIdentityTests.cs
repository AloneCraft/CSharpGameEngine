using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0051 Step 1-6: <see cref="SceneProjectionReconciler"/>の<c>EditorNodeId</c>引き継ぎを固定する。
///
/// <para>
/// Stage Bは「保存の着地でDocumentを維持する」ために、実解析の投影が<b>Documentの投影から</b>
/// IDを引き継ぐことに依存している。セッション中に追加したノードは保存前の実投影に存在しないため、
/// 既定の引き継ぎ元(ひとつ前の実投影)では新IDが払い直され、Documentの履歴が参照するIDを
/// 実投影上で解決できなくなる = 次の保存が「対象Nodeの構文を特定できません」で失敗する。
/// </para>
///
/// <para>
/// <b>引き継げない既知の限界も同時に固定する</b>。ここが<see cref="SceneDocumentLineagePolicy"/>の
/// フォールバックが必要な理由そのもので、限界を消したのではなく「検知して安全側へ縮退する」
/// 設計にしている。
/// </para>
/// </summary>
public sealed class SceneProjectionIdentityTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
        }
        """;

    private static readonly SceneKey Key=new("project-a","Game.MainScene");

    /// <summary>
    /// Documentの投影を引き継ぎ元にすると、セッション中に追加したノードのIDが保たれる。
    /// これがStage Bの前提。
    /// </summary>
    [Fact]
    public void DocumentBasis_PreservesIdsForNodesAddedDuringTheSession(){
        SceneProjectionReconciler Reconciler=new();
        SceneProjection Before=Reconciler.Reconcile(Key,Analyze("""
            new Node { Name="Alpha" },
            new Node { Name="Beta" },
            """),Previous:null);

        // 「Gamma を追加して保存した」あとの実解析に相当する。
        SceneProjection After=Reconciler.Reconcile(Key,Analyze("""
            new Node { Name="Alpha" },
            new Node { Name="Beta" },
            new Node { Name="Gamma" },
            """),Previous:Before);

        Assert.Equal(IdOf(Before,"Alpha"),IdOf(After,"Alpha"));
        Assert.Equal(IdOf(Before,"Beta"),IdOf(After,"Beta"));

        // 追加ノードは引き継ぎ元に居ないので新IDになる。だからこそ、着地時の引き継ぎ元を
        // Document の投影にする必要がある(SceneEditorSessionService.Publish の IdentityBasis)。
        SceneProjection Again=Reconciler.Reconcile(Key,Analyze("""
            new Node { Name="Alpha" },
            new Node { Name="Beta" },
            new Node { Name="Gamma" },
            """),Previous:After);
        Assert.Equal(IdOf(After,"Gamma"),IdOf(Again,"Gamma"));
    }

    /// <summary>リネームは Component 構成が一意なら追跡できる(候補が1件に絞れるため)。</summary>
    [Fact]
    public void Rename_KeepsTheIdWhenTheComponentShapeIsUnique(){
        SceneProjectionReconciler Reconciler=new();
        SceneProjection Before=Reconciler.Reconcile(Key,Analyze("""
            new Node { Name="Alpha", Components=[new Transform { PositionX=1f }] },
            new Node { Name="Beta" },
            """),Previous:null);

        SceneProjection After=Reconciler.Reconcile(Key,Analyze("""
            new Node { Name="AlphaRenamed", Components=[new Transform { PositionX=1f }] },
            new Node { Name="Beta" },
            """),Previous:Before);

        Assert.Equal(IdOf(Before,"Alpha"),IdOf(After,"AlphaRenamed"));
    }

    /// <summary>
    /// <b>既知の限界</b>。Name も Component 構成も同じ兄弟は候補が1件に絞れず、
    /// 両方に新しいIDが払われる。Stage Bはこれを<see cref="SceneDocumentLineagePolicy"/>の
    /// 投影比較で検知し、<see cref="SceneLandingAction.Rebase"/>へ縮退することで
    /// 「保存が突然できなくなる」のを防ぐ。この限界が消えたと誤解しないためのテスト。
    /// </summary>
    [Fact]
    public void IndistinguishableSiblings_LoseTheirIds(){
        SceneProjectionReconciler Reconciler=new();
        const string Shape="""
            new Node { Name="Same" },
            new Node { Name="Same" },
            """;
        SceneProjection Before=Reconciler.Reconcile(Key,Analyze(Shape),Previous:null);
        SceneProjection After=Reconciler.Reconcile(Key,Analyze(Shape),Previous:Before);

        EditorNodeId[]BeforeIds=[..Before.Nodes.Where(N=>N.Node.Name=="Same").Select(N=>N.Id)];
        EditorNodeId[]AfterIds=[..After.Nodes.Where(N=>N.Node.Name=="Same").Select(N=>N.Id)];

        Assert.Equal(2,BeforeIds.Length);
        Assert.Equal(2,AfterIds.Length);
        Assert.Empty(BeforeIds.Intersect(AfterIds));
    }

    private static SceneModel Analyze(string Children){
        string Source=$$"""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[
                    {{Children}}
                ] };
            }
            """;
        CSharpCompilation Compilation=SceneAnalysisTestHelper.CreateCompilation(TransformTypes,Source);
        INamedTypeSymbol SceneClass=SceneAnalysisTestHelper.FindSceneClass(Compilation,"MainScene")!;
        return SceneDeclarationAnalyzer.Analyze(SceneClass,Compilation,CancellationToken.None);
    }

    private static EditorNodeId IdOf(SceneProjection Projection,string NodeName)=>
        Assert.Single(Projection.Nodes,Node=>Node.Node.Name==NodeName).Id;
}
