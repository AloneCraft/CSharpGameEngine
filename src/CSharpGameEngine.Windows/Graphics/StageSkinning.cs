using System.Numerics;
using System.Runtime.CompilerServices;
using CSharpGameEngine.ShaderArtifacts;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D12_ROOT_PARAMETER_TYPE;
using static TerraFX.Interop.DirectX.D3D12_SHADER_VISIBILITY;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// GPUで実行する線形ブレンドスキニング（ADR-0066）。スキニングメッシュごとにレスト頂点（Stageのアップロードヒープ頂点バッファ）を保持し、
/// 影響度バッファとスキニング済み頂点バッファをデフォルトヒープに作る。Skinning.hlslが各フレームの先頭で最新パレットを使って頂点を書き換え、
/// その後に各パスで描画する。パレットはフレームスロットごとに1つのアップロード領域を通るため、GPUが前回読み取ったフレームを完了するまで上書きしない。
/// ルートシグネチャはホスト所有：0 = ルート定数4つb0、1～3 = ルートSRV t0～t2、4 = ルートUAV u0。
/// </summary>
internal sealed unsafe class StageSkinning:IDisposable {
    internal const string ArtifactFileName="Skinning.cgeshaderartifact";
    internal const int InfluencesPerVertex=8;
    private const uint InfluenceStride=32;
    private const uint MatrixBytes=64;
    private const D3D12_RESOURCE_STATES VertexState=D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER;
    private readonly GraphicsDevice Device;
    private readonly string ArtifactPath;
    private readonly uint FrameCount;
    private readonly List<SkinnedMesh>Meshes=[];
    private readonly Dictionary<int,SkinnedMesh>MeshesByIndex=[];
    private ID3D12Resource*PaletteUpload;
    private byte*PaletteMapped;
    private uint PaletteBytesPerFrame;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private bool Disposed;
    private sealed class SkinnedMesh {
        public required int MeshIndex;
        public required uint VertexCount;
        public required uint VertexStride;
        public required int JointCount;
        public required uint PaletteOffset;
        public required ulong RestAddress;
        public ID3D12Resource*Influences;
        public ID3D12Resource*Output;
        public required Matrix4x4[]Palette;
    }
    internal StageSkinning(GraphicsDevice Device,string StageArtifactPath,uint FrameCount){
        this.Device=Device;
        this.FrameCount=FrameCount;
        this.ArtifactPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(StageArtifactPath))??string.Empty,ArtifactFileName);
    }
    internal bool HasMeshes=>this.Meshes.Count>0;
    /// <summary>
    /// レスト頂点が<paramref name="RestVertices"/>にあるスキニングメッシュを登録する。パレットは恒等変換（バインド姿勢）から開始する。
    /// 描画用スキニング頂点バッファのビューを返す。
    /// </summary>
    internal D3D12_VERTEX_BUFFER_VIEW AddMesh(int MeshIndex,ID3D12Resource*RestVertices,uint VertexCount,uint VertexStride,int JointCount,IReadOnlyList<ushort>JointIndices,IReadOnlyList<float>Weights){
        if(JointCount<=0||JointIndices.Count!=VertexCount*InfluencesPerVertex||Weights.Count!=VertexCount*InfluencesPerVertex){
            throw new ArgumentException($"Skinned mesh {MeshIndex} needs {InfluencesPerVertex} influences per vertex and at least one joint.");
        }
        var Packed=PackInfluences(JointIndices,Weights,VertexCount,JointCount);
        var Mesh=new SkinnedMesh {
            MeshIndex=MeshIndex,
            VertexCount=VertexCount,
            VertexStride=VertexStride,
            JointCount=JointCount,
            PaletteOffset=(uint)this.Meshes.Sum(static Existing=>Existing.JointCount)*MatrixBytes,
            RestAddress=RestVertices->GetGPUVirtualAddress(),
            Palette=[.. Enumerable.Repeat(Matrix4x4.Identity,JointCount)],
        };
        Mesh.Influences=this.CreateUploadBuffer(Packed,"creating a skin influence buffer");
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_DEFAULT);
        var Desc=D3D12_RESOURCE_DESC.Buffer((ulong)VertexCount*VertexStride,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
        ID3D12Resource*Output;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,VertexState,null,__uuidof<ID3D12Resource>(),(void**)&Output),
            "ID3D12Device::CreateCommittedResource","creating a skinned vertex buffer");
        Mesh.Output=Output;
        this.Meshes.Add(Mesh);
        this.MeshesByIndex.Add(MeshIndex,Mesh);
        this.ReleasePalette();
        return new D3D12_VERTEX_BUFFER_VIEW { BufferLocation=Output->GetGPUVirtualAddress(),StrideInBytes=VertexStride,SizeInBytes=VertexCount*VertexStride };
    }
    internal bool IsSkinned(int MeshIndex)=>this.MeshesByIndex.ContainsKey(MeshIndex);
    internal (Vector3 Minimum,Vector3 Maximum)ConservativeBounds(int MeshIndex,Vector3 Minimum,Vector3 Maximum){
        if(!this.MeshesByIndex.TryGetValue(MeshIndex,out var Mesh)){ return(Minimum,Maximum); }
        var Min=new Vector3(float.PositiveInfinity);var Max=new Vector3(float.NegativeInfinity);
        foreach(var Joint in Mesh.Palette){
            for(var I=0;I<8;I++){
                var P=Vector3.Transform(new Vector3((I&1)==0?Minimum.X:Maximum.X,(I&2)==0?Minimum.Y:Maximum.Y,(I&4)==0?Minimum.Z:Maximum.Z),Joint);
                Min=Vector3.Min(Min,P);Max=Vector3.Max(Max,P);
            }
        }
        return(Min,Max);
    }
    internal int JointCount(int MeshIndex)=>this.MeshesByIndex.TryGetValue(MeshIndex,out var Mesh)?Mesh.JointCount:0;
    /// <summary>次に記録するフレーム以降で使用するパレットを置き換える。余分な行列は無視し、不足する行列には前回の値を維持する。</summary>
    internal void SetPalette(int MeshIndex,ReadOnlySpan<Matrix4x4>Palette){
        if(!this.MeshesByIndex.TryGetValue(MeshIndex,out var Mesh)){
            throw new ArgumentOutOfRangeException(nameof(MeshIndex),$"Mesh {MeshIndex} is not skinned.");
        }
        Palette[..Math.Min(Palette.Length,Mesh.JointCount)].CopyTo(Mesh.Palette);
    }
    /// <summary>このフレーム用にすべてのメッシュをスキニングする。Computeパイプラインがバインドされたまま戻るため、呼び出し側でグラフィックス状態を復元する。</summary>
    internal void Record(ID3D12GraphicsCommandList*CommandList,uint FrameIndex){
        if(this.Meshes.Count==0){
            return;
        }
        this.EnsurePipeline();
        var FrameBase=FrameIndex*this.PaletteBytesPerFrame;
        foreach(var Mesh in this.Meshes){
            fixed(Matrix4x4*Source=Mesh.Palette){
                Unsafe.CopyBlock(this.PaletteMapped+FrameBase+Mesh.PaletteOffset,Source,(uint)Mesh.JointCount*MatrixBytes);
            }
        }
        var Barriers=stackalloc D3D12_RESOURCE_BARRIER[this.Meshes.Count];
        for(var Index=0;Index<this.Meshes.Count;Index++){
            Barriers[Index]=D3D12_RESOURCE_BARRIER.InitTransition(this.Meshes[Index].Output,VertexState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        }
        CommandList->ResourceBarrier((uint)this.Meshes.Count,Barriers);
        CommandList->SetComputeRootSignature(this.RootSignature);
        CommandList->SetPipelineState(this.PipelineState);
        var PaletteBase=this.PaletteUpload->GetGPUVirtualAddress()+FrameBase;
        uint*Constants=stackalloc uint[4];
        Constants[1]=0;
        Constants[2]=0;
        Constants[3]=0;
        foreach(var Mesh in this.Meshes){
            Constants[0]=Mesh.VertexCount;
            CommandList->SetComputeRoot32BitConstants(0,4,Constants,0);
            CommandList->SetComputeRootShaderResourceView(1,Mesh.RestAddress);
            CommandList->SetComputeRootShaderResourceView(2,Mesh.Influences->GetGPUVirtualAddress());
            CommandList->SetComputeRootShaderResourceView(3,PaletteBase+Mesh.PaletteOffset);
            CommandList->SetComputeRootUnorderedAccessView(4,Mesh.Output->GetGPUVirtualAddress());
            CommandList->Dispatch((Mesh.VertexCount+63u)/64u,1,1);
        }
        for(var Index=0;Index<this.Meshes.Count;Index++){
            Barriers[Index]=D3D12_RESOURCE_BARRIER.InitTransition(this.Meshes[Index].Output,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,VertexState);
        }
        CommandList->ResourceBarrier((uint)this.Meshes.Count,Barriers);
    }
    /// <summary>スキニング済み頂点バッファ（Stageの48バイト頂点）のテスト専用コピー。呼び出し側がキューをアイドル状態にすること。</summary>
    internal byte[]ReadSkinnedVerticesForTesting(int MeshIndex){
        var Mesh=this.MeshesByIndex[MeshIndex];
        return GpuBufferReadback.Read(this.Device,Mesh.Output,(ulong)Mesh.VertexCount*Mesh.VertexStride,VertexState);
    }
    /// <summary>
    /// 頂点ごとに16ビットのジョイントインデックス8個と、16ビットUNORMウェイト8個をパックする。
    /// 丸め誤差は最も重いウェイトへ加えるため、量子化後も合計が正確に1になる。
    /// </summary>
    internal static uint[]PackInfluences(IReadOnlyList<ushort>JointIndices,IReadOnlyList<float>Weights,uint VertexCount,int JointCount){
        var Packed=new uint[VertexCount*8];
        Span<int>Quantized=stackalloc int[InfluencesPerVertex];
        for(var Vertex=0;Vertex<VertexCount;Vertex++){
            var Base=Vertex*InfluencesPerVertex;
            var Total=0;
            var Heaviest=0;
            for(var Slot=0;Slot<InfluencesPerVertex;Slot++){
                var Weight=Math.Clamp(Weights[Base+Slot],0f,1f);
                Quantized[Slot]=(int)MathF.Round(Weight*65535f);
                Total+=Quantized[Slot];
                if(Quantized[Slot]>Quantized[Heaviest]){
                    Heaviest=Slot;
                }
                if(JointIndices[Base+Slot]>=JointCount&&Quantized[Slot]>0){
                    throw new ArgumentException($"Vertex {Vertex} references joint {JointIndices[Base+Slot]} of {JointCount}.");
                }
            }
            Quantized[Heaviest]=Math.Clamp(Quantized[Heaviest]+(65535-Total),0,65535);
            for(var Pair=0;Pair<4;Pair++){
                Packed[(Vertex*8)+Pair]=(uint)JointIndices[Base+(Pair*2)]|((uint)JointIndices[Base+(Pair*2)+1]<<16);
                Packed[(Vertex*8)+4+Pair]=(uint)Quantized[Pair*2]|((uint)Quantized[(Pair*2)+1]<<16);
            }
        }
        return Packed;
    }
    private void ReleasePalette(){
        if(this.PaletteUpload!=null){
            _=this.PaletteUpload->Release();
            this.PaletteUpload=null;
            this.PaletteMapped=null;
        }
    }
    private void EnsurePipeline(){
        if(this.PaletteUpload==null){
            // 構造化バッファのルートSRVアドレスは16バイト単位の行列のみを必要とする。各フレーム領域は256バイト境界に配置する。
            this.PaletteBytesPerFrame=((uint)this.Meshes.Sum(static Mesh=>Mesh.JointCount)*MatrixBytes+255u)&~255u;
            D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_UPLOAD);
            var Desc=D3D12_RESOURCE_DESC.Buffer((ulong)this.PaletteBytesPerFrame*this.FrameCount);
            ID3D12Resource*Buffer;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_GENERIC_READ,null,__uuidof<ID3D12Resource>(),(void**)&Buffer),
                "ID3D12Device::CreateCommittedResource","creating the skin palette upload ring");
            D3D12_RANGE NoRead=new();
            byte*Mapped;
            D3D12Diagnostics.EnsureSuccess(Buffer->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping the skin palette upload ring");
            this.PaletteUpload=Buffer;
            this.PaletteMapped=Mapped;
        }
        if(this.PipelineState!=null){
            return;
        }
        if(!ShaderArtifactRuntime.TryLoad(this.ArtifactPath,ShaderArtifactTargetKind.Compute,out var Package,out var Error)||Package is null){
            throw new InvalidDataException($"Skinned meshes require '{this.ArtifactPath}': {Error}");
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
                "ID3D12Device::CreateComputePipelineState","creating the skinning pipeline");
            this.PipelineState=Pipeline;
        }
    }
    private ID3D12Resource*CreateUploadBuffer(uint[]Data,string Context){
        var Size=(ulong)Data.Length*sizeof(uint);
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_UPLOAD);
        var Desc=D3D12_RESOURCE_DESC.Buffer(Size);
        ID3D12Resource*Buffer;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_GENERIC_READ,null,__uuidof<ID3D12Resource>(),(void**)&Buffer),
            "ID3D12Device::CreateCommittedResource",Context);
        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(Buffer->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map",Context);
        fixed(uint*Source=Data){
            Unsafe.CopyBlock(Mapped,Source,(uint)Size);
        }
        Buffer->Unmap(0,null);
        return Buffer;
    }
    private ID3D12RootSignature*CreateRootSignature(){
        D3D12_ROOT_PARAMETER*Parameters=stackalloc D3D12_ROOT_PARAMETER[5];
        Parameters[0]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[0].Constants=new D3D12_ROOT_CONSTANTS { ShaderRegister=0,Num32BitValues=4 };
        for(var Register=0u;Register<3u;Register++){
            Parameters[1+Register]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
            Parameters[1+Register].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=Register };
        }
        Parameters[4]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_UAV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[4].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=5,pParameters=Parameters };
        ID3DBlob*Signature=null;
        ID3DBlob*Error=null;
        try {
            D3D12Diagnostics.EnsureSuccess(D3D12SerializeRootSignature(&Desc,D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1,&Signature,&Error),
                "D3D12SerializeRootSignature","serializing the skinning root signature");
            ID3D12RootSignature*Result;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateRootSignature(0,Signature->GetBufferPointer(),Signature->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Result),
                "ID3D12Device::CreateRootSignature","creating the skinning root signature");
            return Result;
        } finally {
            if(Signature!=null){ _=Signature->Release(); }
            if(Error!=null){ _=Error->Release(); }
        }
    }
    public void Dispose(){
        if(this.Disposed){
            return;
        }
        this.Disposed=true;
        foreach(var Mesh in this.Meshes){
            if(Mesh.Influences!=null){ _=Mesh.Influences->Release(); }
            if(Mesh.Output!=null){ _=Mesh.Output->Release(); }
        }
        this.Meshes.Clear();
        this.MeshesByIndex.Clear();
        this.ReleasePalette();
        if(this.PipelineState!=null){ _=this.PipelineState->Release(); }
        if(this.RootSignature!=null){ _=this.RootSignature->Release(); }
    }
}
