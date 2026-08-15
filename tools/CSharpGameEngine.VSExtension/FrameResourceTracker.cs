using System;

namespace CSharpGameEngine.VSExtension;

/// <summary>frames-in-flight の各 slot が最後に参照した fence 値を管理する。</summary>
internal sealed class FrameResourceTracker {
    private readonly ulong[]FenceValues;
    private ulong NextFenceValue=1;

    internal FrameResourceTracker(int FrameCount){
        if(FrameCount<=0){ throw new ArgumentOutOfRangeException(nameof(FrameCount)); }
        this.FenceValues=new ulong[FrameCount];
    }

    internal ulong GetRequiredFenceValue(int FrameIndex)=>this.FenceValues[Validate(FrameIndex)];

    internal ulong MarkSubmitted(int FrameIndex){
        ulong Value=this.NextFenceValue++;
        this.FenceValues[Validate(FrameIndex)]=Value;
        return Value;
    }

    internal ulong MarkGpuIdle(){
        ulong Value=this.NextFenceValue++;
        for(var FrameIndex=0;FrameIndex<this.FenceValues.Length;FrameIndex++){
            this.FenceValues[FrameIndex]=Value;
        }
        return Value;
    }

    private int Validate(int FrameIndex)=>FrameIndex>=0&&FrameIndex<this.FenceValues.Length
        ?FrameIndex
        :throw new ArgumentOutOfRangeException(nameof(FrameIndex));
}
