using System.Numerics;
using System.Runtime.CompilerServices;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D_PRIMITIVE_TOPOLOGY;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// The Lumen-like dynamic global illumination and reflections (ADR-0078/M0111) in the frame: <see cref="StageLumen"/> plans and computes, the
/// renderer draws its two raster parts with the Stage root signature (the surface prepass and the surface cache's card captures) and binds its
/// outputs at t16/t17. Lumen runs when the environment asks for it (<see cref="EnvironmentLightingSettings.Lumen"/>), has a global distance field
/// to trace and the adapter's <see cref="Scalability"/> allows it; otherwise the frame is exactly the probe volume and sky light frame of M0110.
/// </summary>
public sealed unsafe partial class StageRenderer {
    /// <summary>The Stage t12-t17 table after the material descriptors: GI irradiance, GI distance, ambient occlusion, exposure, Lumen diffuse and reflections.</summary>
    private const int EnvironmentDescriptorCount=6;
    private StageLumen?Lumen;
    private ID3D12PipelineState*LumenSurfacePipelineState;
    private ID3D12PipelineState*LumenCardPipelineState;
    private byte[]LumenSurfacePixelShaderBytecode=[];
    private byte[]LumenCardPixelShaderBytecode=[];
    private int LumenDescriptorGeneration;
    private bool LumenActiveThisFrame;
    /// <summary>
    /// The adapter-level quality of the dynamic lighting (Unreal's sg.GlobalIlluminationQuality and sg.ReflectionQuality). Lumen's GI runs at High and
    /// Epic, its reflections at High (half resolution) and Epic. The default is <see cref="RenderScalability.DefaultFor"/> the adapter (High on hardware,
    /// Medium on WARP).
    /// </summary>
    public RenderScalability Scalability { get;set; }
    /// <summary>Debugging (Unreal's r.Lumen.Visualize): while Lumen runs, the frame shows this instead of the lit scene.</summary>
    public LumenVisualization LumenVisualization { get;set; }
    /// <summary>Whether the last rendered frame was lit by the Lumen-like GI.</summary>
    internal bool LumenActiveLastFrame { get;private set; }
    /// <summary>What the last Lumen frame did (diagnostics and tests).</summary>
    internal LumenStatistics LumenStatistics=>this.Lumen?.Statistics??default;
    private bool LumenWanted=>this.EnvironmentSettings.Lumen is { GlobalIllumination:true }
        &&this.EnvironmentSettings.DistanceField is not null
        &&this.Scalability.AllowsLumenGlobalIllumination
        &&this.Lumen!.IsSupported;
    /// <summary>
    /// Plans this frame's Lumen work, draws the surface prepass and the card captures, and records the compute passes. Runs after the shadow maps and
    /// the global distance field, before the main pass.
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
        // The surface prepass: the camera's shading normal, roughness and distance per pixel.
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
        // The card captures: each new card draws its mesh alone, in the mesh's own space, through the card's orthographic projection.
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
    /// A capture's object constants: the material of the object with an identity World (the mesh is drawn in its own space), no world grid (its lines
    /// follow the world, not the mesh), the material's own emission (the card lighting applies the object's run-time scale) and no presentation tint.
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
    /// <summary>Test-only: the device's removed reason (S_OK while it lives).</summary>
    internal int DeviceRemovedReasonForTesting=>this.Device!.Device->GetDeviceRemovedReason().Value;
    /// <summary>Test-only: the Lumen diffuse irradiance (rgb) and the distance it belongs to (a) per pixel, after the GPU is idle.</summary>
    internal (Vector4[]Texels,int Width,int Height)ReadLumenDiffuseForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.Lumen!.ReadDiffuseForTesting();
    }
    /// <summary>Test-only: the physical distances of the surfaces used by Lumen's gather and reflection passes.</summary>
    internal (float[]Distances,int Width,int Height)ReadLumenSurfaceDistancesForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.Lumen!.ReadSurfaceDistancesForTesting();
    }
    /// <summary>Test-only: the Lumen reflected radiance per pixel, after the GPU is idle.</summary>
    internal (Vector4[]Texels,int Width,int Height)ReadLumenReflectionForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.Lumen!.ReadReflectionForTesting();
    }
    /// <summary>Test-only: the screen probes' traced radiance and their layout (probes per axis, pixels per probe), after the GPU is idle.</summary>
    internal ((Vector4[]Texels,int Width,int Height)Radiance,(int X,int Y,int Texels)Layout)ReadLumenProbesForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return (this.Lumen!.ReadProbeRadianceForTesting(),this.Lumen.ProbeLayoutForTesting);
    }
    /// <summary>Test-only: the surface cache's average radiance and covered texels on one object's card, after the GPU is idle.</summary>
    internal (Vector3 Average,int Covered)ReadLumenCardForTesting(int ObjectIndex,int Face){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        return this.Lumen!.ReadCardForTesting(ObjectIndex,Face);
    }
}
