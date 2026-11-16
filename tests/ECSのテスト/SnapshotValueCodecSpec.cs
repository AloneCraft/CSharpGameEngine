using System.Text;
using System.Text.Json;
namespace CSharpGameEngine.Entities.Tests;
public sealed class SnapshotValueCodecSpec {
    [Fact]
    public void CanonicalSourceCanRoundWithoutBeingMistakenForCorruptData(){
        const string Text="{\"Value\":\"1.000000059604644775390625\"}";
        var Bytes=Encoding.UTF8.GetBytes(Text);using var Document=JsonDocument.Parse(Bytes);
        var Root=Document.RootElement;EcsSnapshotValueCodec.AssertObject(Root,"Value");
        var Element=Root.GetProperty("Value");
        Assert.Equal(1f,EcsSnapshotValueCodec.ReadSingle(Element));
        Assert.Equal(1d+Math.ScaleB(1d,-24),EcsSnapshotValueCodec.ReadDouble(Element));
        EcsSnapshotValueCodec.AssertCanonicalJson(Bytes,Root);
    }
    [Theory]
    [InlineData("{\"Value\":\"0.1\"}")]
    [InlineData("{\"Value\":\"1.250\"}")]
    [InlineData("{ \"Value\":\"1.25\"}")]
    [InlineData("{\"Value\":\"1\\u002e25\"}")]
    [InlineData("{\"Value\":\"1.25\",\"Value\":\"1.25\"}")]
    [InlineData("{\"Other\":\"1.25\"}")]
    public void InvalidOrNoncanonicalSourceIsRejectedBeforePublication(string Text){
        var Bytes=Encoding.UTF8.GetBytes(Text);using var Document=JsonDocument.Parse(Bytes);
        Assert.Throws<JsonException>(()=>{
            var Root=Document.RootElement;EcsSnapshotValueCodec.AssertObject(Root,"Value");
            EcsSnapshotValueCodec.ReadSingle(Root.GetProperty("Value"));EcsSnapshotValueCodec.AssertCanonicalJson(Bytes,Root);
        });
    }
    [Theory]
    [InlineData("1.25")]
    [InlineData("1.250")]
    public void TheVersionOneJsonNumberIsRejected(string Text){
        // ADR-0098 §5 の D：版 1 の JSON 数値（共通の数値文字列になる前の形）は読まない。
        using var Document=JsonDocument.Parse(Text);
        Assert.Throws<JsonException>(()=>EcsSnapshotValueCodec.ReadSingle(Document.RootElement));
        Assert.Throws<JsonException>(()=>EcsSnapshotValueCodec.ReadDouble(Document.RootElement));
    }
}
