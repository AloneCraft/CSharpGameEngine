using CSharpGameEngine.Rendering.Streaming;
using CSharpGameEngine.ShaderArtifacts;
using TerraFX.Interop.DirectX;
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
/// 遮蔽判定に使う Hi-Z（ADR-0100 決定 3、M0131 単位 1）。主パスが書いた物理距離の画像から、<see cref="Record"/> が HiZ.hlsl を段ごとに 1 回ずつ動かして、
/// 2×2 のいちばん遠い値で半分ずつ縮めた段を作る。段の大きさは <see cref="HiZPyramid.LevelSizes"/>。結果は計算シェーダーから読める状態で残る。
/// 計算のルートシグネチャ（ここが持つ）: 0 = ルート定数 b0（HiZCB）、1 = SRV テーブル t0（元の画像）、2 = UAV テーブル u0（書く段）。
/// 記述子の置き場: 0 = 距離の画像、1〜段の数 = 段ごとの SRV、その後ろに段ごとの UAV、最後に全段の SRV（遮蔽を調べる計算シェーダーが読む）。
/// </summary>
internal sealed unsafe class StageHiZ:IDisposable {
    internal const string ArtifactFileName="HiZ.cgeshaderartifact";
    private const DXGI_FORMAT Format=DXGI_FORMAT_R32_FLOAT;
    private const uint ConstantCount=5;
    private const D3D12_RESOURCE_STATES ReadableState=D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
    private readonly GraphicsDevice Device;
    private readonly string ArtifactPath;
    private ID3D12Resource*Pyramid;
    private ID3D12Resource*DescribedSceneDistance;
    private ID3D12DescriptorHeap*DescriptorHeap;
    private uint DescriptorSize;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private (int Width,int Height)[]Sizes=[];
    private bool Disposed;
    internal StageHiZ(GraphicsDevice Device,string StageArtifactPath){
        this.Device=Device;
        this.ArtifactPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(StageArtifactPath))??string.Empty,ArtifactFileName);
    }
    internal int ScreenWidth { get;private set; }
    internal int ScreenHeight { get;private set; }
    internal int LevelCount=>this.Sizes.Length;
    /// <summary>段の重なり（全段、計算シェーダーから読める状態）。まだ作っていなければ null。</summary>
    internal ID3D12Resource*Result=>this.Pyramid;
    /// <summary>段 0 の大きさ。</summary>
    internal (int Width,int Height)BaseSize=>this.Sizes.Length==0?(0,0):this.Sizes[0];
    /// <summary>全段の SRV を持つ記述子の置き場。<see cref="AllLevelsTable"/> を使う前に計算のコマンドへ設定する。</summary>
    internal ID3D12DescriptorHeap*Heap=>this.DescriptorHeap;
    /// <summary>全段の SRV（記述子の表 1 つ分）。</summary>
    internal D3D12_GPU_DESCRIPTOR_HANDLE AllLevelsTable=>new(this.DescriptorHeap->GetGPUDescriptorHandleForHeapStart(),1+this.Sizes.Length*2,this.DescriptorSize);
    /// <summary>
    /// 画面の大きさ <paramref name="ScreenWidth"/>×<paramref name="ScreenHeight"/> の距離の画像 <paramref name="SceneDistance"/>（描画先の状態）から全段を作る。
    /// 終わると距離の画像は描画先の状態に戻る。計算のルートシグネチャ・PSO・記述子の置き場を置き換えるので、呼び出し側は後で描画の束縛を戻すこと。
    /// </summary>
    internal void Record(ID3D12GraphicsCommandList*CommandList,ID3D12Resource*SceneDistance,int ScreenWidth,int ScreenHeight){
        this.EnsurePipeline();
        this.EnsureSize(SceneDistance,ScreenWidth,ScreenHeight);
        D3D12_RESOURCE_BARRIER ToReadable=D3D12_RESOURCE_BARRIER.InitTransition(SceneDistance,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        CommandList->ResourceBarrier(1,&ToReadable);
        CommandList->SetComputeRootSignature(this.RootSignature);
        CommandList->SetPipelineState(this.PipelineState);
        ID3D12DescriptorHeap*Heap=this.DescriptorHeap;
        CommandList->SetDescriptorHeaps(1,&Heap);
        var Start=this.DescriptorHeap->GetGPUDescriptorHandleForHeapStart();
        var SourceWidth=ScreenWidth;
        var SourceHeight=ScreenHeight;
        var Constants=stackalloc uint[(int)ConstantCount];
        for(var Level=0;Level<this.Sizes.Length;Level++){
            var (Width,Height)=this.Sizes[Level];
            D3D12_RESOURCE_BARRIER ToWrite=D3D12_RESOURCE_BARRIER.InitTransition(this.Pyramid,ReadableState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,(uint)Level);
            CommandList->ResourceBarrier(1,&ToWrite);
            Constants[0]=(uint)SourceWidth;
            Constants[1]=(uint)SourceHeight;
            Constants[2]=(uint)Width;
            Constants[3]=(uint)Height;
            Constants[4]=Level==0?1u:0u;
            CommandList->SetComputeRoot32BitConstants(0,ConstantCount,Constants,0);
            CommandList->SetComputeRootDescriptorTable(1,new D3D12_GPU_DESCRIPTOR_HANDLE(Start,Level==0?0:Level,this.DescriptorSize));
            CommandList->SetComputeRootDescriptorTable(2,new D3D12_GPU_DESCRIPTOR_HANDLE(Start,1+this.Sizes.Length+Level,this.DescriptorSize));
            CommandList->Dispatch(((uint)Width+7u)/8u,((uint)Height+7u)/8u,1);
            D3D12_RESOURCE_BARRIER ToRead=D3D12_RESOURCE_BARRIER.InitTransition(this.Pyramid,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ReadableState,(uint)Level);
            CommandList->ResourceBarrier(1,&ToRead);
            SourceWidth=Width;
            SourceHeight=Height;
        }
        D3D12_RESOURCE_BARRIER ToTarget=D3D12_RESOURCE_BARRIER.InitTransition(SceneDistance,D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
        CommandList->ResourceBarrier(1,&ToTarget);
    }
    /// <summary>試験用: 段 <paramref name="Level"/> を読む（行優先）。GPU が止まっていることは呼び出し側が保証する。</summary>
    internal float[]ReadLevelForTesting(int Level){
        if(this.Pyramid==null){ throw new InvalidOperationException("The Hi-Z pyramid has not been built yet."); }
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)Level,(uint)this.Sizes.Length,nameof(Level));
        var (Data,RowPitch,Width,Height)=GpuTextureReadback.Read(this.Device,this.Pyramid,ReadableState,(uint)Level);
        var Result=new float[Width*Height];
        fixed(byte*Bytes=Data){
            for(var Y=0;Y<Height;Y++){
                new ReadOnlySpan<float>(Bytes+Y*RowPitch,Width).CopyTo(Result.AsSpan(Y*Width,Width));
            }
        }
        return Result;
    }
    private void EnsureSize(ID3D12Resource*SceneDistance,int ScreenWidth,int ScreenHeight){
        if(this.Pyramid!=null&&this.ScreenWidth==ScreenWidth&&this.ScreenHeight==ScreenHeight&&this.DescribedSceneDistance==SceneDistance){ return; }
        // 作り直すのは画面の大きさが変わったときだけ（描画器が GPU の完了を待ってから距離の画像を作り直す）。
        this.ReleaseTargets();
        this.ScreenWidth=ScreenWidth;
        this.ScreenHeight=ScreenHeight;
        this.Sizes=HiZPyramid.LevelSizes(ScreenWidth,ScreenHeight);
        this.DescribedSceneDistance=SceneDistance;
        var Desc=D3D12_RESOURCE_DESC.Tex2D(Format,(ulong)this.Sizes[0].Width,(uint)this.Sizes[0].Height,1,(ushort)this.Sizes.Length);
        Desc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
        D3D12_HEAP_PROPERTIES HeapProperties=new(D3D12_HEAP_TYPE_DEFAULT);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&HeapProperties,D3D12_HEAP_FLAG_NONE,&Desc,ReadableState,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource","creating the Hi-Z pyramid");
        this.Pyramid=Resource;
        this.EnsureHeap((uint)(2+this.Sizes.Length*2));
        this.WriteSrv(SceneDistance,0,0);
        this.WriteSrv(this.Pyramid,(uint)(1+this.Sizes.Length*2),0,(uint)this.Sizes.Length);
        for(var Level=0;Level<this.Sizes.Length;Level++){
            this.WriteSrv(this.Pyramid,(uint)(1+Level),(uint)Level);
            D3D12_UNORDERED_ACCESS_VIEW_DESC Uav=new(){ Format=Format,ViewDimension=D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2D };
            Uav.Texture2D.MipSlice=(uint)Level;
            this.Device.Device->CreateUnorderedAccessView(this.Pyramid,null,&Uav,this.CpuHandle((uint)(1+this.Sizes.Length+Level)));
        }
    }
    private void EnsureHeap(uint Count){
        if(this.DescriptorHeap!=null&&this.DescriptorHeap->GetDesc().NumDescriptors>=Count){ return; }
        if(this.DescriptorHeap!=null){ _=this.DescriptorHeap->Release();this.DescriptorHeap=null; }
        D3D12_DESCRIPTOR_HEAP_DESC Desc=new(){ Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,NumDescriptors=Count,Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE };
        ID3D12DescriptorHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateDescriptorHeap(&Desc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),
            "ID3D12Device::CreateDescriptorHeap","creating the Hi-Z descriptor heap");
        this.DescriptorHeap=Heap;
        this.DescriptorSize=this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
    }
    private void WriteSrv(ID3D12Resource*Resource,uint Slot,uint Mip,uint MipLevels=1){
        D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){
            Format=Format,
            ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        Srv.Texture2D.MostDetailedMip=Mip;
        Srv.Texture2D.MipLevels=MipLevels;
        this.Device.Device->CreateShaderResourceView(Resource,&Srv,this.CpuHandle(Slot));
    }
    private D3D12_CPU_DESCRIPTOR_HANDLE CpuHandle(uint Slot)=>new(this.DescriptorHeap->GetCPUDescriptorHandleForHeapStart(),(int)Slot,this.DescriptorSize);
    private void EnsurePipeline(){
        if(this.PipelineState!=null){ return; }
        if(!ShaderArtifactRuntime.TryLoad(this.ArtifactPath,ShaderArtifactTargetKind.Compute,out var Package,out var Error)||Package is null){
            throw new InvalidDataException($"Occlusion culling requires '{this.ArtifactPath}': {Error}");
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
                "ID3D12Device::CreateComputePipelineState","creating the Hi-Z pipeline");
            this.PipelineState=Pipeline;
        }
    }
    private ID3D12RootSignature*CreateRootSignature(){
        D3D12_DESCRIPTOR_RANGE SrvRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,NumDescriptors=1,BaseShaderRegister=0 };
        D3D12_DESCRIPTOR_RANGE UavRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_UAV,NumDescriptors=1,BaseShaderRegister=0 };
        D3D12_ROOT_PARAMETER*Parameters=stackalloc D3D12_ROOT_PARAMETER[3];
        Parameters[0]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[0].Constants=new D3D12_ROOT_CONSTANTS { ShaderRegister=0,Num32BitValues=ConstantCount };
        Parameters[1]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[1].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&SrvRange };
        Parameters[2]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[2].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&UavRange };
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=3,pParameters=Parameters };
        ID3DBlob*Signature=null;
        ID3DBlob*Error=null;
        try {
            D3D12Diagnostics.EnsureSuccess(D3D12SerializeRootSignature(&Desc,D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1,&Signature,&Error),
                "D3D12SerializeRootSignature","serializing the Hi-Z root signature");
            ID3D12RootSignature*Result;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateRootSignature(0,Signature->GetBufferPointer(),Signature->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Result),
                "ID3D12Device::CreateRootSignature","creating the Hi-Z root signature");
            return Result;
        } finally {
            if(Signature!=null){ _=Signature->Release(); }
            if(Error!=null){ _=Error->Release(); }
        }
    }
    private void ReleaseTargets(){
        this.DescribedSceneDistance=null;
        if(this.Pyramid!=null){ _=this.Pyramid->Release();this.Pyramid=null; }
    }
    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        this.ReleaseTargets();
        if(this.PipelineState!=null){ _=this.PipelineState->Release(); }
        if(this.RootSignature!=null){ _=this.RootSignature->Release(); }
        if(this.DescriptorHeap!=null){ _=this.DescriptorHeap->Release(); }
    }
}
