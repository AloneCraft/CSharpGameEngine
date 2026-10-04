using System.Globalization;
using CSharpGameEngine.SceneAnalysis.Json;

namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// Scene JSON numbers are spelled as .NET spells them, through exact arithmetic that gives the same text on .NET
/// Framework (the VSIX checks <see cref="SceneJsonNumberSamples.Digest"/>). .NET rounds digits and parses correctly, so
/// its own "G" formatting and parse are the oracle here: a document the tools wrote does not change.
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

    [Fact]
    public void SampleDigest_IsTheOneTheVsixChecks()=>Assert.Equal(SceneJsonNumberSamples.Digest,SceneJsonNumberSamples.ComputeDigest());
}
