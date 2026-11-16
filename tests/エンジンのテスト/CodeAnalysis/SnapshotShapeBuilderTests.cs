using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.CodeAnalysis.Tests;
public sealed class SnapshotShapeBuilderTests {
    [Fact]
    public void ShapesSeparateRealValueMeaningFromStorage(){
        const string Source="public struct Data{public LogicalScalar X;public float Color;}";
        var Fixed=GetType(Source,"global using LogicalScalar=CSharpGameEngine.Numerics.LogicalScalar;namespace CSharpGameEngine.Numerics{public struct LogicalScalar{}}");
        var Native=GetType(Source,"global using LogicalScalar=System.Single;");
        Assert.Equal(SnapshotShapeBuilder.Build(Fixed),SnapshotShapeBuilder.Build(Native));
        Assert.Equal("Sample.Data{Color:CSharpGameEngine.Numerics.NumericValue;X:CSharpGameEngine.Numerics.NumericValue}",SnapshotShapeBuilder.Build(Native).Shape);
    }
    [Fact]
    public void ValueGenerationHonorsJsonFieldNamesAndIgnoredFields(){
        const string Attributes="""
            namespace System.Text.Json.Serialization {
                public sealed class JsonPropertyNameAttribute(string Name):System.Attribute {}
                public sealed class JsonIgnoreAttribute:System.Attribute { public int Condition{get;set;}=1; }
            }
            """;
        var Type=GetType("public struct Data{[System.Text.Json.Serialization.JsonIgnore]public int Hidden;[System.Text.Json.Serialization.JsonPropertyName(\"first\")]public int Z;public int A;}",Attributes);
        var (Writer,Reader)=SnapshotGraphCodecBuilder.BuildPortable(Type)!.Value;
        Assert.DoesNotContain("Hidden",Writer,StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden",Reader,StringComparison.Ordinal);
        Assert.Contains("Writer.WritePropertyName(\"first\"); Write1(Writer, Value.@Z)",Writer,StringComparison.Ordinal);
        Assert.Contains("Read1(Element.GetProperty(\"first\"), Writer)",Reader,StringComparison.Ordinal);
        Assert.True(Writer.IndexOf("\"A\"",StringComparison.Ordinal)<Writer.IndexOf("\"first\"",StringComparison.Ordinal));
        Assert.Null(SnapshotGraphCodecBuilder.BuildPortable(GetType("public struct Data{[System.Text.Json.Serialization.JsonIgnore(Condition=2)]public int Value;}",Attributes)));
    }
    [Theory]
    [InlineData("public struct Data{public int Z;public float A;}","Sample.Data{A:CSharpGameEngine.Numerics.NumericValue;Z:System.Int32}")]
    [InlineData("public struct Data{public int? Value;}","Sample.Data{Value:nullable(System.Int32)}")]
    [InlineData("public enum Kind:byte{One} public struct Data{public Kind Value;}","Sample.Data{Value:enum(Sample.Kind:System.Byte)}")]
    [InlineData("public readonly struct Data{public int Value{get;}public bool IsValid=>true;public Data(int Value){this.Value=Value;}}","Sample.Data{Value:System.Int32}")]
    public void ExtractsCanonicalSchemaWithoutRuntimeTypes(string Source,string Expected){
        var Result=SnapshotShapeBuilder.Build(GetType(Source));
        Assert.False(Result.Transient);
        Assert.Equal("Sample.Data",Result.TypeId);
        Assert.Equal(Expected,Result.Shape);
    }
    [Fact]
    public void TransientComponentsNeedNoSerializableMembers(){
        var Type=GetType("public struct Data:CSharpGameEngine.Entities.IRuntimeComponent{public object Native;}");
        var Result=SnapshotShapeBuilder.Build(Type);
        Assert.True(Result.Transient);
        Assert.Equal("transient",Result.Shape);
    }
    [Fact]
    public void PersistentReferenceFieldsAreRejectedAtAnalysisTime(){
        Assert.Throws<NotSupportedException>(()=>SnapshotShapeBuilder.Build(GetType("public struct Data{public object Native;}")));
    }
    [Fact]
    public void ConstructedValuesGenerateDirectConstructorCallsWithoutComputedProperties(){
        var Type=GetType("public readonly struct Data{public float Value{get;}public bool IsValid=>Value>=0;public Data(float Value){if(Value<0)throw new System.ArgumentOutOfRangeException(nameof(Value));this.Value=Value;}}");
        var (Writer,Reader)=SnapshotGraphCodecBuilder.BuildPortable(Type)!.Value;
        Assert.Contains("Writer.WritePropertyName(\"Value\"); Write1(Writer, Value.@Value)",Writer,StringComparison.Ordinal);
        Assert.DoesNotContain("IsValid",Writer,StringComparison.Ordinal);
        Assert.Contains("new global::Sample.Data(MemberValue2)",Reader,StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke",Reader,StringComparison.Ordinal);
    }
    private static INamedTypeSymbol GetType(string Source,string Prelude=""){
        var Text=Prelude+"namespace CSharpGameEngine.Entities{public interface IRuntimeComponent{}} namespace Sample{"+Source+"}";
        var Compilation=CSharpCompilation.Create("Schema",[CSharpSyntaxTree.ParseText(Text)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(Compilation.GetDiagnostics().Where(static Diagnostic=>Diagnostic.Severity==DiagnosticSeverity.Error));
        return Compilation.GetTypeByMetadataName("Sample.Data")!;
    }
}
