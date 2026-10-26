using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Rendering;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D_PRIMITIVE_TOPOLOGY;
using static TerraFX.Interop.DirectX.D3D12_CULL_MODE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_INPUT_CLASSIFICATION;
using static TerraFX.Interop.DirectX.D3D12_PRIMITIVE_TOPOLOGY_TYPE;
using static TerraFX.Interop.DirectX.D3D12_SRV_DIMENSION;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.Windows.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>
/// スクリーンレイヤー（ADR-0078/M0104）：完成フレーム上に描画するゲームUI。トーンマッピングとオーバーレイの後、バックバッファへ直接描画するため、
/// 色はsRGB値としてそのまま書き込まれる。ランタイムは毎フレームレイヤーを設定する（<see cref="SetScreenLayer"/>）。設定されなかったフレームには何も描画しない。
/// テクスチャはコンテンツハッシュで常駐管理し、デバイス喪失後は記録済みレイヤーから再作成する。テクスチャはフレームを記録中でないときに作成し、
/// GPUのアイドルを待つ。フレーム用コマンドリストを共有しているためである。
/// </summary>
public sealed unsafe partial class StageRenderer {
    /// <summary>同時に常駐可能な画面テクスチャの最大種類数（フォントごとにアトラス1つ、画像ごとに1つ）。</summary>
    public const int MaxResidentScreenTextures=64;
    private const int ScreenVertexStride=32;
    private const int ScreenRootConstantCount=4;

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenVertexGpu {
        public float X;
        public float Y;
        public float U;
        public float V;
        public float R;
        public float G;
        public float B;
        public float A;
    }

    private byte[]ScreenVertexShaderBytecode=[];
    private byte[]ScreenPixelShaderBytecode=[];
    private ID3D12RootSignature*ScreenRootSignature;
    private ID3D12PipelineState*ScreenPipelineState;
    private ID3D12Resource*ScreenVertexBuffer;
    private byte*ScreenVertexMapped;
    private ID3D12DescriptorHeap*ScreenSrvHeap;
    private readonly Dictionary<Hash256,nint>ScreenTextures=[];
    private readonly RenderScreenBatch[]ScreenBatches=new RenderScreenBatch[RenderScreenLayer.MaximumBatches];
    private readonly ID3D12Resource*[]ScreenBatchResources=new ID3D12Resource*[RenderScreenLayer.MaximumBatches];
    private int ScreenBatchCount;
    private int ScreenVertexCount;

    /// <summary>現在常駐している画面テクスチャ数（診断とテスト用）。</summary>
    public int ResidentScreenTextureCount=>this.ScreenTextures.Count;

    private void ApplyScreenShaders(ShaderArtifactPackage Package){
        this.ScreenVertexShaderBytecode=Package.GetBytecode("ScreenVSMain");
        this.ScreenPixelShaderBytecode=Package.GetBytecode("ScreenPSMain");
    }

    /// <summary>
    /// これから描画するフレームのスクリーンレイヤーを設定する。<paramref name="Vertices"/>は描画サーフェス上のピクセル座標（原点は左上）。
    /// <paramref name="Batches"/>を順番に描画する。レイヤーは次の<see cref="RenderFrame"/>で消費される。
    /// </summary>
    public void SetScreenLayer(ReadOnlySpan<RenderScreenVertex>Vertices,ReadOnlySpan<RenderScreenBatch>Batches){
        this.ThrowIfDisposed();
        RenderScreenLayer.Validate(Vertices,Batches);
        if(Batches.IsEmpty||Vertices.IsEmpty){
            this.ScreenBatchCount=0;
            this.ScreenVertexCount=0;
            return;
        }
        this.EnsureScreenResources();
        for(int Index=0;Index<Batches.Length;Index++){
            this.ScreenBatchResources[Index]=this.GetOrCreateScreenTexture(Batches[Index].Texture);
        }
        float ScaleX=2f/this.Width;
        float ScaleY=2f/this.Height;
        byte*Destination=this.ScreenVertexMapped+((nuint)this.FrameIndex*(nuint)RenderScreenLayer.MaximumVertices*ScreenVertexStride);
        for(int Index=0;Index<Vertices.Length;Index++){
            ref readonly RenderScreenVertex Vertex=ref Vertices[Index];
            ScreenVertexGpu*Packed=(ScreenVertexGpu*)(Destination+((nuint)Index*ScreenVertexStride));
            Packed->X=(Vertex.Position.X*ScaleX)-1f;
            Packed->Y=1f-(Vertex.Position.Y*ScaleY);
            Packed->U=Vertex.Uv.X;
            Packed->V=Vertex.Uv.Y;
            Packed->R=Vertex.Color.X;
            Packed->G=Vertex.Color.Y;
            Packed->B=Vertex.Color.Z;
            Packed->A=Vertex.Color.W;
        }
        Batches.CopyTo(this.ScreenBatches);
        this.ScreenBatchCount=Batches.Length;
        this.ScreenVertexCount=Vertices.Length;
    }

    private void CreateScreenRootSignature(){
        this.ScreenRootSignature=this.CreateSrvAndConstantsRootSignature(D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT,nameof(this.CreateScreenRootSignature));
    }

    private void CreateScreenPipelineState(){
        fixed(byte*PPosition="POSITION\0"u8)
        fixed(byte*PTexCoord="TEXCOORD\0"u8)
        fixed(byte*PColor="COLOR\0"u8)
        fixed(byte*PVertexShader=this.ScreenVertexShaderBytecode)
        fixed(byte*PPixelShader=this.ScreenPixelShaderBytecode){
            D3D12_INPUT_ELEMENT_DESC*InputElements=stackalloc D3D12_INPUT_ELEMENT_DESC[3]{
                new D3D12_INPUT_ELEMENT_DESC { SemanticName=(sbyte*)PPosition,Format=DXGI_FORMAT_R32G32_FLOAT,AlignedByteOffset=0,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA },
                new D3D12_INPUT_ELEMENT_DESC { SemanticName=(sbyte*)PTexCoord,Format=DXGI_FORMAT_R32G32_FLOAT,AlignedByteOffset=8,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA },
                new D3D12_INPUT_ELEMENT_DESC { SemanticName=(sbyte*)PColor,Format=DXGI_FORMAT_R32G32B32A32_FLOAT,AlignedByteOffset=16,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA },
            };
            D3D12_GRAPHICS_PIPELINE_STATE_DESC PsoDesc=new(){
                InputLayout=new D3D12_INPUT_LAYOUT_DESC { pInputElementDescs=InputElements,NumElements=3 },
                pRootSignature=this.ScreenRootSignature,
                VS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PVertexShader,BytecodeLength=(nuint)this.ScreenVertexShaderBytecode.Length },
                PS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PPixelShader,BytecodeLength=(nuint)this.ScreenPixelShaderBytecode.Length },
                RasterizerState=D3D12_RASTERIZER_DESC.DEFAULT,
                BlendState=D3D12_BLEND_DESC.DEFAULT,
                DepthStencilState=D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                SampleMask=uint.MaxValue,
                PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                NumRenderTargets=1,
                DSVFormat=DXGI_FORMAT_UNKNOWN,
                SampleDesc=new DXGI_SAMPLE_DESC(1,0),
            };
            PsoDesc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
            PsoDesc.DepthStencilState.DepthEnable=FALSE;
            PsoDesc.DepthStencilState.StencilEnable=FALSE;
            // フレームの上にストレートアルファで描画する。バックバッファのアルファチャンネルにはフレームが書いた値を保持する。
            PsoDesc.BlendState.RenderTarget[0]=new D3D12_RENDER_TARGET_BLEND_DESC {
                BlendEnable=TRUE,
                LogicOpEnable=FALSE,
                SrcBlend=D3D12_BLEND.D3D12_BLEND_SRC_ALPHA,
                DestBlend=D3D12_BLEND.D3D12_BLEND_INV_SRC_ALPHA,
                BlendOp=D3D12_BLEND_OP.D3D12_BLEND_OP_ADD,
                SrcBlendAlpha=D3D12_BLEND.D3D12_BLEND_ZERO,
                DestBlendAlpha=D3D12_BLEND.D3D12_BLEND_ONE,
                BlendOpAlpha=D3D12_BLEND_OP.D3D12_BLEND_OP_ADD,
                LogicOp=D3D12_LOGIC_OP.D3D12_LOGIC_OP_NOOP,
                RenderTargetWriteMask=(byte)D3D12_COLOR_WRITE_ENABLE.D3D12_COLOR_WRITE_ENABLE_ALL,
            };
            PsoDesc.RTVFormats[0]=BackBufferFormat;
            ID3D12PipelineState*Pipeline;
            HRESULT Result=this.Device!.Device->CreateGraphicsPipelineState(&PsoDesc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline);
            if(Result.FAILED){ this.Device.PollDebugMessages(); }
            D3D12Diagnostics.EnsureSuccess(Result,"ID3D12Device::CreateGraphicsPipelineState",
                Result.FAILED?"creating the screen layer pipeline state"+Environment.NewLine+string.Join(Environment.NewLine,this.Device.CollectedMessages):"creating the screen layer pipeline state");
            this.ScreenPipelineState=Pipeline;
        }
    }

    /// <summary>初回使用時に頂点バッファとディスクリプタヒープを作成する。スクリーンレイヤーを使わないゲームにはコストを発生させない。</summary>
    private void EnsureScreenResources(){
        if(this.ScreenVertexBuffer!=null){ return; }
        uint BufferSize=checked((uint)(RenderScreenLayer.MaximumVertices*ScreenVertexStride*FrameCount));
        this.CreateUploadBuffer(BufferSize,out this.ScreenVertexBuffer,out this.ScreenVertexMapped,"creating the screen layer vertex buffer");
        D3D12_DESCRIPTOR_HEAP_DESC HeapDesc=new(){
            Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
            NumDescriptors=(uint)(RenderScreenLayer.MaximumBatches*FrameCount),
            Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE,
        };
        ID3D12DescriptorHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateDescriptorHeap(&HeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),
            "ID3D12Device::CreateDescriptorHeap","creating the screen layer descriptor heap");
        this.ScreenSrvHeap=Heap;
    }

    private ID3D12Resource*GetOrCreateScreenTexture(RenderTextureResource Texture){
        if(this.ScreenTextures.TryGetValue(Texture.ContentHash,out nint Existing)){
            return(ID3D12Resource*)Existing;
        }
        if(this.ScreenTextures.Count>=MaxResidentScreenTextures){
            throw new InvalidOperationException($"More than {MaxResidentScreenTextures} distinct screen textures are in use.");
        }
        // アップロードはフレームのコマンドリストと初回のアロケーターを共有するためGPUアイドルが必要。テクスチャが初めて現れた時に実行する。
        this.WaitForGpuIdle();
        ID3D12Resource*Created=this.CreateTextureResource(Texture);
        this.ScreenTextures.Add(Texture.ContentHash,(nint)Created);
        return Created;
    }

    /// <summary>今フレームに設定されたレイヤーを、完成フレームとオーバーレイの上へ描画してから破棄する。</summary>
    private void DrawScreenLayer(){
        if(this.ScreenBatchCount==0){ return; }
        int BatchCount=this.ScreenBatchCount;
        this.ScreenBatchCount=0;
        this.CommandList->SetGraphicsRootSignature(this.ScreenRootSignature);
        this.CommandList->SetPipelineState(this.ScreenPipelineState);
        ID3D12DescriptorHeap*Heap=this.ScreenSrvHeap;
        this.CommandList->SetDescriptorHeaps(1,&Heap);
        fixed(D3D12_VIEWPORT*PViewport=&this.Viewport)
        fixed(RECT*PScissorRect=&this.ScissorRect){
            this.CommandList->RSSetViewports(1,PViewport);
            this.CommandList->RSSetScissorRects(1,PScissorRect);
        }
        this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        D3D12_VERTEX_BUFFER_VIEW View=new(){
            BufferLocation=this.ScreenVertexBuffer->GetGPUVirtualAddress()+((ulong)this.FrameIndex*(ulong)RenderScreenLayer.MaximumVertices*ScreenVertexStride),
            SizeInBytes=checked((uint)(this.ScreenVertexCount*ScreenVertexStride)),
            StrideInBytes=ScreenVertexStride,
        };
        this.CommandList->IASetVertexBuffers(0,1,&View);
        D3D12_SHADER_RESOURCE_VIEW_DESC SrvDesc=new(){
            Format=MaterialTextureFormat,
            ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        SrvDesc.Texture2D.MipLevels=1;
        D3D12_CPU_DESCRIPTOR_HANDLE CpuStart=this.ScreenSrvHeap->GetCPUDescriptorHandleForHeapStart();
        D3D12_GPU_DESCRIPTOR_HANDLE GpuStart=this.ScreenSrvHeap->GetGPUDescriptorHandleForHeapStart();
        // フレームスロットごとにディスクリプターのブロックを用意する。処理中フレームがもう一方のスロットを読み取っている可能性があるため。
        int FirstDescriptor=(int)this.FrameIndex*RenderScreenLayer.MaximumBatches;
        float*Constants=stackalloc float[ScreenRootConstantCount];
        for(int Index=0;Index<BatchCount;Index++){
            RenderScreenBatch Batch=this.ScreenBatches[Index];
            if(Batch.VertexCount==0){ continue; }
            this.Device!.Device->CreateShaderResourceView(this.ScreenBatchResources[Index],&SrvDesc,new D3D12_CPU_DESCRIPTOR_HANDLE(CpuStart,FirstDescriptor+Index,this.SrvDescriptorSize));
            this.CommandList->SetGraphicsRootDescriptorTable(0,new D3D12_GPU_DESCRIPTOR_HANDLE(GpuStart,FirstDescriptor+Index,this.SrvDescriptorSize));
            Constants[0]=Batch.Shading==RenderScreenShading.DistanceField?1f:0f;
            Constants[1]=0f;
            Constants[2]=0f;
            Constants[3]=0f;
            this.CommandList->SetGraphicsRoot32BitConstants(1,ScreenRootConstantCount,Constants,0);
            this.CommandList->DrawInstanced((uint)Batch.VertexCount,1,(uint)Batch.FirstVertex,0);
        }
        this.ScreenVertexCount=0;
    }

    private void ReleaseScreenResources(){
        foreach(nint Pointer in this.ScreenTextures.Values){ _=((ID3D12Resource*)Pointer)->Release(); }
        this.ScreenTextures.Clear();
        Array.Clear(this.ScreenBatchResources);
        this.ScreenBatchCount=0;
        this.ScreenVertexCount=0;
        if(this.ScreenVertexBuffer!=null){
            this.ScreenVertexBuffer->Unmap(0,null);
            this.ScreenVertexMapped=null;
            _=this.ScreenVertexBuffer->Release();
            this.ScreenVertexBuffer=null;
        }
        if(this.ScreenSrvHeap!=null){
            _=this.ScreenSrvHeap->Release();
            this.ScreenSrvHeap=null;
        }
        if(this.ScreenPipelineState!=null){
            _=this.ScreenPipelineState->Release();
            this.ScreenPipelineState=null;
        }
        if(this.ScreenRootSignature!=null){
            _=this.ScreenRootSignature->Release();
            this.ScreenRootSignature=null;
        }
    }
}
