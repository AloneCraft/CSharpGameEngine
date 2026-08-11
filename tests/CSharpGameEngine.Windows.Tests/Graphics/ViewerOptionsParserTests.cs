namespace CSharpGameEngine.Graphics.D3D12.Tests;

public class ViewerOptionsParserTests
{
    [Fact]
    public void Parse_ModelOnly_UsesDefaults()
    {
        ViewerOptionsParseResult Result = ViewerOptionsParser.Parse(["--model", "Samples/Box.glb"]);

        Assert.True(Result.Success);
        Assert.NotNull(Result.Options);
        Assert.Equal("Samples/Box.glb", Result.Options!.ModelPath);
        Assert.Null(Result.Options.Frames);
        Assert.Equal(1280, Result.Options.Width);
        Assert.Equal(720, Result.Options.Height);
        Assert.False(Result.Options.Hidden);
        Assert.False(Result.Options.UseWarp);
        Assert.False(Result.Options.EnableDebugLayer);
        Assert.False(Result.Options.EnableGpuValidation);
        Assert.False(Result.Options.Verify);
    }

    [Fact]
    public void Parse_FullCombination_ParsesAllFlags()
    {
        ViewerOptionsParseResult Result = ViewerOptionsParser.Parse(
            ["--model", "Samples/Box.glb", "--hidden", "--frames", "3", "--warp", "--debug-layer", "--gpu-validation", "--verify"]);

        Assert.True(Result.Success);
        ViewerRunOptions Options = Result.Options!;
        Assert.Equal("Samples/Box.glb", Options.ModelPath);
        Assert.Equal(3, Options.Frames);
        Assert.True(Options.Hidden);
        Assert.True(Options.UseWarp);
        Assert.True(Options.EnableDebugLayer);
        Assert.True(Options.EnableGpuValidation);
        Assert.True(Options.Verify);
    }

    [Fact]
    public void Parse_MissingModel_Fails()
    {
        ViewerOptionsParseResult Result = ViewerOptionsParser.Parse(["--frames", "3"]);

        Assert.False(Result.Success);
        Assert.Null(Result.Options);
        Assert.Contains("--model", Result.Error);
    }

    [Fact]
    public void Parse_ModelMissingValue_Fails()
    {
        ViewerOptionsParseResult Result = ViewerOptionsParser.Parse(["--model"]);

        Assert.False(Result.Success);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("notanumber")]
    public void Parse_InvalidFrames_Fails(string Value)
    {
        ViewerOptionsParseResult Result = ViewerOptionsParser.Parse(["--model", "Samples/Box.glb", "--frames", Value]);

        Assert.False(Result.Success);
        Assert.Null(Result.Options);
    }

    [Theory]
    [InlineData("--width", "0")]
    [InlineData("--width", "-100")]
    [InlineData("--height", "0")]
    [InlineData("--height", "-1")]
    public void Parse_InvalidWidthOrHeight_Rejected(string Flag, string Value)
    {
        ViewerOptionsParseResult Result = ViewerOptionsParser.Parse(["--model", "Samples/Box.glb", Flag, Value]);

        Assert.False(Result.Success);
        Assert.Null(Result.Options);
    }

    [Fact]
    public void Parse_UnknownOption_Fails()
    {
        ViewerOptionsParseResult Result = ViewerOptionsParser.Parse(["--model", "Samples/Box.glb", "--not-a-real-option"]);

        Assert.False(Result.Success);
        Assert.Contains("--not-a-real-option", Result.Error);
    }
}
