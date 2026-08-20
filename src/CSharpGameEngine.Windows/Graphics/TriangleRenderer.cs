using System.Numerics;
using System.Runtime.CompilerServices;
using CSharpGameEngine.Compiler.Hlsl;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D_PRIMITIVE_TOPOLOGY;
using static TerraFX.Interop.DirectX.D3D_ROOT_SIGNATURE_VERSION;
using static TerraFX.Interop.DirectX.D3D12_COMMAND_LIST_TYPE;
using static TerraFX.Interop.DirectX.D3D12_CULL_MODE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_FENCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_INPUT_CLASSIFICATION;
using static TerraFX.Interop.DirectX.D3D12_PRIMITIVE_TOPOLOGY_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D12_ROOT_SIGNATURE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_TEXTURE_COPY_TYPE;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DXGI;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.DirectX.DXGI_SWAP_EFFECT;
using static TerraFX.Interop.Windows.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>
/// Owns the swap chain, pipeline, and vertex buffer for the Windows + D3D12 vertical slice and
/// draws exactly one colored triangle per frame. Not a general-purpose renderer: the next
/// milestone is expected to replace this with a real render graph.
/// </summary>
public sealed unsafe class TriangleRenderer:IDisposable {
    public const int FrameCount=2;
    public const DXGI_FORMAT BackBufferFormat=DXGI_FORMAT_R8G8B8A8_UNORM;

    private static readonly Vector4 ClearColorVector=new(0.02f,0.02f,0.08f,1.0f);
    public static readonly PixelColor ClearColor=new(5,5,20,255);

    private struct Vertex {
        public Vector3 Position;
        public Vector4 Color;
    }

    private GraphicsDevice?Device;
    private IDXGISwapChain3*SwapChain;
    private ID3D12DescriptorHeap*RtvHeap;
    private uint RtvDescriptorSize;
    private readonly ID3D12Resource*[]BackBuffers=new ID3D12Resource*[FrameCount];
    private readonly ID3D12CommandAllocator*[]CommandAllocators=new ID3D12CommandAllocator*[FrameCount];
    private ID3D12GraphicsCommandList*CommandList;
    private ID3D12Fence*Fence;
    private readonly ulong[]FenceValues=new ulong[FrameCount];
    private HANDLE FenceEvent;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private ID3D12Resource*VertexBuffer;
    private D3D12_VERTEX_BUFFER_VIEW VertexBufferView;
    private ID3D12Resource*ReadbackBuffer;
    private ulong ReadbackBufferSize;
    private D3D12_PLACED_SUBRESOURCE_FOOTPRINT ReadbackFootprint;

    private uint FrameIndex;
    private int Width;
    private int Height;
    private bool SizeIsZero;
    private bool Disposed;

    private D3D12_VIEWPORT Viewport;
    private RECT ScissorRect;

    public string AdapterDescription=> this.Device?.AdapterDescription??string.Empty;

    public bool IsWarp=> this.Device?.IsWarp??false;

    public bool DebugLayerEnabled=> this.Device?.DebugLayerEnabled??false;

    public bool GpuBasedValidationEnabled=> this.Device?.GpuBasedValidationEnabled??false;

    public IReadOnlyList<D3D12DebugMessage>DebugMessages=> this.Device?.CollectedMessages??[];

    public int ErrorOrWorseMessageCount=> this.Device?.ErrorOrWorseMessageCount??0;

    public bool ShaderCompilationSucceeded { get;private set;}

    public IReadOnlyList<HlslDiagnostic>ShaderDiagnostics { get;private set;}=[];

    public uint FramesRendered { get;private set;}

    public static TriangleRenderer Create(nint Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,string ShaderPath){
        ArgumentNullException.ThrowIfNull(ShaderPath);
        if(Width<=0||Height<=0){
            throw new ArgumentOutOfRangeException(nameof(Width),"Initial width/height must both be positive.");
        }

        TriangleRenderer Renderer=new(){
            Width=Width,
            Height=Height,
        };

        var Completed=false;
        try {
            Renderer.Initialize((HWND)Hwnd,Width,Height,DeviceOptions,ShaderPath);
            Completed=true;
            return Renderer;
        } finally {
            if(!Completed){
                Renderer.Dispose();
            }
        }
    }

    private TriangleRenderer(){
    }

    private void Initialize(HWND Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,string ShaderPath){
        this.Device=GraphicsDevice.Create(DeviceOptions);

        this.CreateSwapChain(Hwnd,Width,Height);
        this.CreateRtvHeap();
        this.CreateRenderTargetViews();

        for(var I=0;I<FrameCount;I++){
            ID3D12CommandAllocator*Allocator;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,__uuidof<ID3D12CommandAllocator>(),(void**)&Allocator),
                "ID3D12Device::CreateCommandAllocator","creating a frame command allocator");
            this.CommandAllocators[I]=Allocator;
        }

        ID3D12GraphicsCommandList*CommandList;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT, this.CommandAllocators[0],null,__uuidof<ID3D12GraphicsCommandList>(),(void**)&CommandList),
            "ID3D12Device::CreateCommandList","creating the graphics command list");
        this.CommandList=CommandList;
        D3D12Diagnostics.EnsureSuccess(CommandList->Close(),"ID3D12GraphicsCommandList::Close","closing the initial command list state");

        ID3D12Fence*Fence;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateFence(0,D3D12_FENCE_FLAG_NONE,__uuidof<ID3D12Fence>(),(void**)&Fence),
            "ID3D12Device::CreateFence","creating the frame fence");
        this.Fence=Fence;

        this.FenceEvent=CreateEventW(lpEventAttributes:null,bManualReset:FALSE,bInitialState:FALSE,lpName:null);
        if(this.FenceEvent==HANDLE.NULL){
            throw new InvalidOperationException("CreateEventW failed while creating the frame fence event.");
        }

        this.FrameIndex= this.SwapChain->GetCurrentBackBufferIndex();
        this.FenceValues[this.FrameIndex]=1;

        this.CompileShaders(ShaderPath);
        this.CreateRootSignature();
        this.CreatePipelineState();
        this.CreateVertexBuffer();
        this.UpdateViewportAndScissor(Width,Height);
    }

    private void CreateSwapChain(HWND Hwnd,int Width,int Height){
        DXGI_SWAP_CHAIN_DESC1 Desc=new(){
            BufferCount=FrameCount,
            Width=(uint)Width,
            Height=(uint)Height,
            Format=BackBufferFormat,
            BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT,
            SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD,
            SampleDesc=new DXGI_SAMPLE_DESC(1,0),
        };

        IDXGISwapChain1*SwapChain1;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Factory->CreateSwapChainForHwnd(
                                                                                 (IUnknown*)this.Device.CommandQueue,
                                                                                 Hwnd,
                                                                                 &Desc,
                                                                                 pFullscreenDesc:null,
                                                                                 pRestrictToOutput:null,
                                                                                 &SwapChain1),
            "IDXGIFactory4::CreateSwapChainForHwnd","creating the swap chain");

        IDXGISwapChain3*SwapChain3;
        var Hr=SwapChain1->QueryInterface(__uuidof<IDXGISwapChain3>(),(void**)&SwapChain3);
        _=SwapChain1->Release();
        D3D12Diagnostics.EnsureSuccess(Hr,"IDXGISwapChain1::QueryInterface(IDXGISwapChain3)","upgrading the swap chain interface");

        this.SwapChain=SwapChain3;
    }

    private void CreateRtvHeap(){
        D3D12_DESCRIPTOR_HEAP_DESC Desc=new(){
            NumDescriptors=FrameCount,
            Type=D3D12_DESCRIPTOR_HEAP_TYPE_RTV,
        };

        ID3D12DescriptorHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateDescriptorHeap(&Desc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),
            "ID3D12Device::CreateDescriptorHeap","creating the RTV descriptor heap");

        this.RtvHeap=Heap;
        this.RtvDescriptorSize= this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
    }

    private void CreateRenderTargetViews(){
        var RtvBase= this.RtvHeap->GetCPUDescriptorHandleForHeapStart();

        for(uint I=0;I<FrameCount;I++){
            ID3D12Resource*BackBuffer;
            D3D12Diagnostics.EnsureSuccess(this.SwapChain->GetBuffer(I,__uuidof<ID3D12Resource>(),(void**)&BackBuffer),
                "IDXGISwapChain3::GetBuffer","retrieving a back buffer", $"buffer[{I}]");

            D3D12_CPU_DESCRIPTOR_HANDLE RtvHandle=new(RtvBase,(int)I, this.RtvDescriptorSize);
            this.Device!.Device->CreateRenderTargetView(BackBuffer,pDesc:null,RtvHandle);

            this.BackBuffers[I]=BackBuffer;
        }
    }

    private void CompileShaders(string ShaderPath){
        var DebugShaders=false;
#if DEBUG
        DebugShaders = true;
#endif

        var Vs=HlslCompiler.Compile(new HlslCompileRequest {
                                                               FilePath=ShaderPath,
                                                               EntryPoint="VSMain",
                                                               TargetProfile="vs_6_0",
                                                               EnableDebugInfo=DebugShaders,
                                                           });

        var Ps=HlslCompiler.Compile(new HlslCompileRequest {
                                                               FilePath=ShaderPath,
                                                               EntryPoint="PSMain",
                                                               TargetProfile="ps_6_0",
                                                               EnableDebugInfo=DebugShaders,
                                                           });

        this.ShaderCompilationSucceeded=Vs.Success&&Ps.Success;
        this.ShaderDiagnostics=[.. Vs.Diagnostics,.. Ps.Diagnostics];

        if(!this.ShaderCompilationSucceeded){
            var Details=string.Join(Environment.NewLine, this.ShaderDiagnostics);
            throw new InvalidOperationException($"HLSL shader compilation failed for '{ShaderPath}':{Environment.NewLine}{Details}");
        }

        this.VertexShaderBytecode=Vs.Bytecode;
        this.PixelShaderBytecode=Ps.Bytecode;
    }

    private byte[]VertexShaderBytecode=[];
    private byte[]PixelShaderBytecode=[];

    private void CreateRootSignature(){
        D3D12_ROOT_SIGNATURE_DESC RootSignatureDesc=new(){
            Flags=D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT,
        };

        ID3DBlob*SignatureBlob=null;
        ID3DBlob*ErrorBlob=null;
        try {
            var Hr=D3D12SerializeRootSignature(&RootSignatureDesc,D3D_ROOT_SIGNATURE_VERSION_1,&SignatureBlob,&ErrorBlob);
            if(Hr.FAILED){
                var Error=ErrorBlob!=null
                    ?new string((sbyte*)ErrorBlob->GetBufferPointer())
                    :"(no error blob)";
                throw new D3D12Exception("D3D12SerializeRootSignature",Hr.Value,"serializing the empty root signature",Error);
            }

            ID3D12RootSignature*RootSignature;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateRootSignature(0,SignatureBlob->GetBufferPointer(),SignatureBlob->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&RootSignature),
                "ID3D12Device::CreateRootSignature","creating the root signature");
            this.RootSignature=RootSignature;
        } finally {
            if(SignatureBlob!=null){
                _=SignatureBlob->Release();
            }

            if(ErrorBlob!=null){
                _=ErrorBlob->Release();
            }
        }
    }

    private void CreatePipelineState(){
        fixed(byte*PPosition="POSITION\0"u8)
        fixed(byte*PColor="COLOR\0"u8)
        fixed(byte*PVertexShader= this.VertexShaderBytecode)
        fixed(byte*PPixelShader= this.PixelShaderBytecode){
            var InputElements=stackalloc D3D12_INPUT_ELEMENT_DESC[2]{
                                                                       new D3D12_INPUT_ELEMENT_DESC {
                                                                           SemanticName=(sbyte*)PPosition,
                                                                           Format=DXGI_FORMAT_R32G32B32_FLOAT,
                                                                           InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                                                                       },
                                                                       new D3D12_INPUT_ELEMENT_DESC {
                                                                           SemanticName=(sbyte*)PColor,
                                                                           Format=DXGI_FORMAT_R32G32B32A32_FLOAT,
                                                                           AlignedByteOffset=12,
                                                                           InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                                                                       },
                                                                   };

            D3D12_GRAPHICS_PIPELINE_STATE_DESC PsoDesc=new(){
                InputLayout=new D3D12_INPUT_LAYOUT_DESC {
                    pInputElementDescs=InputElements,
                    NumElements=2,
                },
                pRootSignature= this.RootSignature,
                VS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PVertexShader,BytecodeLength=(nuint)this.VertexShaderBytecode.Length },
                PS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PPixelShader,BytecodeLength=(nuint)this.PixelShaderBytecode.Length },
                RasterizerState=D3D12_RASTERIZER_DESC.DEFAULT,
                BlendState=D3D12_BLEND_DESC.DEFAULT,
                DepthStencilState=D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                SampleMask=uint.MaxValue,
                PrimitiveTopologyType=D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                NumRenderTargets=1,
                SampleDesc=new DXGI_SAMPLE_DESC(1,0),
            };
            PsoDesc.DepthStencilState.DepthEnable=FALSE;
            // Vertex winding follows the task's stated top/bottom-left/bottom-right order, which is
            // counter-clockwise in NDC; disable culling rather than reorder the specified vertices.
            PsoDesc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
            PsoDesc.RTVFormats[0]=BackBufferFormat;

            ID3D12PipelineState*PipelineState;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateGraphicsPipelineState(&PsoDesc,__uuidof<ID3D12PipelineState>(),(void**)&PipelineState),
                "ID3D12Device::CreateGraphicsPipelineState","creating the triangle pipeline state");
            this.PipelineState=PipelineState;
        }
    }

    private void CreateVertexBuffer(){
        const int VertexCount=3;
        var Vertices=stackalloc Vertex[VertexCount]{
                                                      new Vertex { Position=new Vector3(0.0f,0.6f,0.0f),Color=new Vector4(1,0,0,1)},
                                                      new Vertex { Position=new Vector3(-0.6f,-0.6f,0.0f),Color=new Vector4(0,1,0,1)},
                                                      new Vertex { Position=new Vector3(0.6f,-0.6f,0.0f),Color=new Vector4(0,0,1,1)},
                                                  };

        var BufferSize=(uint)sizeof(Vertex)*VertexCount;

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        var BufferDesc=D3D12_RESOURCE_DESC.Buffer(BufferSize);

        ID3D12Resource*VertexBuffer;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&VertexBuffer),
            "ID3D12Device::CreateCommittedResource","creating the triangle vertex buffer (upload heap)");

        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(VertexBuffer->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping the vertex buffer");
        Unsafe.CopyBlock(Mapped,Vertices,BufferSize);
        VertexBuffer->Unmap(0,null);

        this.VertexBuffer=VertexBuffer;
        this.VertexBufferView=new D3D12_VERTEX_BUFFER_VIEW {
                                                               BufferLocation=VertexBuffer->GetGPUVirtualAddress(),
                                                               StrideInBytes=(uint)sizeof(Vertex),
                                                               SizeInBytes=BufferSize,
                                                           };
    }

    private void UpdateViewportAndScissor(int Width,int Height){
        this.Viewport=new D3D12_VIEWPORT {
                                             TopLeftX=0,
                                             TopLeftY=0,
                                             Width=Width,
                                             Height=Height,
                                             MinDepth=0.0f,
                                             MaxDepth=1.0f,
                                         };

        this.ScissorRect=new RECT { left=0,top=0,right=Width,bottom=Height };
        this.Width=Width;
        this.Height=Height;
    }

    public void Resize(int Width,int Height){
        if(Width<=0||Height<=0){
            this.SizeIsZero=true;
            return;
        }

        this.SizeIsZero=false;

        if(this.Width==Width&&this.Height==Height){
            return;
        }

        this.WaitForGpuIdle();

        // ResizeBuffers restarts the swap chain's back-buffer index cycle (typically back to 0),
        // which would otherwise desynchronize the per-slot fence values from the fence's actual
        // completed value and hang a later SetEventOnCompletion wait on a value nothing ever
        // signals. Resync every slot to the value we just confirmed the GPU has reached.
        var SyncedFenceValue= this.FenceValues[this.FrameIndex];
        for(var I=0;I<FrameCount;I++){
            this.FenceValues[I]=SyncedFenceValue;
        }

        for(var I=0;I<FrameCount;I++){
            if(this.BackBuffers[I]!=null){
                _= this.BackBuffers[I]->Release();
                this.BackBuffers[I]=null;
            }
        }

        this.ReleaseReadbackBuffer();

        D3D12Diagnostics.EnsureSuccess(this.SwapChain->ResizeBuffers(FrameCount,(uint)Width,(uint)Height,BackBufferFormat,0),
            "IDXGISwapChain3::ResizeBuffers","resizing the swap chain");

        this.CreateRenderTargetViews();
        this.FrameIndex= this.SwapChain->GetCurrentBackBufferIndex();
        this.UpdateViewportAndScissor(Width,Height);
    }

    public void RenderFrame(bool CaptureForVerification=false){
        if(this.SizeIsZero){
            return;
        }

        var Allocator= this.CommandAllocators[this.FrameIndex];
        D3D12Diagnostics.EnsureSuccess(Allocator->Reset(),"ID3D12CommandAllocator::Reset","resetting the frame command allocator");
        D3D12Diagnostics.EnsureSuccess(this.CommandList->Reset(Allocator, this.PipelineState),"ID3D12GraphicsCommandList::Reset","resetting the graphics command list");

        var BackBuffer= this.BackBuffers[this.FrameIndex];

        var ToRenderTarget=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_PRESENT,D3D12_RESOURCE_STATE_RENDER_TARGET);
        this.CommandList->ResourceBarrier(1,&ToRenderTarget);

        D3D12_CPU_DESCRIPTOR_HANDLE RtvHandle=new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),(int)this.FrameIndex, this.RtvDescriptorSize);
        this.CommandList->OMSetRenderTargets(1,&RtvHandle,FALSE,null);

        var ClearColor=ClearColorVector;
        this.CommandList->ClearRenderTargetView(RtvHandle,(float*)&ClearColor,0,null);

        this.CommandList->SetGraphicsRootSignature(this.RootSignature);
        this.CommandList->SetPipelineState(this.PipelineState);

        fixed(D3D12_VIEWPORT*PViewport=&this.Viewport)
        fixed(RECT*PScissorRect=&this.ScissorRect)
        fixed(D3D12_VERTEX_BUFFER_VIEW*PVertexBufferView=&this.VertexBufferView){
            this.CommandList->RSSetViewports(1,PViewport);
            this.CommandList->RSSetScissorRects(1,PScissorRect);
            this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            this.CommandList->IASetVertexBuffers(0,1,PVertexBufferView);
            this.CommandList->DrawInstanced(3,1,0,0);
        }

        if(CaptureForVerification){
            this.EnsureReadbackBuffer();

            var ToCopySource=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_COPY_SOURCE);
            this.CommandList->ResourceBarrier(1,&ToCopySource);

            D3D12_TEXTURE_COPY_LOCATION Dst=new(){ pResource= this.ReadbackBuffer,Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            Dst.PlacedFootprint= this.ReadbackFootprint;
            D3D12_TEXTURE_COPY_LOCATION Src=new(){ pResource=BackBuffer,Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            Src.SubresourceIndex=0;
            this.CommandList->CopyTextureRegion(&Dst,0,0,0,&Src,null);

            var ToPresent=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_COPY_SOURCE,D3D12_RESOURCE_STATE_PRESENT);
            this.CommandList->ResourceBarrier(1,&ToPresent);
        } else {
            var ToPresent=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_PRESENT);
            this.CommandList->ResourceBarrier(1,&ToPresent);
        }

        D3D12Diagnostics.EnsureSuccess(this.CommandList->Close(),"ID3D12GraphicsCommandList::Close","closing the frame command list");

        var CommandListBase=(ID3D12CommandList*)this.CommandList;
        this.Device!.CommandQueue->ExecuteCommandLists(1,&CommandListBase);

        D3D12Diagnostics.EnsureSuccess(this.SwapChain->Present(1,0),"IDXGISwapChain3::Present","presenting the frame");

        this.MoveToNextFrame();
        this.Device.PollDebugMessages();
        this.FramesRendered++;

        if(CaptureForVerification){
            this.WaitForGpuIdle();
            this.LastCapturedFrame= this.ReadBackCapturedFrame();
        }
    }

    public PixelReadbackResult?LastCapturedFrame { get;private set;}

    private void EnsureReadbackBuffer(){
        if(this.ReadbackBuffer!=null){
            return;
        }

        var TextureDesc=D3D12_RESOURCE_DESC.Tex2D(BackBufferFormat,(uint)this.Width,(uint)this.Height,mipLevels:1);

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT Footprint;
        ulong TotalBytes;
        this.Device!.Device->GetCopyableFootprints(&TextureDesc,0,1,0,&Footprint,null,null,&TotalBytes);

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_READBACK);
        var BufferDesc=D3D12_RESOURCE_DESC.Buffer(TotalBytes);

        ID3D12Resource*ReadbackBuffer;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_COPY_DEST,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&ReadbackBuffer),
            "ID3D12Device::CreateCommittedResource","creating the pixel-verification readback buffer");

        this.ReadbackBuffer=ReadbackBuffer;
        this.ReadbackBufferSize=TotalBytes;
        this.ReadbackFootprint=Footprint;
    }

    private void ReleaseReadbackBuffer(){
        if(this.ReadbackBuffer!=null){
            _= this.ReadbackBuffer->Release();
            this.ReadbackBuffer=null;
            this.ReadbackBufferSize=0;
        }
    }

    private PixelReadbackResult ReadBackCapturedFrame(){
        D3D12_RANGE ReadRange=new(){ Begin=0,End=(nuint)this.ReadbackBufferSize };
        void*Mapped;
        D3D12Diagnostics.EnsureSuccess(this.ReadbackBuffer->Map(0,&ReadRange,&Mapped),"ID3D12Resource::Map","mapping the readback buffer");

        var Data=new byte[this.ReadbackBufferSize];
        fixed(byte*PData=Data){
            Unsafe.CopyBlock(PData,Mapped,(uint)this.ReadbackBufferSize);
        }

        D3D12_RANGE WrittenRange=new();
        this.ReadbackBuffer->Unmap(0,&WrittenRange);

        return new PixelReadbackResult {
            Width= this.Width,
            Height= this.Height,
            RowPitch= this.ReadbackFootprint.Footprint.RowPitch,
            Format=BackBufferFormat,
            Pixels=Data,
        };
    }

    private void MoveToNextFrame(){
        var CurrentFenceValue= this.FenceValues[this.FrameIndex];
        D3D12Diagnostics.EnsureSuccess(this.Device!.CommandQueue->Signal(this.Fence,CurrentFenceValue),"ID3D12CommandQueue::Signal","signaling the frame fence");

        this.FrameIndex= this.SwapChain->GetCurrentBackBufferIndex();

        if(this.Fence->GetCompletedValue()< this.FenceValues[this.FrameIndex]){
            D3D12Diagnostics.EnsureSuccess(this.Fence->SetEventOnCompletion(this.FenceValues[this.FrameIndex], this.FenceEvent),"ID3D12Fence::SetEventOnCompletion","waiting for the next frame slot");
            _=WaitForSingleObjectEx(this.FenceEvent,INFINITE,FALSE);
        }

        this.FenceValues[this.FrameIndex]=CurrentFenceValue+1;
    }

    private void WaitForGpuIdle(){
        if(this.Device==null|| this.Fence==null){
            return;
        }

        D3D12Diagnostics.EnsureSuccess(this.Device.CommandQueue->Signal(this.Fence, this.FenceValues[this.FrameIndex]),"ID3D12CommandQueue::Signal","signaling the GPU-idle fence");
        D3D12Diagnostics.EnsureSuccess(this.Fence->SetEventOnCompletion(this.FenceValues[this.FrameIndex], this.FenceEvent),"ID3D12Fence::SetEventOnCompletion","waiting for the GPU to go idle");
        _=WaitForSingleObjectEx(this.FenceEvent,INFINITE,FALSE);
        this.FenceValues[this.FrameIndex]++;
    }

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;

        this.WaitForGpuIdle();

        this.ReleaseReadbackBuffer();

        if(this.VertexBuffer!=null){
            _= this.VertexBuffer->Release();
            this.VertexBuffer=null;
        }

        if(this.PipelineState!=null){
            _= this.PipelineState->Release();
            this.PipelineState=null;
        }

        if(this.RootSignature!=null){
            _= this.RootSignature->Release();
            this.RootSignature=null;
        }

        if(this.FenceEvent!=HANDLE.NULL){
            _=CloseHandle(this.FenceEvent);
            this.FenceEvent=HANDLE.NULL;
        }

        if(this.Fence!=null){
            _= this.Fence->Release();
            this.Fence=null;
        }

        if(this.CommandList!=null){
            _= this.CommandList->Release();
            this.CommandList=null;
        }

        for(var I=0;I<FrameCount;I++){
            if(this.CommandAllocators[I]!=null){
                _= this.CommandAllocators[I]->Release();
                this.CommandAllocators[I]=null;
            }
        }

        for(var I=0;I<FrameCount;I++){
            if(this.BackBuffers[I]!=null){
                _= this.BackBuffers[I]->Release();
                this.BackBuffers[I]=null;
            }
        }

        if(this.RtvHeap!=null){
            _= this.RtvHeap->Release();
            this.RtvHeap=null;
        }

        if(this.SwapChain!=null){
            _= this.SwapChain->Release();
            this.SwapChain=null;
        }

        this.Device?.Dispose();
        this.Device=null;
    }
}
