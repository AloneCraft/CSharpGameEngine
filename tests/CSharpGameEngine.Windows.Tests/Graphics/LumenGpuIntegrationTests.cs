using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using System.Numerics;
using System.Security.Cryptography;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using Xunit.Abstractions;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// ADR-0078/M0111 WARP oracles for the Lumen-like dynamic GI and reflections. The scenes are rendered in world coordinates (the render origin is the
/// world's) at the High scalability (the hardware default); the readbacks are physical irradiance and radiance, so the exposure does not enter the
/// numbers.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class LumenGpuIntegrationTests(ITestOutputHelper Output) {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static readonly Vector3 SunTowardLight=Vector3.Normalize(new Vector3(0.3f,1f,-0.4f));
    /// <summary>Objects of <see cref="Room"/>: 0 floor (white), 1 wall at x = -5 (red, sunlit), 2 wall at x = 5 (green, shaded), 3 back wall (grey), 4 a box (white).</summary>
    private static readonly Matrix4x4[]RoomTransforms=[
        Matrix4x4.CreateScale(10f,0.2f,12f)*Matrix4x4.CreateTranslation(0f,-0.1f,0f),
        Matrix4x4.CreateScale(0.2f,4f,12f)*Matrix4x4.CreateTranslation(-5f,2f,0f),
        Matrix4x4.CreateScale(0.2f,4f,12f)*Matrix4x4.CreateTranslation(5f,2f,0f),
        Matrix4x4.CreateScale(10f,4f,0.2f)*Matrix4x4.CreateTranslation(0f,2f,6f),
        Matrix4x4.CreateTranslation(0f,0.5f,1f),
    ];
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ProbeVolumeTier_KeepsEveryPixelWhateverTheLumenSettings(){
        // Below High, Lumen's settings are read but the frame is exactly the probe volume and sky light frame of M0110.
        string Hash(LumenSettings?Lumen){
            using var Scene=LumenScene.Room(Scalability:RenderScalability.Medium);
            for(var Frame=0;Frame<3;Frame++){
                Scene.Render(Lumen,Capture:Frame==2);
            }
            Assert.False(Scene.Renderer.LumenActiveLastFrame);
            Scene.AssertNoDebugLayerErrors();
            return Convert.ToHexString(SHA256.HashData(Scene.Renderer.LastCapturedFrame!.Pixels));
        }
        Assert.Equal(Hash(null),Hash(LumenSettings.Unreal));
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Lumen_WithoutAGlobalDistanceFieldFallsBackToTheProbeVolumeFrame(){
        string Hash(LumenSettings?Lumen,RenderScalability Scalability){
            using var Scene=LumenScene.Room(Scalability:Scalability);
            for(var Frame=0;Frame<2;Frame++){
                Scene.Render(Lumen,Capture:Frame==1,Field:false);
            }
            Assert.False(Scene.Renderer.LumenActiveLastFrame);
            return Convert.ToHexString(SHA256.HashData(Scene.Renderer.LastCapturedFrame!.Pixels));
        }
        Assert.Equal(Hash(null,RenderScalability.Medium),Hash(LumenSettings.Unreal,RenderScalability.High));
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Lumen_SurfaceCacheCapturesAndLightsEveryCard(){
        using var Scene=LumenScene.Room();
        Scene.Render(LumenSettings.Unreal);
        var First=Scene.Renderer.LumenStatistics;
        Output.WriteLine($"first frame: objects {First.Objects} cards {First.Cards} captures {First.Captures} lit {First.LitCards}/{First.LitTexels} probes {First.TracedProbes}");
        Assert.True(Scene.Renderer.LumenActiveLastFrame);
        Assert.Equal(5,First.Objects);
        Assert.Equal(30,First.Cards);
        Assert.Equal(30,First.Captures);
        Assert.Equal(30,First.LitCards);
        Scene.Render(LumenSettings.Unreal);
        Assert.Equal(0,Scene.Renderer.LumenStatistics.Captures);
        // The sunlit top of the floor is (albedo 0.8) x (the sun through the atmosphere on the floor, plus the sky) / pi; its underside sees neither.
        var Top=Scene.Renderer.ReadLumenCardForTesting(0,2);
        var Bottom=Scene.Renderer.ReadLumenCardForTesting(0,3);
        var Sun=0.8f*100000f*Vector3.Dot(SunTowardLight,Vector3.UnitY)/MathF.PI;
        Output.WriteLine($"floor top {Top.Average} ({Top.Covered} texels), underside {Bottom.Average}, sun alone above the atmosphere {Sun:F0}");
        Assert.True(Top.Covered>1000);
        Assert.InRange(Top.Average.Y,Sun*0.7f,Sun*1.4f);
        Assert.True(Bottom.Average.Y<Top.Average.Y*0.4f);
        // The red wall's sunlit face is red, the green wall's shaded face green.
        var Red=Scene.Renderer.ReadLumenCardForTesting(1,0).Average;
        var Green=Scene.Renderer.ReadLumenCardForTesting(2,1).Average;
        Assert.True(Red.X>5f*Red.Y);
        Assert.True(Green.Y>5f*Green.X);
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Lumen_ColourBleedsFromTheSunlitRedWallOntoTheFloor(){
        using var Scene=LumenScene.Room();
        for(var Frame=0;Frame<10;Frame++){
            Scene.Render(LumenSettings.Unreal);
        }
        var (Diffuse,Width,Height)=Scene.Renderer.ReadLumenDiffuseForTesting();
        // +X is on the screen's left: the green wall's side is the left of the floor, the red wall's the right.
        var Row=Height*3/4;
        var NearGreen=Diffuse[Row*Width+Width/16];
        var NearRed=Diffuse[Row*Width+Width*15/16];
        var GreenRatio=NearGreen.X/NearGreen.Y;
        var RedRatio=NearRed.X/NearRed.Y;
        Output.WriteLine($"floor irradiance near the green wall {NearGreen}, near the red wall {NearRed}: R/G {GreenRatio:F3} vs {RedRatio:F3}");
        Assert.True(RedRatio>1.5f*GreenRatio);
        Assert.True(RedRatio>1.2f);
        AssertFinite(Diffuse);
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Lumen_ConvergesWithinTwentyFramesAndIsDeterministic(){
        float[]Run(out Vector4[]Final){
            using var Scene=LumenScene.Room();
            var Means=new float[40];
            Vector4[]Diffuse=[];
            for(var Frame=0;Frame<Means.Length;Frame++){
                Scene.Render(LumenSettings.Unreal);
                (Diffuse,var Width,var Height)=Scene.Renderer.ReadLumenDiffuseForTesting();
                var Total=0f;
                var Count=0;
                for(var Y=Height/2;Y<Height;Y++){
                    for(var X=0;X<Width;X++){
                        var Texel=Diffuse[Y*Width+X];
                        if(Texel.W>0f){
                            Total+=Texel.X+Texel.Y+Texel.Z;
                            Count++;
                        }
                    }
                }
                Means[Frame]=Total/Math.Max(Count,1);
            }
            Scene.AssertNoDebugLayerErrors();
            Final=Diffuse;
            return Means;
        }
        var Means=Run(out var First);
        // Within 5 % of the 40th frame (the bounces between the surface cache and the radiance cache keep adding a little after that).
        var Settled=Array.FindLastIndex(Means,Mean=>MathF.Abs(Mean-Means[^1])>0.05f*Means[^1])+1;
        var Tight=Array.FindLastIndex(Means,Mean=>MathF.Abs(Mean-Means[^1])>0.02f*Means[^1])+1;
        Output.WriteLine($"mean irradiance per frame: {string.Join(" ",Means.Select(Mean=>Mean.ToString("F0")))}; within 5 % of the last from frame {Settled}, within 2 % from frame {Tight}");
        Assert.InRange(Settled,1,20);
        Run(out var Second);
        Assert.Equal(First,Second);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Lumen_LocalLightAndEmissionReachTheIndirectLight(){
        // No sun and no sky. A point light by the red wall: the floor's far side (by the green wall) gets the red wall's bounce.
        using(var Lamp=LumenScene.Room()){
            for(var Frame=0;Frame<12;Frame++){
                Lamp.Render(LumenSettings.Unreal,Sun:false,Sky:false,Prepare:Renderer=>Renderer.AddPointLight(new Vector3(-4f,2f,0f),Vector3.One,20000f,8f));
            }
            var RedCard=Lamp.Renderer.ReadLumenCardForTesting(1,0).Average;
            var (Lit,Width,Height)=Lamp.Renderer.ReadLumenDiffuseForTesting();
            var FloorNearGreen=Lit[(Height*3/4)*Width+Width/16];
            Output.WriteLine($"point light: red wall card {RedCard}, floor by the green wall {FloorNearGreen}");
            Assert.True(RedCard.X>100f&&RedCard.X>5f*RedCard.Y);
            Assert.True(FloorNearGreen.X>FloorNearGreen.Z*1.2f&&FloorNearGreen.X>1f);
            AssertFinite(Lit);
            Lamp.AssertNoDebugLayerErrors();
        }
        // A blue glowing box is the only light; the object's emissive scale (M0109's brake light switch) turns its bounce off and on.
        using var Glow=LumenScene.Room(BoxEmissiveNits:new Vector3(0f,0f,2000f));
        (Vector4[]Texels,int Width,int Height)Frames(float Scale,int Count){
            for(var Frame=0;Frame<Count;Frame++){
                Glow.Renderer.SetObjectEmissiveScale(4,Scale);
                Glow.Render(LumenSettings.Unreal,Sun:false,Sky:false);
            }
            return Glow.Renderer.ReadLumenDiffuseForTesting();
        }
        var On=Frames(1f,12);
        var Front=LumenScene.FloorPixel(new Vector3(0f,0f,0.2f),On.Width,On.Height);
        var Off=Frames(0f,12);
        // High refreshes 256 radiance cache probes a frame, so the bounce comes back over about twenty frames.
        var Again=Frames(1f,24);
        Output.WriteLine($"emissive box: floor in front of it {On.Texels[Front]}, emission off {Off.Texels[Front]}, on again {Again.Texels[Front]}");
        Assert.True(On.Texels[Front].Z>On.Texels[Front].X*3f&&On.Texels[Front].Z>10f);
        Assert.True(Off.Texels[Front].Z<On.Texels[Front].Z*0.35f);
        Assert.InRange(Again.Texels[Front].Z,On.Texels[Front].Z*0.8f,On.Texels[Front].Z*1.25f);
        AssertFinite(Off.Texels);
        Glow.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Reflections_MirrorFloorShowsTheBoxWhereGeometryPutsItAndRoughnessBlursIt(){
        var Peaks=new List<float>();
        foreach(var Roughness in new[]{ 0.045f,0.25f,0.8f }){
            foreach(var ScreenTraces in Roughness==0.045f?new[]{ true,false }:new[]{ true }){
                using var Scene=LumenScene.Mirror(Roughness);
                var Settings=LumenSettings.Unreal with { ReflectionScreenTraces=ScreenTraces };
                for(var Frame=0;Frame<10;Frame++){
                    Scene.Render(Settings,View:LumenScene.MirrorView);
                }
                var (Reflection,Width,Height)=Scene.Renderer.ReadLumenReflectionForTesting();
                var Expected=LumenScene.ExpectedMirrorBoxPixels(Width,Height);
                var Actual=new bool[Width*Height];
                var Peak=0f;
                for(var Index=0;Index<Actual.Length;Index++){
                    var Texel=Reflection[Index];
                    var Redness=Texel.X/MathF.Max(Texel.Y+Texel.Z,1e-3f);
                    Actual[Index]=Texel.X>2f*MathF.Max(Texel.Y,Texel.Z);
                    if(Expected[Index]){
                        Peak=MathF.Max(Peak,Redness);
                    }
                }
                var Intersection=Expected.Zip(Actual,(A,B)=>A&&B).Count(Value=>Value);
                var Union=Expected.Zip(Actual,(A,B)=>A||B).Count(Value=>Value);
                var Iou=Intersection/(float)Math.Max(Union,1);
                var Error=CentroidDistance(Expected,Actual,Width);
                Output.WriteLine($"roughness {Roughness} screen traces {ScreenTraces}: expected {Expected.Count(Value=>Value)} px, red {Actual.Count(Value=>Value)} px, IoU {Iou:F2}, centroid error {Error:F2} px, peak redness {Peak:F2}");
                if(Roughness==0.045f){
                    // The mirror shows the box where the reflection of its geometry is, through the screen or (off) through the surface cache.
                    Assert.True(Iou>(ScreenTraces?0.6f:0.3f),$"IoU {Iou}");
                    Assert.True(Error<(ScreenTraces?2f:4f),$"centroid error {Error}");
                }
                if(ScreenTraces){
                    Peaks.Add(Peak);
                }
                AssertFinite(Reflection);
                Scene.AssertNoDebugLayerErrors();
            }
        }
        // The rougher the floor, the less sharply red the box's reflection (0.8 is past Unreal's 0.4 limit: the probes' lobe).
        Assert.True(Peaks[0]>Peaks[1]&&Peaks[1]>Peaks[2],string.Join(",",Peaks));
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Lumen_FollowsAMovingBox(){
        var View=LumenScene.RoomView;
        // A red glowing box under the sky moves 2.5 m: its red bounce leaves the floor in front of its old place and appears in front of the new one,
        // and after a while the moved scene matches one that started there (the cards are in the box's own space and follow it).
        using var Moving=LumenScene.Room(BoxEmissiveNits:new Vector3(3000f,0f,0f));
        for(var Frame=0;Frame<12;Frame++){
            Moving.Render(LumenSettings.Unreal,Sun:false,View:View);
        }
        var Before=Moving.Renderer.ReadLumenDiffuseForTesting();
        var Moved=Matrix4x4.CreateTranslation(-2.5f,0.5f,1f);
        for(var Frame=0;Frame<16;Frame++){
            Moving.Render(LumenSettings.Unreal,Sun:false,View:View,Box:Moved);
        }
        var After=Moving.Renderer.ReadLumenDiffuseForTesting();
        using var Fresh=LumenScene.Room(BoxEmissiveNits:new Vector3(3000f,0f,0f));
        for(var Frame=0;Frame<28;Frame++){
            Fresh.Render(LumenSettings.Unreal,Sun:false,View:View,Box:Moved);
        }
        var Reference=Fresh.Renderer.ReadLumenDiffuseForTesting();
        var OldPlace=LumenScene.FloorPixel(new Vector3(0f,0f,0.2f),Before.Width,Before.Height);
        var NewPlace=LumenScene.FloorPixel(new Vector3(-2.5f,0f,0.2f),Before.Width,Before.Height);
        float Red(Vector4 Value)=>Value.X-0.5f*(Value.Y+Value.Z);
        Output.WriteLine($"red bounce at the old place {Red(Before.Texels[OldPlace]):F0} -> {Red(After.Texels[OldPlace]):F0} (fresh {Red(Reference.Texels[OldPlace]):F0}); at the new place {Red(Before.Texels[NewPlace]):F0} -> {Red(After.Texels[NewPlace]):F0} (fresh {Red(Reference.Texels[NewPlace]):F0})");
        Assert.True(Red(Before.Texels[OldPlace])>1.3f*Red(Before.Texels[NewPlace]));
        Assert.True(Red(After.Texels[OldPlace])<0.7f*Red(Before.Texels[OldPlace]));
        Assert.True(Red(After.Texels[NewPlace])>1.2f*Red(Before.Texels[NewPlace]));
        Assert.InRange(Red(After.Texels[NewPlace]),Red(Reference.Texels[NewPlace])*0.75f,Red(Reference.Texels[NewPlace])*1.25f);
        Assert.InRange(Red(After.Texels[OldPlace]),Red(Reference.Texels[OldPlace])*0.75f,Red(Reference.Texels[OldPlace])*1.25f);
        Moving.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Lumen_BoundaryScenesStayFiniteAndClean(){
        // No light at all: nothing to gather.
        using(var Dark=LumenScene.Room()){
            for(var Frame=0;Frame<4;Frame++){
                Dark.Render(LumenSettings.Unreal,Sun:false,Sky:false);
            }
            var (Diffuse,_,_)=Dark.Renderer.ReadLumenDiffuseForTesting();
            Assert.All(Diffuse,Texel=>Assert.True(Texel.X+Texel.Y+Texel.Z<1e-3f));
            Dark.AssertNoDebugLayerErrors();
        }
        // No walls: the open floor's indirect light is the sky (the physical sky needs the sun) and the box; away from the box it is the sky light's.
        using(var Open=LumenScene.Room(Walls:false)){
            for(var Frame=0;Frame<12;Frame++){
                Open.Render(LumenSettings.Unreal);
            }
            var (Diffuse,Width,Height)=Open.Renderer.ReadLumenDiffuseForTesting();
            var Floor=Diffuse[LumenScene.FloorPixel(new Vector3(-3f,0f,-3f),Width,Height)];
            var Sh=Open.Renderer.ReadAtmosphereForTesting().IrradianceSh;
            // Up (+Y) in the sky light's SH: the constant and the y band (0.282095 and 0.488603), the zonal y^2 band of x^2 - y^2 and 3z^2 - 1 terms.
            var SkyUp=Sh[0]*0.282095f+Sh[1]*0.488603f+Sh[6]*(-0.315392f)+Sh[8]*(-0.546274f);
            Output.WriteLine($"open floor irradiance {Floor}, the sky light's irradiance up {SkyUp}");
            Assert.InRange(Floor.Z,SkyUp.Z*0.75f,SkyUp.Z*1.15f);
            Assert.InRange(Floor.Y,SkyUp.Y*0.75f,SkyUp.Y*1.15f);
            AssertFinite(Diffuse);
            Open.AssertNoDebugLayerErrors();
        }
        // A not-finite object, a far object and meshes without a distance field: left out of the cards, the frame stays finite.
        using(var Odd=LumenScene.Room(WithField:false)){
            for(var Frame=0;Frame<3;Frame++){
                Odd.Render(LumenSettings.Unreal,Box:new Matrix4x4(float.NaN,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1));
            }
            Assert.True(Odd.Renderer.LumenActiveLastFrame);
            Assert.Equal(0,Odd.Renderer.LumenStatistics.Cards);
            AssertFinite(Odd.Renderer.ReadLumenDiffuseForTesting().Texels);
            AssertFinite(Odd.Renderer.ReadLumenReflectionForTesting().Texels);
            Odd.AssertNoDebugLayerErrors();
        }
        using(var Huge=LumenScene.Room()){
            for(var Frame=0;Frame<3;Frame++){
                Huge.Render(LumenSettings.Unreal,Box:Matrix4x4.CreateScale(1e5f)*Matrix4x4.CreateTranslation(0f,0f,1e6f));
            }
            Assert.Equal(4,Huge.Renderer.LumenStatistics.Objects);
            AssertFinite(Huge.Renderer.ReadLumenDiffuseForTesting().Texels);
            Huge.AssertNoDebugLayerErrors();
        }
    }
    private static void AssertFinite(Vector4[]Texels){
        Assert.All(Texels,Texel=>Assert.True(float.IsFinite(Texel.X)&&float.IsFinite(Texel.Y)&&float.IsFinite(Texel.Z)&&float.IsFinite(Texel.W)));
    }
    private static float CentroidDistance(bool[]Expected,bool[]Actual,int Width){
        Vector2 Centroid(bool[]Mask){
            var Sum=Vector2.Zero;
            var Count=0;
            for(var Index=0;Index<Mask.Length;Index++){
                if(Mask[Index]){
                    Sum+=new Vector2(Index%Width,Index/Width);
                    Count++;
                }
            }
            return Count>0?Sum/Count:new Vector2(float.NaN);
        }
        return Vector2.Distance(Centroid(Expected),Centroid(Actual));
    }
    internal sealed class LumenScene:IDisposable {
        private const int Width=160;
        private const int Height=90;
        internal static readonly Camera RoomView=CreateView(new Vector3(0f,1.6f,-6f),-0.15f);
        internal static readonly Camera MirrorView=CreateView(new Vector3(0f,1.5f,-4f),-0.35f);
        private readonly Win32Window Window;
        private readonly bool Walls;
        internal StageRenderer Renderer { get; }
        private LumenScene(Win32Window Window,StageRenderer Renderer,bool Walls){
            this.Window=Window;
            this.Renderer=Renderer;
            this.Walls=Walls;
        }
        private static Camera CreateView(Vector3 Position,float Pitch){
            var View=Camera.CreateFirstPerson(Position);
            View.Pitch=Pitch;
            return View;
        }
        private static StageMaterialCreateInfo Material(Vector3 Color,float Roughness=0.9f,float Metallic=0f,Vector3 Emissive=default)=>
            new() { BaseColorFactor=new Vector4(Color,1f),RoughnessFactor=Roughness,MetallicFactor=Metallic,EmissiveNits=Emissive };
        internal static LumenScene Room(RenderScalability?Scalability=null,bool WithField=true,bool Walls=true,Vector3 BoxEmissiveNits=default)=>
            Create([Material(new Vector3(0.8f)),Material(new Vector3(0.8f,0.05f,0.05f)),Material(new Vector3(0.05f,0.8f,0.05f)),Material(new Vector3(0.6f)),Material(new Vector3(0.8f),Emissive:BoxEmissiveNits)],
                Scalability,WithField,Walls);
        /// <summary>A metal floor of the given roughness, a red box on it and the room's walls (grey).</summary>
        internal static LumenScene Mirror(float Roughness)=>
            Create([Material(new Vector3(0.95f),Roughness,1f),Material(new Vector3(0.6f)),Material(new Vector3(0.6f)),Material(new Vector3(0.6f)),Material(new Vector3(0.8f,0.05f,0.05f))],null,true,true);
        private static LumenScene Create(StageMaterialCreateInfo[]Materials,RenderScalability?Scalability,bool WithField,bool Walls){
            var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Lumen",Width=Width,Height=Height,Visible=false });
            var Box=ProceduralMeshFactory.CreateBox(Vector3.One);
            var Mesh=new StageMeshCreateInfo { Positions=Box.Positions,Normals=Box.Normals,Tangents=Box.Normals.Select(StageMeshGeometry.ArbitraryTangentFor).ToArray(),
                Uvs=new Vector2[Box.Positions.Length],Indices=Box.Indices,DistanceField=WithField?MeshDistanceFieldBuilder.Build(Box.Positions,Box.Indices):null };
            var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
                [Mesh],Materials,[.. Enumerable.Range(0,5).Select(Index=>new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=Index })]);
            // High, the hardware default. Epic on WARP intermittently lost the device in a long test run (M0111, unresolved).
            Renderer.Scalability=Scalability??RenderScalability.High;
            return new LumenScene(Window,Renderer,Walls);
        }
        internal void Render(LumenSettings?Lumen,bool Capture=false,bool Sun=true,bool Sky=true,bool Field=true,Matrix4x4?Box=null,Camera?View=null,Action<StageRenderer>?Prepare=null){
            this.Renderer.SetCamera(View??RoomView);
            this.Renderer.SetEnvironmentLighting(new EnvironmentLightingSettings(
                Sky?SkyAtmosphereSettings.Earth:null,Sky?new SkyLightSettings(1f,Vector3.Zero):null,null,
                new ExposureSettings(ExposureMode.PhysicalCamera,14f,0f),ToneMapper.UnrealFilmicSrgb,0.675f,0.0064f,
                DistanceField:Field?new GlobalDistanceFieldSettings(0.5f,3,32,default,Vector3.Zero):null,
                Lumen:Lumen));
            this.Renderer.SetCameraAltitude(1.6f);
            this.Renderer.SetAmbientLight(Vector3.Zero);
            if(Sun){
                this.Renderer.SetDirectionalLight(SunTowardLight,Vector3.One,100000f);
            } else {
                this.Renderer.SetDirectionalLight(SunTowardLight,Vector3.One,0f);
            }
            for(var Index=0;Index<4;Index++){
                var Hidden=!this.Walls&&Index>0;
                this.Renderer.SetObjectTransform(Index,Hidden?Matrix4x4.CreateTranslation(0f,-1000f,0f):RoomTransforms[Index]);
            }
            this.Renderer.SetObjectTransform(4,Box??RoomTransforms[4]);
            Prepare?.Invoke(this.Renderer);
            try {
                this.Renderer.RenderFrame(CaptureForVerification:Capture);
            } catch(D3D12Exception Exception){
                throw new InvalidOperationException($"{Exception.Message} (removed reason 0x{this.Renderer.DeviceRemovedReasonForTesting:X8}): "+string.Join(" | ",this.Renderer.DebugMessages.Take(8)),Exception);
            }
        }
        /// <summary>The pixel (row-major index) where a floor point appears from <see cref="RoomView"/>.</summary>
        internal static int FloorPixel(Vector3 World,int TargetWidth,int TargetHeight){
            var ViewProjection=RoomView.GetView()*RoomView.GetProjection((float)TargetWidth/TargetHeight);
            var Clip=Vector4.Transform(new Vector4(World,1f),ViewProjection);
            var X=(int)((Clip.X/Clip.W*0.5f+0.5f)*TargetWidth);
            var Y=(int)((0.5f-Clip.Y/Clip.W*0.5f)*TargetHeight);
            return Math.Clamp(Y,0,TargetHeight-1)*TargetWidth+Math.Clamp(X,0,TargetWidth-1);
        }
        /// <summary>The pixels of <see cref="MirrorView"/> whose floor point reflects a ray into the red box (and that do not see the box itself).</summary>
        internal static bool[]ExpectedMirrorBoxPixels(int TargetWidth,int TargetHeight){
            var ViewProjection=MirrorView.GetView()*MirrorView.GetProjection((float)TargetWidth/TargetHeight);
            Matrix4x4.Invert(ViewProjection,out var Inverse);
            var Eye=MirrorView.GetEyePosition();
            var Result=new bool[TargetWidth*TargetHeight];
            for(var Y=0;Y<TargetHeight;Y++){
                for(var X=0;X<TargetWidth;X++){
                    var Ndc=new Vector2((X+0.5f)/TargetWidth*2f-1f,1f-(Y+0.5f)/TargetHeight*2f);
                    var Far=Vector4.Transform(new Vector4(Ndc,1f,1f),Inverse);
                    var Direction=Vector3.Normalize(new Vector3(Far.X,Far.Y,Far.Z)/Far.W-Eye);
                    if(Direction.Y>=0f){
                        continue;
                    }
                    var T=-Eye.Y/Direction.Y;
                    var Hit=Eye+Direction*T;
                    if(MathF.Abs(Hit.X)>5f||MathF.Abs(Hit.Z)>6f||RayBox(Eye,Direction,out var BoxT)&&BoxT<T){
                        continue;
                    }
                    var Reflected=Vector3.Reflect(Direction,Vector3.UnitY);
                    Result[Y*TargetWidth+X]=RayBox(Hit+Reflected*1e-3f,Reflected,out _);
                }
            }
            return Result;
        }
        /// <summary>The red box of the scenes: [-0.5, 0.5] x [0, 1] x [0.5, 1.5].</summary>
        private static bool RayBox(Vector3 Origin,Vector3 Direction,out float Distance){
            var Minimum=new Vector3(-0.5f,0f,0.5f);
            var Maximum=new Vector3(0.5f,1f,1.5f);
            var Near=0f;
            var Far=float.MaxValue;
            for(var Axis=0;Axis<3;Axis++){
                var O=Axis==0?Origin.X:Axis==1?Origin.Y:Origin.Z;
                var D=Axis==0?Direction.X:Axis==1?Direction.Y:Direction.Z;
                var Low=Axis==0?Minimum.X:Axis==1?Minimum.Y:Minimum.Z;
                var High=Axis==0?Maximum.X:Axis==1?Maximum.Y:Maximum.Z;
                if(MathF.Abs(D)<1e-8f){
                    if(O<Low||O>High){
                        Distance=0f;
                        return false;
                    }
                    continue;
                }
                var T1=(Low-O)/D;
                var T2=(High-O)/D;
                Near=MathF.Max(Near,MathF.Min(T1,T2));
                Far=MathF.Min(Far,MathF.Max(T1,T2));
            }
            Distance=Near;
            return Near<=Far;
        }
        internal void AssertNoDebugLayerErrors(){
            Assert.True(this.Renderer.ErrorOrWorseMessageCount==0,string.Join(Environment.NewLine,this.Renderer.DebugMessages.Take(10)));
        }
        public void Dispose(){
            this.Renderer.Dispose();
            this.Window.Dispose();
        }
    }
}
