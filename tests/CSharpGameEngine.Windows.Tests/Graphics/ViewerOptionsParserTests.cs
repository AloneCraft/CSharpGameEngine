namespace CSharpGameEngine.Graphics.D3D12.Tests;

public class ViewerOptionsParserTests
{
    [Fact]
    public void Parse_ModelOnly_UsesDefaults()
    {
        ViewerOptionsParseResult result = ViewerOptionsParser.Parse(["--model", "Samples/Box.glb"]);

        Assert.True(result.Success);
        Assert.NotNull(result.Options);
        Assert.Equal("Samples/Box.glb", result.Options!.ModelPath);
        Assert.Null(result.Options.Frames);
        Assert.Equal(1280, result.Options.Width);
        Assert.Equal(720, result.Options.Height);
        Assert.False(result.Options.Hidden);
        Assert.False(result.Options.UseWarp);
        Assert.False(result.Options.EnableDebugLayer);
        Assert.False(result.Options.EnableGpuValidation);
        Assert.False(result.Options.Verify);
    }

    [Fact]
    public void Parse_FullCombination_ParsesAllFlags()
    {
        ViewerOptionsParseResult result = ViewerOptionsParser.Parse(
            ["--model", "Samples/Box.glb", "--hidden", "--frames", "3", "--warp", "--debug-layer", "--gpu-validation", "--verify"]);

        Assert.True(result.Success);
        ViewerRunOptions options = result.Options!;
        Assert.Equal("Samples/Box.glb", options.ModelPath);
        Assert.Equal(3, options.Frames);
        Assert.True(options.Hidden);
        Assert.True(options.UseWarp);
        Assert.True(options.EnableDebugLayer);
        Assert.True(options.EnableGpuValidation);
        Assert.True(options.Verify);
    }

    [Fact]
    public void Parse_MissingModel_Fails()
    {
        ViewerOptionsParseResult result = ViewerOptionsParser.Parse(["--frames", "3"]);

        Assert.False(result.Success);
        Assert.Null(result.Options);
        Assert.Contains("--model", result.Error);
    }

    [Fact]
    public void Parse_ModelMissingValue_Fails()
    {
        ViewerOptionsParseResult result = ViewerOptionsParser.Parse(["--model"]);

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("notanumber")]
    public void Parse_InvalidFrames_Fails(string value)
    {
        ViewerOptionsParseResult result = ViewerOptionsParser.Parse(["--model", "Samples/Box.glb", "--frames", value]);

        Assert.False(result.Success);
        Assert.Null(result.Options);
    }

    [Theory]
    [InlineData("--width", "0")]
    [InlineData("--width", "-100")]
    [InlineData("--height", "0")]
    [InlineData("--height", "-1")]
    public void Parse_InvalidWidthOrHeight_Rejected(string flag, string value)
    {
        ViewerOptionsParseResult result = ViewerOptionsParser.Parse(["--model", "Samples/Box.glb", flag, value]);

        Assert.False(result.Success);
        Assert.Null(result.Options);
    }

    [Fact]
    public void Parse_UnknownOption_Fails()
    {
        ViewerOptionsParseResult result = ViewerOptionsParser.Parse(["--model", "Samples/Box.glb", "--not-a-real-option"]);

        Assert.False(result.Success);
        Assert.Contains("--not-a-real-option", result.Error);
    }
}
