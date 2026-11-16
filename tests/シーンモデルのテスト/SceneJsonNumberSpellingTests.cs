using System.Globalization;
using CSharpGameEngine.SceneAnalysis.Json;

namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// Scene JSON の数値表記は、.NET が出力する形式に合わせる。.NET Framework 上でも同じテキストになるよう、厳密な算術で生成する（VSIX は <see cref="SceneJsonNumberSamples.Digest"/> を確認する）。.NET は桁を丸め、正しく解析するため、ここでは .NET 自身の "G" 書式と解析結果を基準とする。ツールが書き出したドキュメントは変化しない。
/// </summary>
public sealed class SceneJsonNumberSpellingTests {
    private static string Normalize(string Text)=>Text=="-0"?"0":Text;

    private static string DotNetSingle(float Value){
        for(var Digits=6;Digits<=9;Digits++){
            var Text=((double)Value).ToString("G"+Digits,CultureInfo.InvariantCulture);
            if((float)double.Parse(Text,CultureInfo.InvariantCulture)==Value){ return Normalize(Text); }
        }
        return Normalize(((double)Value).ToString("G9",CultureInfo.InvariantCulture));
    }

    private static string DotNetDouble(double Value){
        for(var Digits=15;Digits<=17;Digits++){
            var Text=Value.ToString("G"+Digits,CultureInfo.InvariantCulture);
            if(double.Parse(Text,CultureInfo.InvariantCulture)==Value){ return Normalize(Text); }
        }
        return Value.ToString("R",CultureInfo.InvariantCulture);
    }

    private static string DotNetAngle(float Value,double Scale){
        var Quotient=Value/Scale;
        var Positional=Math.Abs(Quotient)>=1e-4&&Math.Abs(Quotient)<1e15;
        for(var Digits=1;Digits<=17;Digits++){
            var Text=Quotient.ToString("G"+Digits,CultureInfo.InvariantCulture);
            if(Positional&&Text.Contains('E')){ continue; }
            if((float)(double.Parse(Text,CultureInfo.InvariantCulture)*Scale)==Value){ return Normalize(Text); }
        }
        return Quotient.ToString("R",CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Singles_AreSpelledAsDotNetSpellsThem(){
        foreach(var Value in SceneJsonNumberSamples.Singles()){
            Assert.Equal(DotNetSingle(Value),SceneJsonNumbers.FormatSingle(Value));
        }
    }

    [Fact]
    public void Doubles_AreSpelledAsDotNetSpellsThem(){
        foreach(var Value in SceneJsonNumberSamples.Doubles()){
            Assert.Equal(DotNetDouble(Value),SceneJsonNumbers.FormatOther(Value));
        }
    }

    [Fact]
    public void Angles_AreSpelledAsDotNetSpellsThem(){
        foreach(var Value in SceneJsonNumberSamples.Angles()){
            Assert.Equal(DotNetAngle(Value,2d*Math.PI),SceneJsonAngles.FormatTurns(Value,SceneAngleUnit.Radians));
            Assert.Equal(DotNetAngle(Value,360d),SceneJsonAngles.FormatTurns(Value,SceneAngleUnit.Degrees));
            Assert.Equal(DotNetAngle(Value,Math.PI/180d),SceneJsonAngles.FormatDegrees(Value,SceneAngleUnit.Radians));
            Assert.Equal(DotNetAngle(Value,1d),SceneJsonAngles.FormatDegrees(Value,SceneAngleUnit.Degrees));
        }
    }

    /// <summary>
    /// 保持された作成者の表記は、どこでも同じ値に読み戻されなければならない。チェックでは .NET の解析結果と照合する。C# の float リテラルと同様に直接 float へ丸めるため、桁数を問わず（過剰な精度の数値も含めて）適用できる。最後のテキストは 1 と次の float の中間値をわずかに上回るため、先に double として解析してから再度丸めると 1 になる。
    /// </summary>
    [Fact]
    public void ReadsBack_AgreesWithTheParse(){
        foreach(var Value in SceneJsonNumberSamples.Singles().Where(float.IsFinite)){
            for(var Digits=1;Digits<=20;Digits++){
                var Text=((double)Value).ToString("G"+Digits,CultureInfo.InvariantCulture);
                Assert.Equal(float.Parse(Text,CultureInfo.InvariantCulture)==Value,SceneJsonDecimal.ReadsBack(Text,Value));
                Assert.Equal(float.Parse(Text,CultureInfo.InvariantCulture)==MathF.BitIncrement(Value),SceneJsonDecimal.ReadsBack(Text,MathF.BitIncrement(Value)));
            }
        }
        foreach(var Value in SceneJsonNumberSamples.Doubles().Where(double.IsFinite)){
            for(var Digits=1;Digits<=20;Digits++){
                var Text=Value.ToString("G"+Digits,CultureInfo.InvariantCulture);
                Assert.Equal(double.Parse(Text,CultureInfo.InvariantCulture)==Value,SceneJsonDecimal.ReadsBack(Text,Value));
            }
        }
        Assert.Equal((true,true,false),(SceneJsonDecimal.ReadsBack("16777217",16777217),SceneJsonDecimal.ReadsBack("16777217",16777216f),SceneJsonDecimal.ReadsBack("16777219",16777218f)));
        Assert.Equal((false,true),(SceneJsonDecimal.ReadsBack("1.0",1),SceneJsonDecimal.ReadsBack("0.10",0.1m)));
        Assert.Equal((true,false),(SceneJsonDecimal.ReadsBack("1.0000000596046447755",MathF.BitIncrement(1f)),SceneJsonDecimal.ReadsBack("1.0000000596046447755",1f)));
        Assert.Equal((true,false),(SceneJsonDecimal.ReadsBack("3.4028235677973366E+38",float.MaxValue),SceneJsonDecimal.ReadsBack("3.4028235677973367E+38",float.MaxValue)));
    }

    /// <summary>指数部が int の範囲の端または範囲外でも、例外を投げる理由にはならない。その場合は値からテキストを書き出す。</summary>
    [Theory]
    [InlineData("1e-2147483648")]
    [InlineData("1e2147483647")]
    [InlineData("1.5e-2147483648")]
    [InlineData("1e-9223372036854775808")]
    [InlineData("1e99999999999999999999")]
    public void ReadsBack_IsFalseForExtremeExponents(string Text){
        Assert.False(SceneJsonDecimal.ReadsBack(Text,0f));
        Assert.False(SceneJsonDecimal.ReadsBack(Text,0d));
    }

    [Fact]
    public void SampleDigest_IsTheOneTheVsixChecks()=>Assert.Equal(SceneJsonNumberSamples.Digest,SceneJsonNumberSamples.ComputeDigest());
}
