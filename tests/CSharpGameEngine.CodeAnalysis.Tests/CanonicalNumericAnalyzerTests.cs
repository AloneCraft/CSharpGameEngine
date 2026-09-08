using System.Collections.Immutable;
using CSharpGameEngine.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
namespace CSharpGameEngine.CodeAnalysis.Tests;
public sealed class CanonicalNumericAnalyzerTests {
    private static readonly MetadataReference[] References=[
        .. AppDomain.CurrentDomain.GetAssemblies().Where(static Assembly=>!Assembly.IsDynamic&&!string.IsNullOrEmpty(Assembly.Location)).Select(static Assembly=>MetadataReference.CreateFromFile(Assembly.Location)),
    ];
    [Fact]
    public void EngineOwnedDoubleAndBigIntegerDeclarationsAreErrors(){
        var Diagnostics=Analyze("""
            using System.Numerics;
            namespace CSharpGameEngine.Runtime.Sample;
            public sealed class State {
                private double Field;
                public BigInteger Value { get;set; }
                public double Compute(double Input){ double Local=Input;return Local; }
            }
            """);
        Assert.Equal(5,Diagnostics.Count(static Diagnostic=>Diagnostic.Id=="NUM001"));
    }
    [Fact]
    public void LiteralsCastsTypeOfPatternsAndStaticBigIntegerUseAreErrors(){
        var Diagnostics=Analyze("""
            using System.Numerics;
            namespace CSharpGameEngine.Runtime.Sample;
            public sealed class State {
                public object[] Compute(object Value){
                    var A=1.0;
                    var B=1m;
                    var C=(float)(double)1f;
                    var D=typeof(double);
                    var E=Value is decimal;
                    var F=BigInteger.One;
                    return [A,B,C,D,E,F];
                }
            }
            """);
        Assert.Equal(6,Diagnostics.Count(static Diagnostic=>Diagnostic.Id=="NUM001"));
    }
    [Fact]
    public void InferredForbiddenResultsFromFrameworkApisAreErrors(){
        var Diagnostics=Analyze("""
            namespace CSharpGameEngine.Runtime.Sample;
            public sealed class State {
                public object[] Compute(){
                    var Converted=System.Convert.ToDouble("1");
                    var Pi=System.Math.PI;
                    return [Converted,Pi];
                }
            }
            """);
        Assert.Equal(2,Diagnostics.Count(static Diagnostic=>Diagnostic.Id=="NUM001"));
    }
    [Fact]
    public void ExternalFrameworkMemberProjectionIsAccepted(){
        var ExternalReference=CreateReference("""
            namespace System.Windows {
                public sealed class ExternalPoint { public double X=>0d; }
            }
            ""","WindowsBase");
        var Diagnostics=Analyze("""
            namespace CSharpGameEngine.Runtime.Sample;
            public sealed class Projection {
                private readonly System.Windows.ExternalPoint Point=new();
                public float Read()=>(float)this.Point.X;
            }
            """,AdditionalReferences:[ExternalReference]);
        Assert.Empty(Diagnostics);
    }
    [Fact]
    public void ExternalFrameworkResultsCannotRemainAsInferredForbiddenState(){
        var ExternalReference=CreateReference("""
            namespace System.Windows {
                public sealed class ExternalPoint { public double X=>0d; }
            }
            ""","WindowsBase");
        var Diagnostics=Analyze("""
            namespace CSharpGameEngine.Runtime.Sample;
            public sealed class Projection {
                private readonly System.Windows.ExternalPoint Point=new();
                public object Read(){ var Leaked=this.Point.X;return Leaked; }
            }
            """,AdditionalReferences:[ExternalReference]);
        Assert.Single(Diagnostics.Where(static Diagnostic=>Diagnostic.Id=="NUM001"));
    }
    [Fact]
    public void ExternalFrameworkResultsMayFlowDirectlyIntoMarkedProjection(){
        var ExternalReference=CreateReference("""
            namespace System.Windows {
                public sealed class ExternalPoint { public double X=>0d; }
            }
            ""","WindowsBase");
        var Diagnostics=Analyze("""
            namespace CSharpGameEngine.Contracts {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class ExternalNumericAdapterAttribute:System.Attribute { }
            }
            namespace CSharpGameEngine.Runtime.Sample {
                public sealed class Projection {
                    private readonly System.Windows.ExternalPoint Point=new();
                    public float Read()=>Project(this.Point.X);
                    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
                    private static float Project(double Value)=>checked((float)Value);
                }
            }
            """,AdditionalReferences:[ExternalReference]);
        Assert.Empty(Diagnostics);
    }
    [Fact]
    public void FloatLogicalScalarAndMarkedExternalAdaptersAreAccepted(){
        var Diagnostics=Analyze("""
            namespace CSharpGameEngine.Contracts {
                [System.AttributeUsage(System.AttributeTargets.All)]
                public sealed class ExternalNumericAdapterAttribute:System.Attribute { }
            }
            namespace CSharpGameEngine.Runtime.Sample {
                public readonly struct LogicalScalar { }
                public sealed class State {
                    private float Projected;
                    private LogicalScalar Position;
                    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
                    public double Convert(double Value){ double Checked=Value;return Checked; }
                }
            }
            """);
        Assert.Empty(Diagnostics);
    }
    [Fact]
    public void AdapterMarkerDoesNotPermitEngineOwnedForbiddenState(){
        var Diagnostics=Analyze("""
            namespace CSharpGameEngine.Contracts {
                [System.AttributeUsage(System.AttributeTargets.All)]
                public sealed class ExternalNumericAdapterAttribute:System.Attribute { }
            }
            namespace CSharpGameEngine.Runtime.Sample {
                [CSharpGameEngine.Contracts.ExternalNumericAdapter]
                public sealed class LeakingAdapter {
                    private double Stored;
                    public double Convert(double Value){
                        this.Stored=Value;
                        return this.Stored;
                    }
                }
            }
            """);
        Assert.NotEmpty(Diagnostics.Where(static Diagnostic=>Diagnostic.Id=="NUM001"));
    }
    [Fact]
    public void ImplicitExternalFrameworkInterfaceImplementationIsAccepted(){
        var ExternalReference=CreateReference("""
            namespace System.Windows.Data {
                public interface IExternalConverter { double Convert(double Value); }
            }
            ""","PresentationFramework");
        var Diagnostics=Analyze("""
            namespace CSharpGameEngine.Runtime.Sample {
                public sealed class Converter:System.Windows.Data.IExternalConverter {
                    public double Convert(double Value)=>double.IsNaN(Value)?0d:Value;
                }
            }
            """,AdditionalReferences:[ExternalReference]);
        Assert.Empty(Diagnostics);
    }
    [Fact]
    public void TestAssembliesRemainAvailableForIndependentHighPrecisionOracles(){
        var Diagnostics=Analyze("using System.Numerics; public sealed class Oracle { public double Value; public BigInteger Raw; }","CSharpGameEngine.Numerics.Tests");
        Assert.Empty(Diagnostics);
    }
    private static ImmutableArray<Diagnostic> Analyze(string Source,string AssemblyName="CSharpGameEngine.Runtime.NumericPolicySample",IReadOnlyList<MetadataReference>? AdditionalReferences=null){
        var Compilation=CSharpCompilation.Create(AssemblyName,[CSharpSyntaxTree.ParseText(Source,new CSharpParseOptions(LanguageVersion.Latest))],[.. References,.. AdditionalReferences??[]],new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return Compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new CanonicalNumericAnalyzer())).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }
    private static MetadataReference CreateReference(string Source,string AssemblyName){
        var Compilation=CSharpCompilation.Create(AssemblyName,[CSharpSyntaxTree.ParseText(Source,new CSharpParseOptions(LanguageVersion.Latest))],References,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var Stream=new MemoryStream();
        var Result=Compilation.Emit(Stream);
        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        return MetadataReference.CreateFromImage(Stream.ToArray());
    }
}
