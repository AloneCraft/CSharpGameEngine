using System;
using System.Collections.Generic;
using System.Linq;
namespace CSharpGameEngine.Vsix.Automation;
public readonly struct SceneFrameSample {
    public SceneFrameSample(long ElapsedMilliseconds,bool HasRenderedVariation,ulong Signature){
        this.ElapsedMilliseconds=ElapsedMilliseconds;
        this.HasRenderedVariation=HasRenderedVariation;
        this.Signature=Signature;
    }
    public long ElapsedMilliseconds { get; }
    public bool HasRenderedVariation { get; }
    public ulong Signature { get; }
}
public sealed class SceneFrameSequenceAnalysis {
    internal SceneFrameSequenceAnalysis(bool Passed,int FrameCount,int StableFrameCount,
        int BlankFrameCount,int UnstableFrameCount,long MaximumGapMilliseconds,
        float AverageFramesPerSecond,string Detail){
        this.Passed=Passed;
        this.FrameCount=FrameCount;
        this.StableFrameCount=StableFrameCount;
        this.BlankFrameCount=BlankFrameCount;
        this.UnstableFrameCount=UnstableFrameCount;
        this.MaximumGapMilliseconds=MaximumGapMilliseconds;
        this.AverageFramesPerSecond=AverageFramesPerSecond;
        this.Detail=Detail;
    }
    public bool Passed { get; }
    public int FrameCount { get; }
    public int StableFrameCount { get; }
    public int BlankFrameCount { get; }
    public int UnstableFrameCount { get; }
    public long MaximumGapMilliseconds { get; }
    public float AverageFramesPerSecond { get; }
    public string Detail { get; }
}
public static class SceneFrameSequenceAnalyzer {
    private const long MaximumAllowedGapMilliseconds=100;
    private const float MinimumAverageFramesPerSecond=45;
    public static SceneFrameSequenceAnalysis Analyze(IReadOnlyList<SceneFrameSample>Samples,
        long ReadyElapsedMilliseconds,long RequiredStableMilliseconds){
        if(Samples is null){ throw new ArgumentNullException(nameof(Samples)); }
        SceneFrameSample[]Stable=Samples
            .Where(Sample=>Sample.ElapsedMilliseconds>=ReadyElapsedMilliseconds)
            .OrderBy(Sample=>Sample.ElapsedMilliseconds)
            .ToArray();
        long MaximumGap=0;
        for(int Index=1;Index<Samples.Count;Index++){
            MaximumGap=Math.Max(MaximumGap,
                Samples[Index].ElapsedMilliseconds-Samples[Index-1].ElapsedMilliseconds);
        }
        long StableDuration=Stable.Length<2?0:
            Stable[Stable.Length-1].ElapsedMilliseconds-Stable[0].ElapsedMilliseconds;
        float AverageFps=StableDuration<=0?0:
            (Stable.Length-1)*1000f/StableDuration;
        int Blank=Samples.Count(Sample=>!Sample.HasRenderedVariation);
        ulong Dominant=Stable.Where(Sample=>Sample.HasRenderedVariation)
            .GroupBy(Sample=>Sample.Signature)
            .OrderByDescending(Group=>Group.Count())
            .Select(Group=>Group.Key)
            .FirstOrDefault();
        int Unstable=Stable.Count(Sample=>Sample.HasRenderedVariation
            &&BitDifference(Sample.Signature,Dominant)>4);
        bool Passed=StableDuration>=RequiredStableMilliseconds
            &&AverageFps>=MinimumAverageFramesPerSecond
            &&MaximumGap<=MaximumAllowedGapMilliseconds
            &&Blank==0&&Unstable==0;
        string Detail=$"frames={Samples.Count}; stableFrames={Stable.Length}; "
            +$"stableMs={StableDuration}; averageFps={AverageFps:F1}; maxGapMs={MaximumGap}; "
            +$"blank={Blank}; unstable={Unstable}; dominant=0x{Dominant:X16}";
        return new SceneFrameSequenceAnalysis(Passed,Samples.Count,Stable.Length,Blank,
            Unstable,MaximumGap,AverageFps,Detail);
    }
    private static int BitDifference(ulong Left,ulong Right){
        ulong Value=Left^Right;
        int Count=0;
        while(Value!=0){
            Value&=Value-1;
            Count++;
        }
        return Count;
    }
}
