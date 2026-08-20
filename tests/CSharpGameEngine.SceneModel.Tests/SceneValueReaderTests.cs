namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// Scene DSLの値式whitelist(ADR-0040 F-4)。<b>許可する形だけを数え上げる</b>設計なので、
/// Kindごとに1件ずつ「通ること」を、そしてwhitelist外が「落ちること」を検証する。
/// </summary>
public sealed class SceneValueReaderTests {
    private const string AssetsAndFactories="""
        namespace Sample;
        using CSharpGameEngine.Authoring;
        [SceneAssets]
        public static class Assets {
            public const string Stage="Assets/Stage/Test.glb";
            public static readonly MaterialAsset Floor=new MaterialAsset("Assets/Floor.mat");
            public static MaterialAsset Wall { get; }=new MaterialAsset("Assets/Wall.mat");
            public static MaterialAsset Mutable { get; set; }
        }
        public static class NotAssets {
            public static readonly MaterialAsset Hidden=new MaterialAsset("Assets/Hidden.mat");
        }
        public static class Lights {
            [SceneFactory]
            public static DirectionalLight Sun(float intensity)=>new DirectionalLight { Intensity=intensity };
        }
        public static class Vectors {
            [SceneFactory]
            public static System.Numerics.Vector3 Make(float value)=>new System.Numerics.Vector3(value,value,value);
        }
        public static class Levels {
            public static float Plain(float value)=>value;
        }
        """;

    private static SceneValue FirstMemberValue(SceneModel Model)=>Model.Root!.Components[0].Members[0].Value;

    private static SceneModel AnalyzeComponent(string ComponentExpression,params string[]ExtraMembers){
        var Source= $$"""
            using CSharpGameEngine.Entities;
            using Sample;
            using System;
            using System.Numerics;
            namespace Game;
            public partial class MainScene:IScene {
                private const string Prefix="C# Game Engine";
                public Node Root=>new() { Components=[{{ComponentExpression}}] };
            }
            """;
        return SceneAnalysisTestHelper.Analyze([SceneAnalysisTestHelper.CommonComponents,AssetsAndFactories,Source]);
    }

    [Fact]
    public void Literal_PreservesTheOriginalTokenIncludingSuffix(){
        var Model=AnalyzeComponent("new DirectionalLight { Intensity=1.5f }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Value=FirstMemberValue(Model);
        Assert.Equal(SceneValueKind.Literal,Value.Kind);
        Assert.Equal("1.5f",Value.EmitExpressionText);
        Assert.True(Value.IsEditable);
    }

    [Fact]
    public void NegativeLiteral_KeepsTheSignAndSuffix(){
        var Model=AnalyzeComponent("new DirectionalLight { Intensity=-2.5f }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Value=FirstMemberValue(Model);
        Assert.Equal(SceneValueKind.Literal,Value.Kind);
        Assert.Equal("-2.5f",Value.EmitExpressionText);
    }

    [Fact]
    public void ConstantArithmetic_IsFoldedToATypedLiteral(){
        var Model=AnalyzeComponent("new SpotLight { InnerConeAngleRadians=12f*MathF.PI/180f }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Value=FirstMemberValue(Model);
        Assert.Equal(SceneValueKind.ConstantExpression,Value.Kind);
        Assert.EndsWith("f",Value.EmitExpressionText,StringComparison.Ordinal);
        Assert.Equal(0.20943952f,Assert.IsType<float>(Value.ConstantValue),3);

        // Inspectorには畳み込んだ値ではなくユーザーが書いた式を見せる。
        Assert.Equal("12f*MathF.PI/180f",Value.SourceExpressionText);
        Assert.False(Value.IsEditable);
    }

    [Fact]
    public void ConstantInterpolatedString_IsFolded(){
        var Model=AnalyzeComponent("""new Label { Text=$"{Prefix} - Stage" }""");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Value=FirstMemberValue(Model);
        Assert.Equal(SceneValueKind.ConstantExpression,Value.Kind);
        Assert.Equal("C# Game Engine - Stage",Value.ConstantValue);
        Assert.Equal("\"C# Game Engine - Stage\"",Value.EmitExpressionText);
    }

    [Fact]
    public void NameOf_IsFoldedToAStringLiteral(){
        var Model=AnalyzeComponent("new Label { Text=nameof(MainScene) }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Value=FirstMemberValue(Model);
        Assert.Equal(SceneValueKind.ConstantExpression,Value.Kind);
        Assert.Equal("\"MainScene\"",Value.EmitExpressionText);
    }

    [Fact]
    public void EnumMember_IsEmittedFullyQualifiedInsteadOfItsNumericValue(){
        var Model=AnalyzeComponent("new PrimitiveMesh { Type=PrimitiveType.Cube }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Value=FirstMemberValue(Model);
        Assert.Equal(SceneValueKind.EnumMember,Value.Kind);
        Assert.Equal("global::Sample.PrimitiveType.Cube",Value.EmitExpressionText);
        Assert.True(Value.IsEditable);
    }

    [Fact]
    public void DefaultLiteralAndDefaultExpression_AreBothAccepted(){
        var Model=AnalyzeComponent("new MaterialRef { Material=default }");
        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal(SceneValueKind.Default,FirstMemberValue(Model).Kind);

        var Typed=AnalyzeComponent("new MaterialRef { Material=default(MaterialAsset) }");
        SceneAnalysisTestHelper.AssertNoDiagnostics(Typed);
        Assert.Equal(SceneValueKind.Default,FirstMemberValue(Typed).Kind);
        Assert.Equal("default",FirstMemberValue(Typed).EmitExpressionText);
    }

    [Fact]
    public void NestedObjectCreation_IsReadRecursivelyAndFullyQualified(){
        var Model=AnalyzeComponent("new Transform { Position=new Vector3(1f,2f,3f) }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Value=FirstMemberValue(Model);
        Assert.Equal(SceneValueKind.ObjectCreation,Value.Kind);
        Assert.Equal("new global::System.Numerics.Vector3(1f, 2f, 3f)",Value.EmitExpressionText);
        Assert.Equal(3,Value.Arguments.Length);
    }

    [Fact]
    public void ImplicitObjectCreation_ResolvesTheTargetType(){
        var Model=AnalyzeComponent("new Transform { Position=new(1f,2f,3f) }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal("new global::System.Numerics.Vector3(1f, 2f, 3f)",FirstMemberValue(Model).EmitExpressionText);
    }

    [Fact]
    public void NamedConstructorArguments_ArePreserved(){
        var Model=AnalyzeComponent("new Transform { Position=new Vector3(y:2f,x:1f,z:3f) }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal("new global::System.Numerics.Vector3(y: 2f, x: 1f, z: 3f)",FirstMemberValue(Model).EmitExpressionText);
    }

    [Fact]
    public void CollectionExpression_BecomesAnExplicitArrayCreation(){
        var Model=AnalyzeComponent("new Flags { Values=[true,false] }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Value=FirstMemberValue(Model);
        Assert.Equal(SceneValueKind.Collection,Value.Kind);
        Assert.Equal("new bool[] { true, false }",Value.EmitExpressionText);
        Assert.Equal(2,Value.Elements.Length);
    }

    [Fact]
    public void StaticReadonlyFieldOnSceneAssets_IsKeptAsASymbolicReference(){
        var Model=AnalyzeComponent("new MaterialRef { Material=Assets.Floor }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Value=FirstMemberValue(Model);
        Assert.Equal(SceneValueKind.StaticMemberRef,Value.Kind);
        Assert.Equal("global::Sample.Assets.Floor",Value.EmitExpressionText);
        Assert.False(Value.IsEditable);
    }

    [Fact]
    public void ConstStringOnSceneAssets_KeepsSymbolAndResolvedValue(){
        var Model=AnalyzeComponent("new Label { Text=Assets.Stage }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Value=FirstMemberValue(Model);
        Assert.Equal(SceneValueKind.StaticMemberRef,Value.Kind);
        Assert.Equal("global::Sample.Assets.Stage",Value.EmitExpressionText);
        Assert.Equal("Assets/Stage/Test.glb",Value.ConstantValue);
    }

    [Fact]
    public void StaticGetOnlyPropertyOnSceneAssets_IsAccepted(){
        var Model=AnalyzeComponent("new MaterialRef { Material=Assets.Wall }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal(SceneValueKind.StaticMemberRef,FirstMemberValue(Model).Kind);
    }

    [Fact]
    public void WritableStaticPropertyOnSceneAssets_IsRejected(){
        // 書き換え可能なメンバーはSceneの内容がRuntime状態に依存することを意味するため許可しない。
        var Model=AnalyzeComponent("new MaterialRef { Material=Assets.Mutable }");

        SceneAnalysisTestHelper.AssertContainsDiagnostic(Model,"SCENE112");
    }

    [Fact]
    public void StaticMemberOnANonSceneAssetsClass_IsRejected(){
        var Model=AnalyzeComponent("new MaterialRef { Material=NotAssets.Hidden }");

        SceneAnalysisTestHelper.AssertContainsDiagnostic(Model,"SCENE112");
    }

    [Fact]
    public void SceneFactoryInvocation_IsAcceptedAsAMemberValueAndFullyQualified(){
        var Model=AnalyzeComponent("new Transform { Position=Vectors.Make(1f) }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Value=FirstMemberValue(Model);
        Assert.Equal(SceneValueKind.FactoryCall,Value.Kind);
        Assert.Equal("global::Sample.Vectors.Make(1f)",Value.EmitExpressionText);

        // 戻り値をコンパイル時に評価できないため、Inspectorは読み取り専用で見せる。
        Assert.False(Value.IsEditable);
    }

    [Fact]
    public void SceneFactoryInvocation_IsStillRejectedAsAComponentsElement(){
        // Componentの要素は必ず `new T { … }` に限る。Inspectorが常にメンバー一覧を出せるようにするため、
        // ここだけはファクトリ経由の生成を許可しない。
        var Model=AnalyzeComponent("Lights.Sun(3f)");

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE107");
    }

    [Fact]
    public void PlainMethodInvocation_RequiresSceneFactory(){
        var Model=AnalyzeComponent("new DirectionalLight { Intensity=Levels.Plain(1f) }");

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE111");
    }

    [Fact]
    public void PathCombine_IsRejectedBecauseBclMethodsCannotOptIn(){
        var Model=AnalyzeComponent("""new Label { Text=System.IO.Path.Combine("a","b") }""");

        SceneAnalysisTestHelper.AssertContainsDiagnostic(Model,"SCENE111");
    }

    [Fact]
    public void ConditionalWhoseValueIsCompileTimeConstant_IsAccepted(){
        // ゲートは「構文の種類」ではなく「コンパイル時に値が確定するか」である。`true ? 1f : 2f` は
        // C#コンパイラが 1f へ畳み込むため、リテラル 1f と区別できないし、区別する必要もない。
        // 条件が定数でない三項演算子は下の ConditionalOnNonConstant_IsRejected で落ちる。
        var Model=AnalyzeComponent("new DirectionalLight { Intensity=true?1f:2f }");

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        var Value=FirstMemberValue(Model);
        Assert.Equal(SceneValueKind.ConstantExpression,Value.Kind);
        Assert.Equal("1f",Value.EmitExpressionText);
    }

    [Fact]
    public void ConditionalOnNonConstant_IsRejected(){
        var Model=AnalyzeComponent("new DirectionalLight { Intensity=Levels.Plain(1f)>0f?1f:2f }");

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE110");
    }

    [Theory]
    [InlineData("new Label { Text=(string)(object)\"x\" }")]
    [InlineData("new Label { Text=new string('a',3) }")]
    [InlineData("new Flags { Values=new bool[3] }")]
    public void ExpressionsOutsideTheWhitelist_AreRejected(string ComponentExpression){
        var Model=AnalyzeComponent(ComponentExpression);

        var Ids=SceneAnalysisTestHelper.DiagnosticIds(Model);
        Assert.True(
            Ids.Contains("SCENE110")||Ids.Contains("SCENE111")||Ids.Contains("SCENE112"),
            "Expected a whitelist violation but got: "+string.Join(", ",Ids));
    }
}
