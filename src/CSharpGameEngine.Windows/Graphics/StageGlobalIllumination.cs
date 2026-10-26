using CSharpGameEngine.Rendering;
using System.Numerics;
using CSharpGameEngine.ShaderArtifacts;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_RANGE_TYPE;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D12_ROOT_PARAMETER_TYPE;
using static TerraFX.Interop.DirectX.D3D12_SHADER_VISIBILITY;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// 動的拡散GI（ADR-0069）：ラスタライズ方式のDDGI型プローブボリューム。StageRendererが照明済みStageパイプラインを用いて、
/// 予定された各プローブを32x32ピクセルの6面として、このオブジェクトの撮影配列へ記録する。続いて<see cref="RecordConvolution"/>が
/// 各撮影結果をプローブの八面体照度タイル（8x8＋境界）と距離モーメントタイル（16x16＋境界）へ畳み込む。Stageは両アトラスをt12／t13でサンプリングする。
/// Computeルートシグネチャはホスト所有（GlobalIllumination.hlslを参照）：0 = ルートCBV b0 GiCB、1 = SRVテーブルt0-t1
///（撮影放射輝度、撮影深度）、2 = ルートSRV t2（スカイライトSH）、3 = UAVテーブルu0-u1（照度アトラス、距離アトラス）。
/// </summary>
internal sealed unsafe class StageGlobalIllumination:IDisposable {
    internal const string ArtifactFileName="GlobalIllumination.cgeshaderartifact";
    internal const int CaptureResolution=32;
    internal const float CaptureNearMeters=0.05f;
    internal const float CaptureFarMeters=500f;
    internal const int IrradianceTexels=8;
    internal const int DistanceTexels=16;
    internal const int ProbesPerAtlasRow=64;
    internal const int IrradianceTile=IrradianceTexels+2;
    internal const int DistanceTile=DistanceTexels+2;
    internal const int IrradianceAtlasWidth=ProbesPerAtlasRow*IrradianceTile;
    internal const int IrradianceAtlasHeight=GlobalIlluminationSettings.MaxProbeCount/ProbesPerAtlasRow*IrradianceTile;
    internal const int DistanceAtlasWidth=ProbesPerAtlasRow*DistanceTile;
    internal const int DistanceAtlasHeight=GlobalIlluminationSettings.MaxProbeCount/ProbesPerAtlasRow*DistanceTile;
    private const int CaptureSliceCount=GlobalIlluminationSettings.MaxProbesPerFrame*6;
    private const uint ConstantsStride=512;
    private const uint ConstantSlotsPerFrame=GlobalIlluminationSettings.MaxProbesPerFrame+1;
    private const DXGI_FORMAT CaptureColorFormat=DXGI_FORMAT_R16G16B16A16_FLOAT;
    private const DXGI_FORMAT IrradianceFormat=DXGI_FORMAT_R16G16B16A16_FLOAT;
    private const DXGI_FORMAT DistanceFormat=DXGI_FORMAT_R16G16_FLOAT;
    private const uint SrvCaptureColor=0;
    private const uint SrvCaptureDistance=1;
    private const uint UavIrradiance=2;
    private const uint UavDistance=3;
    private const uint DescriptorCount=4;
    private const D3D12_RESOURCE_STATES ShaderResourceState=D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE|D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    private static readonly (Vector3 Forward,Vector3 Up)[]Faces=[
        (Vector3.UnitX,Vector3.UnitY),(-Vector3.UnitX,Vector3.UnitY),
        (Vector3.UnitY,-Vector3.UnitZ),(-Vector3.UnitY,Vector3.UnitZ),
        (Vector3.UnitZ,Vector3.UnitY),(-Vector3.UnitZ,Vector3.UnitY),
    ];
    private readonly GraphicsDevice Device;
    private readonly string ArtifactPath;
    private readonly uint FrameCount;
    private readonly uint FrameConstantsStride;
    private readonly bool[]Captured=new bool[GlobalIlluminationSettings.MaxProbeCount];
    private readonly int[]ScheduledProbes=new int[GlobalIlluminationSettings.MaxProbesPerFrame];
    private readonly float[]ScheduledHysteresis=new float[GlobalIlluminationSettings.MaxProbesPerFrame];
    private ID3D12Resource*CaptureColor;
    private ID3D12Resource*CaptureDepth;
    private ID3D12Resource*CaptureDistance;
    private ID3D12Resource*IrradianceAtlas;
    private ID3D12Resource*DistanceAtlas;
    private ID3D12Resource*ConstantsBuffer;
    private byte*MappedConstants;
    private ID3D12Resource*CaptureFrameConstants;
    private byte*MappedCaptureFrameConstants;
    private ID3D12DescriptorHeap*DescriptorHeap;
    private ID3D12DescriptorHeap*RtvHeap;
    private ID3D12DescriptorHeap*DsvHeap;
    private uint DescriptorSize;
    private uint RtvDescriptorSize;
    private uint DsvDescriptorSize;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private (Vector3 HalfExtent,float Spacing)?Layout;
    private bool ClearPending;
    private int Cursor;
    private bool Disposed;
    internal StageGlobalIllumination(GraphicsDevice Device,string StageArtifactPath,uint FrameCount,uint FrameConstantsStride){
        this.Device=Device;
        this.FrameCount=FrameCount;
        this.FrameConstantsStride=FrameConstantsStride;
        this.ArtifactPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(StageArtifactPath))??string.Empty,ArtifactFileName);
        D3D12_FEATURE_DATA_D3D12_OPTIONS Options=default;
        this.IsSupported=Device.Device->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS,&Options,(uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS)).SUCCEEDED
            &&Options.TypedUAVLoadAdditionalFormats!=0;
    }
    /// <summary>ヒステリシスでは、RGBA16F／RG16F型のUAV読み込みで前回のタイルを参照する。</summary>
    internal bool IsSupported { get; }
    internal bool HasResources=>this.IrradianceAtlas!=null;
    internal ID3D12Resource*IrradianceResource=>this.IrradianceAtlas;
    internal ID3D12Resource*DistanceResource=>this.DistanceAtlas;
    internal int ScheduledCount { get;private set; }
    internal int ScheduledProbe(int Index)=>this.ScheduledProbes[Index];
    /// <summary>
    /// このフレームで使うプローブをラウンドロビン方式で選び、格子配置が変わった場合はすべてのプローブをリセットする。
    /// 最初の撮影ではプローブのタイルを置き換え、2回目以降は<see cref="GlobalIlluminationSettings.Hysteresis"/>とブレンドする。
    /// </summary>
    internal void Schedule(in GlobalIlluminationSettings Settings){
        this.EnsureResources();
        var Key=(Settings.HalfExtent,Settings.ProbeSpacingMeters);
        if(this.Layout!=Key){
            this.Layout=Key;
            Array.Clear(this.Captured);
            this.Cursor=0;
            this.ClearPending=true;
        }
        var ProbeCount=Settings.ProbeCount;
        var Count=Math.Min(Settings.ProbesPerFrame,ProbeCount);
        for(var Index=0;Index<Count;Index++){
            var Probe=(this.Cursor+Index)%ProbeCount;
            this.ScheduledProbes[Index]=Probe;
            this.ScheduledHysteresis[Index]=this.Captured[Probe]?Settings.Hysteresis:0f;
            this.Captured[Probe]=true;
        }
        this.Cursor=(this.Cursor+Count)%ProbeCount;
        this.ScheduledCount=Count;
    }
    /// <summary>
    /// 1面分の撮影に使うビュー投影。90度の正方形視錐台。メインカメラと異なり左手系なので、面の画面+X方向は
    /// <c>Up × Forward</c>となる。これは、撮影テクセルから方向を復元するためにGlobalIllumination.hlslが使う<see cref="FaceBasis"/>のRight方向。
    /// </summary>
    internal static Matrix4x4 FaceViewProjection(Vector3 ProbePosition,int Face){
        var (Forward,Up)=Faces[Face];
        return Matrix4x4.CreateLookAtLeftHanded(ProbePosition,ProbePosition+Forward,Up)
            *Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(MathF.PI/2f,1f,CaptureNearMeters,CaptureFarMeters);
    }
    /// <summary>撮影面が向く方向と、その画面の+X／+Y方向。</summary>
    internal static (Vector3 Forward,Vector3 Right,Vector3 Up)FaceBasis(int Face){
        var (Forward,Up)=Faces[Face];
        return (Forward,Vector3.Cross(Up,Forward),Up);
    }
    internal byte*CaptureFrameSlot(uint FrameIndex,int Capture)=>
        this.MappedCaptureFrameConstants+(FrameIndex*(uint)CaptureSliceCount+(uint)Capture)*this.FrameConstantsStride;
    internal ulong CaptureFrameAddress(uint FrameIndex,int Capture)=>
        this.CaptureFrameConstants->GetGPUVirtualAddress()+(FrameIndex*(uint)CaptureSliceCount+(uint)Capture)*this.FrameConstantsStride;
    internal D3D12_CPU_DESCRIPTOR_HANDLE CaptureRtv(int Capture)=>new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),Capture*2,this.RtvDescriptorSize);
    internal D3D12_CPU_DESCRIPTOR_HANDLE CaptureDistanceRtv(int Capture)=>new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),Capture*2+1,this.RtvDescriptorSize);
    internal D3D12_CPU_DESCRIPTOR_HANDLE CaptureDsv(int Capture)=>new(this.DsvHeap->GetCPUDescriptorHandleForHeapStart(),Capture,this.DsvDescriptorSize);
    /// <summary>撮影配列を書き込み可能にする。<see cref="RecordConvolution"/>と組み合わせて使用する。</summary>
    internal void BeginCapture(ID3D12GraphicsCommandList*CommandList){
        D3D12_RESOURCE_BARRIER*Barriers=stackalloc D3D12_RESOURCE_BARRIER[3];
        Barriers[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.CaptureColor,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
        Barriers[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.CaptureDepth,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_DEPTH_WRITE);
        Barriers[2]=D3D12_RESOURCE_BARRIER.InitTransition(this.CaptureDistance,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
        CommandList->ResourceBarrier(3,Barriers);
    }
    /// <summary>
    /// 予定された撮影結果をそれぞれのプローブタイルへ畳み込む（保留中の配置クリア後）。Stageプログラムからアトラスを読み取り可能な状態にする。
    /// Computeルートシグネチャ、パイプライン、ディスクリプタヒープを置き換える。
    /// </summary>
    internal void RecordConvolution(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,in GlobalIlluminationSettings Settings,ulong SkyShAddress,bool SkyLightEnabled){
        this.EnsurePipeline();
        D3D12_RESOURCE_BARRIER*Before=stackalloc D3D12_RESOURCE_BARRIER[5];
        Before[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.CaptureColor,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        Before[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.CaptureDepth,D3D12_RESOURCE_STATE_DEPTH_WRITE,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        Before[2]=D3D12_RESOURCE_BARRIER.InitTransition(this.IrradianceAtlas,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        Before[3]=D3D12_RESOURCE_BARRIER.InitTransition(this.DistanceAtlas,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        Before[4]=D3D12_RESOURCE_BARRIER.InitTransition(this.CaptureDistance,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        CommandList->ResourceBarrier(5,Before);
        CommandList->SetComputeRootSignature(this.RootSignature);
        CommandList->SetPipelineState(this.PipelineState);
        ID3D12DescriptorHeap*Heap=this.DescriptorHeap;
        CommandList->SetDescriptorHeaps(1,&Heap);
        var Start=this.DescriptorHeap->GetGPUDescriptorHandleForHeapStart();
        CommandList->SetComputeRootDescriptorTable(1,new D3D12_GPU_DESCRIPTOR_HANDLE(Start,(int)SrvCaptureColor,this.DescriptorSize));
        CommandList->SetComputeRootShaderResourceView(2,SkyShAddress);
        CommandList->SetComputeRootDescriptorTable(3,new D3D12_GPU_DESCRIPTOR_HANDLE(Start,(int)UavIrradiance,this.DescriptorSize));
        var Spacing=Settings.GridSpacing;
        var SkyDistance=MathF.Min(4f*MathF.Max(Spacing.X,MathF.Max(Spacing.Y,Spacing.Z)),200f);
        if(this.ClearPending){
            var Slot=this.WriteConstants(FrameIndex,GlobalIlluminationSettings.MaxProbesPerFrame,0,0f,SkyLightEnabled,SkyDistance,Clear:true);
            CommandList->SetComputeRootConstantBufferView(0,Slot);
            CommandList->Dispatch((DistanceAtlasWidth+7)/8,(DistanceAtlasHeight+7)/8,1);
            D3D12_RESOURCE_BARRIER*Clear=stackalloc D3D12_RESOURCE_BARRIER[2];
            Clear[0]=D3D12_RESOURCE_BARRIER.InitUAV(this.IrradianceAtlas);
            Clear[1]=D3D12_RESOURCE_BARRIER.InitUAV(this.DistanceAtlas);
            CommandList->ResourceBarrier(2,Clear);
            this.ClearPending=false;
        }
        for(var Index=0;Index<this.ScheduledCount;Index++){
            var Slot=this.WriteConstants(FrameIndex,Index,this.ScheduledProbes[Index],this.ScheduledHysteresis[Index],SkyLightEnabled,SkyDistance,Clear:false);
            CommandList->SetComputeRootConstantBufferView(0,Slot);
            CommandList->Dispatch((DistanceTile+7)/8,(DistanceTile+7)/8,1);
        }
        D3D12_RESOURCE_BARRIER*After=stackalloc D3D12_RESOURCE_BARRIER[2];
        After[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.IrradianceAtlas,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        After[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.DistanceAtlas,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        CommandList->ResourceBarrier(2,After);
    }
    /// <summary>Stageのt12／t13ビューを書き込む。アトラスが作られるまではnullビュー（読み取り値は0）。</summary>
    internal void WriteShadingDescriptors(D3D12_CPU_DESCRIPTOR_HANDLE Irradiance,D3D12_CPU_DESCRIPTOR_HANDLE Distance){
        D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){
            Format=IrradianceFormat,
            ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        Srv.Texture2D.MipLevels=1;
        this.Device.Device->CreateShaderResourceView(this.IrradianceAtlas,&Srv,Irradiance);
        Srv.Format=DistanceFormat;
        this.Device.Device->CreateShaderResourceView(this.DistanceAtlas,&Srv,Distance);
    }
    /// <summary>照度アトラスと距離アトラス内にある、境界を含むプローブのタイル原点。</summary>
    internal static (int IrradianceX,int IrradianceY,int DistanceX,int DistanceY) TileOrigin(int Probe)=>
        (Probe%ProbesPerAtlasRow*IrradianceTile,Probe/ProbesPerAtlasRow*IrradianceTile,Probe%ProbesPerAtlasRow*DistanceTile,Probe/ProbesPerAtlasRow*DistanceTile);
    /// <summary>極軸を+Zとする八面体エンコード。GlobalIllumination.hlslとStageに合わせる。</summary>
    internal static Vector2 OctahedralEncode(Vector3 Direction){
        var N=Direction/(MathF.Abs(Direction.X)+MathF.Abs(Direction.Y)+MathF.Abs(Direction.Z));
        if(N.Z>=0f){
            return new Vector2(N.X,N.Y);
        }
        return new Vector2((1f-MathF.Abs(N.Y))*(N.X>=0f?1f:-1f),(1f-MathF.Abs(N.X))*(N.Y>=0f?1f:-1f));
    }
    internal static Vector3 OctahedralDecode(Vector2 Encoded){
        var V=new Vector3(Encoded.X,Encoded.Y,1f-MathF.Abs(Encoded.X)-MathF.Abs(Encoded.Y));
        if(V.Z<0f){
            V=new Vector3((1f-MathF.Abs(Encoded.Y))*(Encoded.X>=0f?1f:-1f),(1f-MathF.Abs(Encoded.X))*(Encoded.Y>=0f?1f:-1f),V.Z);
        }
        return Vector3.Normalize(V);
    }
    /// <summary><paramref name="Size"/>個の内側テクセルを持つタイルで、内部テクセル座標<paramref name="X"/>、<paramref name="Y"/>が表す方向。</summary>
    internal static Vector3 TexelDirection(int X,int Y,int Size)=>
        OctahedralDecode(new Vector2((X+0.5f)/Size*2f-1f,(Y+0.5f)/Size*2f-1f));
    private ulong WriteConstants(uint FrameIndex,int Index,int Probe,float Hysteresis,bool SkyLightEnabled,float SkyDistance,bool Clear){
        var Slot=FrameIndex*ConstantSlotsPerFrame+(uint)Index;
        var Target=(float*)(this.MappedConstants+Slot*ConstantsStride);
        for(var Face=0;Face<6;Face++){
            var (Forward,Right,Up)=FaceBasis(Face);
            Store(Target,Face,Right,0f);
            Store(Target,6+Face,Up,0f);
            Store(Target,12+Face,Forward,0f);
        }
        Target[72]=CaptureResolution;
        Target[73]=CaptureNearMeters;
        Target[74]=CaptureFarMeters;
        Target[75]=Hysteresis;
        var Tile=TileOrigin(Probe);
        var Integers=(uint*)Target;
        Integers[76]=(uint)Tile.IrradianceX;
        Integers[77]=(uint)Tile.IrradianceY;
        Integers[78]=(uint)Tile.DistanceX;
        Integers[79]=(uint)Tile.DistanceY;
        Target[80]=SkyLightEnabled?1f:0f;
        Target[81]=SkyDistance;
        Target[82]=0f;
        Target[83]=0f;
        Integers[84]=(uint)(Index*6);
        Integers[85]=Clear?1u:0u;
        Integers[86]=0;
        Integers[87]=0;
        return this.ConstantsBuffer->GetGPUVirtualAddress()+Slot*ConstantsStride;
        static void Store(float*Target,int Row,Vector3 Value,float W){
            Target[Row*4]=Value.X;
            Target[Row*4+1]=Value.Y;
            Target[Row*4+2]=Value.Z;
            Target[Row*4+3]=W;
        }
    }
    private void EnsurePipeline(){
        if(this.PipelineState!=null){
            return;
        }
        if(!ShaderArtifactRuntime.TryLoad(this.ArtifactPath,ShaderArtifactTargetKind.Compute,out var Package,out var Error)||Package is null){
            throw new InvalidDataException($"Dynamic global illumination requires '{this.ArtifactPath}': {Error}");
        }
        var Bytecode=Package.GetBytecode("CSMain");
        this.RootSignature=this.CreateRootSignature();
        fixed(byte*Code=Bytecode){
            D3D12_COMPUTE_PIPELINE_STATE_DESC Desc=new(){
                pRootSignature=this.RootSignature,
                CS=new D3D12_SHADER_BYTECODE { pShaderBytecode=Code,BytecodeLength=(nuint)Bytecode.Length },
            };
            ID3D12PipelineState*Pipeline;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateComputePipelineState(&Desc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline),
                "ID3D12Device::CreateComputePipelineState","creating the global illumination pipeline");
            this.PipelineState=Pipeline;
        }
    }
    private ID3D12RootSignature*CreateRootSignature(){
        D3D12_DESCRIPTOR_RANGE SrvRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,NumDescriptors=2,BaseShaderRegister=0 };
        D3D12_DESCRIPTOR_RANGE UavRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_UAV,NumDescriptors=2,BaseShaderRegister=0 };
        D3D12_ROOT_PARAMETER*Parameters=stackalloc D3D12_ROOT_PARAMETER[4];
        Parameters[0]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[0].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        Parameters[1]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[1].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&SrvRange };
        Parameters[2]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[2].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=2 };
        Parameters[3]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[3].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&UavRange };
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=4,pParameters=Parameters };
        ID3DBlob*Signature=null;
        ID3DBlob*Error=null;
        try {
            D3D12Diagnostics.EnsureSuccess(D3D12SerializeRootSignature(&Desc,D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1,&Signature,&Error),
                "D3D12SerializeRootSignature","serializing the global illumination root signature");
            ID3D12RootSignature*Result;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateRootSignature(0,Signature->GetBufferPointer(),Signature->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Result),
                "ID3D12Device::CreateRootSignature","creating the global illumination root signature");
            return Result;
        } finally {
            if(Signature!=null){ _=Signature->Release(); }
            if(Error!=null){ _=Error->Release(); }
        }
    }
    private void EnsureResources(){
        if(this.IrradianceAtlas!=null){
            return;
        }
        this.DescriptorHeap=this.CreateHeap(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,DescriptorCount,D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE);
        this.RtvHeap=this.CreateHeap(D3D12_DESCRIPTOR_HEAP_TYPE_RTV,CaptureSliceCount*2,D3D12_DESCRIPTOR_HEAP_FLAG_NONE);
        this.DsvHeap=this.CreateHeap(D3D12_DESCRIPTOR_HEAP_TYPE_DSV,CaptureSliceCount,D3D12_DESCRIPTOR_HEAP_FLAG_NONE);
        this.DescriptorSize=this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
        this.RtvDescriptorSize=this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
        this.DsvDescriptorSize=this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_DSV);
        D3D12_CLEAR_VALUE ColorClear=new(){ Format=CaptureColorFormat };
        this.CaptureColor=this.CreateTexture(CaptureColorFormat,CaptureResolution,CaptureResolution,CaptureSliceCount,D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,&ColorClear);
        D3D12_CLEAR_VALUE DistanceClear=new(){ Format=DXGI_FORMAT_R32_FLOAT };
        this.CaptureDistance=this.CreateTexture(DXGI_FORMAT_R32_FLOAT,CaptureResolution,CaptureResolution,CaptureSliceCount,D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,&DistanceClear);
        D3D12_CLEAR_VALUE DepthClear=new(){ Format=DXGI_FORMAT_D32_FLOAT };
        DepthClear.DepthStencil.Depth=1f;
        this.CaptureDepth=this.CreateTexture(DXGI_FORMAT_R32_TYPELESS,CaptureResolution,CaptureResolution,CaptureSliceCount,D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,&DepthClear);
        this.IrradianceAtlas=this.CreateTexture(IrradianceFormat,IrradianceAtlasWidth,IrradianceAtlasHeight,1,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS,ShaderResourceState,null);
        this.DistanceAtlas=this.CreateTexture(DistanceFormat,DistanceAtlasWidth,DistanceAtlasHeight,1,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS,ShaderResourceState,null);
        for(var Slice=0;Slice<CaptureSliceCount;Slice++){
            D3D12_RENDER_TARGET_VIEW_DESC Rtv=new(){ Format=CaptureColorFormat,ViewDimension=D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2DARRAY };
            Rtv.Texture2DArray=new D3D12_TEX2D_ARRAY_RTV { FirstArraySlice=(uint)Slice,ArraySize=1 };
            this.Device.Device->CreateRenderTargetView(this.CaptureColor,&Rtv,this.CaptureRtv(Slice));
            Rtv.Format=DXGI_FORMAT_R32_FLOAT;
            this.Device.Device->CreateRenderTargetView(this.CaptureDistance,&Rtv,this.CaptureDistanceRtv(Slice));
            D3D12_DEPTH_STENCIL_VIEW_DESC Dsv=new(){ Format=DXGI_FORMAT_D32_FLOAT,ViewDimension=D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2DARRAY };
            Dsv.Texture2DArray=new D3D12_TEX2D_ARRAY_DSV { FirstArraySlice=(uint)Slice,ArraySize=1 };
            this.Device.Device->CreateDepthStencilView(this.CaptureDepth,&Dsv,this.CaptureDsv(Slice));
        }
        D3D12_SHADER_RESOURCE_VIEW_DESC ArraySrv=new(){
            Format=CaptureColorFormat,
            ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2DARRAY,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        ArraySrv.Texture2DArray=new D3D12_TEX2D_ARRAY_SRV { MipLevels=1,ArraySize=CaptureSliceCount };
        this.Device.Device->CreateShaderResourceView(this.CaptureColor,&ArraySrv,this.CpuHandle(SrvCaptureColor));
        ArraySrv.Format=DXGI_FORMAT_R32_FLOAT;
        this.Device.Device->CreateShaderResourceView(this.CaptureDistance,&ArraySrv,this.CpuHandle(SrvCaptureDistance));
        D3D12_UNORDERED_ACCESS_VIEW_DESC Uav=new(){ Format=IrradianceFormat,ViewDimension=D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2D };
        this.Device.Device->CreateUnorderedAccessView(this.IrradianceAtlas,null,&Uav,this.CpuHandle(UavIrradiance));
        Uav.Format=DistanceFormat;
        this.Device.Device->CreateUnorderedAccessView(this.DistanceAtlas,null,&Uav,this.CpuHandle(UavDistance));
        this.CreateUpload((ulong)this.FrameCount*ConstantSlotsPerFrame*ConstantsStride,"creating the global illumination constants",out this.ConstantsBuffer,out this.MappedConstants);
        this.CreateUpload((ulong)this.FrameCount*CaptureSliceCount*this.FrameConstantsStride,"creating the probe capture frame constants",out this.CaptureFrameConstants,out this.MappedCaptureFrameConstants);
        this.ClearPending=true;
    }
    private ID3D12DescriptorHeap*CreateHeap(D3D12_DESCRIPTOR_HEAP_TYPE Type,uint Count,D3D12_DESCRIPTOR_HEAP_FLAGS Flags){
        D3D12_DESCRIPTOR_HEAP_DESC Desc=new(){ Type=Type,NumDescriptors=Count,Flags=Flags };
        ID3D12DescriptorHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateDescriptorHeap(&Desc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),
            "ID3D12Device::CreateDescriptorHeap","creating a global illumination descriptor heap");
        return Heap;
    }
    private ID3D12Resource*CreateTexture(DXGI_FORMAT Format,int Width,int Height,int ArraySize,D3D12_RESOURCE_FLAGS Flags,D3D12_RESOURCE_STATES State,D3D12_CLEAR_VALUE*Clear){
        var Desc=D3D12_RESOURCE_DESC.Tex2D(Format,(ulong)Width,(uint)Height,(ushort)ArraySize,1);
        Desc.Flags|=Flags;
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_DEFAULT);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,State,Clear,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource","creating a global illumination texture");
        return Resource;
    }
    private void CreateUpload(ulong Size,string Context,out ID3D12Resource*Resource,out byte*Mapped){
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_UPLOAD);
        var Desc=D3D12_RESOURCE_DESC.Buffer(Size);
        ID3D12Resource*Created;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_GENERIC_READ,null,__uuidof<ID3D12Resource>(),(void**)&Created),
            "ID3D12Device::CreateCommittedResource",Context);
        D3D12_RANGE NoRead=new();
        byte*Pointer;
        D3D12Diagnostics.EnsureSuccess(Created->Map(0,&NoRead,(void**)&Pointer),"ID3D12Resource::Map",Context);
        Resource=Created;
        Mapped=Pointer;
    }
    private D3D12_CPU_DESCRIPTOR_HANDLE CpuHandle(uint Slot)=>new(this.DescriptorHeap->GetCPUDescriptorHandleForHeapStart(),(int)Slot,this.DescriptorSize);
    /// <summary>
    /// テスト専用の、両アトラスのGPU読み戻し。呼び出し側はレンダラーのキューをアイドル状態にすること。
    /// コピーは専用のコマンドリストとフェンスで実行する。
    /// </summary>
    internal GlobalIlluminationReadback ReadbackForTesting(){
        if(this.IrradianceAtlas==null){
            throw new InvalidOperationException("Global illumination has not rendered a frame yet.");
        }
        ID3D12CommandAllocator*Allocator=null;
        ID3D12GraphicsCommandList*List=null;
        ID3D12Resource*Readback=null;
        ID3D12Fence*Fence=null;
        HANDLE Event=HANDLE.NULL;
        try {
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,__uuidof<ID3D12CommandAllocator>(),(void**)&Allocator),"CreateCommandAllocator","GI readback");
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,Allocator,null,__uuidof<ID3D12GraphicsCommandList>(),(void**)&List),"CreateCommandList","GI readback");
            D3D12_PLACED_SUBRESOURCE_FOOTPRINT IrradianceFootprint;
            D3D12_PLACED_SUBRESOURCE_FOOTPRINT DistanceFootprint;
            ulong IrradianceBytes;
            ulong DistanceBytes;
            var IrradianceDesc=this.IrradianceAtlas->GetDesc();
            var DistanceDesc=this.DistanceAtlas->GetDesc();
            this.Device.Device->GetCopyableFootprints(&IrradianceDesc,0,1,0,&IrradianceFootprint,null,null,&IrradianceBytes);
            this.Device.Device->GetCopyableFootprints(&DistanceDesc,0,1,0,&DistanceFootprint,null,null,&DistanceBytes);
            DistanceFootprint.Offset=(IrradianceBytes+511UL)&~511UL;
            var TotalBytes=DistanceFootprint.Offset+DistanceBytes;
            D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_READBACK);
            var BufferDesc=D3D12_RESOURCE_DESC.Buffer(TotalBytes);
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_COPY_DEST,null,__uuidof<ID3D12Resource>(),(void**)&Readback),"CreateCommittedResource","GI readback");
            D3D12_RESOURCE_BARRIER*ToCopy=stackalloc D3D12_RESOURCE_BARRIER[2];
            ToCopy[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.IrradianceAtlas,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_SOURCE);
            ToCopy[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.DistanceAtlas,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_SOURCE);
            List->ResourceBarrier(2,ToCopy);
            D3D12_TEXTURE_COPY_LOCATION Destination=new(){ pResource=Readback,Type=D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT,PlacedFootprint=IrradianceFootprint };
            D3D12_TEXTURE_COPY_LOCATION Source=new(){ pResource=this.IrradianceAtlas,Type=D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            List->CopyTextureRegion(&Destination,0,0,0,&Source,null);
            Destination.PlacedFootprint=DistanceFootprint;
            Source.pResource=this.DistanceAtlas;
            List->CopyTextureRegion(&Destination,0,0,0,&Source,null);
            D3D12_RESOURCE_BARRIER*Back=stackalloc D3D12_RESOURCE_BARRIER[2];
            Back[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.IrradianceAtlas,D3D12_RESOURCE_STATE_COPY_SOURCE,ShaderResourceState);
            Back[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.DistanceAtlas,D3D12_RESOURCE_STATE_COPY_SOURCE,ShaderResourceState);
            List->ResourceBarrier(2,Back);
            D3D12Diagnostics.EnsureSuccess(List->Close(),"Close","GI readback");
            ID3D12CommandList*Base=(ID3D12CommandList*)List;
            this.Device.CommandQueue->ExecuteCommandLists(1,&Base);
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateFence(0,D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE,__uuidof<ID3D12Fence>(),(void**)&Fence),"CreateFence","GI readback");
            Event=CreateEventW(null,FALSE,FALSE,null);
            D3D12Diagnostics.EnsureSuccess(this.Device.CommandQueue->Signal(Fence,1),"Signal","GI readback");
            if(Fence->GetCompletedValue()<1){
                D3D12Diagnostics.EnsureSuccess(Fence->SetEventOnCompletion(1,Event),"SetEventOnCompletion","GI readback");
                _=WaitForSingleObjectEx(Event,INFINITE,FALSE);
            }
            byte*Mapped;
            D3D12_RANGE Range=new(){ Begin=0,End=(nuint)TotalBytes };
            D3D12Diagnostics.EnsureSuccess(Readback->Map(0,&Range,(void**)&Mapped),"Map","GI readback");
            try {
                var Irradiance=new Vector4[IrradianceAtlasWidth*IrradianceAtlasHeight];
                for(var Y=0;Y<IrradianceAtlasHeight;Y++){
                    var Row=(Half*)(Mapped+IrradianceFootprint.Offset+(ulong)Y*IrradianceFootprint.Footprint.RowPitch);
                    for(var X=0;X<IrradianceAtlasWidth;X++){
                        Irradiance[Y*IrradianceAtlasWidth+X]=new Vector4((float)Row[X*4],(float)Row[X*4+1],(float)Row[X*4+2],(float)Row[X*4+3]);
                    }
                }
                var Distance=new Vector2[DistanceAtlasWidth*DistanceAtlasHeight];
                for(var Y=0;Y<DistanceAtlasHeight;Y++){
                    var Row=(Half*)(Mapped+DistanceFootprint.Offset+(ulong)Y*DistanceFootprint.Footprint.RowPitch);
                    for(var X=0;X<DistanceAtlasWidth;X++){
                        Distance[Y*DistanceAtlasWidth+X]=new Vector2((float)Row[X*2],(float)Row[X*2+1]);
                    }
                }
                return new GlobalIlluminationReadback(Irradiance,Distance);
            } finally {
                D3D12_RANGE NoWrite=new();
                Readback->Unmap(0,&NoWrite);
            }
        } finally {
            if(Event!=HANDLE.NULL){ _=CloseHandle(Event); }
            if(Fence!=null){ _=Fence->Release(); }
            if(Readback!=null){ _=Readback->Release(); }
            if(List!=null){ _=List->Release(); }
            if(Allocator!=null){ _=Allocator->Release(); }
        }
    }
    public void Dispose(){
        if(this.Disposed){
            return;
        }
        this.Disposed=true;
        if(this.ConstantsBuffer!=null){
            this.ConstantsBuffer->Unmap(0,null);
            _=this.ConstantsBuffer->Release();
        }
        if(this.CaptureFrameConstants!=null){
            this.CaptureFrameConstants->Unmap(0,null);
            _=this.CaptureFrameConstants->Release();
        }
        if(this.PipelineState!=null){ _=this.PipelineState->Release(); }
        if(this.RootSignature!=null){ _=this.RootSignature->Release(); }
        if(this.DescriptorHeap!=null){ _=this.DescriptorHeap->Release(); }
        if(this.RtvHeap!=null){ _=this.RtvHeap->Release(); }
        if(this.DsvHeap!=null){ _=this.DsvHeap->Release(); }
        if(this.CaptureColor!=null){ _=this.CaptureColor->Release(); }
        if(this.CaptureDepth!=null){ _=this.CaptureDepth->Release(); }
        if(this.CaptureDistance!=null){ _=this.CaptureDistance->Release(); }
        if(this.IrradianceAtlas!=null){ _=this.IrradianceAtlas->Release(); }
        if(this.DistanceAtlas!=null){ _=this.DistanceAtlas->Release(); }
    }
}
/// <summary>プローブアトラスのテスト専用コピー（行優先。サイズは<see cref="StageGlobalIllumination"/>のアトラスに合わせる）。</summary>
internal sealed record GlobalIlluminationReadback(Vector4[]Irradiance,Vector2[]Distance){
    /// <summary><paramref name="Direction"/>方向に対するプローブの照度（最も近い内部テクセル）。</summary>
    internal Vector4 ProbeIrradiance(int Probe,Vector3 Direction){
        var Origin=StageGlobalIllumination.TileOrigin(Probe);
        var Texel=Interior(Direction,StageGlobalIllumination.IrradianceTexels);
        return this.Irradiance[(Origin.IrradianceY+1+Texel.Y)*StageGlobalIllumination.IrradianceAtlasWidth+Origin.IrradianceX+1+Texel.X];
    }
    internal Vector4 ProbeIrradianceTexel(int Probe,int X,int Y){
        var Origin=StageGlobalIllumination.TileOrigin(Probe);
        return this.Irradiance[(Origin.IrradianceY+1+Y)*StageGlobalIllumination.IrradianceAtlasWidth+Origin.IrradianceX+1+X];
    }
    internal Vector2 ProbeDistance(int Probe,Vector3 Direction){
        var Origin=StageGlobalIllumination.TileOrigin(Probe);
        var Texel=Interior(Direction,StageGlobalIllumination.DistanceTexels);
        return this.Distance[(Origin.DistanceY+1+Texel.Y)*StageGlobalIllumination.DistanceAtlasWidth+Origin.DistanceX+1+Texel.X];
    }
    private static (int X,int Y) Interior(Vector3 Direction,int Size){
        var Encoded=StageGlobalIllumination.OctahedralEncode(Vector3.Normalize(Direction));
        return (Math.Clamp((int)((Encoded.X*0.5f+0.5f)*Size),0,Size-1),Math.Clamp((int)((Encoded.Y*0.5f+0.5f)*Size),0,Size-1));
    }
}
