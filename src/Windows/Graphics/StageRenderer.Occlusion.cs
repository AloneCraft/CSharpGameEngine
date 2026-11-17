using CSharpGameEngine.Rendering.Pipeline;
using TerraFX.Interop.DirectX;
using static CSharpGameEngine.Rendering.Pipeline.StageGpuLayouts;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D_PRIMITIVE_TOPOLOGY;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>どの塊の一覧を間接描画するか（M0131 単位 2）。</summary>
internal enum VirtualGeometryList {
    /// <summary>細かさで選んだすべての塊（影・Lumen・カメラプレビュー、遮蔽を使わない主パス）。</summary>
    Selected,
    /// <summary>主パスの 1 パス目: 選んだ塊のうち、前のフレームで見えた塊。</summary>
    FirstPass,
    /// <summary>主パスの 2 パス目: 1 パス目で描かず、Hi-Z で隠れていなかった塊。</summary>
    SecondPass,
}
/// <summary>
/// 仮想ジオメトリの遮蔽判定（ADR-0100 決定 3、M0131）。主パスだけを 2 パスにする（影・Lumen・カメラプレビューは細かさで選んだ全部を描く）。
/// 1. 選択のとき、前のフレームで見えた塊を 1 パス目の一覧にも詰める。
/// 2. 主パスで、仮想ジオメトリでない物体と 1 パス目の塊を描く。
/// 3. そこまでの距離の画像から Hi-Z を作り（<see cref="StageHiZ"/>）、選んだ塊を調べて見えた印を書き直し、2 パス目の一覧を作る。
/// 4. 主パスの束縛を戻して、2 パス目の塊を描く。
/// 隠れていると捨てた塊は、描いても 1 パス目までの物に負けるので、遮蔽を切った絵と同じ絵になる。
/// </summary>
public sealed unsafe partial class StageRenderer {
    private StageHiZ?HiZ;
    /// <summary>主パスで仮想ジオメトリの遮蔽判定を使うか。既定は使う。</summary>
    internal bool VirtualGeometryOcclusion { get;set; }=true;
    /// <summary>このフレームの選択で、1 パス目の一覧を作った（主パスを 2 パスで描く）。</summary>
    private bool VirtualGeometryOcclusionThisFrame;
    /// <summary>試験用: 主パスの最後に Hi-Z を作る（仮想ジオメトリの遮蔽を使わないフレームでも）。</summary>
    internal bool BuildHiZAfterMainPassForTesting { get;set; }
    /// <summary>主パスで物体を描く。遮蔽を使うフレームは 2 パスで描く。</summary>
    private void DrawMainPassObjects(ref FrameRecording Recording){
        if(!this.VirtualGeometryOcclusionThisFrame){
            this.DrawObjects(ExcludedObjectIndex:-1,Recording.MainPipelineDesc,CountForTesting:true);
            return;
        }
        this.DrawObjects(ExcludedObjectIndex:-1,Recording.MainPipelineDesc,CountForTesting:true,List:VirtualGeometryList.FirstPass);
        this.RecordVirtualGeometryOcclusionTest();
        this.RebindMainPass(Recording.MainPipeline);
        _=this.DrawSelectedVirtualGeometry(Recording.MainPipelineDesc,VirtualGeometryVerticesRootParameter,null,List:VirtualGeometryList.SecondPass);
    }
    /// <summary>1 パス目までの距離から Hi-Z を作り、選んだ塊を調べて 2 パス目の一覧を作る。</summary>
    private void RecordVirtualGeometryOcclusionTest(){
        var HiZ=this.HiZ!;
        HiZ.Record(this.CommandList,this.SceneDistanceBuffer,this.Width,this.Height);
        this.TransitionVirtualGeometryBuffers(D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        this.CommandList->SetComputeRootSignature(this.VirtualGeometrySelectionRootSignature);
        this.CommandList->SetPipelineState(this.VirtualGeometrySelectionPipeline);
        ID3D12DescriptorHeap*Heap=HiZ.Heap;
        this.CommandList->SetDescriptorHeaps(1,&Heap);
        this.CommandList->SetComputeRootDescriptorTable(9,HiZ.AllLevelsTable);
        var (HiZWidth,HiZHeight)=HiZ.BaseSize;
        StageVirtualGeometryOcclusion Occlusion=new(){
            ViewProjection=this.CurrentViewProjection,
            ScreenWidth=(uint)this.Width,
            ScreenHeight=(uint)this.Height,
            HiZWidth=(uint)HiZWidth,
            HiZHeight=(uint)HiZHeight,
            HiZLevelCount=(uint)HiZ.LevelCount,
        };
        this.CommandList->SetComputeRoot32BitConstants(1,VirtualGeometryOcclusionConstantCount,&Occlusion,0);
        for(var ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
            if(!this.DrawsWithVirtualGeometry(ObjectIndex)){ continue; }
            this.DispatchVirtualGeometry(ObjectIndex,StageVirtualGeometrySelectionFlags.Test);
        }
        this.TransitionVirtualGeometryBuffers(D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
    }
    /// <summary>計算で置き換わった主パスの束縛（ルートシグネチャ、記述子の置き場、描画先、視点）を戻す。</summary>
    private void RebindMainPass(ID3D12PipelineState*Pipeline){
        this.BindMainPassState(Pipeline);
        var HdrRtvHandle=this.HdrRenderTargetHandle;
        var DsvHandle=this.DsvHeap->GetCPUDescriptorHandleForHeapStart();
        this.CommandList->OMSetRenderTargets(2,&HdrRtvHandle,TRUE,&DsvHandle);
        fixed(D3D12_VIEWPORT*PViewport=&this.Viewport)
        fixed(TerraFX.Interop.Windows.RECT*PScissorRect=&this.ScissorRect){
            this.CommandList->RSSetViewports(1,PViewport);
            this.CommandList->RSSetScissorRects(1,PScissorRect);
        }
        this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    }
    /// <summary>主パスの最後で、試験の印があれば Hi-Z を作る。計算の束縛に置き換わるので、後のパスのために印を立てる。</summary>
    private void RecordHiZAfterMainPass(ref FrameRecording Recording){
        if(!this.BuildHiZAfterMainPassForTesting){ return; }
        this.HiZ!.Record(this.CommandList,this.SceneDistanceBuffer,this.Width,this.Height);
        Recording.RanComputeAfterMainPass=true;
    }
    /// <summary>試験用: Hi-Z の段 <paramref name="Level"/>（行優先）と、その大きさ。GPU の完了を待って読む。</summary>
    internal (float[]Values,int Width,int Height)ReadHiZLevelForTesting(int Level){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        var Values=this.HiZ!.ReadLevelForTesting(Level);
        var Sizes=CSharpGameEngine.Rendering.Streaming.HiZPyramid.LevelSizes(this.HiZ.ScreenWidth,this.HiZ.ScreenHeight);
        return (Values,Sizes[Level].Width,Sizes[Level].Height);
    }
    /// <summary>試験用: 最後に作った Hi-Z の段の数（作っていなければ 0）。</summary>
    internal int HiZLevelCountForTesting=>this.HiZ?.Result==null?0:this.HiZ.LevelCount;
    /// <summary>試験用: 物体ごとに、最後のフレームの主パスの 1 パス目と 2 パス目で描いた塊の数（遮蔽を使わなかった物体は (-1, -1)）。GPU の完了を待って読む。</summary>
    internal (int First,int Second)[]ReadVirtualGeometryPassCountsForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        var Result=new (int First,int Second)[this.ObjectCount];
        for(var ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
            if(!this.VirtualGeometryOcclusionThisFrame||!this.DrawsWithVirtualGeometry(ObjectIndex)){
                Result[ObjectIndex]=(-1,-1);
                continue;
            }
            var Bytes=GpuBufferReadback.Read(this.Device!,this.VirtualGeometryObjects[ObjectIndex].PassArguments,(ulong)sizeof(D3D12_DRAW_ARGUMENTS)*2,D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);
            Result[ObjectIndex]=(BitConverter.ToInt32(Bytes,4),BitConverter.ToInt32(Bytes,sizeof(D3D12_DRAW_ARGUMENTS)+4));
        }
        return Result;
    }
}
