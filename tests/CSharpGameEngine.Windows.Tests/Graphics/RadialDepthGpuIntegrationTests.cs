using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// M0099 Phase 4 (ADR-0076): WARP oracles for the radial depth mode. Its colour must be the standard colour wherever
/// the same surface wins, its depth must order surfaces correctly at every scale, and the passes around it (sky,
/// sun cascades, AO, previews, frame slots, device recovery) must keep working with extreme far planes.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class RadialDepthGpuIntegrationTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static readonly LinearLightColor White=new(1f,1f,1f);
    private static readonly LightShadowSettings Shadow=new(0,new ShadowBias(0.005f));
    private static readonly Vector4 Red=new(0.9f,0.05f,0.05f,1f);
    private static readonly Vector4 Green=new(0.05f,0.9f,0.05f,1f);
    private static Camera Standard(Vector3 Eye,float PitchDegrees=0f,float NearZ=0.1f,float FarZ=300f){
        var View=Camera.CreateFirstPerson(Eye);
        View.Pitch=PitchDegrees*MathF.PI/180f;
        View.NearZ=NearZ;
        View.FarZ=FarZ;
        View.DepthMapping=RenderDepthMapping.Standard;
        return View;
    }
    private static Camera Radial(Camera View){
        View.DepthMapping=RenderDepthMapping.Radial;
        return View;
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Radial_RendersTheStandardPixelsBitForBitOnTrianglesItDoesNotClip(){
        // Objects float apart in depth (no contact lines) and lie wholly inside the clip range or wholly outside it:
        // no triangle is clipped, so the radial image must equal the standard image bit for bit.
        using var Scene=FloatingScene.Create(Environment:false,Clipped:false);
        var View=Standard(new Vector3(0f,1.6f,-4f),PitchDegrees:-10f,FarZ:40f);
        var StandardPixels=Scene.Render(View);
        var RadialPixels=Scene.Render(Radial(View));
        Assert.Equal(StandardPixels,RadialPixels);
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Radial_RendersTheStandardPixelsWithinRoundingOnClippedTriangles(){
        // A floor crossing the eye plane and the far plane and a box straddling the near plane: the radial main pass
        // clips with the standard projection's distances (SV_ClipDistance, because its conservative depth row runs
        // without depth clipping), which may round the clipped vertices differently, so interpolated values may differ
        // by one 8-bit level on those triangles.
        using var Scene=FloatingScene.Create(Environment:false);
        var View=Standard(new Vector3(0f,1.6f,-4f),PitchDegrees:-10f,FarZ:40f);
        AssertWithinOneLevel(Scene.Render(View),Scene.Render(Radial(View)));
        Scene.AssertNoDebugLayerErrors();
    }
    /// <summary>Every channel within one 8-bit level, and at most 1 % of the pixels different at all.</summary>
    private static void AssertWithinOneLevel(byte[]Expected,byte[]Actual){
        Assert.Equal(Expected.Length,Actual.Length);
        var Largest=0;
        var Different=0;
        for(var Index=0;Index<Expected.Length;Index+=4){
            var PixelLargest=0;
            for(var Channel=0;Channel<3;Channel++){
                PixelLargest=Math.Max(PixelLargest,Math.Abs(Expected[Index+Channel]-Actual[Index+Channel]));
            }
            Largest=Math.Max(Largest,PixelLargest);
            if(PixelLargest>0){ Different++; }
        }
        Assert.True(Largest<=1&&Different<=Expected.Length/4/100,$"largest difference {Largest}, {Different} of {Expected.Length/4} pixels differ");
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Radial_RendersTheStandardEnvironmentPixels(){
        // Sky composite, sun cascades, GI and AO: a radial camera with a game-sized range hands the inverting passes
        // the camera projection itself, so the frame equals the standard one (within rounding on the clipped floor).
        // GI keeps history between frames, so each mode renders from a fresh renderer.
        var View=Standard(new Vector3(0f,1.6f,-4f),PitchDegrees:-10f,FarZ:40f);
        AssertWithinOneLevel(FloatingScene.RenderFresh(View,Environment:true),FloatingScene.RenderFresh(Radial(View),Environment:true));
        Assert.Equal(FloatingScene.RenderFresh(View,Environment:true,Clipped:false),FloatingScene.RenderFresh(Radial(View),Environment:true,Clipped:false));
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Radial_KeepsSkyShadowsAndOcclusionFiniteWithExtremeFarPlanes(){
        // Far planes of 1e7 m and 1e18 m would make the camera matrix singular for the sky, the cascades and the AO
        // prepass; the auxiliary projection keeps them finite. Both far planes clamp the auxiliary far plane to
        // near * 65536, so they render identical colour; a 300 m far plane (beyond all geometry) differs only by
        // rounding.
        var Ordinary=FloatingScene.RenderFresh(Radial(Standard(new Vector3(0f,1.6f,-4f),PitchDegrees:-10f,FarZ:300f)),Environment:true);
        var Planetary=FloatingScene.RenderFresh(Radial(Standard(new Vector3(0f,1.6f,-4f),PitchDegrees:-10f,FarZ:1e7f)),Environment:true);
        var Interstellar=FloatingScene.RenderFresh(Radial(Standard(new Vector3(0f,1.6f,-4f),PitchDegrees:-10f,FarZ:1e18f)),Environment:true);
        Assert.Equal(Planetary,Interstellar);
        var Close=0;
        for(var Index=0;Index<Ordinary.Length;Index++){
            if(Math.Abs(Ordinary[Index]-Planetary[Index])<=2){ Close++; }
        }
        Assert.True(Close>=Ordinary.Length*0.99,$"{Close} of {Ordinary.Length} channels within 2 levels");
    }
    [WindowsOnlyTheory]
    [Trait("Category","GPU")]
    [InlineData(2e4f,0.05f,false)]
    [InlineData(1e15f,1e10f,true)]
    [InlineData(5e18f,1e14f,true)]
    public void Radial_SeparatesNearlyCoplanarSurfacesFarAway(float Distance,float Gap,bool StandardFails){
        // A red quad in front of a green one, Gap metres apart at Distance metres (relative gaps of 2.5e-6 to 2e-5,
        // above the ~3e-6 budget of ADR-0076 at those ranges). The green one is drawn first, so equal depths keep
        // it. The radial depth shows only the red quad. From 1e15 m the standard depth rounds both to the cleared
        // 1.0 and shows neither (WARP happens to resolve the 20 km pair, so that case only checks radial).
        using var Scene=QuadScene.Create([Green,Red]);
        var View=Standard(Vector3.Zero,FarZ:Distance*1.5f);
        Matrix4x4[]Transforms=[QuadScene.Facing(Distance+Gap,Distance*2f),QuadScene.Facing(Distance,Distance*2f)];
        var StandardFrame=Scene.Render(View,Transforms);
        var RadialFrame=Scene.Render(Radial(View),Transforms);
        if(StandardFails){
            Assert.NotEqual(1f,Scene.RedFraction(StandardFrame));
        }
        Assert.Equal(1f,Scene.RedFraction(RadialFrame));
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Radial_DepthMatchesTheCpuEncoding(){
        using var Scene=QuadScene.Create([Red]);
        var View=Radial(Standard(Vector3.Zero,FarZ:1000f));
        Scene.Render(View,[QuadScene.Facing(50f,400f)]);
        var Depth=Scene.Renderer.ReadDepthForTesting();
        var Width=Scene.Renderer.LastCapturedFrame!.Width;
        var Height=Scene.Renderer.LastCapturedFrame!.Height;
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,View.NearZ,View.FarZ,View.FovYRadians,(float)Width/Height,out var Projection));
        var TanHalfFov=MathF.Tan(View.FovYRadians*0.5f);
        foreach(var (X,Y) in new[]{(Width/2,Height/2),(3,4),(Width-5,Height-2)}){
            // The view ray through the pixel centre meets the quad at z = 50.
            var NdcX=(X+0.5f)/Width*2f-1f;
            var NdcY=1f-(Y+0.5f)/Height*2f;
            var Ray=new Vector3(NdcX*TanHalfFov*Width/Height,NdcY*TanHalfFov,1f);
            var Expected=Projection.EncodeDepth(50f*Ray.Length());
            var Actual=Depth[Y*Width+X];
            Assert.True(MathF.Abs(Actual-Expected)<=Expected*2e-6f,$"({X},{Y}) depth {Actual:R} expected {Expected:R}");
        }
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Radial_RendersTheSameImageFromBothFrameSlots(){
        using var Scene=FloatingScene.Create(Environment:false);
        var View=Radial(Standard(new Vector3(0f,1.6f,-4f),PitchDegrees:-10f,FarZ:1e9f));
        Assert.Equal(Scene.Render(View,Frames:2),Scene.Render(View,Frames:3));
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void RadialThumbnail_SeparatesNearlyCoplanarSurfaces(){
        using var Scene=QuadScene.Create([Green,Red]);
        var Main=Standard(Vector3.Zero,FarZ:1.5e15f);
        var Thumbnail=Main.GetView()*Main.GetProjection(1f);
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,Main.NearZ,Main.FarZ,Main.FovYRadians,1f,out var Projection));
        Matrix4x4[]Transforms=[QuadScene.Facing(1e15f+1e10f,2e15f),QuadScene.Facing(1e15f,2e15f)];
        var Frame=Scene.Render(Main,Transforms,Renderer=>Renderer.SetCameraPreviews([
            new RenderCameraPreview(0,0,64,64,Thumbnail,Vector3.Zero),
            new RenderCameraPreview(96,0,64,64,Thumbnail,Vector3.Zero,DepthMapping:RenderDepthMapping.Radial,Radial:Projection),
        ]));
        Assert.NotEqual(1f,Scene.RedFraction(Frame,0,0,64,64));
        Assert.Equal(1f,Scene.RedFraction(Frame,96,0,64,64));
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Radial_RejectsAnIncompleteCamera(){
        using var Scene=QuadScene.Create([Red]);
        var View=Radial(Standard(Vector3.Zero));
        View.RadialTransitionMeters=0f;
        Assert.Throws<ArgumentException>(()=>Scene.Renderer.SetCamera(View));
        var Observer=Standard(Vector3.Zero);
        Observer.DepthMapping=RenderDepthMapping.RadialObserver;
        Assert.Throws<ArgumentOutOfRangeException>(()=>Scene.Renderer.SetCamera(Observer));
        Assert.Throws<ArgumentException>(()=>Scene.Renderer.SetCameraPreviews([
            new RenderCameraPreview(0,0,16,16,Matrix4x4.Identity,Vector3.Zero,DepthMapping:RenderDepthMapping.Radial),
        ]));
    }
    /// <summary>The view-projection matrix and radial parameters an editor would hand over for <paramref name="View"/>.</summary>
    private static (Matrix4x4 ViewProjection,Vector3 Eye,RadialViewParameters Radial)AsMatrix(Camera View,float Aspect)=>
        (View.GetView()*View.GetProjection(Aspect),View.GetEyePosition(),new RadialViewParameters(View.FovYRadians,View.NearZ,View.FarZ,View.RadialTransitionMeters));
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void RadialViewProjection_RendersWhatTheRadialCameraRenders(){
        // M0100: an editor hands the renderer its camera as one perspective matrix. Drawn radial from that matrix, the
        // frame and its depth equal the radial camera's.
        using var Scene=FloatingScene.Create(Environment:false);
        var View=Radial(Standard(new Vector3(0f,1.6f,-4f),PitchDegrees:-10f,FarZ:40f));
        var CameraPixels=Scene.Render(View);
        var CameraDepth=Scene.Renderer.ReadDepthForTesting();
        var (ViewProjection,Eye,Parameters)=AsMatrix(View,(float)Scene.Window.ClientWidth/Scene.Window.ClientHeight);
        var MatrixPixels=Scene.Render(Renderer=>Renderer.SetViewProjection(ViewProjection,Eye,Parameters));
        Assert.Equal(CameraPixels,MatrixPixels);
        Assert.Equal(CameraDepth,Scene.Renderer.ReadDepthForTesting());
        // The same matrix drawn standard writes another depth, so the comparison above went through the radial path.
        Scene.Render(Renderer=>Renderer.SetViewProjection(ViewProjection,Eye));
        Assert.NotEqual(CameraDepth,Scene.Renderer.ReadDepthForTesting());
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void RadialViewProjection_WithAnExtremeRange_RendersWhatTheRadialCameraRenders(){
        // Near 1 mm and far 1e7 m: the sky, the sun cascades and the AO prepass take the auxiliary matrix, which the
        // matrix path rebuilds from the matrix's own view depth.
        var View=Radial(Standard(new Vector3(0f,1.6f,-4f),PitchDegrees:-10f,NearZ:0.001f,FarZ:1e7f));
        var (ViewProjection,Eye,Parameters)=AsMatrix(View,160f/90f);
        Assert.Equal(FloatingScene.RenderFresh(View,Environment:true),
            FloatingScene.RenderFresh(Renderer=>Renderer.SetViewProjection(ViewProjection,Eye,Parameters),Environment:true));
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void RadialViewProjection_RejectsOrthographicMatricesAndInvalidParameters(){
        using var Scene=FloatingScene.Create(Environment:false);
        var (ViewProjection,Eye,Parameters)=AsMatrix(Radial(Standard(new Vector3(0f,1.6f,-4f))),160f/90f);
        var Orthographic=Matrix4x4.CreateLookAt(Eye,Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreateOrthographic(10f,10f,0.1f,100f);
        Assert.Throws<ArgumentException>(()=>Scene.Renderer.SetViewProjection(Orthographic,Eye,Parameters));
        Assert.Throws<ArgumentException>(()=>Scene.Renderer.SetViewProjection(ViewProjection,Eye,Parameters with { NearZ=0f }));
        Assert.Throws<ArgumentException>(()=>Scene.Renderer.SetViewProjection(ViewProjection,Eye,Parameters with { FarZ=Parameters.NearZ }));
        Assert.Throws<ArgumentException>(()=>Scene.Renderer.SetViewProjection(ViewProjection,Eye,Parameters with { TransitionMeters=float.NaN }));
        Assert.Throws<ArgumentException>(()=>Scene.Renderer.SetViewProjection(ViewProjection,Eye,Parameters with { FovYRadians=0f }));
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void RadialViewProjection_SurvivesDeviceRecovery(){
        var Task=System.Threading.Tasks.Task.Run(static()=>{
            var Roster=RenderServer.BuildPrimitiveRoster([PrimitiveType.Box],0,0);
            using var Window=new Win32Window(new Win32WindowOptions { Title="Radial matrix recovery",Width=128,Height=128,Visible=false });
            using var Renderer=RenderServer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,
                new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,Roster.Meshes,Roster.Materials,Roster.Objects);
            var View=Camera.CreateDefault(Vector3.Zero);
            View.FarZ=1e12f;
            var (ViewProjection,Eye,Parameters)=AsMatrix(View,1f);
            Renderer.SetViewProjection(ViewProjection,Eye,Parameters);
            Renderer.SetAmbientLight(new Vector3(0.1f));
            Renderer.AddDirectionalLight(new DirectionalLightSubmission(Vector3.Normalize(new Vector3(1f,-1f,1f)),White,new(100f),Shadow));
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Renderer.RenderFrame(CaptureForVerification:true);
            var Before=Renderer.LastCapturedFrame!.Pixels.ToArray();
            var DepthBefore=Renderer.ReadDepthForTesting();
            Renderer.RemoveDeviceForTesting();
            Renderer.RenderFrame(CaptureForVerification:true);
            Assert.Equal(2u,Renderer.DeviceSnapshot.Generation);
            Assert.Equal(Before,Renderer.LastCapturedFrame!.Pixels);
            Assert.Equal(DepthBefore,Renderer.ReadDepthForTesting());
            // The box 4.5 m away: radial depth log2(1 + 4.5) / log2(1 + rMax) is about 0.06; standard depth with a 1e12 m
            // far plane would be about 0.98.
            var Center=DepthBefore[64*128+64];
            Assert.True(Center>0f&&Center<0.2f,$"centre depth {Center}");
            Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
        });
        Assert.True(Task.Wait(TimeSpan.FromSeconds(60)));
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void RadialCamera_RendersIdenticalPixelsAfterDeviceRecovery(){
        var Task=System.Threading.Tasks.Task.Run(static()=>{
            var Roster=RenderServer.BuildPrimitiveRoster([PrimitiveType.Box],0,0);
            using var Window=new Win32Window(new Win32WindowOptions { Title="Radial depth recovery",Width=128,Height=128,Visible=false });
            using var Renderer=RenderServer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,
                new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,Roster.Meshes,Roster.Materials,Roster.Objects);
            var View=Camera.CreateDefault(Vector3.Zero);
            View.DepthMapping=RenderDepthMapping.Radial;
            View.FarZ=1e12f;
            Renderer.SetCamera(View);
            Renderer.SetAmbientLight(new Vector3(0.1f));
            Renderer.AddDirectionalLight(new DirectionalLightSubmission(Vector3.Normalize(new Vector3(1f,-1f,1f)),White,new(100f),Shadow));
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Renderer.RenderFrame(CaptureForVerification:true);
            var Before=Renderer.LastCapturedFrame!.Pixels.ToArray();
            Renderer.RemoveDeviceForTesting();
            Renderer.RenderFrame(CaptureForVerification:true);
            Assert.Equal(2u,Renderer.DeviceSnapshot.Generation);
            Assert.Equal(Before,Renderer.LastCapturedFrame!.Pixels);
            Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
        });
        Assert.True(Task.Wait(TimeSpan.FromSeconds(60)));
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ObserverPreview_DrawsTheCompressedWorldAndMarksTheCentre(){
        // A sphere of radius 3e5 m, 1e6 m to the side of the radial camera. The observer inset must show it where the
        // CPU compression T·G(r) puts it; the main view outside the inset must not change; the marker is optional.
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Radial Observer",Width=256,Height=144,Visible=false });
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            [ToMesh(ProceduralMeshFactory.CreateSphere(0.5f))],[new StageMaterialCreateInfo { BaseColorFactor=Red,RoughnessFactor=1f,MetallicFactor=0f }],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        var Center=Radial(Standard(Vector3.Zero,FarZ:1e7f));
        Assert.True(RadialDepthProjection.TryCreate(RadialDepthCurve.Default,Center.NearZ,Center.FarZ,Center.FovYRadians,256f/144f,out var Projection));
        var Eye=new Vector3(0f,0f,-60f);
        var ObserverView=Camera.CreateFirstPerson(Vector3.Zero).GetView()*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3f,1f,0.1f,60f+1.05f*Projection.ObservedWorldRadius);
        (int X,int Y,int Size)Inset=(128,0,128);
        byte[]Render(RenderCameraPreview[]Previews){
            for(var Frame=0;Frame<2;Frame++){
                Renderer.SetCamera(Center);
                Renderer.SetAmbientLight(new Vector3(1f));
                Renderer.SetObjectTransform(0,Matrix4x4.CreateScale(6e5f)*Matrix4x4.CreateTranslation(1e6f,0f,0f));
                Renderer.SetCameraPreviews(Previews);
                Renderer.RenderFrame(CaptureForVerification:Frame==1);
            }
            return Renderer.LastCapturedFrame!.Pixels.ToArray();
        }
        RenderCameraPreview Observer(bool Mark)=>new(Inset.X,Inset.Y,Inset.Size,Inset.Size,ObserverView,Eye,
            DepthMapping:RenderDepthMapping.RadialObserver,Radial:Projection,RadialCenter:Vector3.Zero,MarkRadialCenter:Mark);
        var Without=Render([]);
        var Marked=Render([Observer(true)]);
        var Unmarked=Render([Observer(false)]);
        var RowPitch=(int)Renderer.LastCapturedFrame!.RowPitch;
        // The sphere centre compresses to T·G(1e6) metres along +X; project it through the observer view.
        var Mapped=new Vector3(Projection.Curve.Map(1e6f),0f,0f)-Eye;
        var Clip=Vector4.Transform(new Vector4(Mapped,1f),ObserverView);
        var PixelX=Inset.X+(int)((Clip.X/Clip.W*0.5f+0.5f)*Inset.Size);
        var PixelY=Inset.Y+(int)((0.5f-Clip.Y/Clip.W*0.5f)*Inset.Size);
        var Offset=PixelY*RowPitch+PixelX*4;
        Assert.True(Unmarked[Offset]>Unmarked[Offset+1]+40,$"pixel ({PixelX},{PixelY}) = {Unmarked[Offset]},{Unmarked[Offset+1]},{Unmarked[Offset+2]}");
        var OutsideChanged=false;
        var MarkerPixels=0;
        for(var Y=0;Y<144;Y++){
            for(var X=0;X<256;X++){
                var Index=Y*RowPitch+X*4;
                var InInset=X>=Inset.X&&X<Inset.X+Inset.Size&&Y>=Inset.Y&&Y<Inset.Y+Inset.Size;
                if(!InInset&&(Without[Index]!=Marked[Index]||Without[Index+1]!=Marked[Index+1]||Without[Index+2]!=Marked[Index+2])){
                    OutsideChanged=true;
                }
                if(InInset&&Marked[Index]>200&&Marked[Index+1]>150&&Marked[Index+2]<100&&!(Unmarked[Index]>200&&Unmarked[Index+1]>150&&Unmarked[Index+2]<100)){
                    MarkerPixels++;
                }
            }
        }
        Assert.False(OutsideChanged);
        Assert.True(MarkerPixels>0,"The centre-camera marker must be drawn.");
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    private static StageMeshCreateInfo ToMesh(ProceduralMeshData Data){
        var Tangents=new Vector4[Data.Positions.Length];
        for(var Index=0;Index<Tangents.Length;Index++){
            var Normal=Data.Normals[Index];
            var Reference=MathF.Abs(Normal.Y)<0.99f?Vector3.UnitY:Vector3.UnitX;
            Tangents[Index]=new Vector4(Vector3.Normalize(Vector3.Cross(Reference,Normal)),1f);
        }
        return new StageMeshCreateInfo { Positions=Data.Positions,Normals=Data.Normals,Tangents=Tangents,Uvs=new Vector2[Data.Positions.Length],Indices=Data.Indices };
    }
    /// <summary>A grid floor, floating objects, one straddling the near plane and one beyond a 40 m far plane.</summary>
    private sealed class FloatingScene:IDisposable {
        private FloatingScene(Win32Window Window,StageRenderer Renderer,bool Environment,bool Clipped){
            this.Window=Window;
            this.Renderer=Renderer;
            this.Environment=Environment;
            this.Clipped=Clipped;
        }
        public Win32Window Window { get; }
        public StageRenderer Renderer { get; }
        public bool Environment { get; }
        /// <summary>
        /// True keeps the floor (crossing the eye and far planes) and the box straddling the near plane; false moves the
        /// floor behind the camera and the box into view, so no triangle is clipped.
        /// </summary>
        public bool Clipped { get; }
        public static FloatingScene Create(bool Environment,bool Clipped=true){
            var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Radial Depth Test",Width=192,Height=108,Visible=false });
            StageMeshCreateInfo[]Meshes=[
                ToMesh(ProceduralMeshFactory.CreatePlane(Width:400f,Depth:400f)),
                ToMesh(ProceduralMeshFactory.CreateBox(new Vector3(1f,1f,1f))),
                ToMesh(ProceduralMeshFactory.CreateSphere(0.5f)),
                ToMesh(ProceduralMeshFactory.CreateBox(new Vector3(8f,4f,0.2f))),
            ];
            StageMaterialCreateInfo[]Materials=[
                new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.18f,0.18f,0.18f,1f),RoughnessFactor=1f,MetallicFactor=0f,
                    WorldGrid=new StageWorldGrid(1f,5,5f/512f,new Vector3(0.026f),new Vector3(0.0885f)) },
                new StageMaterialCreateInfo { BaseColorFactor=Red,RoughnessFactor=0.6f,MetallicFactor=0f },
                new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.9f,0.8f,0.5f,1f),RoughnessFactor=0.3f,MetallicFactor=1f },
                new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.6f,0.6f,0.65f,1f),RoughnessFactor=0.9f,MetallicFactor=0f },
            ];
            StageObjectCreateInfo[]Objects=[
                new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },
                new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=1 },
                new StageObjectCreateInfo { MeshIndex=2,MaterialIndex=2 },
                new StageObjectCreateInfo { MeshIndex=3,MaterialIndex=3 },
                new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=2 },
                new StageObjectCreateInfo { MeshIndex=3,MaterialIndex=1 },
            ];
            var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,Meshes,Materials,Objects);
            Assert.True(Renderer.ShaderArtifactLoaded,string.Join(System.Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
            return new FloatingScene(Window,Renderer,Environment,Clipped);
        }
        /// <summary>Renders from a new renderer, so temporal state (GI history) cannot leak between compared frames.</summary>
        public static byte[]RenderFresh(Camera View,bool Environment,int Frames=4,bool Clipped=true)=>
            RenderFresh(Renderer=>Renderer.SetCamera(View),Environment,Frames,Clipped);
        public static byte[]RenderFresh(Action<StageRenderer>SetView,bool Environment,int Frames=4,bool Clipped=true){
            using var Scene=Create(Environment,Clipped);
            var Pixels=Scene.Render(SetView,Frames);
            Scene.AssertNoDebugLayerErrors();
            return Pixels;
        }
        public byte[]Render(Camera View,int Frames=2)=>this.Render(Renderer=>Renderer.SetCamera(View),Frames);
        /// <summary>Renders with the view <paramref name="SetView"/> sets each frame (a camera or a view-projection).</summary>
        public byte[]Render(Action<StageRenderer>SetView,int Frames=2){
            for(var Frame=0;Frame<Frames;Frame++){
                SetView(this.Renderer);
                if(this.Environment){
                    this.Renderer.SetEnvironmentLighting(new EnvironmentLightingSettings(
                        SkyAtmosphereSettings.Earth,new SkyLightSettings(1f,Vector3.Zero),
                        new PhysicalHeightFogSettings(0.02f,0.2f,0f,new Vector3(0.4f,0.5f,0.6f),1f,1f),
                        new ExposureSettings(ExposureMode.PhysicalCamera,14f,0f),ToneMapper.UnrealFilmicSrgb,0.675f,0.0064f,
                        new GlobalIlluminationSettings(new Vector3(0f,1.6f,-0.5f),new Vector3(4f,1.4f,3.5f),2f,64,0.5f,1f),
                        new AmbientOcclusionSettings(1f,0.5f)));
                    this.Renderer.SetCameraAltitude(1.6f);
                    this.Renderer.SetLight(new DirectionalLightSubmission(Vector3.Normalize(new Vector3(0.4f,-0.8f,0.5f)),White,new(100000f),Shadow));
                } else {
                    this.Renderer.SetAmbientLight(new Vector3(0.08f));
                    this.Renderer.Submit(new DirectionalLightSubmission(Vector3.Normalize(new Vector3(0.4f,-0.8f,0.5f)),White,new(3f),Shadow));
                    this.Renderer.Submit(new PointLightSubmission(new Vector3(-1.5f,1.6f,-1f),new(1f,0.5f,0.2f),new(600f),new(8f),Shadow));
                }
                this.Renderer.SetObjectTransform(0,this.Clipped?Matrix4x4.Identity:Matrix4x4.CreateTranslation(0f,-1000f,-1000f));
                this.Renderer.SetObjectTransform(1,Matrix4x4.CreateRotationY(0.5f)*Matrix4x4.CreateTranslation(-0.9f,1.2f,0f));
                this.Renderer.SetObjectTransform(2,Matrix4x4.CreateTranslation(0.9f,0.9f,-0.6f));
                this.Renderer.SetObjectTransform(3,Matrix4x4.CreateTranslation(0f,2.5f,3f));
                // Straddles the near plane at the right edge of the view.
                this.Renderer.SetObjectTransform(4,Matrix4x4.CreateTranslation(0.6f,1.3f,this.Clipped?-3.6f:-1.5f));
                // Beyond the 40 m far plane.
                this.Renderer.SetObjectTransform(5,Matrix4x4.CreateTranslation(0f,2f,60f));
                this.Renderer.RenderFrame(CaptureForVerification:Frame==Frames-1);
            }
            return this.Renderer.LastCapturedFrame!.Pixels.ToArray();
        }
        public void AssertNoDebugLayerErrors()=>
            Assert.DoesNotContain(this.Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
        public void Dispose(){
            this.Renderer.Dispose();
            this.Window.Dispose();
        }
    }
    /// <summary>Quads facing the camera (which looks along +Z from the origin), lit only by the flat ambient term.</summary>
    private sealed class QuadScene:IDisposable {
        private QuadScene(Win32Window Window,StageRenderer Renderer,int QuadCount){
            this.Window=Window;
            this.Renderer=Renderer;
            this.QuadCount=QuadCount;
        }
        public Win32Window Window { get; }
        public StageRenderer Renderer { get; }
        public int QuadCount { get; }
        public static QuadScene Create(Vector4[]Colors){
            var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Radial Depth Quads",Width=160,Height=90,Visible=false });
            StageMeshCreateInfo[]Meshes=[ToMesh(ProceduralMeshFactory.CreatePlane(Width:1f,Depth:1f))];
            var Materials=Colors.Select(static Color=>new StageMaterialCreateInfo { BaseColorFactor=Color,RoughnessFactor=1f,MetallicFactor=0f }).ToArray();
            var Objects=Enumerable.Range(0,Colors.Length).Select(static Index=>new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=Index }).ToArray();
            var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,Meshes,Materials,Objects);
            Assert.True(Renderer.ShaderArtifactLoaded,string.Join(System.Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
            return new QuadScene(Window,Renderer,Colors.Length);
        }
        /// <summary>A square of the given size facing the camera at the given distance.</summary>
        public static Matrix4x4 Facing(float Distance,float Size)=>
            Matrix4x4.CreateScale(Size,1f,Size)*Matrix4x4.CreateRotationX(-MathF.PI/2f)*Matrix4x4.CreateTranslation(0f,0f,Distance);
        public byte[]Render(Camera View,Matrix4x4[]Transforms,Action<StageRenderer>?Setup=null){
            for(var Frame=0;Frame<2;Frame++){
                this.Renderer.SetCamera(View);
                this.Renderer.SetAmbientLight(new Vector3(1f));
                Setup?.Invoke(this.Renderer);
                for(var Index=0;Index<this.QuadCount;Index++){
                    this.Renderer.SetObjectTransform(Index,Transforms[Index]);
                }
                this.Renderer.RenderFrame(CaptureForVerification:Frame==1);
            }
            return this.Renderer.LastCapturedFrame!.Pixels.ToArray();
        }
        /// <summary>The fraction of pixels in the rectangle (the whole frame by default) where red dominates green.</summary>
        public float RedFraction(byte[]Pixels,int Left=0,int Top=0,int Width=-1,int Height=-1){
            var Frame=this.Renderer.LastCapturedFrame!;
            Width=Width<0?Frame.Width:Width;
            Height=Height<0?Frame.Height:Height;
            var Red=0;
            for(var Y=Top;Y<Top+Height;Y++){
                for(var X=Left;X<Left+Width;X++){
                    var Offset=(int)(Y*Frame.RowPitch)+X*4;
                    if(Pixels[Offset]>Pixels[Offset+1]+40){ Red++; }
                }
            }
            return Red/(float)(Width*Height);
        }
        public void AssertNoDebugLayerErrors()=>
            Assert.DoesNotContain(this.Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
        public void Dispose(){
            this.Renderer.Dispose();
            this.Window.Dispose();
        }
    }
}
