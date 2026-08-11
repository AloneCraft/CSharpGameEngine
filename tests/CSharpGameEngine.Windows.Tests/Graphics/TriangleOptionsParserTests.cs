namespace CSharpGameEngine.Graphics.D3D12.Tests;

public class TriangleOptionsParserTests {
    [Fact]
    public void Parse_NoArgs_UsesDefaults(){
        TriangleOptionsParseResult Result=TriangleOptionsParser.Parse([]);

        Assert.True(Result.Success);
        Assert.NotNull(Result.Options);
        Assert.Null(Result.Options!.Frames);
        Assert.Equal(1280,Result.Options.Width);
        Assert.Equal(720,Result.Options.Height);
        Assert.False(Result.Options.Hidden);
        Assert.False(Result.Options.UseWarp);
        Assert.False(Result.Options.EnableDebugLayer);
        Assert.False(Result.Options.EnableGpuValidation);
        Assert.False(Result.Options.Verify);
    }

    [Fact]
    public void Parse_FullVerifyCombination_ParsesAllFlags(){
        TriangleOptionsParseResult Result=TriangleOptionsParser.Parse(
            ["--hidden","--frames","3","--warp","--debug-layer","--gpu-validation","--verify"]);

        Assert.True(Result.Success);
        TriangleRunOptions Options=Result.Options!;
        Assert.Equal(3,Options.Frames);
        Assert.True(Options.Hidden);
        Assert.True(Options.UseWarp);
        Assert.True(Options.EnableDebugLayer);
        Assert.True(Options.EnableGpuValidation);
        Assert.True(Options.Verify);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("notanumber")]
    public void Parse_InvalidFrames_Fails(string Value){
        TriangleOptionsParseResult Result=TriangleOptionsParser.Parse(["--frames",Value]);

        Assert.False(Result.Success);
        Assert.Null(Result.Options);
        Assert.NotNull(Result.Error);
    }

    [Fact]
    public void Parse_FramesMissingValue_Fails(){
        TriangleOptionsParseResult Result=TriangleOptionsParser.Parse(["--frames"]);

        Assert.False(Result.Success);
    }

    [Theory]
    [InlineData("--width","0")]
    [InlineData("--width","-100")]
    [InlineData("--height","0")]
    [InlineData("--height","-1")]
    public void Parse_InvalidWidthOrHeight_Rejected(string Flag,string Value){
        TriangleOptionsParseResult Result=TriangleOptionsParser.Parse([Flag,Value]);

        Assert.False(Result.Success);
        Assert.Null(Result.Options);
    }

    [Fact]
    public void Parse_ValidWidthAndHeight_Accepted(){
        TriangleOptionsParseResult Result=TriangleOptionsParser.Parse(["--width","640","--height","480"]);

        Assert.True(Result.Success);
        Assert.Equal(640,Result.Options!.Width);
        Assert.Equal(480,Result.Options.Height);
    }

    [Fact]
    public void Parse_UnknownOption_Fails(){
        TriangleOptionsParseResult Result=TriangleOptionsParser.Parse(["--not-a-real-option"]);

        Assert.False(Result.Success);
        Assert.Contains("--not-a-real-option",Result.Error);
    }
}
