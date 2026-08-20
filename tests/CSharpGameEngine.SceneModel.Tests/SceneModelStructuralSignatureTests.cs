namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// M0051: 書き戻しの往復検証に使う署名が、意味の変化を確実に捉え、かつソース位置のような
/// 「変わって当然のもの」では変わらないことを固定する。この性質が崩れると、往復検証が
/// 誤検知(常に失敗)か見逃し(壊れても気付かない)のどちらかに転ぶ。
/// </summary>
public sealed class SceneModelStructuralSignatureTests {
    private const string BaseScene="""
        using CSharpGameEngine.Entities;
        using Sample;
        using System.Numerics;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() {
                Name="Root",
                Components=[new Transform { Position=new Vector3(1f,2f,3f) }],
                Children=[new Node { Name="Child",Components=[new PrimitiveMesh { Type=PrimitiveType.Cube }] }],
            };
        }
        """;

    private static string SignatureOf(string SceneSource)=>
        SceneModelStructuralSignature.Compute(SceneAnalysisTestHelper.Analyze(SceneSource));

    [Fact]
    public void IdenticalSourceProducesIdenticalSignature(){
        Assert.Equal(SignatureOf(BaseScene),SignatureOf(BaseScene));
    }

    /// <summary>
    /// 空白・改行・コメントはソース位置を丸ごとずらすが、Sceneの意味は変えない。
    /// 署名がここで変わってしまうと、往復検証が常に失敗して保存できなくなる。
    /// </summary>
    [Fact]
    public void FormattingAndCommentsDoNotChangeTheSignature(){
        var Reformatted="""
                        using CSharpGameEngine.Entities;
                        using Sample;
                        using System.Numerics;
                        namespace Game;
                        public partial class MainScene:IScene {
                            // このコメントは意味を変えない
                            public Node Root=>new() {
                                Name="Root",

                                Components=[
                                    new Transform {
                                        Position=new Vector3(1f,2f,3f)
                                    }
                                ],
                                Children=[
                                    new Node { Name="Child",Components=[new PrimitiveMesh { Type=PrimitiveType.Cube }] },
                                ],
                            };
                        }
                        """;

        Assert.Equal(SignatureOf(BaseScene),SignatureOf(Reformatted));
    }

    [Fact]
    public void ChangedMemberValueChangesTheSignature(){
        var Changed=BaseScene.Replace("new Vector3(1f,2f,3f)","new Vector3(1f,2f,9f)");
        Assert.NotEqual(SignatureOf(BaseScene),SignatureOf(Changed));
    }

    [Fact]
    public void ChangedEnumMemberChangesTheSignature(){
        var Changed=BaseScene.Replace("PrimitiveType.Cube","PrimitiveType.Sphere");
        Assert.NotEqual(SignatureOf(BaseScene),SignatureOf(Changed));
    }

    [Fact]
    public void RenamedNodeChangesTheSignature(){
        var Changed=BaseScene.Replace("Name=\"Child\"","Name=\"Renamed\"");
        Assert.NotEqual(SignatureOf(BaseScene),SignatureOf(Changed));
    }

    [Fact]
    public void RemovedComponentChangesTheSignature(){
        var Changed=BaseScene.Replace(
                                      "Components=[new PrimitiveMesh { Type=PrimitiveType.Cube }]","Components=[]");
        Assert.NotEqual(SignatureOf(BaseScene),SignatureOf(Changed));
    }

    /// <summary>
    /// 同じ内容のノードでも位置が違えば別物。Pathを署名に含めている理由の固定。
    /// </summary>
    [Fact]
    public void MovedNodeChangesTheSignature(){
        var TwoChildren="""
                        using CSharpGameEngine.Entities;
                        using Sample;
                        namespace Game;
                        public partial class MainScene:IScene {
                            public Node Root=>new() {
                                Children=[new Node { Name="A" },new Node { Name="B" }],
                            };
                        }
                        """;
        var Swapped=TwoChildren.Replace(
                                        "[new Node { Name=\"A\" },new Node { Name=\"B\" }]",
                                        "[new Node { Name=\"B\" },new Node { Name=\"A\" }]");

        Assert.NotEqual(SignatureOf(TwoChildren),SignatureOf(Swapped));
    }

    /// <summary>
    /// 定数式は畳み込み後の値が同じなら等価とみなす(EmitExpressionTextで比較しているため)。
    /// ユーザーが書いた式の見た目が変わっただけで保存が中止されては困る。
    /// </summary>
    [Fact]
    public void ConstantExpressionsWithTheSameFoldedValueAreEquivalent(){
        var Literal="""
                    using CSharpGameEngine.Entities;
                    using Sample;
                    namespace Game;
                    public partial class MainScene:IScene {
                        public Node Root=>new() { Components=[new DirectionalLight { Intensity=6f }] };
                    }
                    """;
        var Expression=Literal.Replace("Intensity=6f","Intensity=2f*3f");

        Assert.Equal(SignatureOf(Literal),SignatureOf(Expression));
    }
}
