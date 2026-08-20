using CSharpGameEngine.Compiler.Hlsl;

namespace CSharpGameEngine.Compiler.Hlsl.Tests;

public class HlslCompilerTests {
    private static string TriangleShaderPath=>Path.Combine(AppContext.BaseDirectory,"Shaders","Triangle.hlsl");

    private static string SyntaxErrorShaderPath=>Path.Combine(AppContext.BaseDirectory,"TestShaders","SyntaxError.hlsl");

    private static string ModelShaderPath=>Path.Combine(AppContext.BaseDirectory,"Shaders","Model.hlsl");
    private static string StageShaderPath=>Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.hlsl");

    [Fact]
    public void Compile_ValidVertexShader_Succeeds(){
        var Result=HlslCompiler.Compile(new HlslCompileRequest {
                                                                   FilePath=TriangleShaderPath,
                                                                   EntryPoint="VSMain",
                                                                   TargetProfile="vs_6_0",
                                                               });

        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        Assert.NotEmpty(Result.Bytecode);
    }

    [Fact]
    public void Compile_ValidPixelShader_Succeeds(){
        var Result=HlslCompiler.Compile(new HlslCompileRequest {
                                                                   FilePath=TriangleShaderPath,
                                                                   EntryPoint="PSMain",
                                                                   TargetProfile="ps_6_0",
                                                               });

        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        Assert.NotEmpty(Result.Bytecode);
    }

    [Fact]
    public void Compile_MissingEntryPoint_Fails(){
        var Result=HlslCompiler.Compile(new HlslCompileRequest {
                                                                   FilePath=TriangleShaderPath,
                                                                   EntryPoint="DoesNotExist",
                                                                   TargetProfile="vs_6_0",
                                                               });

        Assert.False(Result.Success);
        Assert.Empty(Result.Bytecode);
        Assert.NotEmpty(Result.Diagnostics);
    }

    [Fact]
    public void Compile_SyntaxError_ReportsDiagnostics(){
        var Result=HlslCompiler.Compile(new HlslCompileRequest {
                                                                   FilePath=SyntaxErrorShaderPath,
                                                                   EntryPoint="VSMain",
                                                                   TargetProfile="vs_6_0",
                                                               });

        Assert.False(Result.Success);
        Assert.Empty(Result.Bytecode);
        Assert.Contains(Result.Diagnostics,D=>D.Severity==HlslDiagnosticSeverity.Error);
    }

    [Fact]
    public void Compile_WithDebugInfo_StillSucceeds(){
        var Result=HlslCompiler.Compile(new HlslCompileRequest {
                                                                   FilePath=TriangleShaderPath,
                                                                   EntryPoint="PSMain",
                                                                   TargetProfile="ps_6_0",
                                                                   EnableDebugInfo=true,
                                                               });

        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        Assert.NotEmpty(Result.Bytecode);
    }

    [Fact]
    public void Compile_ModelVertexShader_Succeeds(){
        var Result=HlslCompiler.Compile(new HlslCompileRequest {
                                                                   FilePath=ModelShaderPath,
                                                                   EntryPoint="VSMain",
                                                                   TargetProfile="vs_6_0",
                                                               });

        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        Assert.NotEmpty(Result.Bytecode);
    }

    [Fact]
    public void Compile_ModelPixelShader_Succeeds(){
        var Result=HlslCompiler.Compile(new HlslCompileRequest {
                                                                   FilePath=ModelShaderPath,
                                                                   EntryPoint="PSMain",
                                                                   TargetProfile="ps_6_0",
                                                               });

        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        Assert.NotEmpty(Result.Bytecode);
    }

    [Fact]
    public void Compile_StagePostProcessShaders_Succeeds(){
        var Vertex=HlslCompiler.Compile(new HlslCompileRequest {
                                                                   FilePath=StageShaderPath,
                                                                   EntryPoint="FullscreenVS",
                                                                   TargetProfile="vs_6_0",
                                                               });
        var Pixel=HlslCompiler.Compile(new HlslCompileRequest {
                                                                  FilePath=StageShaderPath,
                                                                  EntryPoint="PostProcessPS",
                                                                  TargetProfile="ps_6_0",
                                                              });

        Assert.True(Vertex.Success,string.Join(Environment.NewLine,Vertex.Diagnostics));
        Assert.True(Pixel.Success,string.Join(Environment.NewLine,Pixel.Diagnostics));
        Assert.NotEmpty(Vertex.Bytecode);
        Assert.NotEmpty(Pixel.Bytecode);
    }
}
