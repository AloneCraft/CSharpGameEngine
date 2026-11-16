using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.CodeAnalysis.Tests;
public sealed class PortableSnapshotCodecTests {
    [Theory]
    [InlineData("{\"Value\":\"1.000000059604644775390625\"}",true,"1")]
    [InlineData("{\"Value\":\"1.25\"}",true,"1.25")]
    [InlineData("{\"Value\":1.25}",false,"")]
    [InlineData("{\"Value\":\"0.1\"}",false,"")]
    [InlineData("{ \"Value\":\"1.25\"}",false,"")]
    public void GeneratedRealValuesValidateSourceBeforeRounding(string Json,bool Expected,string Value){
        // 版 1 の JSON 数値（"{\"Value\":1.25}"）は、ADR-0098 §5 の D により読まない。
        const string Source="namespace Sample; public struct Data {public float Value;}";
        var Assembly=Compile(Source);
        var Result=(string)GameSystemGeneratorTestHelper.Invoke(Assembly,"Sample.Probe","Read",Json)!;
        Assert.Equal(Expected?Value:"REJECTED",Result);
    }
    [Fact]
    public void ConstructorNormalizationCannotSilentlyReplacePersistedState(){
        const string Source="namespace Sample; public readonly struct Data {public float Value{get;} public Data(float Value){this.Value=Value<0?0:Value;}}";
        var Assembly=Compile(Source);
        Assert.Equal("REJECTED",GameSystemGeneratorTestHelper.Invoke(Assembly,"Sample.Probe","Read","{\"Value\":\"-1\"}"));
        Assert.Equal("1.25",GameSystemGeneratorTestHelper.Invoke(Assembly,"Sample.Probe","Read","{\"Value\":\"1.25\"}"));
    }
    private static System.Reflection.Assembly Compile(string Source){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([(null,Source)]);
        var Shape=SnapshotGraphCodecBuilder.BuildPortable(Compilation.GetTypeByMetadataName("Sample.Data")!);
        Assert.NotNull(Shape);
        var Probe="namespace Sample; public static class Probe { private static byte[] Serialize(in Data Value) {"+Shape.Value.Writer+"}\n"
            +"private static bool Deserialize(byte[] Payload,out Data Value,out string Error) {"+Shape.Value.Reader.Replace("$SERIALIZE$","Serialize")+"}\n"
            +"public static string Read(string Json) {if(!Deserialize(global::System.Text.Encoding.UTF8.GetBytes(Json),out var Value,out _))return \"REJECTED\";return Value.Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture);}}";
        Compilation=Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(Probe,(CSharpParseOptions)Compilation.SyntaxTrees.First().Options));
        return GameSystemGeneratorTestHelper.CompileAndLoad(Compilation);
    }
}
