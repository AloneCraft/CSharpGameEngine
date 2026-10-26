using static CSharpGameEngine.Rendering.Pipeline.StageGpuLayouts;
using CSharpGameEngine.Rendering;
using System.Numerics;
using System.Runtime.CompilerServices;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D_PRIMITIVE_TOPOLOGY;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// フレーム内のLumen風動的グローバルイルミネーションと反射（ADR-0078/M0111）。<see cref="StageLumen"/>が計画と計算を行う。
/// レンダラーはStageルートシグネチャを使って2つのラスタライズ部分（サーフェスプリパスとサーフェスキャッシュのカード撮影）を描画し、
/// 出力をt16／t17へバインドする。環境設定で要求され（<see cref="EnvironmentLightingSettings.Lumen"/>）、
/// トレース可能なグローバル距離場があり、アダプターの<see cref="Scalability"/>が許す場合に実行する。
/// それ以外のフレームはM0110のプローブボリュームとスカイライトだけのフレームと同じ。
/// </summary>
public sealed unsafe partial class StageRenderer {
    /// <summary>マテリアルディスクリプターの後に続く、Stageのt12～t17テーブル。GI照度、GI距離、アンビエントオクルージョン、露出、Lumen拡散、反射。</summary>
    private const int EnvironmentDescriptorCount=8;
    private StageLumen?Lumen;
    private ID3D12PipelineState*LumenSurfacePipelineState;
    private ID3D12PipelineState*LumenCardPipelineState;
    private byte[]LumenSurfacePixelShaderBytecode=[];
    private byte[]LumenCardPixelShaderBytecode=[];
    private int LumenDescriptorGeneration;
    private bool LumenActiveThisFrame;
    /// <summary>
    /// 動的照明のアダプターレベルの品質（Unrealのsg.GlobalIlluminationQualityおよびsg.ReflectionQuality）。
    /// LumenのGIはHigh／Epicで実行し、反射はHigh（半解像度）／Epicで実行する。既定値はアダプターの<see cref="RenderScalability.DefaultFor"/>を使う
    ///（ハードウェアではHigh、WARPではMedium）。
    /// </summary>
    public RenderScalability Scalability { get;set; }
    /// <summary>デバッグ用（Unrealのr.Lumen.Visualize）。Lumen実行中は、照明済みシーンの代わりにこの映像を表示する。</summary>
    public LumenVisualization LumenVisualization { get;set; }
    /// <summary>直近の描画フレームがLumen風GIで照明されたかどうか。</summary>
    internal bool LumenActiveLastFrame { get;private set; }
    /// <summary>直近のLumenフレームが行った処理（診断とテスト用）。</summary>
    internal LumenStatistics LumenStatistics=>this.Lumen?.Statistics??default;
    private bool LumenWanted=>this.EnvironmentSettings.Lumen is { GlobalIllumination:true }
        &&this.EnvironmentSettings.DistanceField is not null
        &&this.Scalability.AllowsLumenGlobalIllumination
        &&this.Lumen!.IsSupported;
    /// <summary>
    /// 今フレームのLumen処理を計画し、サーフェスプリパスとカード撮影を描画して、Computeパスを記録する。
    /// シャドウマップとグローバル距離場の後、メインパスの前に実行する。
    /// </summary>
    private void RenderLumen(){
        if(!this.LumenActiveThisFrame){
            return;
        }
        var Lumen=this.Lumen!;
        var Field=this.GlobalDistanceField!;
        if(!Matrix4x4.Invert(this.InvertibleViewProjection,out var Inverse)){
            Inverse=Matrix4x4.Identity;
        }
        var Input=new LumenFrameInput(this.EnvironmentSettings.Lumen!.Value,this.Scalability,this.EnvironmentSettings.DistanceField!.Value,Field.ClipmapConstants,Field.LevelCount,
            this.CurrentViewProjection,Inverse,this.CurrentCameraPosition,this.Width,this.Height,this.FramesRendered,this.AutoExposureActive,this.CurrentExposureScale());
        Lumen.Prepare(this.FrameIndex,Input,this.ObjectActive,this.ObjectMeshIndices,this.ObjectMaterialIndices,this.ObjectTransforms,this.ObjectEmissiveScales,this.ObjectCount,
            Mesh=>Field.TryGetMeshRecord(Mesh,out var Record)?Record:-1,Mesh=>(this.MeshBoundsMinimums[Mesh],this.MeshBoundsMaximums[Mesh]),this.WaitForGpuIdle);
        if(this.LumenDescriptorGeneration!=Lumen.Generation){
            this.WriteEnvironmentDescriptors();
        }
        // サーフェスプリパス：ピクセルごとのカメラ空間法線、粗さ、距離。
        Lumen.BeginSurface(this.CommandList);
        D3D12_CPU_DESCRIPTOR_HANDLE*SurfaceTargets=stackalloc D3D12_CPU_DESCRIPTOR_HANDLE[2];
        SurfaceTargets[0]=Lumen.SurfaceRtv(0);
        SurfaceTargets[1]=Lumen.SurfaceRtv(1);
        var SurfaceDepth=Lumen.SurfaceDsv;
        this.CommandList->OMSetRenderTargets(2,SurfaceTargets,FALSE,&SurfaceDepth);
        this.BindMainPassState(this.LumenSurfacePipelineState);
        fixed(D3D12_VIEWPORT*PViewport=&this.Viewport)
        fixed(RECT*PScissorRect=&this.ScissorRect){
            this.CommandList->RSSetViewports(1,PViewport);
            this.CommandList->RSSetScissorRects(1,PScissorRect);
        }
        this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        this.DrawObjects(ExcludedObjectIndex:-1);
        Lumen.EndSurface(this.CommandList);
        // カード撮影：新しい各カードについて、メッシュだけをメッシュ自身の空間に描画し、カードの正射影を使う。
        if(Lumen.CaptureCount>0){
            Lumen.BeginCaptures(this.CommandList);
            D3D12_CPU_DESCRIPTOR_HANDLE*CardTargets=stackalloc D3D12_CPU_DESCRIPTOR_HANDLE[3];
            for(var Target=0;Target<3;Target++){
                CardTargets[Target]=Lumen.CardRtv(Target);
            }
            var CardDepth=Lumen.CardDsv;
            this.CommandList->OMSetRenderTargets(3,CardTargets,FALSE,&CardDepth);
            this.BindMainPassState(this.LumenCardPipelineState);
            var Source=this.FrameSlot(this.FrameIndex);
            for(var Index=0;Index<Lumen.CaptureCount;Index++){
                var Capture=Lumen.CaptureAt(Index);
                var FrameDestination=Lumen.CaptureFrameSlot(this.FrameIndex,Index);
                Unsafe.CopyBlock(FrameDestination,Source,FrameConstantsSize);
                var Projection=Capture.Projection;
                Unsafe.CopyBlock(FrameDestination+FrameViewProjectionOffset,&Projection,(uint)sizeof(Matrix4x4));
                *(Vector4*)(FrameDestination+FrameLumenParamsOffset)=Vector4.Zero;
                *(Vector4*)(FrameDestination+FrameDepthEncoding0Offset)=Vector4.Zero;
                this.WriteLumenCaptureObjectConstants(Lumen.CaptureObjectSlot(this.FrameIndex,Index),Capture.ObjectIndex);
                Lumen.ClearCard(this.CommandList,Capture);
                D3D12_VIEWPORT Viewport=new(){ TopLeftX=Capture.X,TopLeftY=Capture.Y,Width=Capture.Width,Height=Capture.Height,MinDepth=0f,MaxDepth=1f };
                RECT Scissor=new(){ left=Capture.X,top=Capture.Y,right=Capture.X+Capture.Width,bottom=Capture.Y+Capture.Height };
                this.CommandList->RSSetViewports(1,&Viewport);
                this.CommandList->RSSetScissorRects(1,&Scissor);
                this.CommandList->SetGraphicsRootConstantBufferView(0,Lumen.CaptureFrameAddress(this.FrameIndex,Index));
                this.CommandList->SetGraphicsRootConstantBufferView(1,Lumen.CaptureObjectAddress(this.FrameIndex,Index));
                this.CommandList->SetGraphicsRootDescriptorTable(2,this.GetMaterialDescriptorTable(this.ObjectMaterialIndices[Capture.ObjectIndex]));
                var MeshIndex=this.ObjectMeshIndices[Capture.ObjectIndex];
                fixed(D3D12_VERTEX_BUFFER_VIEW*PVertexBufferView=&this.VertexBufferViews[MeshIndex])
                fixed(D3D12_INDEX_BUFFER_VIEW*PIndexBufferView=&this.IndexBufferViews[MeshIndex]){
                    this.CommandList->IASetVertexBuffers(0,1,PVertexBufferView);
                    this.CommandList->IASetIndexBuffer(PIndexBufferView);
                    this.CommandList->DrawIndexedInstanced(this.IndexCounts[MeshIndex],1,0,0,0);
                }
            }
            Lumen.EndCaptures(this.CommandList);
        }
        Lumen.RecordCompute(this.CommandList,this.FrameIndex,new LumenComputeBindings {
            FrameConstants=this.GetFrameConstantsAddress(this.FrameIndex),
            LocalLights=this.GetLocalLightsAddress(this.FrameIndex),
            SpotShadowProjections=this.GetSpotShadowProjectionAddress(this.FrameIndex),
            PointShadowProjections=this.GetPointShadowProjectionAddress(this.FrameIndex),
            SkyIrradianceSh=this.Atmosphere!.ShBufferAddress,
            ExposureState=this.AutoExposureStage!.StateAddress,
            ExposureStateResource=this.AutoExposureStage.StateResource,
            ShadowAtlas=this.ShadowAtlas,
            SkyMap=this.EnvironmentSettings.Atmosphere is not null?this.Atmosphere.SkyRadianceMapResource:null,
            Field=Field,
        });
    }
    /// <summary>
    /// 撮影時のオブジェクト定数。Worldを恒等変換にし、メッシュを自身の空間で描画する。ワールド格子は使用しない（線をメッシュではなく
    /// ワールドに沿わせるため）。マテリアル本来の発光を使う（カード照明で実行時のオブジェクト倍率を適用）。プレゼンテーション色は加えない。
    /// </summary>
    private void WriteLumenCaptureObjectConstants(byte*Destination,int ObjectIndex){
        var MaterialIndex=this.ObjectMaterialIndices[ObjectIndex];
        var World=Matrix4x4.Identity;
        Unsafe.CopyBlock(Destination+ObjectWorldOffset,&World,(uint)sizeof(Matrix4x4));
        var BaseColorFactor=this.MaterialBaseColorFactors[MaterialIndex];
        Unsafe.CopyBlock(Destination+ObjectBaseColorFactorOffset,&BaseColorFactor,(uint)sizeof(Vector4));
        var MaterialParams=new Vector4(this.MaterialRoughnessFactors[MaterialIndex],this.MaterialMetallicFactors[MaterialIndex],0f,0f);
        Unsafe.CopyBlock(Destination+ObjectMaterialParamsOffset,&MaterialParams,(uint)sizeof(Vector4));
        var EmissiveNits=new Vector4(this.MaterialEmissiveNits[MaterialIndex],0f);
        Unsafe.CopyBlock(Destination+ObjectEmissiveFactorOffset,&EmissiveNits,(uint)sizeof(Vector4));
        var Zero=Vector4.Zero;
        Unsafe.CopyBlock(Destination+ObjectWorldGridParamsOffset,&Zero,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Destination+ObjectWorldGridColorOffset,&Zero,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Destination+ObjectWorldSubGridColorOffset,&Zero,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Destination+ObjectWorldGridAnchorOffset,&Zero,(uint)sizeof(Vector4));
        Unsafe.CopyBlock(Destination+ObjectConservativeRasterDepthOffset,&Zero,(uint)sizeof(Vector4));
    }
    /// <summary>テスト専用：デバイスが削除された理由（動作中はS_OK）。</summary>
    internal int DeviceRemovedReasonForTesting=>this.Device!.Device->GetDeviceRemovedReason().Value;
    /// <summary>テスト専用：GPUアイドル後に読み取る、Lumenの拡散照度（rgb）と、各ピクセルが対応する距離（a）。</summary>
    internal (Vector4[]Texels,int Width,int Height)ReadLumenDiffuseForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.Lumen!.ReadDiffuseForTesting();
    }
    /// <summary>テスト専用：LumenのGather／反射パスが使う面までの物理距離。</summary>
    internal (float[]Distances,int Width,int Height)ReadLumenSurfaceDistancesForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.Lumen!.ReadSurfaceDistancesForTesting();
    }
    /// <summary>テスト専用：GPUアイドル後に読み取る、ピクセルごとのLumen反射放射輝度。</summary>
    internal (Vector4[]Texels,int Width,int Height)ReadLumenReflectionForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.Lumen!.ReadReflectionForTesting();
    }
    /// <summary>テスト専用：GPUアイドル後に読み取る、スクリーンプローブのトレース放射輝度と配置（軸あたりのプローブ数、プローブあたりのピクセル数）。</summary>
    internal ((Vector4[]Texels,int Width,int Height)Radiance,(int X,int Y,int Texels)Layout)ReadLumenProbesForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return (this.Lumen!.ReadProbeRadianceForTesting(),this.Lumen.ProbeLayoutForTesting);
    }
    /// <summary>テスト専用：GPUアイドル後に読み取る、オブジェクト1つのカードにおけるサーフェスキャッシュの平均放射輝度と対象テクセル数。</summary>
    internal (Vector3 Average,int Covered)ReadLumenCardForTesting(int ObjectIndex,int Face){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.Lumen!.ReadCardForTesting(ObjectIndex,Face);
    }
}
