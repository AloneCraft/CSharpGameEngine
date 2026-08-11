using CSharpGameEngine.Compiler.Hlsl;

namespace CSharpGameEngine.Compiler.Hlsl.Tests;

public class HlslCompilerTests
{
    private static string TriangleShaderPath => Path.Combine(AppContext.BaseDirectory, "Shaders", "Triangle.hlsl");

    private static string SyntaxErrorShaderPath => Path.Combine(AppContext.BaseDirectory, "TestShaders", "SyntaxError.hlsl");

    private static string ModelShaderPath => Path.Combine(AppContext.BaseDirectory, "Shaders", "Model.hlsl");

    [Fact]
    public void Compile_ValidVertexShader_Succeeds()
    {
        HlslCompileResult Result = HlslCompiler.Compile(new HlslCompileRequest
        {
            FilePath = TriangleShaderPath,
            EntryPoint = "VSMain",
            TargetProfile = "vs_6_0",
        });

        Assert.True(Result.Success, string.Join(Environment.NewLine, Result.Diagnostics));
        Assert.NotEmpty(Result.Bytecode);
    }

    [Fact]
    public void Compile_ValidPixelShader_Succeeds()
    {
        HlslCompileResult Result = HlslCompiler.Compile(new HlslCompileRequest
        {
            FilePath = TriangleShaderPath,
            EntryPoint = "PSMain",
            TargetProfile = "ps_6_0",
        });

        Assert.True(Result.Success, string.Join(Environment.NewLine, Result.Diagnostics));
        Assert.NotEmpty(Result.Bytecode);
    }

    [Fact]
    public void Compile_MissingEntryPoint_Fails()
    {
        HlslCompileResult Result = HlslCompiler.Compile(new HlslCompileRequest
        {
            FilePath = TriangleShaderPath,
            EntryPoint = "DoesNotExist",
            TargetProfile = "vs_6_0",
        });

        Assert.False(Result.Success);
        Assert.Empty(Result.Bytecode);
        Assert.NotEmpty(Result.Diagnostics);
    }

    [Fact]
    public void Compile_SyntaxError_ReportsDiagnostics()
    {
        HlslCompileResult Result = HlslCompiler.Compile(new HlslCompileRequest
        {
            FilePath = SyntaxErrorShaderPath,
            EntryPoint = "VSMain",
            TargetProfile = "vs_6_0",
        });

        Assert.False(Result.Success);
        Assert.Empty(Result.Bytecode);
        Assert.Contains(Result.Diagnostics, D => D.Severity == HlslDiagnosticSeverity.Error);
    }

    [Fact]
    public void Compile_WithDebugInfo_StillSucceeds()
    {
        HlslCompileResult Result = HlslCompiler.Compile(new HlslCompileRequest
        {
            FilePath = TriangleShaderPath,
            EntryPoint = "PSMain",
            TargetProfile = "ps_6_0",
            EnableDebugInfo = true,
        });

        Assert.True(Result.Success, string.Join(Environment.NewLine, Result.Diagnostics));
        Assert.NotEmpty(Result.Bytecode);
    }

    [Fact]
    public void Compile_ModelVertexShader_Succeeds()
    {
        HlslCompileResult Result = HlslCompiler.Compile(new HlslCompileRequest
        {
            FilePath = ModelShaderPath,
            EntryPoint = "VSMain",
            TargetProfile = "vs_6_0",
        });

        Assert.True(Result.Success, string.Join(Environment.NewLine, Result.Diagnostics));
        Assert.NotEmpty(Result.Bytecode);
    }

    [Fact]
    public void Compile_ModelPixelShader_Succeeds()
    {
        HlslCompileResult Result = HlslCompiler.Compile(new HlslCompileRequest
        {
            FilePath = ModelShaderPath,
            EntryPoint = "PSMain",
            TargetProfile = "ps_6_0",
        });

        Assert.True(Result.Success, string.Join(Environment.NewLine, Result.Diagnostics));
        Assert.NotEmpty(Result.Bytecode);
    }
}
