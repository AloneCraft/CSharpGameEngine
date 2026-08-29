using System.Numerics;
using System.Runtime.CompilerServices;
using CSharpGameEngine.ShaderArtifacts;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D_PRIMITIVE_TOPOLOGY;
using static TerraFX.Interop.DirectX.D3D_ROOT_SIGNATURE_VERSION;
using static TerraFX.Interop.DirectX.D3D12_COMMAND_LIST_TYPE;
using static TerraFX.Interop.DirectX.D3D12_COMPARISON_FUNC;
using static TerraFX.Interop.DirectX.D3D12_CULL_MODE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_RANGE_TYPE;
using static TerraFX.Interop.DirectX.D3D12_FENCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_FILTER;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_INPUT_CLASSIFICATION;
using static TerraFX.Interop.DirectX.D3D12_PRIMITIVE_TOPOLOGY_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D12_ROOT_PARAMETER_TYPE;
using static TerraFX.Interop.DirectX.D3D12_ROOT_SIGNATURE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_SHADER_VISIBILITY;
using static TerraFX.Interop.DirectX.D3D12_SRV_DIMENSION;
using static TerraFX.Interop.DirectX.D3D12_STATIC_BORDER_COLOR;
using static TerraFX.Interop.DirectX.D3D12_TEXTURE_ADDRESS_MODE;
using static TerraFX.Interop.DirectX.D3D12_TEXTURE_COPY_TYPE;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DXGI;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.DirectX.DXGI_SWAP_EFFECT;
using static TerraFX.Interop.Windows.Windows;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>
/// Owns the swap chain, depth buffer, pipeline, and geometry buffers for the glTF model-viewer
/// vertical slice: one static indexed mesh, one fixed auto-framed camera, simple directional
/// Lambertian shading. Deliberately a separate class from <see cref="TriangleRenderer"/> rather
/// than a shared abstraction - see ADR-0004.
/// </summary>
public sealed unsafe class ModelRenderer:IDisposable {
    public const int FrameCount=2;
    public const DXGI_FORMAT BackBufferFormat=DXGI_FORMAT_R8G8B8A8_UNORM;
    public const DXGI_FORMAT DepthBufferFormat=DXGI_FORMAT_D32_FLOAT;

    private static readonly Vector4 ClearColorVector=new(0.02f,0.02f,0.08f,1.0f);
    public static readonly PixelColor ClearColor=new(5,5,20,255);

    private struct Vertex {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 Uv;
    }

    private GraphicsDevice?Device;
    private IDXGISwapChain3*SwapChain;
    private ID3D12DescriptorHeap*RtvHeap;
    private uint RtvDescriptorSize;
    private readonly ID3D12Resource*[]BackBuffers=new ID3D12Resource*[FrameCount];
    private ID3D12DescriptorHeap*DsvHeap;
    private ID3D12Resource*DepthBuffer;
    private readonly ID3D12CommandAllocator*[]CommandAllocators=new ID3D12CommandAllocator*[FrameCount];
    private ID3D12GraphicsCommandList*CommandList;
    private ID3D12Fence*Fence;
    private readonly ulong[]FenceValues=new ulong[FrameCount];
    private HANDLE FenceEvent;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private ID3D12Resource*VertexBuffer;
    private D3D12_VERTEX_BUFFER_VIEW VertexBufferView;
    private ID3D12Resource*IndexBuffer;
    private D3D12_INDEX_BUFFER_VIEW IndexBufferView;
    private uint IndexCount;
    private ID3D12Resource*ConstantBuffer;
    private byte*ConstantBufferMapped;
    private ID3D12Resource*Texture;
    private ID3D12DescriptorHeap*SrvHeap;
    private Vector4 BaseColorFactor;
    private ID3D12Resource*ReadbackBuffer;
    private ulong ReadbackBufferSize;
    private D3D12_PLACED_SUBRESOURCE_FOOTPRINT ReadbackFootprint;

    private uint FrameIndex;
    private int Width;
    private int Height;
    private bool SizeIsZero;
    private bool Disposed;
    private string ShaderArtifactPath=string.Empty;
    private ShaderArtifactMonitor?ShaderArtifactMonitor;

    private Vector3 BoundsCenter;
    private float BoundsRadius;

    private D3D12_VIEWPORT Viewport;
    private RECT ScissorRect;

    public string AdapterDescription=> this.Device?.AdapterDescription??string.Empty;

    public bool IsWarp=> this.Device?.IsWarp??false;

    public bool DebugLayerEnabled=> this.Device?.DebugLayerEnabled??false;

    public bool GpuBasedValidationEnabled=> this.Device?.GpuBasedValidationEnabled??false;

    public IReadOnlyList<D3D12DebugMessage>DebugMessages=> this.Device?.CollectedMessages??[];

    public int ErrorOrWorseMessageCount=> this.Device?.ErrorOrWorseMessageCount??0;

    public static bool ShaderHotReloadEnabled=>ShaderArtifactMonitor.IsEnabled;

    public bool ShaderArtifactLoaded { get;private set;}

    public string ShaderSemanticHash { get;private set;}=string.Empty;

    public IReadOnlyList<string>ShaderArtifactDiagnostics { get;private set;}=[];

    public uint FramesRendered { get;private set;}

    public PixelReadbackResult?LastCapturedFrame { get;private set;}

    /// <summary>False if no base-color texture was supplied, or it failed to decode - in either case a 1x1 white fallback texture is used, see <see cref="TextureLoadError"/>.</summary>
    public bool TextureLoaded { get;private set;}

    public string?TextureLoadError { get;private set;}

    public static ModelRenderer Create(
        nint Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,string ShaderArtifactPath,
        IReadOnlyList<Vector3>Positions,IReadOnlyList<Vector3>Normals,IReadOnlyList<Vector2>Uvs,IReadOnlyList<uint>Indices,
        byte[]?BaseColorTextureBytes,Vector4 BaseColorFactor,
        Vector3 BoundsCenter,float BoundsRadius){
        ArgumentNullException.ThrowIfNull(ShaderArtifactPath);
        ArgumentNullException.ThrowIfNull(Positions);
        ArgumentNullException.ThrowIfNull(Normals);
        ArgumentNullException.ThrowIfNull(Uvs);
        ArgumentNullException.ThrowIfNull(Indices);
        if(Width<=0||Height<=0){
            throw new ArgumentOutOfRangeException(nameof(Width),"Initial width/height must both be positive.");
        }

        if(Positions.Count==0||Positions.Count!=Normals.Count||Positions.Count!=Uvs.Count||Indices.Count==0){
            throw new ArgumentException("Mesh must have at least one vertex (positions.Count == normals.Count == uvs.Count) and at least one index.");
        }

        ModelRenderer Renderer=new(){
            Width=Width,
            Height=Height,
            BoundsCenter=BoundsCenter,
            BoundsRadius=BoundsRadius,
            BaseColorFactor=BaseColorFactor,
        };

        var Completed=false;
        try {
            Renderer.Initialize((HWND)Hwnd,Width,Height,DeviceOptions,ShaderArtifactPath,Positions,Normals,Uvs,Indices,BaseColorTextureBytes);
            Completed=true;
            return Renderer;
        } finally {
            if(!Completed){
                Renderer.Dispose();
            }
        }
    }

    private ModelRenderer(){
    }

    private void Initialize(HWND Hwnd,int Width,int Height,GraphicsDeviceOptions DeviceOptions,string ShaderArtifactPath,
        IReadOnlyList<Vector3>Positions,IReadOnlyList<Vector3>Normals,IReadOnlyList<Vector2>Uvs,IReadOnlyList<uint>Indices,
        byte[]?BaseColorTextureBytes){
        this.Device=GraphicsDevice.Create(DeviceOptions);

        this.CreateSwapChain(Hwnd,Width,Height);
        this.CreateRtvHeap();
        this.CreateRenderTargetViews();
        this.CreateDsvHeap();
        this.CreateDepthBuffer(Width,Height);

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

        this.ShaderArtifactPath=Path.GetFullPath(ShaderArtifactPath);
        this.LoadShaderArtifact(this.ShaderArtifactPath);
        this.CreateRootSignature();
        this.CreatePipelineState();
        this.ShaderArtifactMonitor=new ShaderArtifactMonitor(this.ShaderArtifactPath);
        this.CreateVertexBuffer(Positions,Normals,Uvs);
        this.CreateIndexBuffer(Indices);
        this.CreateConstantBuffer();
        this.CreateTexture(BaseColorTextureBytes);
        this.UpdateViewportAndScissor(Width,Height);
        this.UpdateTransformConstantBuffer(Width,Height);
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
        HRESULT Hr=SwapChain1->QueryInterface(__uuidof<IDXGISwapChain3>(),(void**)&SwapChain3);
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
        D3D12_CPU_DESCRIPTOR_HANDLE RtvBase= this.RtvHeap->GetCPUDescriptorHandleForHeapStart();

        for(uint I=0;I<FrameCount;I++){
            ID3D12Resource*BackBuffer;
            D3D12Diagnostics.EnsureSuccess(this.SwapChain->GetBuffer(I,__uuidof<ID3D12Resource>(),(void**)&BackBuffer),
                "IDXGISwapChain3::GetBuffer","retrieving a back buffer", $"buffer[{I}]");

            D3D12_CPU_DESCRIPTOR_HANDLE RtvHandle=new(RtvBase,(int)I, this.RtvDescriptorSize);
            this.Device!.Device->CreateRenderTargetView(BackBuffer,pDesc:null,RtvHandle);

            this.BackBuffers[I]=BackBuffer;
        }
    }

    private void CreateDsvHeap(){
        D3D12_DESCRIPTOR_HEAP_DESC Desc=new(){
            NumDescriptors=1,
            Type=D3D12_DESCRIPTOR_HEAP_TYPE_DSV,
        };

        ID3D12DescriptorHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateDescriptorHeap(&Desc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),
            "ID3D12Device::CreateDescriptorHeap","creating the DSV descriptor heap");

        this.DsvHeap=Heap;
    }

    private void CreateDepthBuffer(int Width,int Height){
        D3D12_RESOURCE_DESC ResourceDesc=D3D12_RESOURCE_DESC.Tex2D(DepthBufferFormat,(uint)Width,(uint)Height,mipLevels:1);
        ResourceDesc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_DEFAULT);
        D3D12_CLEAR_VALUE ClearValue=new(DepthBufferFormat,1.0f,0);

        ID3D12Resource*DepthBuffer;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&ResourceDesc,D3D12_RESOURCE_STATE_DEPTH_WRITE,&ClearValue,__uuidof<ID3D12Resource>(),(void**)&DepthBuffer),
            "ID3D12Device::CreateCommittedResource","creating the depth buffer");

        D3D12_DEPTH_STENCIL_VIEW_DESC DsvDesc=new(){
            Format=DepthBufferFormat,
            ViewDimension=D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D,
        };
        this.Device.Device->CreateDepthStencilView(DepthBuffer,&DsvDesc, this.DsvHeap->GetCPUDescriptorHandleForHeapStart());

        this.DepthBuffer=DepthBuffer;
    }

private void LoadShaderArtifact(string ShaderArtifactPath){
        if(!ShaderArtifactRuntime.TryLoad(ShaderArtifactPath,ShaderArtifactTargetKind.Model,out var Package,out var Error)||Package is null){
            this.ShaderArtifactLoaded=false;
            this.ShaderArtifactDiagnostics=[Error];
            throw new InvalidOperationException($"Shader artifact load failed for '{ShaderArtifactPath}':{Environment.NewLine}{Error}");
        }
        this.VertexShaderBytecode=Package.GetBytecode("VSMain");
        this.PixelShaderBytecode=Package.GetBytecode("PSMain");
        this.ShaderArtifactLoaded=true;
        this.ShaderSemanticHash=Package.Document.SemanticHash;
        this.ShaderArtifactDiagnostics=[];
    }
    private void TryReloadShaderArtifact(){
        if(this.ShaderArtifactMonitor?.ConsumeReloadRequest()!=true){
            return;
        }
        if(!ShaderArtifactRuntime.TryLoad(this.ShaderArtifactPath,ShaderArtifactTargetKind.Model,out var Package,out var Error)||Package is null){
            this.ShaderArtifactDiagnostics=[Error];
            return;
        }
        var OldVertexShader=this.VertexShaderBytecode;
        var OldPixelShader=this.PixelShaderBytecode;
        var OldPipelineState=this.PipelineState;
        try{
            this.VertexShaderBytecode=Package.GetBytecode("VSMain");
            this.PixelShaderBytecode=Package.GetBytecode("PSMain");
            this.CreatePipelineState();
            this.WaitForGpuIdle();
            if(OldPipelineState!=null){
                _=OldPipelineState->Release();
            }
            this.ShaderArtifactLoaded=true;
            this.ShaderSemanticHash=Package.Document.SemanticHash;
            this.ShaderArtifactDiagnostics=[];
        }catch(Exception Exception){
            this.VertexShaderBytecode=OldVertexShader;
            this.PixelShaderBytecode=OldPixelShader;
            if(this.PipelineState!=OldPipelineState){
                _=this.PipelineState->Release();
                this.PipelineState=OldPipelineState;
            }
            this.ShaderArtifactDiagnostics=[Exception.Message];
        }
    }
    private byte[]VertexShaderBytecode=[];
    private byte[]PixelShaderBytecode=[];

    private void CreateRootSignature(){
        // ShaderVisibility is ALL, not just VERTEX: PSMain also reads BaseColorFactor out of the
        // same TransformCB (b0). A vertex-only-visible root parameter would fail PSO creation
        // since the pixel shader references a root parameter the root signature doesn't expose to
        // that stage.
        D3D12_ROOT_PARAMETER TransformParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL,
        };
        TransformParam.Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0,RegisterSpace=0 };

        D3D12_DESCRIPTOR_RANGE SrvRange=new(){
            RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,
            NumDescriptors=1,
            BaseShaderRegister=0,
        };

        D3D12_ROOT_PARAMETER TextureParam=new(){
            ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };

        D3D12_STATIC_SAMPLER_DESC StaticSampler=new(){
            Filter=D3D12_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU=D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            AddressV=D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            AddressW=D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            ComparisonFunc=D3D12_COMPARISON_FUNC_NEVER,
            BorderColor=D3D12_STATIC_BORDER_COLOR_OPAQUE_WHITE,
            MaxLOD=float.MaxValue,
            ShaderRegister=0,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_PIXEL,
        };

        D3D12_ROOT_PARAMETER*RootParams=stackalloc D3D12_ROOT_PARAMETER[2];
        RootParams[0]=TransformParam;
        RootParams[1]=TextureParam;
        RootParams[1].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&SrvRange };

        D3D12_ROOT_SIGNATURE_DESC RootSignatureDesc=new(){
            NumParameters=2,
            pParameters=RootParams,
            NumStaticSamplers=1,
            pStaticSamplers=&StaticSampler,
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
                throw new D3D12Exception("D3D12SerializeRootSignature",Hr.Value,"serializing the model root signature",Error);
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
        fixed(byte*PNormal="NORMAL\0"u8)
        fixed(byte*PTexCoord="TEXCOORD\0"u8)
        fixed(byte*PVertexShader= this.VertexShaderBytecode)
        fixed(byte*PPixelShader= this.PixelShaderBytecode){
            D3D12_INPUT_ELEMENT_DESC*InputElements=stackalloc D3D12_INPUT_ELEMENT_DESC[3]{
                new D3D12_INPUT_ELEMENT_DESC {
                    SemanticName=(sbyte*)PPosition,
                    Format=DXGI_FORMAT_R32G32B32_FLOAT,
                    InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
                new D3D12_INPUT_ELEMENT_DESC {
                    SemanticName=(sbyte*)PNormal,
                    Format=DXGI_FORMAT_R32G32B32_FLOAT,
                    AlignedByteOffset=12,
                    InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
                new D3D12_INPUT_ELEMENT_DESC {
                    SemanticName=(sbyte*)PTexCoord,
                    Format=DXGI_FORMAT_R32G32_FLOAT,
                    AlignedByteOffset=24,
                    InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA,
                },
            };

            D3D12_GRAPHICS_PIPELINE_STATE_DESC PsoDesc=new(){
                InputLayout=new D3D12_INPUT_LAYOUT_DESC {
                    pInputElementDescs=InputElements,
                    NumElements=3,
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
                DSVFormat=DepthBufferFormat,
                SampleDesc=new DXGI_SAMPLE_DESC(1,0),
            };
            // Real backface culling is deferred (see ADR-0004); the depth buffer alone is enough
            // for a closed mesh to look correct from any external viewpoint at this stage.
            PsoDesc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
            PsoDesc.RTVFormats[0]=BackBufferFormat;

            ID3D12PipelineState*PipelineState;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateGraphicsPipelineState(&PsoDesc,__uuidof<ID3D12PipelineState>(),(void**)&PipelineState),
                "ID3D12Device::CreateGraphicsPipelineState","creating the model pipeline state");
            this.PipelineState=PipelineState;
        }
    }

    private void CreateVertexBuffer(IReadOnlyList<Vector3>Positions,IReadOnlyList<Vector3>Normals,IReadOnlyList<Vector2>Uvs){
        var VertexCount=Positions.Count;
        Vertex[]Vertices=new Vertex[VertexCount];
        for(var I=0;I<VertexCount;I++){
            Vertices[I]=new Vertex { Position=Positions[I],Normal=Normals[I],Uv=Uvs[I]};
        }

        var BufferSize=(uint)(sizeof(Vertex)*VertexCount);

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(BufferSize);

        ID3D12Resource*VertexBuffer;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&VertexBuffer),
            "ID3D12Device::CreateCommittedResource","creating the model vertex buffer (upload heap)");

        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(VertexBuffer->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping the vertex buffer");
        fixed(Vertex*PVertices=Vertices){
            Unsafe.CopyBlock(Mapped,PVertices,BufferSize);
        }
        VertexBuffer->Unmap(0,null);

        this.VertexBuffer=VertexBuffer;
        this.VertexBufferView=new D3D12_VERTEX_BUFFER_VIEW {
                                                               BufferLocation=VertexBuffer->GetGPUVirtualAddress(),
                                                               StrideInBytes=(uint)sizeof(Vertex),
                                                               SizeInBytes=BufferSize,
                                                           };
    }

    private void CreateIndexBuffer(IReadOnlyList<uint>Indices){
        var IndexCount=Indices.Count;
        var IndexArray=Indices as uint[]??[.. Indices];
        var BufferSize=(uint)(sizeof(uint)*IndexCount);

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(BufferSize);

        ID3D12Resource*IndexBuffer;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&IndexBuffer),
            "ID3D12Device::CreateCommittedResource","creating the model index buffer (upload heap)");

        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(IndexBuffer->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping the index buffer");
        fixed(uint*PIndices=IndexArray){
            Unsafe.CopyBlock(Mapped,PIndices,BufferSize);
        }
        IndexBuffer->Unmap(0,null);

        this.IndexBuffer=IndexBuffer;
        this.IndexCount=(uint)IndexCount;
        this.IndexBufferView=new D3D12_INDEX_BUFFER_VIEW {
                                                             BufferLocation=IndexBuffer->GetGPUVirtualAddress(),
                                                             SizeInBytes=BufferSize,
                                                             Format=DXGI_FORMAT_R32_UINT,
                                                         };
    }

    private void CreateConstantBuffer(){
        // D3D12 constant buffer resources must be sized in multiples of 256 bytes even though the
        // shader only reads the first 64 (one float4x4).
        const uint ConstantBufferSize=256;

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(ConstantBufferSize);

        ID3D12Resource*ConstantBuffer;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&HeapProps,D3D12_HEAP_FLAG_NONE,&BufferDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&ConstantBuffer),
            "ID3D12Device::CreateCommittedResource","creating the transform constant buffer");

        D3D12_RANGE NoRead=new();
        byte*Mapped;
        D3D12Diagnostics.EnsureSuccess(ConstantBuffer->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping the transform constant buffer");

        this.ConstantBuffer=ConstantBuffer;
        this.ConstantBufferMapped=Mapped;
    }

    private void CreateTexture(byte[]?BaseColorTextureBytes){
        DecodedImage Image= this.DecodeOrFallback(BaseColorTextureBytes);

        D3D12_RESOURCE_DESC TextureDesc=D3D12_RESOURCE_DESC.Tex2D(DXGI_FORMAT_R8G8B8A8_UNORM,(uint)Image.Width,(uint)Image.Height,mipLevels:1);

        D3D12_HEAP_PROPERTIES DefaultHeapProps=new(D3D12_HEAP_TYPE_DEFAULT);
        ID3D12Resource*Texture;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&DefaultHeapProps,D3D12_HEAP_FLAG_NONE,&TextureDesc,D3D12_RESOURCE_STATE_COPY_DEST,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&Texture),
            "ID3D12Device::CreateCommittedResource","creating the base-color texture");
        this.Texture=Texture;

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT Footprint;
        ulong TotalBytes;
        this.Device.Device->GetCopyableFootprints(&TextureDesc,0,1,0,&Footprint,null,null,&TotalBytes);

        D3D12_HEAP_PROPERTIES UploadHeapProps=new(D3D12_HEAP_TYPE_UPLOAD);
        D3D12_RESOURCE_DESC StagingDesc=D3D12_RESOURCE_DESC.Buffer(TotalBytes);

        ID3D12Resource*Staging;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&UploadHeapProps,D3D12_HEAP_FLAG_NONE,&StagingDesc,D3D12_RESOURCE_STATE_GENERIC_READ,pOptimizedClearValue:null,__uuidof<ID3D12Resource>(),(void**)&Staging),
            "ID3D12Device::CreateCommittedResource","creating the texture upload staging buffer");

        try {
            // GetCopyableFootprints' RowPitch is 256-byte aligned; the decoded image is tightly
            // packed (stride = Width*4). These differ whenever Width*4 isn't already a multiple of
            // 256, so this must be a per-row copy, not one bulk CopyBlock.
            D3D12_RANGE NoRead=new();
            byte*Mapped;
            D3D12Diagnostics.EnsureSuccess(Staging->Map(0,&NoRead,(void**)&Mapped),"ID3D12Resource::Map","mapping the texture upload staging buffer");

            var SrcRowPitch=(uint)Image.Width*4;
            var DstRowPitch=Footprint.Footprint.RowPitch;
            fixed(byte*PSrc=Image.Pixels){
                for(uint Y=0;Y<Image.Height;Y++){
                    Unsafe.CopyBlock(Mapped+(Y*DstRowPitch),PSrc+(Y*SrcRowPitch),SrcRowPitch);
                }
            }
            Staging->Unmap(0,null);

            D3D12Diagnostics.EnsureSuccess(this.CommandAllocators[0]->Reset(),"ID3D12CommandAllocator::Reset","resetting the allocator for the texture upload");
            D3D12Diagnostics.EnsureSuccess(this.CommandList->Reset(this.CommandAllocators[0],null),"ID3D12GraphicsCommandList::Reset","resetting the command list for the texture upload");

            D3D12_TEXTURE_COPY_LOCATION Dst=new(){ pResource=Texture,Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            Dst.SubresourceIndex=0;
            D3D12_TEXTURE_COPY_LOCATION Src=new(){ pResource=Staging,Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            Src.PlacedFootprint=Footprint;
            this.CommandList->CopyTextureRegion(&Dst,0,0,0,&Src,null);

            D3D12_RESOURCE_BARRIER ToShaderResource=D3D12_RESOURCE_BARRIER.InitTransition(Texture,D3D12_RESOURCE_STATE_COPY_DEST,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
            this.CommandList->ResourceBarrier(1,&ToShaderResource);

            D3D12Diagnostics.EnsureSuccess(this.CommandList->Close(),"ID3D12GraphicsCommandList::Close","closing the texture upload command list");

            ID3D12CommandList*CommandListBase=(ID3D12CommandList*)this.CommandList;
            this.Device.CommandQueue->ExecuteCommandLists(1,&CommandListBase);

            this.WaitForGpuIdle();
        } finally {
            _=Staging->Release();
        }

        D3D12_DESCRIPTOR_HEAP_DESC SrvHeapDesc=new(){
            Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
            NumDescriptors=1,
            Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE,
        };
        ID3D12DescriptorHeap*SrvHeap;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateDescriptorHeap(&SrvHeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&SrvHeap),
            "ID3D12Device::CreateDescriptorHeap","creating the SRV descriptor heap");
        this.SrvHeap=SrvHeap;

        D3D12_SHADER_RESOURCE_VIEW_DESC SrvDesc=new(){
            Format=DXGI_FORMAT_R8G8B8A8_UNORM,
            ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        SrvDesc.Texture2D.MipLevels=1;
        this.Device.Device->CreateShaderResourceView(Texture,&SrvDesc,SrvHeap->GetCPUDescriptorHandleForHeapStart());
    }

    private DecodedImage DecodeOrFallback(byte[]?BaseColorTextureBytes){
        // Every mesh gets a real SRV bound, even when there is no source texture (or it fails to
        // decode) - a procedural 1x1 white texture keeps this a single PSO/shader path with no
        // branching, and multiplies harmlessly with BaseColorFactor in the pixel shader.
        static DecodedImage WhiteFallback()=>new(1,1,[255,255,255,255]);

        if(BaseColorTextureBytes is null||BaseColorTextureBytes.Length==0){
            this.TextureLoaded=false;
            return WhiteFallback();
        }

        WicDecodeResult DecodeResult=WicImageDecoder.Decode(BaseColorTextureBytes);
        if(DecodeResult.Success){
            this.TextureLoaded=true;
            return DecodeResult.Image!;
        }

        this.TextureLoaded=false;
        this.TextureLoadError=DecodeResult.Error;
        return WhiteFallback();
    }

    private void UpdateTransformConstantBuffer(int Width,int Height){
        var FovY=MathF.PI/4f;
        var Radius=MathF.Max(this.BoundsRadius,0.001f);
        var Distance=Radius/MathF.Sin(FovY/2f)*1.5f;

        Vector3 Eye= this.BoundsCenter+Vector3.Normalize(new Vector3(1f,0.75f,1f))*Distance;
        // Shares Camera.GetView's view-matrix construction (ADR-0026) instead of duplicating the
        // Matrix4x4.CreateLookAt call - this is still ModelRenderer's own independent bounds-fit eye
        // position, not a Camera instance, so only the shared LookAt wrapper is reused here.
        Matrix4x4 View=Camera.BuildViewMatrix(Eye, this.BoundsCenter,Vector3.UnitY);
        Matrix4x4 Projection=Matrix4x4.CreatePerspectiveFieldOfView(
            FovY,
            (float)Width/Height,
            MathF.Max(Distance-(Radius*2f),0.01f),
            Distance+(Radius*4f));

        Matrix4x4 Mvp=View*Projection;
        Unsafe.CopyBlock(this.ConstantBufferMapped,&Mvp,(uint)sizeof(Matrix4x4));

        Vector4 BaseColorFactor=this.BaseColorFactor;
        Unsafe.CopyBlock(this.ConstantBufferMapped+64,&BaseColorFactor,(uint)sizeof(Vector4));
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

        // See TriangleRenderer.Resize / ADR-0003 §8: ResizeBuffers restarts the swap chain's
        // back-buffer index cycle, which would otherwise desynchronize the per-slot fence values.
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

        if(this.DepthBuffer!=null){
            _= this.DepthBuffer->Release();
            this.DepthBuffer=null;
        }

        D3D12Diagnostics.EnsureSuccess(this.SwapChain->ResizeBuffers(FrameCount,(uint)Width,(uint)Height,BackBufferFormat,0),
            "IDXGISwapChain3::ResizeBuffers","resizing the swap chain");

        this.CreateRenderTargetViews();
        this.CreateDepthBuffer(Width,Height);
        this.FrameIndex= this.SwapChain->GetCurrentBackBufferIndex();
        this.UpdateViewportAndScissor(Width,Height);
        this.UpdateTransformConstantBuffer(Width,Height);
    }

    public void RenderFrame(bool CaptureForVerification=false){
        this.TryReloadShaderArtifact();
        if(this.SizeIsZero){
            return;
        }

        ID3D12CommandAllocator*Allocator= this.CommandAllocators[this.FrameIndex];
        D3D12Diagnostics.EnsureSuccess(Allocator->Reset(),"ID3D12CommandAllocator::Reset","resetting the frame command allocator");
        D3D12Diagnostics.EnsureSuccess(this.CommandList->Reset(Allocator, this.PipelineState),"ID3D12GraphicsCommandList::Reset","resetting the graphics command list");

        ID3D12Resource*BackBuffer= this.BackBuffers[this.FrameIndex];

        D3D12_RESOURCE_BARRIER ToRenderTarget=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_PRESENT,D3D12_RESOURCE_STATE_RENDER_TARGET);
        this.CommandList->ResourceBarrier(1,&ToRenderTarget);

        D3D12_CPU_DESCRIPTOR_HANDLE RtvHandle=new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),(int)this.FrameIndex, this.RtvDescriptorSize);
        D3D12_CPU_DESCRIPTOR_HANDLE DsvHandle= this.DsvHeap->GetCPUDescriptorHandleForHeapStart();
        this.CommandList->OMSetRenderTargets(1,&RtvHandle,FALSE,&DsvHandle);

        Vector4 ClearColor=ClearColorVector;
        this.CommandList->ClearRenderTargetView(RtvHandle,(float*)&ClearColor,0,null);
        this.CommandList->ClearDepthStencilView(DsvHandle,D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,1.0f,0,0,null);

        this.CommandList->SetGraphicsRootSignature(this.RootSignature);
        this.CommandList->SetPipelineState(this.PipelineState);

        ID3D12DescriptorHeap*SrvHeap=this.SrvHeap;
        this.CommandList->SetDescriptorHeaps(1,&SrvHeap);
        this.CommandList->SetGraphicsRootConstantBufferView(0, this.ConstantBuffer->GetGPUVirtualAddress());
        this.CommandList->SetGraphicsRootDescriptorTable(1,SrvHeap->GetGPUDescriptorHandleForHeapStart());

        fixed(D3D12_VIEWPORT*PViewport=&this.Viewport)
        fixed(RECT*PScissorRect=&this.ScissorRect)
        fixed(D3D12_VERTEX_BUFFER_VIEW*PVertexBufferView=&this.VertexBufferView)
        fixed(D3D12_INDEX_BUFFER_VIEW*PIndexBufferView=&this.IndexBufferView){
            this.CommandList->RSSetViewports(1,PViewport);
            this.CommandList->RSSetScissorRects(1,PScissorRect);
            this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            this.CommandList->IASetVertexBuffers(0,1,PVertexBufferView);
            this.CommandList->IASetIndexBuffer(PIndexBufferView);
            this.CommandList->DrawIndexedInstanced(this.IndexCount,1,0,0,0);
        }

        if(CaptureForVerification){
            this.EnsureReadbackBuffer();

            D3D12_RESOURCE_BARRIER ToCopySource=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_COPY_SOURCE);
            this.CommandList->ResourceBarrier(1,&ToCopySource);

            D3D12_TEXTURE_COPY_LOCATION Dst=new(){ pResource= this.ReadbackBuffer,Type=D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            Dst.PlacedFootprint= this.ReadbackFootprint;
            D3D12_TEXTURE_COPY_LOCATION Src=new(){ pResource=BackBuffer,Type=D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            Src.SubresourceIndex=0;
            this.CommandList->CopyTextureRegion(&Dst,0,0,0,&Src,null);

            D3D12_RESOURCE_BARRIER ToPresent=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_COPY_SOURCE,D3D12_RESOURCE_STATE_PRESENT);
            this.CommandList->ResourceBarrier(1,&ToPresent);
        } else {
            D3D12_RESOURCE_BARRIER ToPresent=D3D12_RESOURCE_BARRIER.InitTransition(BackBuffer,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_PRESENT);
            this.CommandList->ResourceBarrier(1,&ToPresent);
        }

        D3D12Diagnostics.EnsureSuccess(this.CommandList->Close(),"ID3D12GraphicsCommandList::Close","closing the frame command list");

        ID3D12CommandList*CommandListBase=(ID3D12CommandList*)this.CommandList;
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

    private void EnsureReadbackBuffer(){
        if(this.ReadbackBuffer!=null){
            return;
        }

        D3D12_RESOURCE_DESC TextureDesc=D3D12_RESOURCE_DESC.Tex2D(BackBufferFormat,(uint)this.Width,(uint)this.Height,mipLevels:1);

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT Footprint;
        ulong TotalBytes;
        this.Device!.Device->GetCopyableFootprints(&TextureDesc,0,1,0,&Footprint,null,null,&TotalBytes);

        D3D12_HEAP_PROPERTIES HeapProps=new(D3D12_HEAP_TYPE_READBACK);
        D3D12_RESOURCE_DESC BufferDesc=D3D12_RESOURCE_DESC.Buffer(TotalBytes);

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
        this.ShaderArtifactMonitor?.Dispose();
        this.ShaderArtifactMonitor=null;

        this.WaitForGpuIdle();

        this.ReleaseReadbackBuffer();

        if(this.SrvHeap!=null){
            _= this.SrvHeap->Release();
            this.SrvHeap=null;
        }

        if(this.Texture!=null){
            _= this.Texture->Release();
            this.Texture=null;
        }

        if(this.ConstantBuffer!=null){
            this.ConstantBuffer->Unmap(0,null);
            this.ConstantBufferMapped=null;
            _= this.ConstantBuffer->Release();
            this.ConstantBuffer=null;
        }

        if(this.IndexBuffer!=null){
            _= this.IndexBuffer->Release();
            this.IndexBuffer=null;
        }

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

        if(this.DepthBuffer!=null){
            _= this.DepthBuffer->Release();
            this.DepthBuffer=null;
        }

        if(this.DsvHeap!=null){
            _= this.DsvHeap->Release();
            this.DsvHeap=null;
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
