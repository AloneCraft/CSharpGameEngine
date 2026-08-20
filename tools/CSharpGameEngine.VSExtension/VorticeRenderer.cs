using System;
using System.Numerics;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Minimal in-process D3D12 renderer for the VSIX tool window (ADR-0008), built on
/// <c>Vortice.Windows</c> (netstandard2.0-compatible) instead of the net10.0-only
/// <c>TerraFX.Interop.Windows</c> the rest of the engine uses. Deliberately not shared with
/// <c>CSharpGameEngine.Graphics.D3D12</c> - simplest possible synchronous (no frames-in-flight)
/// renderer: fixed camera, Lambertian shading, no textures/materials beyond baseColorFactor.
/// </summary>
internal sealed class VorticeRenderer:IDisposable {
    private const int FrameCount=2;
    private static readonly Format BackBufferFormat=Format.R8G8B8A8_UNorm;
    private static readonly Format DepthBufferFormat=Format.D32_Float;

    private readonly ID3D12Device Device;
    private readonly ID3D12CommandQueue CommandQueue;
    private readonly IDXGISwapChain3 SwapChain;
    private readonly ID3D12DescriptorHeap RtvHeap;
    private readonly int RtvDescriptorSize;
    private readonly ID3D12Resource[]RenderTargets=new ID3D12Resource[FrameCount];
    private readonly ID3D12DescriptorHeap DsvHeap;
    private ID3D12Resource DepthBuffer;
    private readonly ID3D12CommandAllocator CommandAllocator;
    private readonly ID3D12GraphicsCommandList CommandList;
    private readonly ID3D12RootSignature RootSignature;
    private readonly ID3D12PipelineState PipelineState;
    private readonly ID3D12Resource VertexBuffer;
    private readonly VertexBufferView VertexBufferView;
    private readonly ID3D12Resource IndexBuffer;
    private readonly IndexBufferView IndexBufferView;
    private readonly int IndexCount;
    private readonly ID3D12Resource ConstantBuffer;
    private readonly int ConstantBufferSize;
    private readonly ID3D12Fence Fence;
    private ulong FenceValue;
    private readonly IntPtr FenceEvent;

    private readonly Vector3 BoundsCenter;
    private readonly float BoundsRadius;
    private readonly Vector4 BaseColorFactor;

    private int Width;
    private int Height;

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex {
        public Vector3 Position;
        public Vector3 Normal;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TransformConstants {
        public Matrix4x4 Mvp;
        public Vector4 BaseColorFactor;
    }

    private const string ShaderSource="""
        cbuffer TransformCB : register(b0)
        {
            row_major float4x4 MVP;
            float4 BaseColorFactor;
        };

        struct VertexInput { float3 Position : POSITION; float3 Normal : NORMAL; };
        struct VertexOutput { float4 Position : SV_Position; float3 Normal : NORMAL; };

        static const float3 LightDirectionRaw = float3(0.4, 0.7, 0.5);
        static const float3 AmbientColor = float3(0.12, 0.12, 0.15);

        VertexOutput VSMain(VertexInput input)
        {
            VertexOutput output;
            output.Position = mul(float4(input.Position, 1.0), MVP);
            output.Normal = input.Normal;
            return output;
        }

        float4 PSMain(VertexOutput input) : SV_Target
        {
            float3 n = normalize(input.Normal);
            float3 lightDirection = normalize(LightDirectionRaw);
            float ndotl = max(dot(n, lightDirection), 0.0);
            return float4(saturate(AmbientColor + BaseColorFactor.rgb * ndotl), 1.0);
        }
        """;

    public VorticeRenderer(IntPtr Hwnd,int Width,int Height,SimpleMesh Mesh){
        this.Width=Math.Max(1,Width);
        this.Height=Math.Max(1,Height);
        this.BoundsCenter=Mesh.BoundsCenter;
        this.BoundsRadius=Mesh.BoundsRadius>0f?Mesh.BoundsRadius:1f;
        this.BaseColorFactor=Mesh.BaseColorFactor;

        using var Factory=DXGI.CreateDXGIFactory1<IDXGIFactory4>();

        IDXGIAdapter1?ChosenAdapter=null;
        for(var I=0;Factory.EnumAdapters1(I,out var Adapter).Success;I++){
            if((Adapter.Description1.Flags&AdapterFlags.Software)!=0){
                Adapter.Dispose();
                continue;
            }

            ChosenAdapter=Adapter;
            break;
        }

        this.Device=ChosenAdapter is not null
                        ?D3D12.D3D12CreateDevice<ID3D12Device>(ChosenAdapter,FeatureLevel.Level_11_0)
                        :D3D12.D3D12CreateDevice<ID3D12Device>(null,FeatureLevel.Level_11_0);
        ChosenAdapter?.Dispose();

        this.CommandQueue= this.Device.CreateCommandQueue(CommandListType.Direct);

        SwapChainDescription1 SwapChainDescription=new(){
            Width=this.Width,
            Height=this.Height,
            Format=BackBufferFormat,
            BufferUsage=Usage.RenderTargetOutput,
            BufferCount=FrameCount,
            SampleDescription=new SampleDescription(1,0),
            SwapEffect=SwapEffect.FlipDiscard,
        };

        using var TempSwapChain=Factory.CreateSwapChainForHwnd(this.CommandQueue,Hwnd,SwapChainDescription);
        this.SwapChain=TempSwapChain.QueryInterface<IDXGISwapChain3>();

        this.RtvHeap= this.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView,FrameCount));
        this.RtvDescriptorSize= this.Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        this.CreateRenderTargetViews();

        this.DsvHeap= this.Device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView,1));
        this.DepthBuffer= this.CreateDepthBuffer(this.Width,this.Height);

        this.CommandAllocator= this.Device.CreateCommandAllocator(CommandListType.Direct);
        this.CommandList= this.Device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, this.CommandAllocator);
        this.CommandList.Close();

        RootParameter1 TransformParameter=new(RootParameterType.ConstantBufferView,new RootDescriptor1(0,0),ShaderVisibility.All);
        RootSignatureDescription1 RootSignatureDescription=new(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            [TransformParameter]);
        this.RootSignature= this.Device.CreateRootSignature(0,new VersionedRootSignatureDescription(RootSignatureDescription));

        var VsCompileResult=Compiler.Compile(ShaderSource,"VSMain","VorticeRenderer","vs_5_0",out var VertexShaderBlob,out var VsErrorBlob);
        if(VsCompileResult.Failure||VertexShaderBlob is null){
            throw new InvalidOperationException($"Vertex shader compilation failed: {VsErrorBlob?.AsString()}");
        }

        var PsCompileResult=Compiler.Compile(ShaderSource,"PSMain","VorticeRenderer","ps_5_0",out var PixelShaderBlob,out var PsErrorBlob);
        if(PsCompileResult.Failure||PixelShaderBlob is null){
            throw new InvalidOperationException($"Pixel shader compilation failed: {PsErrorBlob?.AsString()}");
        }

        using var VertexShaderBlobOwned=VertexShaderBlob;
        using var PixelShaderBlobOwned=PixelShaderBlob;
        VsErrorBlob?.Dispose();
        PsErrorBlob?.Dispose();

        InputElementDescription[]InputElements=[
            new InputElementDescription("POSITION",0,Format.R32G32B32_Float,0,0),
            new InputElementDescription("NORMAL",0,Format.R32G32B32_Float,12,0),
        ];

        GraphicsPipelineStateDescription PsoDescription=new(){
            RootSignature= this.RootSignature,
            VertexShader=VertexShaderBlobOwned.AsBytes(),
            PixelShader=PixelShaderBlobOwned.AsBytes(),
            InputLayout=new InputLayoutDescription(InputElements),
            SampleMask=uint.MaxValue,
            PrimitiveTopologyType=PrimitiveTopologyType.Triangle,
            RasterizerState=RasterizerDescription.CullNone,
            BlendState=BlendDescription.Opaque,
            DepthStencilState=DepthStencilDescription.Default,
            DepthStencilFormat=DepthBufferFormat,
            RenderTargetFormats=[BackBufferFormat],
            SampleDescription=new SampleDescription(1,0),
        };
        this.PipelineState= this.Device.CreateGraphicsPipelineState(PsoDescription);

        var Vertices=new Vertex[Mesh.Positions.Length];
        for(var I=0;I<Vertices.Length;I++){
            Vertices[I]=new Vertex { Position=Mesh.Positions[I],Normal=Mesh.Normals[I]};
        }

        var VertexBufferSize=Vertices.Length*Marshal.SizeOf<Vertex>();
        this.VertexBuffer= this.CreateUploadBuffer(VertexBufferSize,Vertices);
        this.VertexBufferView=new VertexBufferView(this.VertexBuffer.GPUVirtualAddress,VertexBufferSize,Marshal.SizeOf<Vertex>());

        this.IndexCount=Mesh.Indices.Length;
        var IndexBufferSize= this.IndexCount*sizeof(uint);
        this.IndexBuffer= this.CreateUploadBuffer(IndexBufferSize,Mesh.Indices);
        // is32Bit: true - mesh.Indices is uint[] (32-bit). Passing false here silently
        // reinterprets the index buffer as 16-bit indices, scrambling the triangle list into
        // garbage geometry (confirmed empirically - this was the cause of the "bowtie"-looking
        // Box.glb render).
        this.IndexBufferView=new IndexBufferView(this.IndexBuffer.GPUVirtualAddress,IndexBufferSize,true);

        this.ConstantBufferSize=(Marshal.SizeOf<TransformConstants>()+255)&~255;
        this.ConstantBuffer= this.Device.CreateCommittedResource(
                                                                 HeapType.Upload,
                                                                 new ResourceDescription {
                                                                     Dimension=ResourceDimension.Buffer,
                                                                     Width=(ulong)this.ConstantBufferSize,
                                                                     Height=1,
                                                                     DepthOrArraySize=1,
                                                                     MipLevels=1,
                                                                     SampleDescription=new SampleDescription(1,0),
                                                                     Layout=TextureLayout.RowMajor,
                                                                     Format=Format.Unknown,
                                                                 },
                                                                 ResourceStates.GenericRead);

        this.Fence= this.Device.CreateFence(0);
        this.FenceValue=1;
        this.FenceEvent=NativeEvent.CreateAutoResetEvent();

        this.UpdateTransformConstantBuffer();
    }

    private ID3D12Resource CreateUploadBuffer<T>(int SizeInBytes,T[]Data) where T:struct {
        var Resource= this.Device.CreateCommittedResource(
                                                          HeapType.Upload,
                                                          ResourceDescription.Buffer((ulong)SizeInBytes),
                                                          ResourceStates.GenericRead);

        // Avoid `unsafe`/`fixed`/raw pointers: the WPF XAML markup-compile pass builds this
        // project through a generated temporary .csproj that does not inherit AllowUnsafeBlocks
        // (confirmed empirically - see ADR-0008), so unsafe pointer code fails to compile there
        // even though this project's own csproj enables it. MemoryMarshal.Write works on a Span
        // without needing an unsafe context.
        var Mapped=Resource.Map<byte>(0,SizeInBytes);
        var ElementSize=Marshal.SizeOf<T>();
        for(var I=0;I<Data.Length;I++){
            MemoryMarshal.Write(Mapped.Slice(I*ElementSize,ElementSize),ref Data[I]);
        }
        Resource.Unmap(0,null);
        return Resource;
    }

    private ID3D12Resource CreateDepthBuffer(int W,int H){
        var Description=ResourceDescription.Texture2D(DepthBufferFormat,(uint)W,(uint)H,1,1);
        Description.Flags=ResourceFlags.AllowDepthStencil;

        var Buffer= this.Device.CreateCommittedResource(
                                                        HeapType.Default,
                                                        Description,
                                                        ResourceStates.DepthWrite,
                                                        new ClearValue(DepthBufferFormat,1.0f,0));

        this.Device.CreateDepthStencilView(Buffer,null, this.DsvHeap.GetCPUDescriptorHandleForHeapStart());
        return Buffer;
    }

    private void CreateRenderTargetViews(){
        var Handle= this.RtvHeap.GetCPUDescriptorHandleForHeapStart();
        for(var I=0;I<FrameCount;I++){
            this.RenderTargets[I]= this.SwapChain.GetBuffer<ID3D12Resource>(I);
            this.Device.CreateRenderTargetView(this.RenderTargets[I],null,Handle);
            Handle+= this.RtvDescriptorSize;
        }
    }

    private void UpdateTransformConstantBuffer(){
        var Eye= this.BoundsCenter+Vector3.Normalize(new Vector3(1,0.75f,1))*(this.BoundsRadius*3.0f);
        var View=Matrix4x4.CreateLookAt(Eye, this.BoundsCenter,Vector3.UnitY);
        var Projection=Matrix4x4.CreatePerspectiveFieldOfView(
                                                              (float)(Math.PI/4.0),Math.Max(1, this.Width)/(float)Math.Max(1, this.Height),0.05f, this.BoundsRadius*20f);
        var Mvp=View*Projection;

        TransformConstants Constants=new(){ Mvp=Mvp,BaseColorFactor= this.BaseColorFactor };
        var Mapped= this.ConstantBuffer.Map<byte>(0, this.ConstantBufferSize);
        MemoryMarshal.Write(Mapped,ref Constants);
        this.ConstantBuffer.Unmap(0,null);
    }

    public void Resize(int NewWidth,int NewHeight){
        NewWidth=Math.Max(1,NewWidth);
        NewHeight=Math.Max(1,NewHeight);
        if(NewWidth== this.Width&&NewHeight== this.Height){
            return;
        }

        this.WaitForGpuIdle();

        for(var I=0;I<FrameCount;I++){
            this.RenderTargets[I].Dispose();
        }

        this.DepthBuffer.Dispose();

        this.Width=NewWidth;
        this.Height=NewHeight;
        this.SwapChain.ResizeBuffers(FrameCount, this.Width, this.Height,BackBufferFormat,SwapChainFlags.None);
        this.CreateRenderTargetViews();
        this.DepthBuffer= this.CreateDepthBuffer(this.Width, this.Height);

        this.UpdateTransformConstantBuffer();
    }

    public void RenderFrame(){
        this.CommandAllocator.Reset();
        this.CommandList.Reset(this.CommandAllocator, this.PipelineState);

        var FrameIndex= this.SwapChain.CurrentBackBufferIndex;

        this.CommandList.ResourceBarrierTransition(this.RenderTargets[FrameIndex],ResourceStates.Present,ResourceStates.RenderTarget);

        var RtvHandle= this.RtvHeap.GetCPUDescriptorHandleForHeapStart()+(FrameIndex* this.RtvDescriptorSize);
        var DsvHandle= this.DsvHeap.GetCPUDescriptorHandleForHeapStart();
        this.CommandList.OMSetRenderTargets(RtvHandle,DsvHandle);
        this.CommandList.ClearRenderTargetView(RtvHandle,new Vortice.Mathematics.Color4(5/255f,5/255f,20/255f,1f));
        this.CommandList.ClearDepthStencilView(DsvHandle,ClearFlags.Depth,1.0f,0);

        this.CommandList.SetGraphicsRootSignature(this.RootSignature);
        this.CommandList.SetGraphicsRootConstantBufferView(0, this.ConstantBuffer.GPUVirtualAddress);
        this.CommandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        this.CommandList.IASetVertexBuffers(0, this.VertexBufferView);
        this.CommandList.IASetIndexBuffer(this.IndexBufferView);
        this.CommandList.RSSetViewport(0,0, this.Width, this.Height);
        this.CommandList.RSSetScissorRect(this.Width, this.Height);
        this.CommandList.DrawIndexedInstanced(this.IndexCount,1,0,0,0);

        this.CommandList.ResourceBarrierTransition(this.RenderTargets[FrameIndex],ResourceStates.RenderTarget,ResourceStates.Present);
        this.CommandList.Close();

        this.CommandQueue.ExecuteCommandList(this.CommandList);
        this.SwapChain.Present(1,PresentFlags.None);

        this.WaitForGpuIdle();
    }

    private void WaitForGpuIdle(){
        var ValueToSignal= this.FenceValue++;
        this.CommandQueue.Signal(this.Fence,ValueToSignal);
        if(this.Fence.CompletedValue<ValueToSignal){
            this.Fence.SetEventOnCompletion(ValueToSignal, this.FenceEvent);
            NativeEvent.Wait(this.FenceEvent);
        }
    }

    public void Dispose(){
        this.WaitForGpuIdle();

        this.ConstantBuffer.Dispose();
        this.IndexBuffer.Dispose();
        this.VertexBuffer.Dispose();
        this.PipelineState.Dispose();
        this.RootSignature.Dispose();
        this.CommandList.Dispose();
        this.CommandAllocator.Dispose();
        this.DepthBuffer.Dispose();
        this.DsvHeap.Dispose();
        for(var I=0;I<FrameCount;I++){
            this.RenderTargets[I].Dispose();
        }

        this.RtvHeap.Dispose();
        this.SwapChain.Dispose();
        this.CommandQueue.Dispose();
        this.Fence.Dispose();
        NativeEvent.Close(this.FenceEvent);
        this.Device.Dispose();
    }
}
