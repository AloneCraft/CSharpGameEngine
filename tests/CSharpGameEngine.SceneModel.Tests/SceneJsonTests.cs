using CSharpGameEngine.SceneAnalysis.Json;
namespace CSharpGameEngine.SceneModel.Tests;
/// <summary>ADR-0070: the dependency-free scene JSON reader, writer and number spelling.</summary>
public sealed class SceneJsonTests {
    [Fact]
    public void Reader_KeepsOrderSpansAndNumberText(){
        const string Text="{\n  \"b\": 1.50,\n  \"a\": [true, null, \"x\\u0041\"],\n  \"c\": -2E-3\n}\n";
        var Root=SceneJsonReader.Parse(Text);
        Assert.Equal(["b","a","c"],Root.Properties.Select(static Property=>Property.Name));
        var B=Root.Get("b")!;
        Assert.Equal("1.50",B.Text);
        Assert.Equal("1.50",Text.Substring(B.Start,B.End-B.Start));
        Assert.Equal("xA",Root.Get("a")!.Items[2].Text);
        Assert.Equal(SceneJsonKind.Null,Root.Get("a")!.Items[1].Kind);
        Assert.Equal("-2E-3",Root.Get("c")!.Text);
        Assert.Equal((4,8),SceneJsonReader.LineAndColumn(Text,Root.Get("c")!.Start));
    }
    [Theory]
    [InlineData("{\"a\":1,\"a\":2}","Duplicate")]
    [InlineData("{\"a\":1,}","property name")]
    [InlineData("[1,]","Unexpected")]
    [InlineData("{\"a\":01}","Expected")]
    [InlineData("// c\n{}","Unexpected")]
    [InlineData("{\"a\":1}x","after")]
    [InlineData("{\"a\":\"\u0001\"}","Control")]
    [InlineData("{\"a\":１}","Unexpected")]
    public void Reader_RejectsNonStrictJson(string Text,string MessagePart){
        var Error=Assert.Throws<SceneJsonException>(()=>SceneJsonReader.Parse(Text));
        Assert.Contains(MessagePart,Error.Message,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Writer_LaysOutMultilineContainersAndInlineValuesDeterministically(){
        var Document=SceneJsonOut.Object(Multiline:true)
            .Add("name",SceneJsonOut.String("Floor \"A\""))
            .Add("members",SceneJsonOut.Object(Multiline:true)
                .Add("Position",SceneJsonOut.Object().Add("R",SceneJsonOut.Number("0")).Add("U",SceneJsonOut.Number("1.5")))
                .Add("Tags",SceneJsonOut.Array().Add(SceneJsonOut.Boolean(true)).Add(SceneJsonOut.Null())))
            .Add("children",SceneJsonOut.Array(Multiline:true));
        var Text=SceneJsonWriter.Write(Document);
        Assert.Equal("{\n  \"name\": \"Floor \\\"A\\\"\",\n  \"members\": {\n    \"Position\": { \"R\": 0, \"U\": 1.5 },\n    \"Tags\": [ true, null ]\n  },\n  \"children\": []\n}\n",Text);
        var Reparsed=SceneJsonReader.Parse(Text);
        Assert.Equal("Floor \"A\"",Reparsed.Get("name")!.Text);
    }
    [Theory]
    [InlineData(0f,"0")]
    [InlineData(4f,"4")]
    [InlineData(0.2f,"0.2")]
    [InlineData(3.0201263f,"3.0201263")]
    [InlineData(1.5707964f,"1.5707964")]
    [InlineData(44.765083f,"44.765083")]
    [InlineData(-5.722046E-06f,"-5.722046E-06")]
    public void SingleSpelling_IsTheShortestRoundTripText(float Value,string Expected){
        var Text=SceneJsonNumbers.FormatSingle(Value);
        Assert.Equal(Expected,Text);
        Assert.Equal(Value==0f?0f:Value,float.Parse(Text,System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(SceneJsonKind.Number,SceneJsonReader.Parse(Text).Kind);
    }
    [Fact]
    public void NegativeZero_HasTheSingleSpellingOfZero()=>Assert.Equal("0",SceneJsonNumbers.FormatSingle(-0f));
    [Fact]
    public void SingleSpelling_RoundTripsRandomValues(){
        var Random=new Random(5489);
        for(var Index=0;Index<20000;Index++){
            var Value=BitConverter.Int32BitsToSingle(Random.Next());
            if(!float.IsFinite(Value)){ continue; }
            var Text=SceneJsonNumbers.FormatSingle(Value);
            Assert.Equal(Value==0f?0f:Value,float.Parse(Text,System.Globalization.CultureInfo.InvariantCulture));
            SceneJsonReader.Parse(Text);
        }
    }
}
