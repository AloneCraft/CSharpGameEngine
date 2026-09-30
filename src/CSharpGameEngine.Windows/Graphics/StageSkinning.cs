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
/// GPU linear blend skinning (ADR-0066). Each skinned mesh keeps its rest vertices (the Stage
/// upload-heap vertex buffer) and gets an influence buffer and a skinned vertex buffer in the default
/// heap; Skinning.hlsl rewrites the skinned buffer at the start of every frame from the latest
/// palette, before any pass draws. Palettes go through one upload region per frame slot, so a slot
/// is rewritten only after the GPU finished the frame that last read it. Root signature
/// (host-owned): 0 = 4 root constants b0, 1..3 = root SRVs t0..t2, 4 = root UAV u0.
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
    /// Registers a skinned mesh whose rest vertices live in <paramref name="RestVertices"/>. The palette
    /// starts at identity (the bind pose). Returns the view of the skinned vertex buffer to draw.
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
    internal int JointCount(int MeshIndex)=>this.MeshesByIndex.TryGetValue(MeshIndex,out var Mesh)?Mesh.JointCount:0;
    /// <summary>Replaces the palette used from the next recorded frame on. Extra matrices are ignored; missing ones keep their last value.</summary>
    internal void SetPalette(int MeshIndex,ReadOnlySpan<Matrix4x4>Palette){
        if(!this.MeshesByIndex.TryGetValue(MeshIndex,out var Mesh)){
            throw new ArgumentOutOfRangeException(nameof(MeshIndex),$"Mesh {MeshIndex} is not skinned.");
        }
        Palette[..Math.Min(Palette.Length,Mesh.JointCount)].CopyTo(Mesh.Palette);
    }
    /// <summary>Skins every mesh for this frame. Leaves a compute pipeline bound; the caller restores its graphics state.</summary>
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
    /// <summary>Test-only copy of a skinned vertex buffer (48-byte Stage vertices). The caller guarantees the queue is idle.</summary>
    internal byte[]ReadSkinnedVerticesForTesting(int MeshIndex){
        var Mesh=this.MeshesByIndex[MeshIndex];
        return GpuBufferReadback.Read(this.Device,Mesh.Output,(ulong)Mesh.VertexCount*Mesh.VertexStride,VertexState);
    }
    /// <summary>
    /// Packs eight 16-bit joint indices and eight 16-bit unorm weights per vertex. The rounding
    /// remainder goes to the heaviest weight so the quantized weights still sum to exactly one.
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
            // Root SRV addresses of structured buffers need 16-byte-multiple matrices only; each frame region is 256-byte aligned.
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
