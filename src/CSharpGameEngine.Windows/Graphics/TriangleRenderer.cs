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

    public string AdapterDescription=>Device?.AdapterDescription??string.Empty;

    public bool IsWarp=>Device?.IsWarp??false;

    public bool DebugLayerEnabled=>Device?.DebugLayerEnabled??false;

    public bool GpuBasedValidationEnabled=>Device?.GpuBasedValidationEnabled??false;

    public IReadOnlyList<D3D12DebugMessage>DebugMessages=>Device?.CollectedMessages??[];

    public int ErrorOrWorseMessageCount=>Device?.ErrorOrWorseMessageCount??0;

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
        Device=GraphicsDevice.Create(DeviceOptions);

        CreateSwapChain(Hwnd,Width,Height);
        CreateRtvHeap();
        CreateRenderTargetViews();

        for(var i=0;i<FrameCount;i++){
            ID3D12CommandAllocator*Allocator;
            D3D12Diagnostics.EnsureSuccess(
                Device.Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,__uuidof<ID3D12CommandAllocator>(),(void**)&Allocator),
                "ID3D12Device::CreateCommandAllocator","creating a frame command allocator");
            CommandAllocators[i]=Allocator;
        }

        ID3D12GraphicsCommandList*CommandList;
        D3D12Diagnostics.EnsureSuccess(
            Device.Device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,CommandAllocators[0],null,__uuidof<ID3D12GraphicsCommandList>(),(void**)&CommandList),
            "ID3D12Device::CreateCommandList","creating the graphics command list");
        this.CommandList=CommandList;
        D3D12Diagnostics.EnsureSuccess(CommandList->Close(),"ID3D12GraphicsCommandList::Close","closing the initial command list state");

        ID3D12Fence*Fence;
        D3D12Diagnostics.EnsureSuccess(
            Device.Device->CreateFence(0,D3D12_FENCE_FLAG_NONE,__uuidof<ID3D12Fence>(),(void**)&Fence),
            "ID3D12Device::CreateFence","creating the frame fence");
        this.Fence=Fence;

        FenceEvent=CreateEventW(lpEventAttributes:null,bManualReset:FALSE,bInitialState:FALSE,lpName:null);
        if(FenceEvent==HANDLE.NULL){
            throw new InvalidOperationException("CreateEventW failed while creating the frame fence event.");
        }

        FrameIndex=SwapChain->GetCurrentBackBufferIndex();
        FenceValues[FrameIndex]=1;

        CompileShaders(ShaderPath);
        CreateRootSignature();
        CreatePipelineState();
        CreateVertexBuffer();
        UpdateViewportAndScissor(Width,Height);
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
        D3D12Diagnostics.EnsureSuccess(
            Device!.Factory->CreateSwapChainForHwnd(
                (IUnknown*)Device.CommandQueue,
                Hwnd,
                &Desc,
                pFullscreenDesc:null,
                pRestrictToOutput:null,
                &SwapChain1),
            "IDXGIFactory4::CreateSwapChainForHwnd","creating the swap chain");

        IDXGISwapChain3*SwapChain3;
        HRESULT Hr=SwapChain1->QueryInterface(__uuidof<IDXGISwapChain3>(),(void**)&SwapChain3);
        _=SwapChain1->Release();
        D3D12Diagnostics.EnsureSuccess(Hr,"IDXGISwapChain1::QueryInterface(IDXGISwapChain3)","upgrading the swap chain interface");

        SwapChain=SwapChain3;
    }

    private void CreateRtvHeap(){
        D3D12_DESCRIPTOR_HEAP_DESC Desc=new(){
            NumDescriptors=FrameCount,
            Type=D3D12_DESCRIPTOR_HEAP_TYPE_RTV,
        };

        ID3D12DescriptorHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(
            Device!.Device->CreateDescriptorHeap(&Desc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),
            "ID3D12Device::CreateDescriptorHeap","creating the RTV descriptor heap");

        RtvHeap=Heap;
        RtvDescriptorSize=Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
    }

    private void CreateRenderTargetViews(){
        D3D12_CPU_DESCRIPTOR_HANDLE RtvBase=RtvHeap->GetCPUDescriptorHandleForHeapStart();

        for(uint i=0;i<FrameCount;i++){
            ID3D12Resource*BackBuffer;
            D3D12Diagnostics.EnsureSuccess(
                SwapChain->GetBuffer(i,__uuidof<ID3D12Resource>(),(void**)&BackBuffer),
                "IDXGISwapChain3::GetBuffer","retrieving a back buffer", $"buffer[{i}]");

            D3D12_CPU_DESCRIPTOR_HANDLE RtvHandle=new(RtvBase,(int)i,RtvDescriptorSize);
            Device!.Device->CreateRenderTargetView(BackBuffer,pDesc:null,RtvHandle);

            BackBuffers[i]=BackBuffer;
        }
    }

    private void CompileShaders(string ShaderPath){
        var DebugShaders=false;
#if DEBUG
        DebugShaders = true;
#endif

        HlslCompileResult Vs=HlslCompiler.Compile(new HlslCompileRequest {
            FilePath=ShaderPath,
            EntryPoint="VSMain",
            TargetProfile="vs_6_0",
            EnableDebugInfo=DebugShaders,
        });

        HlslCompileResult Ps=HlslCompiler.Compile(new HlslCompileRequest {
            FilePath=ShaderPath,
            EntryPoint="PSMain",
            TargetProfile="ps_6_0",
            EnableDebugInfo=DebugShaders,
        });

        ShaderCompilationSucceeded=Vs.Success&&Ps.Success;
        ShaderDiagnostics=[.. Vs.Diagnostics,.. Ps.Diagnostics];

        if(!ShaderCompilationSucceeded){
            var Details=string.Join(Environment.NewLine,ShaderDiagnostics);
            throw new InvalidOperationException($"HLSL shader compilation failed for '{ShaderPath}':{Environment.NewLine}{Details}");
        }

        VertexShaderBytecode=Vs.Bytecode;
        PixelShaderBytecode=Ps.Bytecode;
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
            HRESULT Hr=D3D12SerializeRootSignature(&RootSignatureDesc,D3D_ROOT_SIGNATURE_VERSION_1,&SignatureBlob,&ErrorBlob);
            if(Hr.FAILED){
                var Error=ErrorBlob!=null
                    ?new string((sbyte*)ErrorBlob->GetBufferPointer())
                    :"(no error blob)";
                throw new D3D12Exception("D3D12SerializeRootSignature",Hr.Value,"serializing the empty root signature",Error);
            }

            ID3D12RootSignature*RootSignature;
            D3D12Diagnostics.EnsureSuccess(
                Device!.Device->CreateRootSignature(0,SignatureBlob->GetBufferPointer(),SignatureBlob->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&RootSignature),
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
        fixed(byte*PVertexShader=VertexShaderBytecode)
        fixed(byte*PPixelShader=PixelShaderBytecode){
            D3D12_INPUT_ELEMENT_DESC*InputElements=stackalloc D3D12_INPUT_ELEMENT_DESC[2]{
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
                pRootSignature=RootSignature,
                VS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PVertexShader,BytecodeLength=(nuint)VertexShaderBytecode.Length },
                PS=new D3D12_SHADER_BYTECODE { pShaderBytecode=PPixelShader,BytecodeLength=(nuint)PixelShaderBytecode.Length },
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
            D3D12Diagnostics.EnsureSuccess(
                Device!.Device->CreateGraphicsPipelineState(&PsoDesc,__uuidof<ID3D12PipelineState>(),(void**)&PipelineState),
                "ID3D12Device::CreateGraphicsPipelineState","creating the triangle pipeline state");
            this.PipelineState=PipelineState;
        }
    }

    private void CreateVertexBuffer(){
        const int VertexCount=3;
        Vertex*Vertices=stackalloc Vertex[VertexCount]{
            new Vertex { Position=new Vector3(0.0f,0.6f,0.0f),Color=new Vector4(1,0,0,1)},
            new Vertex { Position=new Vector3(-0.6f,-0.6f,0.0f),Color=new Vector4(0,1,0,1)},
            new Vertex { Position=new Vector3(0.6f,-0.6f,0.0f),Color=new Vector4(0,0,1,1)},
        };

        var BufferSize=(uint)sizeof(Vertex)*VertexCount;

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(BufferSize);

        ID3D12Resource*VertexBuffer;
        D3D12Diagnostics.EnsureSuccess(
            Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&VertexBuffer),
            "ID3D12Device::CreateCommittedResource","creating the triangle vertex buffer (upload heap)");

        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(VertexBuffer->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping the vertex buffer");
        Unsafe.CopyBlock(Mapped,Vertices,BufferSize);
        VertexBuffer->Unmap(0,null);

        this.VertexBuffer=VertexBuffer;
        VertexBufferView=new D3D12_VERTEX_BUFFER_VIEW {
            BufferLocation=VertexBuffer->GetGPUVirtualAddress(),
            StrideInBytes=(uint)sizeof(Vertex),
            SizeInBytes=BufferSize,
        };
    }

    private void UpdateViewportAndScissor(int Width,int Height){
        Viewport=new D3D12_VIEWPORT {
            TopLeftX=0,
            TopLeftY=0,
            Width=Width,
            Height=Height,
            MinDepth=0.0f,
            MaxDepth=1.0f,
        };

        ScissorRect=new RECT { left=0,top=0,right=Width,bottom=Height };
        this.Width=Width;
        this.Height=Height;
    }

    public void Resize(int Width,int Height){
        if(Width<=0||Height<=0){
            SizeIsZero=true;
            return;
        }

        SizeIsZero=false;

        if(this.Width==Width&&this.Height==Height){
            return;
        }

        WaitForGpuIdle();

        // ResizeBuffers restarts the swap chain's back-buffer index cycle (typically back to 0),
        // which would otherwise desynchronize the per-slot fence values from the fence's actual
        // completed value and hang a later SetEventOnCompletion wait on a value nothing ever
        // signals. Resync every slot to the value we just confirmed the GPU has reached.
        var SyncedFenceValue=FenceValues[FrameIndex];
        for(var i=0;i<FrameCount;i++){
            FenceValues[i]=SyncedFenceValue;
        }

        for(var i=0;i<FrameCount;i++){
            if(BackBuffers[i]!=null){
                _=BackBuffers[i]->Release();
                BackBuffers[i]=null;
            }
        }

        ReleaseReadbackBuffer();

        D3D12Diagnostics.EnsureSuccess(
            SwapChain->ResizeBuffers(FrameCount,(uint)Width,(uint)Height,BackBufferFormat,0),
            "IDXGISwapChain3::ResizeBuffers","resizing the swap chain");

        CreateRenderTargetViews();
        FrameIndex=SwapChain->GetCurrentBackBufferIndex();
        UpdateViewportAndScissor(Width,Height);
    }

    public void RenderFrame(bool CaptureForVerification=false){
        if(SizeIsZero){
            return;
        }

        ID3D12CommandAllocator*Allocator=CommandAllocators[FrameIndex];
        D3D12Diagnostics.EnsureSuccess(Allocator->Reset(),"ID3D12CommandAllocator::Reset","resetting the frame command allocator");
        D3D12Diagnostics.EnsureSuccess(CommandList->Reset(Allocator,PipelineState),"ID3D12GraphicsCommandList::Reset","resetting the graphics command list");

        ID3D12Resource*BackBuffer=BackBuffers[FrameIndex];

        D3D12_RESOURCE_BARRIER ToRenderTarget=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_PRESENT,D3D12_RESOURCE_STATE_RENDER_TARGET);
        CommandList->ResourceBarrier(1,&ToRenderTarget);

        D3D12_CPU_DESCRIPTOR_HANDLE RtvHandle=new(RtvHeap->GetCPUDescriptorHandleForHeapStart(),(int)FrameIndex,RtvDescriptorSize);
        CommandList->OMSetRenderTargets(1,&RtvHandle,FALSE,null);

        Vector4 clearColor=ClearColorVector;
        CommandList->ClearRenderTargetView(RtvHandle,(float*)&clearColor,0,null);

        CommandList->SetGraphicsRootSignature(RootSignature);
        CommandList->SetPipelineState(PipelineState);

        fixed(D3D12_VIEWPORT*PViewport=&Viewport)
        fixed(RECT*PScissorRect=&ScissorRect)
        fixed(D3D12_VERTEX_BUFFER_VIEW*PVertexBufferView=&VertexBufferView){
            CommandList->RSSetViewports(1,PViewport);
            CommandList->RSSetScissorRects(1,PScissorRect);
            CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            CommandList->IASetVertexBuffers(0,1,PVertexBufferView);
            CommandList->DrawInstanced(3,1,0,0);
        }

        if(CaptureForVerification){
            EnsureReadbackBuffer();

            D3D12_RESOURCE_BARRIER ToCopySource=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_COPY_SOURCE);
            CommandList->ResourceBarrier(1,&ToCopySource);

            D3D12_TEXTURE_COPY_LOCATION Dst=new(){ pResource=ReadbackBuffer,Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            Dst.PlacedFootprint=ReadbackFootprint;
            D3D12_TEXTURE_COPY_LOCATION Src=new(){ pResource=BackBuffer,Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            Src.SubresourceIndex=0;
            CommandList->CopyTextureRegion(&Dst,0,0,0,&Src,null);

            D3D12_RESOURCE_BARRIER ToPresent=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_COPY_SOURCE,D3D12_RESOURCE_STATE_PRESENT);
            CommandList->ResourceBarrier(1,&ToPresent);
        } else {
            D3D12_RESOURCE_BARRIER toPresent=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_PRESENT);
            CommandList->ResourceBarrier(1,&toPresent);
        }

        D3D12Diagnostics.EnsureSuccess(CommandList->Close(),"ID3D12GraphicsCommandList::Close","closing the frame command list");

        ID3D12CommandList*CommandListBase=(ID3D12CommandList*)CommandList;
        Device!.CommandQueue->ExecuteCommandLists(1,&CommandListBase);

        D3D12Diagnostics.EnsureSuccess(SwapChain->Present(1,0),"IDXGISwapChain3::Present","presenting the frame");

        MoveToNextFrame();
        Device.PollDebugMessages();
        FramesRendered++;

        if(CaptureForVerification){
            WaitForGpuIdle();
            LastCapturedFrame=ReadBackCapturedFrame();
        }
    }

    public PixelReadbackResult?LastCapturedFrame { get;private set;}

    private void EnsureReadbackBuffer(){
        if(this.ReadbackBuffer!=null){
            return;
        }

        D3D12_RESOURCE_DESC TextureDesc=D3D12_RESOURCE_DESC.Tex2D(BackBufferFormat,(uint)Width,(uint)Height,mipLevels:1);

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT Footprint;
        ulong TotalBytes;
        Device!.Device->GetCopyableFootprints(&TextureDesc,0,1,0,&Footprint,null,null,&TotalBytes);

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_READBACK);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(TotalBytes);

        ID3D12Resource*ReadbackBuffer;
        D3D12Diagnostics.EnsureSuccess(
            Device.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_COPY_DEST,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&ReadbackBuffer),
            "ID3D12Device::CreateCommittedResource","creating the pixel-verification readback buffer");

        this.ReadbackBuffer=ReadbackBuffer;
        ReadbackBufferSize=TotalBytes;
        ReadbackFootprint=Footprint;
    }

    private void ReleaseReadbackBuffer(){
        if(ReadbackBuffer!=null){
            _=ReadbackBuffer->Release();
            ReadbackBuffer=null;
            ReadbackBufferSize=0;
        }
    }

    private PixelReadbackResult ReadBackCapturedFrame(){
        D3D12_RANGE ReadRange=new(){ Begin=0,End=(nuint)ReadbackBufferSize };
        void*Mapped;
        D3D12Diagnostics.EnsureSuccess(ReadbackBuffer->Map(0,&ReadRange,&Mapped),"ID3D12Resource::Map","mapping the readback buffer");

        var Data=new byte[ReadbackBufferSize];
        fixed(byte*PData=Data){
            Unsafe.CopyBlock(PData,Mapped,(uint)ReadbackBufferSize);
        }

        D3D12_RANGE WrittenRange=new();
        ReadbackBuffer->Unmap(0,&WrittenRange);

        return new PixelReadbackResult {
            Width=Width,
            Height=Height,
            RowPitch=ReadbackFootprint.Footprint.RowPitch,
            Format=BackBufferFormat,
            Pixels=Data,
        };
    }

    private void MoveToNextFrame(){
        var CurrentFenceValue=FenceValues[FrameIndex];
        D3D12Diagnostics.EnsureSuccess(Device!.CommandQueue->Signal(Fence,CurrentFenceValue),"ID3D12CommandQueue::Signal","signaling the frame fence");

        FrameIndex=SwapChain->GetCurrentBackBufferIndex();

        if(Fence->GetCompletedValue()<FenceValues[FrameIndex]){
            D3D12Diagnostics.EnsureSuccess(Fence->SetEventOnCompletion(FenceValues[FrameIndex],FenceEvent),"ID3D12Fence::SetEventOnCompletion","waiting for the next frame slot");
            _=WaitForSingleObjectEx(FenceEvent,INFINITE,FALSE);
        }

        FenceValues[FrameIndex]=CurrentFenceValue+1;
    }

    private void WaitForGpuIdle(){
        if(Device==null||Fence==null){
            return;
        }

        D3D12Diagnostics.EnsureSuccess(Device.CommandQueue->Signal(Fence,FenceValues[FrameIndex]),"ID3D12CommandQueue::Signal","signaling the GPU-idle fence");
        D3D12Diagnostics.EnsureSuccess(Fence->SetEventOnCompletion(FenceValues[FrameIndex],FenceEvent),"ID3D12Fence::SetEventOnCompletion","waiting for the GPU to go idle");
        _=WaitForSingleObjectEx(FenceEvent,INFINITE,FALSE);
        FenceValues[FrameIndex]++;
    }

    public void Dispose(){
        if(Disposed){
            return;
        }

        Disposed=true;

        WaitForGpuIdle();

        ReleaseReadbackBuffer();

        if(VertexBuffer!=null){
            _=VertexBuffer->Release();
            VertexBuffer=null;
        }

        if(PipelineState!=null){
            _=PipelineState->Release();
            PipelineState=null;
        }

        if(RootSignature!=null){
            _=RootSignature->Release();
            RootSignature=null;
        }

        if(FenceEvent!=HANDLE.NULL){
            _=CloseHandle(FenceEvent);
            FenceEvent=HANDLE.NULL;
        }

        if(Fence!=null){
            _=Fence->Release();
            Fence=null;
        }

        if(CommandList!=null){
            _=CommandList->Release();
            CommandList=null;
        }

        for(var i=0;i<FrameCount;i++){
            if(CommandAllocators[i]!=null){
                _=CommandAllocators[i]->Release();
                CommandAllocators[i]=null;
            }
        }

        for(var i=0;i<FrameCount;i++){
            if(BackBuffers[i]!=null){
                _=BackBuffers[i]->Release();
                BackBuffers[i]=null;
            }
        }

        if(RtvHeap!=null){
            _=RtvHeap->Release();
            RtvHeap=null;
        }

        if(SwapChain!=null){
            _=SwapChain->Release();
            SwapChain=null;
        }

        Device?.Dispose();
        Device=null;
    }
}
