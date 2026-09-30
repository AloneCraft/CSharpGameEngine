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
