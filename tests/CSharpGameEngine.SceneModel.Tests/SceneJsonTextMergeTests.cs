using CSharpGameEngine.SceneAnalysis.Json;
using Xunit;

namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// M0095 3-5: an edit is written into the author's text. Only changed values change; the author's layout
/// and number spellings stay, and the result always reads as the edited document.
/// </summary>
public sealed class SceneJsonTextMergeTests {
    private const string HandFormatted="{\n  \"className\":\"Main\",\n  \"root\": { \"components\": [ {\"type\": \"T\", \"members\": { \"X\":1.0, \"Y\": 2 } } ],\n    \"children\": [] }\n}\n";
    private const string Canonical="{\n  \"className\": \"Main\",\n  \"root\": {\n    \"components\": [\n      {\n        \"type\": \"T\",\n        \"members\": { \"X\": 1, \"Y\": 2 }\n      }\n    ],\n    \"children\": []\n  }\n}\n";

    [Fact]
    public void UnchangedDocument_KeepsTheOldTextExactly(){
        Assert.Equal(HandFormatted,SceneJsonTextMerge.Merge(HandFormatted,Canonical));
    }

    [Fact]
    public void ChangedNumber_ReplacesOnlyThatNumber(){
        var Edited=Canonical.Replace("\"Y\": 2","\"Y\": 3.5");
        Assert.Equal(HandFormatted.Replace("\"Y\": 2","\"Y\": 3.5"),SceneJsonTextMerge.Merge(HandFormatted,Edited));
    }

    [Fact]
    public void AddedChild_ReplacesOnlyTheChangedArray(){
        var Edited=Canonical.Replace("\"children\": []","\"children\": [\n      { \"name\": \"Box\" }\n    ]");
        var Merged=SceneJsonTextMerge.Merge(HandFormatted,Edited);
        Assert.StartsWith("{\n  \"className\":\"Main\",\n  \"root\": { \"components\": [ {\"type\": \"T\", \"members\": { \"X\":1.0, \"Y\": 2 } } ],",Merged);
        Assert.True(SceneJsonTextMerge.SameValue(SceneJsonReader.Parse(Merged),SceneJsonReader.Parse(Edited)));
    }

    [Fact]
    public void CrlfDocument_KeepsItsLineEndings(){
        var Old=HandFormatted.Replace("\n","\r\n");
        var Edited=Canonical.Replace("\n","\r\n").Replace("\"X\": 1","\"X\": 4");
        Assert.Equal(Old.Replace("\"X\":1.0","\"X\":4"),SceneJsonTextMerge.Merge(Old,Edited));
    }

    [Fact]
    public void ReorderedProperties_FallBackToTheNewObjectText(){
        var Edited=Canonical.Replace("\"members\": { \"X\": 1, \"Y\": 2 }","\"members\": { \"Y\": 2, \"X\": 1 }");
        var Merged=SceneJsonTextMerge.Merge(HandFormatted,Edited);
        Assert.Contains("\"members\": { \"Y\": 2, \"X\": 1 }",Merged);
        Assert.True(SceneJsonTextMerge.SameValue(SceneJsonReader.Parse(Merged),SceneJsonReader.Parse(Edited)));
    }

    [Theory]
    [InlineData("1","1.0",true)]
    [InlineData("0.5","5e-1",true)]
    [InlineData("-0","0",true)]
    [InlineData("120","1.2E2",true)]
    [InlineData("16777217","16777216",false)]
    [InlineData("0.1","0.10000001",false)]
    [InlineData("-1","1",false)]
    public void NumberForms_CompareExactly(string Left,string Right,bool Same){
        Assert.Equal(Same,SceneJsonTextMerge.NormalizeNumber(Left)==SceneJsonTextMerge.NormalizeNumber(Right));
    }

    [Fact]
    public void UnreadableOldText_ReturnsTheNewText(){
        Assert.Equal(Canonical,SceneJsonTextMerge.Merge("{ broken",Canonical));
    }
}
