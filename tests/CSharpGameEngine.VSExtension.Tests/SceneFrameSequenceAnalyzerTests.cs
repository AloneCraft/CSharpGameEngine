using CSharpGameEngine.Vsix.Automation;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class SceneFrameSequenceAnalyzerTests {
    [Fact]
    public void StableSixtyHertzSequence_Passes(){
        SceneFrameSample[]Samples=Enumerable.Range(0,700)
            .Select(Index=>new SceneFrameSample(Index*16,true,0xA55AA55AA55AA55AUL))
            .ToArray();
        SceneFrameSequenceAnalysis Result=SceneFrameSequenceAnalyzer.Analyze(
            Samples,ReadyElapsedMilliseconds:1000,RequiredStableMilliseconds:10000);
        Assert.True(Result.Passed,Result.Detail);
        Assert.Equal(0,Result.BlankFrameCount);
        Assert.Equal(0,Result.UnstableFrameCount);
    }
    [Fact]
    public void OneBlankFrameAfterReady_Fails(){
        SceneFrameSample[]Samples=Enumerable.Range(0,700)
            .Select(Index=>new SceneFrameSample(Index*16,Index!=300,0xA55AA55AA55AA55AUL))
            .ToArray();
        SceneFrameSequenceAnalysis Result=SceneFrameSequenceAnalyzer.Analyze(
            Samples,ReadyElapsedMilliseconds:1000,RequiredStableMilliseconds:10000);
        Assert.False(Result.Passed);
        Assert.Equal(1,Result.BlankFrameCount);
    }
    [Fact]
    public void BlankFrameDuringSceneTransition_Fails(){
        SceneFrameSample[]Samples=Enumerable.Range(0,700)
            .Select(Index=>new SceneFrameSample(Index*16,Index!=20,0xA55AA55AA55AA55AUL))
            .ToArray();
        SceneFrameSequenceAnalysis Result=SceneFrameSequenceAnalyzer.Analyze(
            Samples,ReadyElapsedMilliseconds:1000,RequiredStableMilliseconds:10000);
        Assert.False(Result.Passed);
        Assert.Equal(1,Result.BlankFrameCount);
    }
    [Fact]
    public void OldSceneSignatureReturningAfterReady_Fails(){
        SceneFrameSample[]Samples=Enumerable.Range(0,700)
            .Select(Index=>new SceneFrameSample(Index*16,true,
                Index==400?0x5AA55AA55AA55AA5UL:0xA55AA55AA55AA55AUL))
            .ToArray();
        SceneFrameSequenceAnalysis Result=SceneFrameSequenceAnalyzer.Analyze(
            Samples,ReadyElapsedMilliseconds:1000,RequiredStableMilliseconds:10000);
        Assert.False(Result.Passed);
        Assert.Equal(1,Result.UnstableFrameCount);
    }
    [Fact]
    public void UniformBrightnessAlternationAfterReady_Fails(){
        // Two frame slots with different ambient light: every cell brightens and darkens together, so
        // the relative 8x8 signature never changes; only the absolute luminance reveals the flicker.
        SceneFrameSample[]Samples=Enumerable.Range(0,700)
            .Select(Index=>new SceneFrameSample(Index*16,true,0xA55AA55AA55AA55AUL,Index%2==0?120f:60f))
            .ToArray();
        SceneFrameSequenceAnalysis Result=SceneFrameSequenceAnalyzer.Analyze(
            Samples,ReadyElapsedMilliseconds:1000,RequiredStableMilliseconds:10000);
        Assert.False(Result.Passed,Result.Detail);
        Assert.True(Result.BrightnessUnstableFrameCount>300,Result.Detail);
    }
    [Fact]
    public void SmallCaptureNoiseInBrightness_Passes(){
        SceneFrameSample[]Samples=Enumerable.Range(0,700)
            .Select(Index=>new SceneFrameSample(Index*16,true,0xA55AA55AA55AA55AUL,100f+(Index%3)))
            .ToArray();
        SceneFrameSequenceAnalysis Result=SceneFrameSequenceAnalyzer.Analyze(
            Samples,ReadyElapsedMilliseconds:1000,RequiredStableMilliseconds:10000);
        Assert.True(Result.Passed,Result.Detail);
        Assert.Equal(0,Result.BrightnessUnstableFrameCount);
    }
    [Fact]
    public void CaptureGapLongerThanOneHundredMilliseconds_Fails(){
        SceneFrameSample[]Samples=Enumerable.Range(0,700)
            .Select(Index=>new SceneFrameSample(Index*16+(Index>=350?120:0),true,7UL))
            .ToArray();
        SceneFrameSequenceAnalysis Result=SceneFrameSequenceAnalyzer.Analyze(
            Samples,ReadyElapsedMilliseconds:1000,RequiredStableMilliseconds:10000);
        Assert.False(Result.Passed);
        Assert.True(Result.MaximumGapMilliseconds>100);
    }
}
