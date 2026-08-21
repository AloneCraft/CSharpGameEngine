using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Source-of-truth precision must remain source-controlled, rather than leaking into per-user VS
/// settings.  These tests deliberately operate only on Roslyn trees, so they also protect the
/// Options-page behavior without requiring a running Visual Studio instance.
/// </summary>
public sealed class TransformPrecisionSourceEditorTests {
    private const string ValidSource="""
using CSharpGameEngine.Entities;

[assembly: TransformPrecision(GameEnginePrecision.TotalBits, GameEnginePrecision.FractionalBits)]

public static class GameEnginePrecision {
    public const int TotalBits = 384;
    public const int FractionalBits = 173;
}
""";

    [Fact]
    public void Analyze_ValidCanonicalSource_ReturnsTheExistingConfiguration(){
        var Analysis=TransformPrecisionSourceEditor.Analyze(Parse(ValidSource));

        Assert.True(Analysis.IsEditable);
        Assert.NotNull(Analysis.Configuration);
        Assert.Equal(384,Analysis.Configuration!.Value.TotalBits);
        Assert.Equal(173,Analysis.Configuration.Value.FractionalBits);
    }

    [Fact]
    public void Apply_ValidConfiguration_ChangesOnlyTheTwoSourceConstants(){
        var Result=TransformPrecisionSourceEditor.Apply(Parse(ValidSource),256,96);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Text=Result.Root!.ToFullString();
        Assert.Contains("[assembly: TransformPrecision(GameEnginePrecision.TotalBits, GameEnginePrecision.FractionalBits)]",Text);
        Assert.Contains("public const int TotalBits = 256;",Text);
        Assert.Contains("public const int FractionalBits = 96;",Text);
        Assert.DoesNotContain("384",Text);
        Assert.DoesNotContain("173",Text);
    }

    [Fact]
    public void Apply_UnsupportedTotalBits_RefusesToEditTheSource(){
        var Result=TransformPrecisionSourceEditor.Apply(Parse(ValidSource),100,40);

        Assert.False(Result.Succeeded);
        Assert.Null(Result.Root);
        Assert.Contains("総ビット数",Result.ErrorMessage);
    }

    [Theory]
    [InlineData(384,0)]
    [InlineData(384,382)]
    public void Apply_InvalidFractionalBitCount_RefusesToEditTheSource(int TotalBits,int FractionalBits){
        var Result=TransformPrecisionSourceEditor.Apply(Parse(ValidSource),TotalBits,FractionalBits);

        Assert.False(Result.Succeeded);
        Assert.Contains("小数部ビット数",Result.ErrorMessage);
    }

    [Fact]
    public void Analyze_NonLiteralConstant_RefusesToGuessAtUserCode(){
        const string Source="""
public static class GameEnginePrecision {
    public const int TotalBits = 192 + 192;
    public const int FractionalBits = 173;
}
""";

        var Analysis=TransformPrecisionSourceEditor.Analyze(Parse(Source));

        Assert.False(Analysis.IsEditable);
        Assert.Contains("整数リテラル",Analysis.ErrorMessage);
    }

    [Fact]
    public void Analyze_AmbiguousPrecisionClasses_RefusesToChooseOne(){
        const string Source="""
public static class GameEnginePrecision {
    public const int TotalBits = 384;
    public const int FractionalBits = 173;
}
public static class Holder {
    public static class GameEnginePrecision {
        public const int TotalBits = 384;
        public const int FractionalBits = 173;
    }
}
""";

        var Analysis=TransformPrecisionSourceEditor.Analyze(Parse(Source));

        Assert.False(Analysis.IsEditable);
        Assert.Contains("一意",Analysis.ErrorMessage);
    }

    [Fact]
    public void CreatePreview_Default384Over173_ShowsResolutionAndRange(){
        TransformPrecisionPreview Preview=TransformPrecisionPolicy.CreatePreview(384,173);

        Assert.True(Preview.IsValid);
        Assert.Equal(211,Preview.IntegerBits);
        Assert.Contains("Q211.173",Preview.Format);
        Assert.Contains("2^-173",Preview.Resolution);
        Assert.Contains("m",Preview.Range);
    }

    [Fact]
    public void Validate_SupportedLimits_AcceptsTheContractBoundaries(){
        Assert.True(TransformPrecisionPolicy.Validate(64,1).IsValid);
        Assert.True(TransformPrecisionPolicy.Validate(512,509).IsValid);
        Assert.False(TransformPrecisionPolicy.Validate(512,510).IsValid);
    }

    private static Microsoft.CodeAnalysis.SyntaxNode Parse(string Source)=>
        CSharpSyntaxTree.ParseText(Source).GetRoot();
}
