using CSharpGameEngine.Rendering.Pipeline;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D12_CULL_MODE;
using static TerraFX.Interop.DirectX.D3D12_INPUT_CLASSIFICATION;
using static TerraFX.Interop.DirectX.D3D12_PRIMITIVE_TOPOLOGY_TYPE;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// PSO のキャッシュ（ADR-0087 §1-4）。L4 の <see cref="StagePipelineDesc"/> を値で比べるキーにし、D3D12 の PSO を作るだけを受け持つ。
/// どの PSO を作るかは <see cref="StagePipelines.All"/> が決め、描画のコードは解決済みの PSO のフィールドを使う。
/// </summary>
public sealed unsafe partial class StageRenderer {
    private Dictionary<StagePipelineDesc,nint>PipelineStates=new();
    /// <summary><see cref="StagePipelines.All"/> をすべて作る。途中で失敗したら作ったものを解放して例外を投げ、今のキャッシュは変えない。</summary>
    private Dictionary<StagePipelineDesc,nint>BuildPipelineStates(){
        var Created=new Dictionary<StagePipelineDesc,nint>();
        try{
            foreach(var Desc in StagePipelines.All){
                Created.Add(Desc,(nint)this.CreatePipelineState(Desc));
            }
            return Created;
        }catch{
            ReleasePipelineStates(Created);
            throw;
        }
    }
    /// <summary>新しいキャッシュを使い始め、描画が使う PSO のフィールドをそこから取り直す。前のキャッシュを返す（呼び出し側が GPU の完了後に解放する）。</summary>
    private Dictionary<StagePipelineDesc,nint>UsePipelineStates(Dictionary<StagePipelineDesc,nint>Next){
        var Previous=this.PipelineStates;
        this.PipelineStates=Next;
        this.PipelineState=this.ResolvePipelineState(StagePipelines.Lit);
        this.PresentationPipelineState=this.ResolvePipelineState(StagePipelines.Presentation);
        this.WireframePipelineState=this.ResolvePipelineState(StagePipelines.Wireframe);
        this.CameraPreviewPipelineState=this.ResolvePipelineState(StagePipelines.CameraPreview);
        this.CameraPreviewWireframePipelineState=this.ResolvePipelineState(StagePipelines.CameraPreviewWireframe);
        this.ObserverPipelineState=this.ResolvePipelineState(StagePipelines.Observer);
        this.ObserverWireframePipelineState=this.ResolvePipelineState(StagePipelines.ObserverWireframe);
        this.LumenSurfacePipelineState=this.ResolvePipelineState(StagePipelines.LumenSurface);
        this.LumenCardPipelineState=this.ResolvePipelineState(StagePipelines.LumenCard);
        this.OverlayTrianglePipelineState=this.ResolvePipelineState(StagePipelines.OverlayTriangles);
        this.OverlayLinePipelineState=this.ResolvePipelineState(StagePipelines.OverlayLines);
        this.ShadowPipelineState=this.ResolvePipelineState(StagePipelines.Shadow);
        this.PostProcessPipelineState=this.ResolvePipelineState(StagePipelines.PostProcess);
        this.ScreenPipelineState=this.ResolvePipelineState(StagePipelines.Screen);
        return Previous;
    }
    private ID3D12PipelineState*ResolvePipelineState(in StagePipelineDesc Desc)=>
        this.PipelineStates.TryGetValue(Desc,out var Pipeline)?(ID3D12PipelineState*)Pipeline:null;
    private static void ReleasePipelineStates(Dictionary<StagePipelineDesc,nint>Pipelines){
        foreach(var Pipeline in Pipelines.Values){
            _=((ID3D12PipelineState*)Pipeline)->Release();
        }
        Pipelines.Clear();
    }
    /// <summary>キャッシュのすべての PSO を解放し、フィールドを空にする（破棄のとき）。</summary>
    private void ReleaseAllPipelineStates(){
        ReleasePipelineStates(this.UsePipelineStates(new Dictionary<StagePipelineDesc,nint>()));
    }
    private byte[]BytecodeFor(string Entry)=>Entry switch{
        "VSMain"=>this.VertexShaderBytecode,
        "PSMain"=>this.PixelShaderBytecode,
        "PreviewPSMain"=>this.PreviewPixelShaderBytecode,
        "PreviewCleanPSMain"=>this.PreviewCleanPixelShaderBytecode,
        "ObserverVSMain"=>this.ObserverVertexShaderBytecode,
        "SurfacePrepassPSMain"=>this.LumenSurfacePixelShaderBytecode,
        "LumenCardPSMain"=>this.LumenCardPixelShaderBytecode,
        "OverlayVSMain"=>this.OverlayVertexShaderBytecode,
        "OverlayPSMain"=>this.OverlayPixelShaderBytecode,
        "VSShadowMain"=>this.ShadowVertexShaderBytecode,
        "FullscreenVS"=>this.FullscreenVertexShaderBytecode,
        "PostProcessPS"=>this.PostProcessPixelShaderBytecode,
        "ScreenVSMain"=>this.ScreenVertexShaderBytecode,
        "ScreenPSMain"=>this.ScreenPixelShaderBytecode,
        _=>throw new ArgumentOutOfRangeException(nameof(Entry),Entry,"The Stage shader artifact has no such entry."),
    };
    private ID3D12RootSignature*RootSignatureFor(StageRootSignature Signature)=>Signature switch{
        StageRootSignature.Main=>this.RootSignature,
        StageRootSignature.Shadow=>this.ShadowRootSignature,
        StageRootSignature.PostProcess=>this.PostProcessRootSignature,
        StageRootSignature.Screen=>this.ScreenRootSignature,
        _=>throw new ArgumentOutOfRangeException(nameof(Signature)),
    };
    /// <summary>1 つの PSO を作る。設定は、値の各項目を D3D12 の設定へそのまま写したもの。</summary>
    private ID3D12PipelineState*CreatePipelineState(in StagePipelineDesc Desc){
        var VertexShader=this.BytecodeFor(Desc.VertexEntry);
        var PixelShader=Desc.PixelEntry is null?[]:this.BytecodeFor(Desc.PixelEntry);
        fixed(byte*PPosition="POSITION\0"u8)
        fixed(byte*PNormal="NORMAL\0"u8)
        fixed(byte*PTangent="TANGENT\0"u8)
        fixed(byte*PTexCoord="TEXCOORD\0"u8)
        fixed(byte*PColor="COLOR\0"u8)
        fixed(byte*PVertexShader=VertexShader)
        fixed(byte*PPixelShader=PixelShader){
            var InputElements=stackalloc D3D12_INPUT_ELEMENT_DESC[4];
            var InputElementCount=0u;
            switch(Desc.InputLayout){
                case StageInputLayout.Mesh:
                    InputElements[0]=new(){ SemanticName=(sbyte*)PPosition,Format=DXGI_FORMAT_R32G32B32_FLOAT,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA };
                    InputElements[1]=new(){ SemanticName=(sbyte*)PNormal,Format=DXGI_FORMAT_R32G32B32_FLOAT,AlignedByteOffset=12,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA };
                    InputElements[2]=new(){ SemanticName=(sbyte*)PTangent,Format=DXGI_FORMAT_R32G32B32A32_FLOAT,AlignedByteOffset=24,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA };
                    InputElements[3]=new(){ SemanticName=(sbyte*)PTexCoord,Format=DXGI_FORMAT_R32G32_FLOAT,AlignedByteOffset=40,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA };
                    InputElementCount=4;
                    break;
                case StageInputLayout.OverlayColor:
                    InputElements[0]=new(){ SemanticName=(sbyte*)PPosition,Format=DXGI_FORMAT_R32G32B32_FLOAT,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA };
                    InputElements[1]=new(){ SemanticName=(sbyte*)PColor,Format=DXGI_FORMAT_R32G32B32A32_FLOAT,AlignedByteOffset=12,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA };
                    InputElementCount=2;
                    break;
                case StageInputLayout.Screen:
                    InputElements[0]=new(){ SemanticName=(sbyte*)PPosition,Format=DXGI_FORMAT_R32G32_FLOAT,AlignedByteOffset=0,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA };
                    InputElements[1]=new(){ SemanticName=(sbyte*)PTexCoord,Format=DXGI_FORMAT_R32G32_FLOAT,AlignedByteOffset=8,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA };
                    InputElements[2]=new(){ SemanticName=(sbyte*)PColor,Format=DXGI_FORMAT_R32G32B32A32_FLOAT,AlignedByteOffset=16,InputSlotClass=D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA };
                    InputElementCount=3;
                    break;
            }
            D3D12_GRAPHICS_PIPELINE_STATE_DESC PsoDesc=new(){
                pRootSignature=this.RootSignatureFor(Desc.RootSignature),
                VS=new D3D12_SHADER_BYTECODE{ pShaderBytecode=PVertexShader,BytecodeLength=(nuint)VertexShader.Length },
                RasterizerState=D3D12_RASTERIZER_DESC.DEFAULT,
                BlendState=D3D12_BLEND_DESC.DEFAULT,
                DepthStencilState=D3D12_DEPTH_STENCIL_DESC.DEFAULT,
                SampleMask=uint.MaxValue,
                PrimitiveTopologyType=Desc.Topology==StagePrimitiveTopology.Line?D3D12_PRIMITIVE_TOPOLOGY_TYPE_LINE:D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE,
                SampleDesc=new DXGI_SAMPLE_DESC(1,0),
            };
            if(InputElementCount>0){
                PsoDesc.InputLayout=new D3D12_INPUT_LAYOUT_DESC{ pInputElementDescs=InputElements,NumElements=InputElementCount };
            }
            if(Desc.PixelEntry is not null){
                PsoDesc.PS=new D3D12_SHADER_BYTECODE{ pShaderBytecode=PPixelShader,BytecodeLength=(nuint)PixelShader.Length };
            }
            PsoDesc.RasterizerState.CullMode=D3D12_CULL_MODE_NONE;
            PsoDesc.RasterizerState.FillMode=Desc.Fill==StageFillMode.Wireframe?D3D12_FILL_MODE.D3D12_FILL_MODE_WIREFRAME:D3D12_FILL_MODE.D3D12_FILL_MODE_SOLID;
            PsoDesc.RasterizerState.DepthClipEnable=Desc.DepthClip?TRUE:FALSE;
            switch(Desc.Targets){
                case StageRenderTargets.SceneColorAndDistance:
                    PsoDesc.NumRenderTargets=2;
                    PsoDesc.RTVFormats[0]=HdrColorFormat;
                    PsoDesc.RTVFormats[1]=DXGI_FORMAT_R32_FLOAT;
                    if(!Desc.WriteSceneDistance){
                        PsoDesc.BlendState.IndependentBlendEnable=TRUE;
                        PsoDesc.BlendState.RenderTarget[1].RenderTargetWriteMask=0;
                    }
                    break;
                case StageRenderTargets.LumenSurface:
                    PsoDesc.NumRenderTargets=2;
                    PsoDesc.RTVFormats[0]=StageLumen.SurfaceNormalFormat;
                    PsoDesc.RTVFormats[1]=StageLumen.SurfaceDistanceFormat;
                    break;
                case StageRenderTargets.LumenCard:
                    PsoDesc.NumRenderTargets=3;
                    PsoDesc.RTVFormats[0]=StageLumen.CardAlbedoFormat;
                    PsoDesc.RTVFormats[1]=StageLumen.CardNormalFormat;
                    PsoDesc.RTVFormats[2]=StageLumen.CardEmissiveFormat;
                    break;
                case StageRenderTargets.BackBuffer:
                    PsoDesc.NumRenderTargets=1;
                    PsoDesc.RTVFormats[0]=BackBufferFormat;
                    break;
                case StageRenderTargets.None:
                    PsoDesc.NumRenderTargets=0;
                    break;
            }
            PsoDesc.DSVFormat=Desc.Depth switch{
                StageDepthTarget.Scene=>DepthBufferFormat,
                StageDepthTarget.ShadowAtlas=>ShadowAtlasDsvFormat,
                _=>DXGI_FORMAT_UNKNOWN,
            };
            if(!Desc.DepthTest){
                PsoDesc.DepthStencilState.DepthEnable=FALSE;
                PsoDesc.DepthStencilState.StencilEnable=FALSE;
            }
            if(Desc.Blend!=StageBlend.Opaque){
                var KeepFrameAlpha=Desc.Blend==StageBlend.AlphaOverKeepingFrameAlpha;
                PsoDesc.BlendState.RenderTarget[0]=new D3D12_RENDER_TARGET_BLEND_DESC{
                    BlendEnable=TRUE,
                    LogicOpEnable=FALSE,
                    SrcBlend=D3D12_BLEND.D3D12_BLEND_SRC_ALPHA,
                    DestBlend=D3D12_BLEND.D3D12_BLEND_INV_SRC_ALPHA,
                    BlendOp=D3D12_BLEND_OP.D3D12_BLEND_OP_ADD,
                    SrcBlendAlpha=KeepFrameAlpha?D3D12_BLEND.D3D12_BLEND_ZERO:D3D12_BLEND.D3D12_BLEND_ONE,
                    DestBlendAlpha=KeepFrameAlpha?D3D12_BLEND.D3D12_BLEND_ONE:D3D12_BLEND.D3D12_BLEND_INV_SRC_ALPHA,
                    BlendOpAlpha=D3D12_BLEND_OP.D3D12_BLEND_OP_ADD,
                    LogicOp=D3D12_LOGIC_OP.D3D12_LOGIC_OP_NOOP,
                    RenderTargetWriteMask=(byte)D3D12_COLOR_WRITE_ENABLE.D3D12_COLOR_WRITE_ENABLE_ALL,
                };
            }
            ID3D12PipelineState*Pipeline;
            HRESULT Result=this.Device!.Device->CreateGraphicsPipelineState(&PsoDesc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline);
            if(Result.FAILED){
                this.Device.PollDebugMessages();
                D3D12Diagnostics.EnsureSuccess(Result,"ID3D12Device::CreateGraphicsPipelineState",
                    $"creating the Stage pipeline state {Desc.VertexEntry}/{Desc.PixelEntry??"(depth only)"}"+Environment.NewLine+string.Join(Environment.NewLine,this.Device.CollectedMessages));
            }
            return Pipeline;
        }
    }
}
