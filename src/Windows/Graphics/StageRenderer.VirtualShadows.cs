using System.Numerics;
using static CSharpGameEngine.Rendering.Pipeline.StageGpuLayouts;
using System.Runtime.CompilerServices;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Pipeline;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// 太陽の影の準備（カスケード影と仮想シャドウの共通部分）。仮想シャドウの計画と置き場は <c>StageRenderer.VirtualShadowMaps.cs</c>（M0132）。
/// </summary>
public sealed unsafe partial class StageRenderer {
    /// <summary><see cref="SetViewSource"/> で受け取ったビューの元。<see cref="SetView"/> で固定のビューを受け取ると消える。</summary>
    private RenderViewSource RetainedViewSource;
    private ID3D12QueryHeap*ShadowTimestampHeap;
    private StageFeedbackReadback?ShadowTimestampReadback;
    private ulong ShadowTimestampFrequency;
    public float CascadedShadowGpuMilliseconds{get;private set;}
    public float VirtualShadowGpuMilliseconds{get;private set;}
    /// <summary>このフレームの仮想シャドウの計画の統計（仮想シャドウを使わなかったフレームは 0）。</summary>
    public VirtualShadowStatistics VirtualShadowStatistics=>this.ShadowMapPlannedThisFrame?this.ShadowMapPlanner.Statistics:default;
    public DirectionalShadowSettings DirectionalShadows=>this.ShadowSettings;
    private DirectionalShadowSettings ShadowSettings=>this.EnvironmentSettings.DirectionalShadows??new(DirectionalShadowMode.Cascaded);
    private void CreateVirtualShadows(){
        this.CreateVirtualShadowMaps();
        var QueryDesc=new D3D12_QUERY_HEAP_DESC { Type=D3D12_QUERY_HEAP_TYPE.D3D12_QUERY_HEAP_TYPE_TIMESTAMP,Count=FrameCount*4 };
        ID3D12QueryHeap*Query;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateQueryHeap(&QueryDesc,__uuidof<ID3D12QueryHeap>(),(void**)&Query),nameof(this.CreateVirtualShadows),"shadow timestamps");
        this.ShadowTimestampHeap=Query;
        this.ShadowTimestampReadback=new StageFeedbackReadback(this.Device,FrameCount,4*sizeof(ulong),"shadow timestamp");
        ulong Frequency;
        D3D12Diagnostics.EnsureSuccess(this.Device.CommandQueue->GetTimestampFrequency(&Frequency),nameof(this.CreateVirtualShadows),"shadow timestamp frequency");
        this.ShadowTimestampFrequency=Frequency;
    }
    /// <summary>柔らかい影の定数を書き、仮想シャドウを使うなら計画してページを描く。</summary>
    private void PrepareVirtualShadows(){
        var Frame=this.FrameSlot(this.FrameIndex);
        *(Vector4*)(Frame+FrameVirtualParamsOffset)=Vector4.Zero;
        var Settings=this.ShadowSettings;
        *(Vector4*)(Frame+FrameDirectionalSoftShadowOffset)=new(MathF.Tan(Settings.SourceAngularDiameterDegrees*MathF.PI/360f),Settings.MaximumSoftShadowRadiusMeters,Settings.SoftShadows?1f:0f,0f);
        this.RenderVirtualShadowMapPages();
    }
    /// <summary>
    /// 影を落とす物のうち、行列 <paramref name="Projection"/> の範囲に入るものを、今の描画先とビューポートへ描く。
    /// 影のルートシグネチャ・PSO・物体の表と、行列の定数バッファを束縛してから呼ぶ。仮想ジオメトリの物体は、主ビューで選んだ塊で影を落とす（M0130 単位 5）。
    /// </summary>
    private void DrawShadowCasters(Matrix4x4 Projection,List<VirtualShadowBounds>Objects,bool VariantAvailable){
        this.VirtualGeometryShadowCasters.Clear();
        foreach(var Object in Objects){
            if(!Object.CastsShadow||!IntersectsProjection(Object,Projection)){ continue; }
            if(this.SkipsNormalDraw(Object.Id,VariantAvailable)){
                this.VirtualGeometryShadowCasters.Add(Object.Id);
                continue;
            }
            var ObjectIndex=Object.Id;
            var Mesh=this.ObjectMeshIndices[ObjectIndex];
            this.SetDrawConstants(ObjectIndex);
            fixed(D3D12_VERTEX_BUFFER_VIEW*Vertex=&this.VertexBufferViews[Mesh])
            fixed(D3D12_INDEX_BUFFER_VIEW*Indices=&this.IndexBufferViews[Mesh]){
                this.CommandList->IASetVertexBuffers(0,1,Vertex);
                this.CommandList->IASetIndexBuffer(Indices);
                this.CommandList->DrawIndexedInstanced(this.IndexCounts[Mesh],1,0,0,0);
            }
        }
        if(this.VirtualGeometryShadowCasters.Count>0){
            _=this.DrawSelectedVirtualGeometry(StagePipelines.Shadow,ShadowVirtualGeometryVerticesRootParameter,this.VirtualGeometryShadowCasters.Contains);
        }
    }
    private static bool IntersectsProjection(VirtualShadowBounds Object,Matrix4x4 Projection){
        Span<int>Outside=stackalloc int[6];
        for(var I=0;I<8;I++){
            var P=Vector4.Transform(new Vector4((I&1)==0?Object.Minimum.X:Object.Maximum.X,(I&2)==0?Object.Minimum.Y:Object.Maximum.Y,(I&4)==0?Object.Minimum.Z:Object.Maximum.Z,1f),Projection);
            if(P.X< -P.W){ Outside[0]++; }if(P.X>P.W){ Outside[1]++; }if(P.Y< -P.W){ Outside[2]++; }if(P.Y>P.W){ Outside[3]++; }if(P.Z<0f){ Outside[4]++; }if(P.Z>P.W){ Outside[5]++; }
        }
        return !Outside.Contains(8);
    }
    private void DisposeVirtualShadows(){
        this.DisposeVirtualShadowMaps();
        if(this.ShadowTimestampHeap!=null){ _=this.ShadowTimestampHeap->Release();this.ShadowTimestampHeap=null; }
        this.ShadowTimestampReadback?.Dispose();
        this.ShadowTimestampReadback=null;
    }
    /// <summary>仮想シャドウの置き場の 1 ページで、仮想ジオメトリで影を落とす物体（毎ページ使い直す）。</summary>
    private readonly List<int>VirtualGeometryShadowCasters=[];
    private void DrawShadowComparison(in StagePipelineDesc MainPipeline){
        var Source=this.FrameSlot(this.FrameIndex);
        var Left=this.FrameSlot(this.FrameIndex,1);
        var Right=this.FrameSlot(this.FrameIndex,2);
        Unsafe.CopyBlock(Left,Source,FrameConstantsSize);
        Unsafe.CopyBlock(Right,Source,FrameConstantsSize);
        ((Vector4*)(Left+FrameVirtualParamsOffset))->X=0f;
        var Half=this.Width/2;
        for(var Side=0;Side<2;Side++){
            var X=Side*Half;
            var Viewport=new D3D12_VIEWPORT { TopLeftX=X,Width=Half,Height=this.Height,MaxDepth=1f };
            var Rect=new RECT { left=X,top=0,right=X+Half,bottom=this.Height };
            this.CommandList->RSSetViewports(1,&Viewport);this.CommandList->RSSetScissorRects(1,&Rect);
            this.CommandList->SetGraphicsRootConstantBufferView(0,this.GetFrameConstantsAddress(this.FrameIndex,Side+1));
            this.DrawObjects(-1,MainPipeline);
        }
        this.CommandList->SetGraphicsRootConstantBufferView(0,this.GetFrameConstantsAddress(this.FrameIndex));
    }
    private void ReadShadowTimings(uint?CompletedSlot=null){
        var Slot=CompletedSlot??this.FrameIndex;
        // MoveToNextFrameは、このスロットが再度書き込み可能になる前に対応するフェンスの完了を待機する。
        Span<ulong>Data=stackalloc ulong[4];
        if(this.ShadowTimestampReadback is null||!this.ShadowTimestampReadback.TryRead(Slot,Data)){ return; }
        this.CascadedShadowGpuMilliseconds=(Data[1]-Data[0])*1000f/this.ShadowTimestampFrequency;
        this.VirtualShadowGpuMilliseconds=(Data[3]-Data[2])*1000f/this.ShadowTimestampFrequency;
    }
    private void ShadowTimestamp(uint Index)=>this.CommandList->EndQuery(this.ShadowTimestampHeap,D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP,this.FrameIndex*4+Index);
}
