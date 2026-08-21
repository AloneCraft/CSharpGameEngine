using CSharpGameEngine.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// Transformの実際の移行より先に、プロジェクトごとの固定精度構成を生成器だけで完結して検証する。
/// このテストはEntitiesアセンブリに依存せず、入力側の属性を最小のソースとして定義するため、
/// Generatorの属性契約そのものが壊れたときに原因を局所化できる。
/// </summary>
public sealed class TransformPrecisionGeneratorTests {
    private const string TransformPrecisionAttributeSource="""
        namespace CSharpGameEngine.Entities;

        [global::System.AttributeUsage(global::System.AttributeTargets.Assembly,AllowMultiple=false,Inherited=false)]
        public sealed class TransformPrecisionAttribute:global::System.Attribute
        {
            public TransformPrecisionAttribute(int totalBits,int fractionalBits)
            {
                TotalBits=totalBits;
                FractionalBits=fractionalBits;
            }

            public int TotalBits { get; }

            public int FractionalBits { get; }
        }
        """;

    private static readonly MetadataReference[]References=[
        .. AppDomain.CurrentDomain.GetAssemblies()
            .Where(static Assembly=>
                !Assembly.IsDynamic
                && !string.IsNullOrEmpty(Assembly.Location)
                && !string.Equals(Assembly.GetName().Name,"CSharpGameEngine.Entities",StringComparison.Ordinal))
            .Select(static Assembly=>MetadataReference.CreateFromFile(Assembly.Location)),
    ];

    [Theory]
    [InlineData(64,1)]
    [InlineData(64,61)]
    [InlineData(128,1)]
    [InlineData(192,1)]
    [InlineData(256,1)]
    [InlineData(320,1)]
    [InlineData(384,173)]
    [InlineData(448,1)]
    [InlineData(512,1)]
    [InlineData(512,509)]
    public void SupportedConfiguration_EmitsSizedScalarAndWideStorage(int totalBits,int fractionalBits){
        var Result=RunGenerator(SourceWithAttributes($"[assembly: CSharpGameEngine.Entities.TransformPrecision({totalBits},{fractionalBits})]"));

        Assert.Empty(Result.Diagnostics);

        var Source=Assert.Single(Result.Results[0].GeneratedSources).SourceText.ToString();

        Assert.Contains($"public const int TotalBits = {totalBits};",Source,StringComparison.Ordinal);
        Assert.Contains($"public const int FractionalBits = {fractionalBits};",Source,StringComparison.Ordinal);
        Assert.Contains($"internal const int LimbCount = {totalBits/64};",Source,StringComparison.Ordinal);
        Assert.Contains($"internal const int WideLimbCount = {totalBits/32};",Source,StringComparison.Ordinal);
        Assert.Equal(totalBits/64,CountOccurrences(Source,"internal readonly ulong Limb"));
        Assert.Equal(totalBits/32,CountOccurrences(Source,"internal readonly ulong WideLimb"));
    }

    [Fact]
    public void Default384Over173Configuration_CompilesWithGeneratedStorage(){
        var Compilation=CreateCompilation(SourceWithAttributes(
            "[assembly: CSharpGameEngine.Entities.TransformPrecision(384,173)]"));
        GeneratorDriver Driver=CSharpGeneratorDriver.Create(new TransformPrecisionGenerator());

        Driver=Driver.RunGeneratorsAndUpdateCompilation(Compilation,out var OutputCompilation,out var GeneratorDiagnostics);

        Assert.Empty(GeneratorDiagnostics);
        Assert.Empty(OutputCompilation.GetDiagnostics().Where(static Diagnostic=>Diagnostic.Severity==DiagnosticSeverity.Error));
    }

    [Fact]
    public void Default384Over173Configuration_UsesExactFixedLimbLayout(){
        var Compilation=CreateCompilation(SourceWithAttributes(
            "[assembly: CSharpGameEngine.Entities.TransformPrecision(384,173)]"));
        GeneratorDriver Driver=CSharpGeneratorDriver.Create(new TransformPrecisionGenerator());

        Driver=Driver.RunGeneratorsAndUpdateCompilation(Compilation,out var OutputCompilation,out var GeneratorDiagnostics);

        Assert.Empty(GeneratorDiagnostics);

        using MemoryStream AssemblyStream=new();
        var EmitResult=OutputCompilation.Emit(AssemblyStream);
        Assert.True(EmitResult.Success,string.Join("\n",EmitResult.Diagnostics));

        var Assembly=System.Reflection.Assembly.Load(AssemblyStream.ToArray());
        var Scalar=Assembly.GetType("CSharpGameEngine.Entities.FixedScalar");
        var Wide=Assembly.GetType("CSharpGameEngine.Entities.FixedScalarWide");

        Assert.NotNull(Scalar);
        Assert.NotNull(Wide);

        Assert.Equal(384/8,System.Runtime.InteropServices.Marshal.SizeOf(Scalar!));
        Assert.Equal((384*2)/8,System.Runtime.InteropServices.Marshal.SizeOf(Wide!));
    }

    [Fact]
    public void ChangedConfiguration_RegeneratesDifferentLayout(){
        var First=GeneratedText(RunGenerator(SourceWithAttributes(
            "[assembly: CSharpGameEngine.Entities.TransformPrecision(384,173)]")));
        var Second=GeneratedText(RunGenerator(SourceWithAttributes(
            "[assembly: CSharpGameEngine.Entities.TransformPrecision(128,32)]")));

        Assert.NotEqual(First,Second);
        Assert.Contains("internal const int LimbCount = 6;",First,StringComparison.Ordinal);
        Assert.Contains("internal const int LimbCount = 2;",Second,StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(63,1,"TRFPREC002")]
    [InlineData(384,0,"TRFPREC003")]
    [InlineData(384,382,"TRFPREC003")]
    public void InvalidConfiguration_ReportsTargetedDiagnostic(int totalBits,int fractionalBits,string diagnosticId){
        var Result=RunGenerator(SourceWithAttributes($"[assembly: CSharpGameEngine.Entities.TransformPrecision({totalBits},{fractionalBits})]"));

        Assert.Contains(Result.Diagnostics,Diagnostic=>Diagnostic.Id==diagnosticId);
        Assert.Empty(Result.Results[0].GeneratedSources);
    }

    [Fact]
    public void DuplicateAssemblyConfiguration_ReportsTargetedDiagnosticAndDoesNotEmit(){
        var Result=RunGenerator(SourceWithAttributes(
            "[assembly: CSharpGameEngine.Entities.TransformPrecision(384,173)]\n[assembly: CSharpGameEngine.Entities.TransformPrecision(128,32)]"));

        Assert.Contains(Result.Diagnostics,Diagnostic=>Diagnostic.Id=="TRFPREC001");
        Assert.Empty(Result.Results[0].GeneratedSources);
    }

    [Fact]
    public void ProjectWithoutTransformPrecisionConfiguration_IsUnaffected(){
        var Result=RunGenerator(TransformPrecisionAttributeSource);

        Assert.Empty(Result.Diagnostics);
        Assert.Empty(Result.Results[0].GeneratedSources);
    }

    [Fact]
    public void GeneratedFixedScalar_ParsesAndCalculatesWithoutFloatingPointStorage(){
        var Compilation=CreateCompilation(SourceWithAttributes(
            "[assembly: CSharpGameEngine.Entities.TransformPrecision(384,173)]"));
        GeneratorDriver Driver=CSharpGeneratorDriver.Create(new TransformPrecisionGenerator());
        Driver=Driver.RunGeneratorsAndUpdateCompilation(Compilation,out var OutputCompilation,out var GeneratorDiagnostics);
        Assert.Empty(GeneratorDiagnostics);
        using MemoryStream AssemblyStream=new();
        var Emit=OutputCompilation.Emit(AssemblyStream);
        Assert.True(Emit.Success,string.Join("\n",Emit.Diagnostics));

        var Assembly=System.Reflection.Assembly.Load(AssemblyStream.ToArray());
        Type? Scalar=Assembly.GetType("CSharpGameEngine.Entities.FixedScalar");
        Assert.NotNull(Scalar);
        System.Reflection.MethodInfo? Parse=Scalar.GetMethod("ParseInvariant",new[]{typeof(string)});
        System.Reflection.MethodInfo? ToString=Scalar.GetMethod("ToStringInvariant",Type.EmptyTypes);
        System.Reflection.MethodInfo? FromDouble=Scalar.GetMethod("FromDouble",new[]{typeof(double)});
        System.Reflection.MethodInfo? Multiply=Scalar.GetMethod("op_Multiply",new[]{Scalar,Scalar});
        Assert.NotNull(Parse);
        Assert.NotNull(ToString);
        Assert.NotNull(FromDouble);
        Assert.NotNull(Multiply);

        object Eighth=Parse.Invoke(null,new object[]{"0.125"})!;
        object OnePointFive=FromDouble.Invoke(null,new object[]{1.5d})!;
        object Two=FromDouble.Invoke(null,new object[]{2d})!;
        object Product=Multiply.Invoke(null,new[]{OnePointFive,Two})!;

        Assert.Equal("0.125",ToString.Invoke(Eighth,null));
        Assert.Equal("3",ToString.Invoke(Product,null));
    }

    [Fact]
    public void GeneratedFixedRotationScalar_RejectsOutOfRangeQuaternionComponent(){
        var Compilation=CreateCompilation(SourceWithAttributes(
            "[assembly: CSharpGameEngine.Entities.TransformPrecision(128,32)]"));
        GeneratorDriver Driver=CSharpGeneratorDriver.Create(new TransformPrecisionGenerator());
        Driver=Driver.RunGeneratorsAndUpdateCompilation(Compilation,out var OutputCompilation,out _);
        using MemoryStream AssemblyStream=new();
        Assert.True(OutputCompilation.Emit(AssemblyStream).Success);
        var Assembly=System.Reflection.Assembly.Load(AssemblyStream.ToArray());
        Type? Scalar=Assembly.GetType("CSharpGameEngine.Entities.FixedScalar");
        Type? Rotation=Assembly.GetType("CSharpGameEngine.Entities.FixedRotationScalar");
        Assert.NotNull(Scalar);
        Assert.NotNull(Rotation);
        object TooLarge=Scalar.GetMethod("FromDouble",new[]{typeof(double)})!.Invoke(null,new object[]{1.1d})!;
        var Error=Assert.Throws<System.Reflection.TargetInvocationException>(()=>
            Rotation.GetMethod("FromFixedScalar",new[]{Scalar})!.Invoke(null,new[]{TooLarge}));
        Assert.IsType<ArgumentOutOfRangeException>(Error.InnerException);
    }

    private static GeneratorDriverRunResult RunGenerator(string source){
        GeneratorDriver Driver=CSharpGeneratorDriver.Create(new TransformPrecisionGenerator());
        return Driver.RunGenerators(CreateCompilation(source)).GetRunResult();
    }

    private static CSharpCompilation CreateCompilation(string source)=>
        CSharpCompilation.Create(
            "TransformPrecisionGeneratorTests",
            [CSharpSyntaxTree.ParseText(source,new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static string SourceWithAttributes(string attributes)=>
        attributes+"\n"+TransformPrecisionAttributeSource;

    private static string GeneratedText(GeneratorDriverRunResult result)=>
        Assert.Single(result.Results[0].GeneratedSources).SourceText.ToString();

    private static int CountOccurrences(string source,string value){
        var Count=0;
        var Start=0;

        while((Start=source.IndexOf(value,Start,StringComparison.Ordinal))>=0){
            Count++;
            Start+=value.Length;
        }

        return Count;
    }
}
