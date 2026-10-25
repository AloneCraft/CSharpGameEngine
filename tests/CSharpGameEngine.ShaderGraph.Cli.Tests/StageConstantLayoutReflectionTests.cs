using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using CSharpGameEngine.Compiler.Hlsl;
using CSharpGameEngine.Rendering.Pipeline;
using CSharpGameEngine.ShaderArtifacts;
using CSharpGameEngine.ShaderGraph;
using CSharpGameEngine.ShaderGraph.Cli;
namespace CSharpGameEngine.ShaderGraph.Cli.Tests;
public sealed class StageConstantLayoutReflectionTests {
    [Theory]
    [InlineData("VSMain","vs_6_0",true)]
    [InlineData("PSMain","ps_6_0",true)]
    [InlineData("ObserverVSMain","vs_6_0",true)]
    [InlineData("SurfacePrepassPSMain","ps_6_0",true)]
    [InlineData("LumenCardPSMain","ps_6_0",false)]
    public void NativeStageConstantsMatchActualDxcReflection(string Entry,string Profile,bool UsesFrame){
        var Graph=ShaderGraphDependencyComposition.Resolve(Path.Combine(AppContext.BaseDirectory,"Graphs","Stage.cgeshadergraph")).Composed;
        var Source=HlslSyntaxGraphConverter.ExportWithSourceMap(Graph).Hlsl+StagePresentationShaderSource.Source;
        var Buffers=Compile(Source,Entry,Profile);
        if(UsesFrame){ Compare<StageFrameConstants>(Assert.Single(Buffers,Buffer=>Buffer.Name=="FrameCB")); }
        else { Assert.DoesNotContain(Buffers,Buffer=>Buffer.Name=="FrameCB"); }
        Compare<StageObjectConstants>(Assert.Single(Buffers,Buffer=>Buffer.Name=="ObjectCB"));
    }
    [Fact]
    public void LumenUsesTheSameFrameLayoutWithItsExplicitInverseMatrixAlias(){
        var Buffers=Compile(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Graphs","Lumen.hlsl")),"CSMain","cs_6_0");
        Compare<StageFrameConstants>(Assert.Single(Buffers,Buffer=>Buffer.Name=="FrameCB"));
    }
    private static IReadOnlyList<HlslConstantBufferLayout>Compile(string Source,string Entry,string Profile){
        var PathValue=Path.GetTempFileName();
        try {
            File.WriteAllText(PathValue,Source);
            var Result=HlslCompiler.Compile(new HlslCompileRequest { FilePath=PathValue,EntryPoint=Entry,TargetProfile=Profile });
            Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
            Assert.NotEmpty(Result.DxcReflection);
            return HlslShaderReflection.ReadConstantBuffers(Result.DxcReflection);
        } finally { File.Delete(PathValue); }
    }
    private static void Compare<T>(HlslConstantBufferLayout Buffer) where T:struct {
        Assert.Equal((uint)Marshal.SizeOf<T>(),Buffer.Size);
        var Fields=typeof(T).GetFields(BindingFlags.Instance|BindingFlags.Public);
        Assert.Equal(Fields.Length,Buffer.Variables.Count);
        foreach(var Variable in Buffer.Variables){
            var Name=Variable.Name=="StageInverseViewProjection"?"InverseViewProjection":Variable.Name;
            var Field=Assert.Single(Fields,Field=>Field.Name==Name);
            Assert.Equal((uint)Marshal.OffsetOf<T>(Name).ToInt64(),Variable.Offset);
            Assert.Equal((uint)Marshal.SizeOf(Field.FieldType),Variable.Size);
            if(Field.FieldType==typeof(Matrix4x4)||Field.FieldType==typeof(StageMatrixArray4)){
                Assert.Equal("D3D_SVC_MATRIX_ROWS",Variable.Class);
                Assert.Equal(4u,Variable.Rows);
                Assert.Equal(4u,Variable.Columns);
            }
        }
    }
}
