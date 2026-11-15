using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
namespace CSharpGameEngine.CodeAnalysis.Tests;
public sealed class NumericProfileCompatibilityAnalyzerTests {
    [Theory]
    [InlineData("Numerics","float32","float32",false)]
    [InlineData("World","fixed-128-64","float32",true)]
    [InlineData("Numerics",null,"float32",true)]
    [InlineData("World",null,"float64",true)]
    [InlineData("Entities",null,"float64",true)]
    [InlineData("IndependentLibrary",null,"float64",false)]
    [InlineData("SharedRuntime","fixed-64-32","fixed-64-31",true)]
    [InlineData("Numerics","float32",null,false)]
    public async Task RejectsIncompatibleDependenciesWithoutRejectingIndependentLibraries(string Name,string?Profile,string?Expected,bool Rejected){
        var References=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(static Path=>!EngineAssemblyFiles.IsEngineAssembly(Path))
            .Select(static Path=>MetadataReference.CreateFromFile(Path)).ToArray();
        var Source=Profile is null?"public sealed class Dependency {}":"[assembly:System.Reflection.AssemblyMetadata(\"CgeNumericProfile\",\""+Profile+"\")] public sealed class Dependency {}";
        var Library=CSharpCompilation.Create(Name,[CSharpSyntaxTree.ParseText(Source)],References,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var Stream=new MemoryStream();var Emitted=Library.Emit(Stream);Assert.True(Emitted.Success,string.Join(Environment.NewLine,Emitted.Diagnostics));
        var Compilation=CSharpCompilation.Create("Consumer",[CSharpSyntaxTree.ParseText("public sealed class Consumer {}")],[..References,MetadataReference.CreateFromImage(Stream.ToArray())],new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var Diagnostics=await Compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new NumericProfileCompatibilityAnalyzer()),new AnalyzerOptions([],new Options(Expected))).GetAnalyzerDiagnosticsAsync();
        if(Rejected){ var Error=Assert.Single(Diagnostics);Assert.Equal("NUM002",Error.Id);Assert.Equal(DiagnosticSeverity.Error,Error.Severity);Assert.Contains(Name,Error.GetMessage(),StringComparison.Ordinal); }else{ Assert.Empty(Diagnostics); }
    }
    private sealed class Options(string?Profile):AnalyzerConfigOptionsProvider {
        private sealed class Values(string?Profile):AnalyzerConfigOptions {
            public override bool TryGetValue(string Key,[System.Diagnostics.CodeAnalysis.NotNullWhen(true)]out string?Value){ Value=Key=="build_property.CgeNumericProfileId"?Profile:null;return Value is not null; }
        }
        private readonly AnalyzerConfigOptions Data=new Values(Profile);
        public override AnalyzerConfigOptions GlobalOptions=>this.Data;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree Tree)=>this.Data;
        public override AnalyzerConfigOptions GetOptions(AdditionalText Text)=>this.Data;
    }
}
