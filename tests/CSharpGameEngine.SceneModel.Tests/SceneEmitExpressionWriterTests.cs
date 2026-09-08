using System.Globalization;

namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// 定数をC#リテラルへ整形する境界値。ここが崩れると<b>生成コードがコンパイルできなくなる</b> —
/// 特に<c>float</c>フィールドへ<c>double</c>リテラルを出すと暗黙変換が無くエラーになる。
/// </summary>
public sealed class SceneEmitExpressionWriterTests {
    [Fact]
    public void SingleValues_AlwaysCarryTheFloatSuffix(){
        Assert.Equal("1.5f",SceneEmitExpressionWriter.WriteConstant(1.5f));
        Assert.Equal("10f",SceneEmitExpressionWriter.WriteConstant(10f));
        Assert.Equal("0f",SceneEmitExpressionWriter.WriteConstant(0f));
        Assert.Equal("-2.25f",SceneEmitExpressionWriter.WriteConstant(-2.25f));
    }

    [Fact]
    public void NonCanonicalRealValues_AreRejectedByTheEmitterBoundary(){
        Assert.Equal(SceneEmitExpressionWriter.DefaultExpression,SceneEmitExpressionWriter.WriteConstant(1.5d));
        Assert.Equal(SceneEmitExpressionWriter.DefaultExpression,SceneEmitExpressionWriter.WriteConstant(1.5m));
    }

    [Fact]
    public void FloatingPointValues_RoundTripThroughTheEmittedText(){
        // G9/G17は往復可能な書式指定子。R は .NET Framework の float で往復しないため使わない
        // (このソースはnet472のVSIXへもリンクされる)。
        foreach(var Value in new[]{ 0.20943952f,3.14159274f,1.401298E-45f,3.402823E+38f }){
            var Text=SceneEmitExpressionWriter.WriteConstant(Value);
            var Parsed=float.Parse(Text.TrimEnd('f'),NumberStyles.Float,CultureInfo.InvariantCulture);
            Assert.Equal(Value,Parsed);
        }
    }

    [Fact]
    public void NonFiniteValues_BecomeFullyQualifiedFieldReferences(){
        Assert.Equal("global::System.Single.NaN",SceneEmitExpressionWriter.WriteConstant(float.NaN));
        Assert.Equal("global::System.Single.PositiveInfinity",SceneEmitExpressionWriter.WriteConstant(float.PositiveInfinity));
        Assert.Equal("global::System.Single.NegativeInfinity",SceneEmitExpressionWriter.WriteConstant(float.NegativeInfinity));
    }

    [Fact]
    public void IntegralValues_CarryTheSuffixOnlyWhenTheLiteralWouldOtherwiseChangeType(){
        Assert.Equal("42",SceneEmitExpressionWriter.WriteConstant(42));
        Assert.Equal("42L",SceneEmitExpressionWriter.WriteConstant(42L));
        Assert.Equal("42UL",SceneEmitExpressionWriter.WriteConstant(42UL));
        Assert.Equal("42U",SceneEmitExpressionWriter.WriteConstant(42U));
    }

    [Fact]
    public void StringsAndCharsAreQuotedAndEscaped(){
        Assert.Equal("\"plain\"",SceneEmitExpressionWriter.WriteConstant("plain"));
        Assert.Equal("\"with \\\"quote\\\"\"",SceneEmitExpressionWriter.WriteConstant("with \"quote\""));
        Assert.Equal("\"tab\\there\"",SceneEmitExpressionWriter.WriteConstant("tab\there"));
        Assert.Equal("'x'",SceneEmitExpressionWriter.WriteConstant('x'));
    }

    [Fact]
    public void BooleansAndNull(){
        Assert.Equal("true",SceneEmitExpressionWriter.WriteConstant(true));
        Assert.Equal("false",SceneEmitExpressionWriter.WriteConstant(false));
        Assert.Equal("null",SceneEmitExpressionWriter.WriteConstant(null));
    }

    [Fact]
    public void ObjectCreation_OmitsEmptyArgumentAndInitializerLists(){
        Assert.Equal(
            "new global::Sample.Transform()",
            SceneEmitExpressionWriter.WriteObjectCreation("global::Sample.Transform",[],[]));
        Assert.Equal(
            "new global::Sample.Transform() { Scale = 2f }",
            SceneEmitExpressionWriter.WriteObjectCreation("global::Sample.Transform",[],["Scale = 2f"]));
        Assert.Equal(
            "new global::Sample.Transform(1f, 2f)",
            SceneEmitExpressionWriter.WriteObjectCreation("global::Sample.Transform",["1f","2f"],[]));
    }

    [Fact]
    public void ArrayCreation_IsExplicitlyTypedSoGeneratedCodeNeverRelisOnTargetTyping(){
        Assert.Equal("new bool[] { true, false }",SceneEmitExpressionWriter.WriteArrayCreation("bool",["true","false"]));
        Assert.Equal("new bool[0]",SceneEmitExpressionWriter.WriteArrayCreation("bool",[]));
    }

    [Fact]
    public void NamedArguments_ArePreserved(){
        Assert.Equal("1f",SceneEmitExpressionWriter.WriteArgument(null,"1f"));
        Assert.Equal("x: 1f",SceneEmitExpressionWriter.WriteArgument("x","1f"));
    }
}
