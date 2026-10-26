using CSharpGameEngine.SceneAnalysis.Json;
using Xunit;

namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// M0095 3-5：編集内容を作成者のテキストへ反映する。変更された値だけを変更し、作成者のレイアウトと数値表記を維持する。結果は常に編集後のドキュメントとして読み込める。
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

    /// <summary>
    /// キーの順序は作成者のレイアウトを表す。オブジェクトをキー順に比較していたため、作成者が標準とは異なる順序を使うと、編集のたびにそのオブジェクト（子要素を含むノード全体）が標準形式で書き直されていた。
    /// </summary>
    [Fact]
    public void ReorderedProperties_KeepTheAuthorsOrder(){
        var Edited=Canonical.Replace("\"members\": { \"X\": 1, \"Y\": 2 }","\"members\": { \"Y\": 2, \"X\": 1 }");
        Assert.Equal(HandFormatted,SceneJsonTextMerge.Merge(HandFormatted,Edited));
        var Moved=Edited.Replace("\"Y\": 2","\"Y\": 5");
        Assert.Equal(HandFormatted.Replace("\"Y\": 2","\"Y\": 5"),SceneJsonTextMerge.Merge(HandFormatted,Moved));
    }

    /// <summary>ノードの空のコンポーネントや子要素、コンポーネントの空のメンバーは、同じく除外して読み込まれるため、そのまま除外しておく。</summary>
    [Fact]
    public void EmptyListsTheAuthorLeftOut_StayOut(){
        const string Old="{\n  \"root\": {\n    \"name\": \"Root\",\n    \"children\": [ { \"name\": \"Leaf\", \"components\": [ { \"type\": \"T\" } ] } ]\n  }\n}\n";
        const string New="{\n  \"root\": {\n    \"name\": \"Root\",\n    \"components\": [],\n    \"children\": [\n      {\n        \"name\": \"Leaf\",\n        \"components\": [ { \"type\": \"T\", \"members\": {} } ],\n        \"children\": []\n      }\n    ]\n  }\n}\n";
        Assert.Equal(Old,SceneJsonTextMerge.Merge(Old,New));
        Assert.Equal(Old.Replace("\"Leaf\"","\"Leaf2\""),SceneJsonTextMerge.Merge(Old,New.Replace("\"Leaf\"","\"Leaf2\"")));
    }

    /// <summary>
    /// 新しいドキュメントが追加するキーは、旧テキスト内で最も近い直前のキーの後に、作成者の空白を保って挿入する 1 つのプロパティであり、オブジェクト全体を標準形式で書き換えない。メンバー内では空のリストも値となる。
    /// </summary>
    [Fact]
    public void AddedKey_IsInsertedWithTheAuthorsSpacing(){
        const string Old="{\n  \"root\": {\n    \"name\": \"Root\",\n    \"components\": [ { \"type\": \"T\", \"members\": { \"X\":1.0, \"Z\": 3 } } ]\n  }\n}\n";
        var WithId=SceneJsonTextMerge.Merge(Old,"{ \"root\": { \"id\": \"a\", \"name\": \"Root\", \"components\": [ { \"type\": \"T\", \"members\": { \"X\": 1, \"Z\": 3 } } ], \"children\": [] } }");
        Assert.Equal(Old.Replace("    \"name\": \"Root\",","    \"id\": \"a\",\n    \"name\": \"Root\","),WithId);
        var WithMember=SceneJsonTextMerge.Merge(Old,"{ \"root\": { \"name\": \"Root\", \"components\": [ { \"type\": \"T\", \"members\": { \"X\": 1, \"Y\": [], \"Z\": 3 } } ] } }");
        Assert.Equal(Old.Replace("\"X\":1.0,","\"X\":1.0, \"Y\": [],"),WithMember);
    }

    [Fact]
    public void DroppedKey_ReplacesTheObject(){
        var Edited=Canonical.Replace("\"members\": { \"X\": 1, \"Y\": 2 }","\"members\": { \"X\": 1 }");
        var Merged=SceneJsonTextMerge.Merge(HandFormatted,Edited);
        Assert.Contains("\"members\": { \"X\": 1 }",Merged);
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
