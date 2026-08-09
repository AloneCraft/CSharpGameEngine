namespace CSharpGameEngine.Graphics.D3D12.Tests;

public class TriangleOptionsParserTests
{
    [Fact]
    public void Parse_NoArgs_UsesDefaults()
    {
        TriangleOptionsParseResult result = TriangleOptionsParser.Parse([]);

        Assert.True(result.Success);
        Assert.NotNull(result.Options);
        Assert.Null(result.Options!.Frames);
        Assert.Equal(1280, result.Options.Width);
        Assert.Equal(720, result.Options.Height);
        Assert.False(result.Options.Hidden);
        Assert.False(result.Options.UseWarp);
        Assert.False(result.Options.EnableDebugLayer);
        Assert.False(result.Options.EnableGpuValidation);
        Assert.False(result.Options.Verify);
    }

    [Fact]
    public void Parse_FullVerifyCombination_ParsesAllFlags()
    {
        TriangleOptionsParseResult result = TriangleOptionsParser.Parse(
            ["--hidden", "--frames", "3", "--warp", "--debug-layer", "--gpu-validation", "--verify"]);

        Assert.True(result.Success);
        TriangleRunOptions options = result.Options!;
        Assert.Equal(3, options.Frames);
        Assert.True(options.Hidden);
        Assert.True(options.UseWarp);
        Assert.True(options.EnableDebugLayer);
        Assert.True(options.EnableGpuValidation);
        Assert.True(options.Verify);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("notanumber")]
    public void Parse_InvalidFrames_Fails(string value)
    {
        TriangleOptionsParseResult result = TriangleOptionsParser.Parse(["--frames", value]);

        Assert.False(result.Success);
        Assert.Null(result.Options);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_FramesMissingValue_Fails()
    {
        TriangleOptionsParseResult result = TriangleOptionsParser.Parse(["--frames"]);

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("--width", "0")]
    [InlineData("--width", "-100")]
    [InlineData("--height", "0")]
    [InlineData("--height", "-1")]
    public void Parse_InvalidWidthOrHeight_Rejected(string flag, string value)
    {
        TriangleOptionsParseResult result = TriangleOptionsParser.Parse([flag, value]);

        Assert.False(result.Success);
        Assert.Null(result.Options);
    }

    [Fact]
    public void Parse_ValidWidthAndHeight_Accepted()
    {
        TriangleOptionsParseResult result = TriangleOptionsParser.Parse(["--width", "640", "--height", "480"]);

        Assert.True(result.Success);
        Assert.Equal(640, result.Options!.Width);
        Assert.Equal(480, result.Options.Height);
    }

    [Fact]
    public void Parse_UnknownOption_Fails()
    {
        TriangleOptionsParseResult result = TriangleOptionsParser.Parse(["--not-a-real-option"]);

        Assert.False(result.Success);
        Assert.Contains("--not-a-real-option", result.Error);
    }
}
