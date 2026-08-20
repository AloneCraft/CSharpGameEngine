using System.Linq;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0053: パレットが書き込むobject creation式の組み立て。追加したComponentがInspectorで
/// 「解析待ち…」のまま編集できない不具合の直接の対策なので、書き込み可能メンバーが漏れなく
/// 明示されること、そして<b>コンストラクタ既定値を初期化子が潰さないこと</b>を固定する。
/// </summary>
public sealed class SceneComponentInitializerPolicyTests {
    [Fact]
    public void CreateExpression_ListsEveryWritableFieldSoTheInspectorCanEditThemImmediately(){
        var Expression=Build("""
                             namespace Sample;
                             public struct Light { public float Intensity; public float Range; }
                             ""","Sample.Light");

        Assert.Equal("new global::Sample.Light { Intensity=default, Range=default }",Expression);
    }

    /// <summary>
    /// <c>Transform</c>と同じ形。素朴に全フィールドへ<c>default</c>を撒くと
    /// <c>RotationW</c>/<c>Scale</c>が0になり、identity transformでなくなる。
    /// </summary>
    [Fact]
    public void CreateExpression_KeepsValuesSetByTheParameterlessConstructor(){
        var Expression=Build("""
                             namespace Sample;
                             public struct Transform {
                                 public float PositionX;
                                 public float RotationW;
                                 public float ScaleX;
                                 public Transform(){ this.RotationW=1f;this.ScaleX=1f; }
                             }
                             ""","Sample.Transform");

        Assert.Equal("new global::Sample.Transform { PositionX=default, RotationW=1f, ScaleX=1f }",Expression);
    }

    /// <summary>
    /// 単純代入の並びとして読めないコンストラクタは意味を再現できない。既定値を取り違えるくらいなら
    /// 何も書かずに<c>new T()</c>へ縮退し、コンストラクタへ完全に委ねる。
    /// </summary>
    [Fact]
    public void CreateExpression_FallsBackToTheBareConstructorWhenTheBodyIsNotPlainAssignments(){
        var Expression=Build("""
                             namespace Sample;
                             public struct Computed {
                                 public float Value;
                                 public Computed(){ if(System.Environment.TickCount>0){ this.Value=1f; } }
                             }
                             ""","Sample.Computed");

        Assert.Equal("new global::Sample.Computed { Value=default }",Expression);
    }

    [Fact]
    public void CreateExpression_TypeWithoutWritableMembers_StaysABareConstruction(){
        var Expression=Build("""
                             namespace Sample;
                             public struct Marker { }
                             ""","Sample.Marker");

        Assert.Equal("new global::Sample.Marker()",Expression);
    }

    /// <summary>読み取り専用・定数・staticはobject initializerで代入できないので列挙しない。</summary>
    [Fact]
    public void CreateExpression_SkipsMembersThatCannotAppearInAnObjectInitializer(){
        var Expression=Build("""
                             namespace Sample;
                             public struct Mixed {
                                 public float Writable;
                                 public readonly float ReadOnlyField;
                                 public const float Constant=1f;
                                 public static float StaticField;
                                 private float PrivateField;
                                 public float PrivateSetter { get; private set; }
                                 public float Computed=>1f;
                             }
                             ""","Sample.Mixed");

        Assert.Equal("new global::Sample.Mixed { Writable=default }",Expression);
    }

    /// <summary><c>init</c>アクセサはobject initializerで代入できるので含める。</summary>
    [Fact]
    public void CreateExpression_IncludesInitOnlyProperties(){
        var Expression=Build("""
                             namespace Sample;
                             public struct Configured { public int Count { get; init; } }
                             ""","Sample.Configured");

        Assert.Equal("new global::Sample.Configured { Count=default }",Expression);
    }

    private static string Build(string Source,string MetadataName){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(Source);
        var Type=Compilation.GetTypeByMetadataName(MetadataName);
        Assert.NotNull(Type);
        return SceneComponentInitializerPolicy.CreateExpression(
            Type!,Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }
}
