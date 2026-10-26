using CSharpGameEngine.Rendering.View;
using CSharpGameEngine.Rendering.Server;
using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Runtime.Windows.Tests;

/// <summary>
/// ADR-0078/M0104: the screen layer through WARP. Quads in pixels land where they are asked (top-left origin), blend over the finished frame,
/// a distance-field batch thresholds the texture alpha at one half, the layer lasts one frame, and it survives a device loss.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public class ScreenLayerTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Size=128;

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ScreenLayer_DrawsBlendsLastsOneFrameAndSurvivesDeviceRecovery(){
        RunWithTimeout(RunQuadScenario,TimeSpan.FromSeconds(90));
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ScreenLayer_DistanceFieldBatchThresholdsTheAlphaAtOneHalf(){
        RunWithTimeout(RunDistanceFieldScenario,TimeSpan.FromSeconds(90));
    }

    [WindowsOnlyFact]
    public void ScreenLayerValidation_RejectsBrokenLayers(){
        RenderScreenVertex Vertex=new(Vector2.Zero,Vector2.Zero,Vector4.One);
        RenderScreenVertex[]Triangle=[Vertex,Vertex,Vertex];
        RenderScreenBatch Batch=new(RenderMaterialDefaults.White,RenderScreenShading.Image,0,3);

        RenderScreenLayer.Validate(Triangle,[Batch]);
        Assert.Throws<ArgumentException>(()=>RenderScreenLayer.Validate(Triangle,[Batch with { VertexCount=2 }]));
        Assert.Throws<ArgumentException>(()=>RenderScreenLayer.Validate(Triangle,[Batch with { FirstVertex=3 }]));
        Assert.Throws<ArgumentException>(()=>RenderScreenLayer.Validate(Triangle,[Batch with { FirstVertex=-3 }]));
        Assert.Throws<ArgumentException>(()=>RenderScreenLayer.Validate(Triangle,[Batch with { Shading=(RenderScreenShading)9 }]));
        Assert.Throws<ArgumentException>(()=>RenderScreenLayer.Validate(Triangle,[Batch with { Texture=null! }]));
        Assert.Throws<ArgumentException>(()=>RenderScreenLayer.Validate([Vertex with { Color=new Vector4(1f,1f,1f,2f) },Vertex,Vertex],[Batch]));
        Assert.Throws<ArgumentException>(()=>RenderScreenLayer.Validate([Vertex with { Position=new Vector2(float.NaN,0f) },Vertex,Vertex],[Batch]));
        Assert.Throws<ArgumentException>(()=>RenderScreenLayer.Validate(new RenderScreenVertex[RenderScreenLayer.MaximumVertices+3],[Batch]));
        Assert.Throws<ArgumentException>(()=>RenderScreenLayer.Validate(Triangle,new RenderScreenBatch[RenderScreenLayer.MaximumBatches+1]));
    }

    private static void RunWithTimeout(Action Action,TimeSpan Timeout){
        Task Task=Task.Run(Action);
        if(!Task.Wait(Timeout)){
            throw new TimeoutException($"GPU test did not complete within {Timeout}.");
        }
    }

    private static RenderServer CreateServer(Win32Window Window){
        (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,StageObjectCreateInfo[]Objects)=
            PrimitiveRoster.Build([PrimitiveType.Box],0,0);
        return D3D12RenderServer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,
            new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },
            ShaderArtifactPath,Meshes,Materials,Objects);
    }

    private static Win32Window CreateWindow(string Title)=>new(new Win32WindowOptions { Title=Title,Width=Size,Height=Size,Visible=false });

    private static PixelReadbackResult Render(RenderServer Renderer,ReadOnlySpan<RenderScreenVertex>Vertices,ReadOnlySpan<RenderScreenBatch>Batches,bool SetLayer){
        Renderer.SetCamera(CSharpGameEngine.Rendering.View.RenderCamera.CreateDefault(Vector3.Zero));
        Renderer.SetAmbientLight(new Vector3(0.4f));
        Renderer.SetDirectionalLight(new DirectionalLightSubmission(Vector3.UnitY,new LinearLightColor(1f,1f,1f),new Illuminance(2f),new LightShadowSettings(0,new ShadowBias(0.0015f))));
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);
        Renderer.SetOverlay([],[]);
        if(SetLayer){ Renderer.SetScreenLayer(Vertices,Batches); }
        Renderer.RenderFrame(CaptureForVerification:true);
        return Assert.IsType<PixelReadbackResult>(Renderer.LastCapturedFrame);
    }

    private static (byte R,byte G,byte B) Pixel(PixelReadbackResult Frame,int X,int Y){
        int Offset=checked((int)((Y*Frame.RowPitch)+(X*4)));
        return(Frame.Pixels[Offset],Frame.Pixels[Offset+1],Frame.Pixels[Offset+2]);
    }

    /// <summary>Two triangles covering the pixel rectangle.</summary>
    private static RenderScreenVertex[]Quad(float Left,float Top,float Right,float Bottom,Vector4 Color,float U0=0f,float U1=1f)=>[
        new(new Vector2(Left,Top),new Vector2(U0,0f),Color),
        new(new Vector2(Right,Top),new Vector2(U1,0f),Color),
        new(new Vector2(Left,Bottom),new Vector2(U0,1f),Color),
        new(new Vector2(Right,Top),new Vector2(U1,0f),Color),
        new(new Vector2(Right,Bottom),new Vector2(U1,1f),Color),
        new(new Vector2(Left,Bottom),new Vector2(U0,1f),Color),
    ];

    private static void AssertNear((byte R,byte G,byte B) Actual,(int R,int G,int B) Expected,int Tolerance,string Where){
        Assert.True(Math.Abs(Actual.R-Expected.R)<=Tolerance&&Math.Abs(Actual.G-Expected.G)<=Tolerance&&Math.Abs(Actual.B-Expected.B)<=Tolerance,
            $"{Where}: expected about ({Expected.R},{Expected.G},{Expected.B}), got ({Actual.R},{Actual.G},{Actual.B}).");
    }

    private static void RunQuadScenario(){
        using Win32Window Window=CreateWindow("CSharpGameEngine Screen Layer GPU Test");
        using RenderServer Renderer=CreateServer(Window);

        var Baseline=Render(Renderer,[],[],SetLayer:false);
        string BaselineHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Baseline.Pixels));

        // A red opaque quad at the top left and a half-transparent green one at the lower right.
        RenderScreenVertex[]Vertices=[
            .. Quad(16,16,48,48,new Vector4(1f,0f,0f,1f)),
            .. Quad(80,80,112,112,new Vector4(0f,1f,0f,0.5f)),
        ];
        RenderScreenBatch[]Batches=[new(RenderMaterialDefaults.White,RenderScreenShading.Image,0,Vertices.Length)];
        var Layered=Render(Renderer,Vertices,Batches,SetLayer:true);
        string LayeredHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Layered.Pixels));
        Assert.NotEqual(BaselineHash,LayeredHash);

        AssertNear(Pixel(Layered,32,32),(255,0,0),1,"opaque red quad, top left");
        // Same column, lower row: a vertically flipped layer would put the red quad here.
        Assert.Equal(Pixel(Baseline,32,96),Pixel(Layered,32,96));
        // Outside both quads the frame is untouched.
        Assert.Equal(Pixel(Baseline,2,2),Pixel(Layered,2,2));
        Assert.Equal(Pixel(Baseline,120,60),Pixel(Layered,120,60));
        // Edges: pixel 16 is inside the quad (its centre 16.5 is covered), pixel 15 is not; 47 is inside, 48 is not.
        AssertNear(Pixel(Layered,16,16),(255,0,0),1,"first covered pixel");
        Assert.Equal(Pixel(Baseline,15,16),Pixel(Layered,15,16));
        AssertNear(Pixel(Layered,47,47),(255,0,0),1,"last covered pixel");
        Assert.Equal(Pixel(Baseline,48,48),Pixel(Layered,48,48));
        // Half-transparent green over whatever the frame has there: result = 0.5 green + 0.5 frame (sRGB values blended as written).
        var Under=Pixel(Baseline,96,96);
        AssertNear(Pixel(Layered,96,96),((int)(Under.R*0.5),(int)(255*0.5+Under.G*0.5),(int)(Under.B*0.5)),2,"half-transparent green quad");

        // The layer lasts one frame: the next frame, without SetScreenLayer, is the baseline again.
        var Next=Render(Renderer,[],[],SetLayer:false);
        Assert.Equal(BaselineHash,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Next.Pixels)));
        Assert.Equal(1,Renderer.ResidentScreenTextureCount);

        // A lost device is replaced from the recorded frame, layer included.
        Render(Renderer,Vertices,Batches,SetLayer:true);
        Renderer.RemoveDeviceForTesting();
        Renderer.RenderFrame(CaptureForVerification:true);
        var Recovered=Assert.IsType<PixelReadbackResult>(Renderer.LastCapturedFrame);
        Assert.Equal(LayeredHash,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Recovered.Pixels)));
        Assert.Equal(2u,Renderer.DeviceSnapshot.Generation);
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }

    private static void RunDistanceFieldScenario(){
        using Win32Window Window=CreateWindow("CSharpGameEngine Screen Layer Distance Field GPU Test");
        using RenderServer Renderer=CreateServer(Window);
        var Baseline=Render(Renderer,[],[],SetLayer:false);

        // A 256 x 1 texture whose alpha rises from 0 to 255 and is white elsewhere, drawn over the whole width in blue: the distance field
        // is 0.5 about the middle, so the left part shows the frame and the right part is solid blue.
        byte[]Pixels=new byte[256*4];
        for(int X=0;X<256;X++){
            Pixels[X*4]=255;
            Pixels[(X*4)+1]=255;
            Pixels[(X*4)+2]=255;
            Pixels[(X*4)+3]=(byte)X;
        }
        var Ramp=RenderArtifactCodec.CreateProceduralTexture(256,1,Pixels);
        RenderScreenVertex[]Vertices=Quad(0,32,Size,96,new Vector4(0f,0f,1f,1f));
        RenderScreenBatch[]Batches=[new(Ramp,RenderScreenShading.DistanceField,0,Vertices.Length)];
        var Frame=Render(Renderer,Vertices,Batches,SetLayer:true);

        Assert.Equal(Pixel(Baseline,20,64),Pixel(Frame,20,64));
        Assert.Equal(Pixel(Baseline,50,64),Pixel(Frame,50,64));
        AssertNear(Pixel(Frame,80,64),(0,0,255),1,"inside the field");
        AssertNear(Pixel(Frame,120,64),(0,0,255),1,"inside the field, near the right edge");
        // Rows outside the quad are the frame.
        Assert.Equal(Pixel(Baseline,100,10),Pixel(Frame,100,10));
        Assert.Equal(Pixel(Baseline,100,120),Pixel(Frame,100,120));
        // The edge is soft: some pixel between 50 and 80 is neither the frame nor solid blue.
        bool Soft=false;
        for(int X=50;X<80;X++){
            var P=Pixel(Frame,X,64);
            if(P!=Pixel(Baseline,X,64)&&P!=((byte)0,(byte)0,(byte)255)){ Soft=true; }
        }
        Assert.True(Soft,"The distance-field edge should have anti-aliased pixels.");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }
}
