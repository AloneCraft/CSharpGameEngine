using Xunit;
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
    /// <summary>
    /// エントリーごとに、使うバッファの配置が L4 の構造体と一致する。使わないバッファは最適化で消えるので、無いことも確かめる
    ///（頂点は材質の表を読まず、ピクセルは描画ごとの定数ではなく頂点が渡した物体の番号を使う。カードは FrameCB を読まない）。
    /// </summary>
    [Theory]
    [InlineData("VSMain","vs_6_0",true,true,false)]
    [InlineData("PSMain","ps_6_0",true,false,true)]
    [InlineData("ObserverVSMain","vs_6_0",true,true,false)]
    [InlineData("PreviewPSMain","ps_6_0",true,true,true)]
    [InlineData("SurfacePrepassPSMain","ps_6_0",true,false,true)]
    [InlineData("LumenCardPSMain","ps_6_0",false,false,true)]
    public void NativeStageLayoutsMatchActualDxcReflection(string Entry,string Profile,bool UsesFrame,bool UsesDraw,bool UsesMaterials){
        var Graph=ShaderGraphDependencyComposition.Resolve(Path.Combine(AppContext.BaseDirectory,"Graphs","Stage.cgeshadergraph")).Composed;
        var Source=HlslSyntaxGraphConverter.ExportWithSourceMap(Graph).Hlsl+StagePresentationShaderSource.Source;
        var Buffers=Compile(Source,Entry,Profile);
        Assert.DoesNotContain(Buffers,Buffer=>Buffer.Name=="ObjectCB");
        CompareIfUsed<StageFrameConstants>(Buffers,"FrameCB",UsesFrame,ConstantBuffer:true);
        CompareIfUsed<StageDrawConstants>(Buffers,"DrawCB",UsesDraw,ConstantBuffer:true);
        CompareIfUsed<StageInstanceData>(Buffers,"Instances",true,ConstantBuffer:false);
        CompareIfUsed<StageMaterialData>(Buffers,"Materials",UsesMaterials,ConstantBuffer:false);
    }
    [Fact]
    public void ShadowVertexShaderReadsOnlyTheDrawConstantsAndTheInstanceTable(){
        var Graph=ShaderGraphDependencyComposition.Resolve(Path.Combine(AppContext.BaseDirectory,"Graphs","Stage.cgeshadergraph")).Composed;
        var Buffers=Compile(HlslSyntaxGraphConverter.ExportWithSourceMap(Graph).Hlsl+StagePresentationShaderSource.Source,"VSShadowMain","vs_6_0");
        CompareIfUsed<StageDrawConstants>(Buffers,"DrawCB",true,ConstantBuffer:true);
        CompareIfUsed<StageInstanceData>(Buffers,"Instances",true,ConstantBuffer:false);
        Assert.DoesNotContain(Buffers,Buffer=>Buffer.Name is "FrameCB" or "Materials");
    }
    [Fact]
    public void LumenUsesTheSameFrameLayoutWithItsExplicitInverseMatrixAlias(){
        var Buffers=Compile(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Graphs","Lumen.hlsl")),"CSMain","cs_6_0");
        Compare<StageFrameConstants>(Assert.Single(Buffers,Buffer=>Buffer.Name=="FrameCB"),ConstantBuffer:true);
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
    private static void CompareIfUsed<T>(IReadOnlyList<HlslConstantBufferLayout>Buffers,string Name,bool Used,bool ConstantBuffer) where T:struct {
        if(Used){ Compare<T>(Assert.Single(Buffers,Buffer=>Buffer.Name==Name),ConstantBuffer); }
        else { Assert.DoesNotContain(Buffers,Buffer=>Buffer.Name==Name); }
    }
    /// <summary>定数バッファの大きさは 16 バイト単位に切り上がる。構造化バッファの大きさは要素 1 件の大きさそのもの。</summary>
    private static void Compare<T>(HlslConstantBufferLayout Buffer,bool ConstantBuffer) where T:struct {
        var Size=(uint)Marshal.SizeOf<T>();
        Assert.Equal(ConstantBuffer?(Size+15u)/16u*16u:Size,Buffer.Size);
        var Fields=typeof(T).GetFields(BindingFlags.Instance|BindingFlags.Public);
        Assert.Equal(Fields.Length,Buffer.Variables.Count);
        foreach(var Variable in Buffer.Variables){
            var Name=Variable.Name=="StageInverseViewProjection"?"InverseViewProjection":Variable.Name.StartsWith("Draw",StringComparison.Ordinal)?Variable.Name["Draw".Length..]:Variable.Name;
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
