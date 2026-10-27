extern alias PreviewHost;
using System.Numerics;
using CSharpGameEngine.Contracts;
using PreviewHost::CSharpGameEngine.EditorPreview.Host;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Windows.Tests.Runtime;
/// <summary>プロトコル 10：プレビューホストの照明・背景・環境・床（M0127）。WARP で実際に描き、ピクセルで確かめる。</summary>
[Collection(CSharpGameEngine.Graphics.D3D12.Tests.D3D12GpuCollection.Name)]
public sealed class RenderPreviewLightingSessionTests {
    private static string ShaderArtifactPath=>Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Size=96;
    private static readonly GraphicsDeviceOptions Options=new(){ UseWarp=true,EnableDebugLayer=true };
    private sealed class Preview:IDisposable {
        private readonly Win32Window Window;
        internal readonly RenderPreviewRenderSession Session;
        internal readonly RenderPreviewCamera Camera;
        internal readonly RenderPreviewHostRequest OpenRequest;
        internal Preview(bool Floor=false,float FloorSize=40f){
            this.Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Preview Lighting Test",Width=Size,Height=Size,Visible=false });
            this.OpenRequest=CreateOpen(this.Window,Floor,FloorSize);
            this.Camera=this.OpenRequest.Scene!.Camera!;
            this.Session=RenderPreviewRenderSession.Open(this.OpenRequest,Options);
        }
        internal RenderPreviewFrame Frame(RenderPreviewLighting?Lighting,RenderPreviewPresentationMode Mode=RenderPreviewPresentationMode.Lit)=>new(){
            Camera=this.Camera,PresentationMode=Mode,Lighting=Lighting,
            Objects=this.OpenRequest.Scene!.Objects.Select(static Object=>new RenderPreviewObjectTransform { ObjectId=Object.ObjectId,World=Object.World }).ToArray(),
        };
        internal PixelReadbackResult Render(RenderPreviewLighting?Lighting,RenderPreviewPresentationMode Mode=RenderPreviewPresentationMode.Lit,string?Snapshot=null){
            this.Session.UpdateFrame(this.Frame(Lighting,Mode));
            this.Session.RenderForVerification();
            Assert.Equal(0,this.Session.ErrorOrWorseMessageCount);
            if(Snapshot is not null){ PreviewFramePng.SaveIfRequested(this.Session.LastCapturedFrame!,Snapshot); }
            return this.Session.LastCapturedFrame!;
        }
        public void Dispose(){
            this.Session.Dispose();
            this.Window.Dispose();
        }
    }
    private static RenderPreviewHostRequest CreateOpen(Win32Window Window,bool Floor,float FloorSize=40f){
        var Box=ProceduralMeshFactory.CreateBox(Vector3.One);
        var Camera=CSharpGameEngine.Rendering.View.RenderCamera.CreateDefault(Vector3.Zero);
        var ViewProjection=Camera.GetView()*Camera.GetProjection((float)Window.ClientWidth/Window.ClientHeight);
        List<RenderPreviewMesh>Meshes=[new(){ Positions=Flatten(Box.Positions),Normals=Flatten(Box.Normals),Indices=Box.Indices }];
        List<RenderPreviewMaterial>Materials=[new(){ BaseColor=[0.6f,0.6f,0.6f,1f] }];
        List<RenderPreviewObject>Objects=[new(){ ObjectId="AssetPreview",MeshIndex=0,MaterialIndex=0,World=RenderPreviewMatrix4x4.Identity }];
        if(Floor){
            var Plane=ProceduralMeshFactory.CreatePlane(Width:FloorSize,Depth:FloorSize);
            Meshes.Add(new(){ Positions=Flatten(Plane.Positions),Normals=Flatten(Plane.Normals),Indices=Plane.Indices });
            Materials.Add(new(){ BaseColor=[0.5f,0.5f,0.5f,1f],WorldGrid=new(){ GridSizeMeters=1f,SubGridDivisions=4,WireHalfWidth=0.05f,GridColor=[1f,0f,0f],SubGridColor=[0f,0f,1f] } });
            Objects.Add(new(){ ObjectId="PreviewFloor",MeshIndex=1,MaterialIndex=1,World=ToWire(Matrix4x4.CreateTranslation(0f,-1f,0f)) });
        }
        return new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=1,Command=RenderPreviewHostCommand.Open,
            SurfaceHandle=Window.Handle,Width=Window.ClientWidth,Height=Window.ClientHeight,ShaderArtifactPath=ShaderArtifactPath,
            Scene=new RenderPreviewScene {
                Camera=new RenderPreviewCamera {
                    ViewProjection=ToWire(ViewProjection),
                    Position=[Camera.GetEyePosition().X,Camera.GetEyePosition().Y,Camera.GetEyePosition().Z],
                    FovYRadians=Camera.FovYRadians,NearZ=Camera.NearZ,FarZ=Camera.FarZ,
                },
                Meshes=[..Meshes],Materials=[..Materials],Objects=[..Objects],
            },
        };
    }
    private static float[]Flatten(IReadOnlyList<Vector3>Values){
        var Result=new float[Values.Count*3];
        for(var Index=0;Index<Values.Count;Index++){ Result[Index*3]=Values[Index].X;Result[Index*3+1]=Values[Index].Y;Result[Index*3+2]=Values[Index].Z; }
        return Result;
    }
    private static RenderPreviewMatrix4x4 ToWire(in Matrix4x4 Value)=>new(){ Values=[Value.M11,Value.M12,Value.M13,Value.M14,Value.M21,Value.M22,Value.M23,Value.M24,Value.M31,Value.M32,Value.M33,Value.M34,Value.M41,Value.M42,Value.M43,Value.M44] };
    private static (int R,int G,int B)At(PixelReadbackResult Frame,int X,int Y){
        var Offset=(long)Y*Frame.RowPitch+X*4;
        return Frame.Format==RenderPixelFormat.Bgra8Unorm
            ?(Frame.Pixels[Offset+2],Frame.Pixels[Offset+1],Frame.Pixels[Offset])
            :(Frame.Pixels[Offset],Frame.Pixels[Offset+1],Frame.Pixels[Offset+2]);
    }
    private static int Luma((int R,int G,int B)Pixel)=>(Pixel.R*30+Pixel.G*59+Pixel.B*11)/100;
    /// <summary>フレーム全体の平均輝度。背景を黒にした照明で使い、立方体だけの明るさを測る。</summary>
    private static int Brightness(PixelReadbackResult Frame){
        var Total=0;
        for(var Y=0;Y<Size;Y++){ for(var X=0;X<Size;X++){ Total+=Luma(At(Frame,X,Y)); } }
        return Total/(Size*Size);
    }
    /// <summary>テストのカメラから原点（立方体）へ向かう向き。この向きに進む光は、カメラに向いた面を照らす。</summary>
    private static float[]TowardTheSubject(Preview Test){
        var Position=new Vector3(Test.Camera.Position[0],Test.Camera.Position[1],Test.Camera.Position[2]);
        var Direction=Vector3.Normalize(-Position);
        return [Direction.X,Direction.Y,Direction.Z];
    }
    private static RenderPreviewLighting Legacy(int Revision=0)=>new(){
        EnvironmentRevision=Revision,Ambient=[0.4f,0.4f,0.4f],
        Directionals=[new(){ Forward=[0f,1f,0f],IlluminanceLux=2f,CastsShadows=true,ShadowBiasMeters=0.0015f }],
    };
    [Fact]
    public void WARP_LegacyEquivalentLightingMatchesTheLookWithoutAnyLighting(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview();
        var Without=Test.Render(null).Pixels.ToArray();
        var With=Test.Render(Legacy()).Pixels.ToArray();
        Assert.Equal(Without,With);
    }
    [Fact]
    public void WARP_BackgroundOverridesTheClearColorAndNullRestoresTheDefault(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview();
        var Default=At(Test.Render(Legacy(0)),2,2);
        Assert.InRange(Math.Abs(Default.R-RenderPreviewTestColors.DefaultClear.R),0,2);
        var Override=Legacy(1);
        Override.Background=[0.2f,0.4f,0.1f];
        var Changed=At(Test.Render(Override),2,2);
        Assert.InRange(Math.Abs(Changed.R-51),0,3);
        Assert.InRange(Math.Abs(Changed.G-102),0,3);
        Assert.InRange(Math.Abs(Changed.B-26),0,3);
        var Restored=At(Test.Render(Legacy(2)),2,2);
        Assert.Equal(Default,Restored);
    }
    [Fact]
    public void WARP_BackgroundIsNotReappliedWhileTheRevisionIsUnchanged(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview();
        var First=Legacy(5);
        First.Background=[0.9f,0.1f,0.1f];
        Test.Render(First);
        // 同じ版で背景だけ変えても適用されない（永続項目は版が変わったときだけ適用する契約）。
        var Second=Legacy(5);
        Second.Background=[0.1f,0.9f,0.1f];
        var Pixel=At(Test.Render(Second),2,2);
        Assert.True(Pixel.R>200&&Pixel.G<60,$"R={Pixel.R} G={Pixel.G}");
    }
    [Fact]
    public void WARP_LightsDriveTheShadingAndNoDirectionalLightRemovesTheDefaultSun(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview();
        var Dark=new RenderPreviewLighting { EnvironmentRevision=1,Ambient=[0f,0f,0f],Background=[0f,0f,0f] };
        var DarkBrightness=Brightness(Test.Render(Dark,Snapshot:"light-dark"));
        Assert.InRange(DarkBrightness,0,2);
        var Lit=new RenderPreviewLighting { EnvironmentRevision=2,Ambient=[0f,0f,0f],Background=[0f,0f,0f],Directionals=[new(){ Forward=TowardTheSubject(Test),IlluminanceLux=6f }] };
        var LitBrightness=Brightness(Test.Render(Lit,Snapshot:"light-sun"));
        Assert.True(LitBrightness>DarkBrightness+3,$"lit={LitBrightness} dark={DarkBrightness}");
        var Ambient=new RenderPreviewLighting { EnvironmentRevision=3,Ambient=[0.8f,0.8f,0.8f],Background=[0f,0f,0f] };
        Assert.True(Brightness(Test.Render(Ambient,Snapshot:"light-ambient"))>DarkBrightness+3);
    }
    [Fact]
    public void WARP_PointAndSpotLightsAddLightEveryFrameWithoutAccumulating(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview();
        var Eye=Test.Camera.Position;
        RenderPreviewLighting Make(int Revision)=>new(){
            EnvironmentRevision=Revision,Ambient=[0f,0f,0f],Background=[0f,0f,0f],
            Points=[new(){ Position=[Eye[0],Eye[1]+0.3f,Eye[2]],FluxLumens=3000f,RangeMeters=30f }],
        };
        var Dark=Brightness(Test.Render(new RenderPreviewLighting { EnvironmentRevision=1,Ambient=[0f,0f,0f],Background=[0f,0f,0f] }));
        var First=Brightness(Test.Render(Make(2)));
        var Second=Brightness(Test.Render(Make(2)));
        var Third=Brightness(Test.Render(Make(2)));
        Assert.True(First>Dark+2,$"point={First} dark={Dark}");
        Assert.Equal(First,Second);
        Assert.Equal(First,Third);
        var Spot=new RenderPreviewLighting {
            EnvironmentRevision=3,Ambient=[0f,0f,0f],Background=[0f,0f,0f],
            Spots=[new(){ Position=Eye,Forward=TowardTheSubject(Test),FluxLumens=4000f,RangeMeters=30f,InnerConeDegrees=30f,OuterConeDegrees=60f }],
        };
        Assert.True(Brightness(Test.Render(Spot))>Dark+2);
    }
    [Fact]
    public void WARP_AtmosphereEnvironmentDrawsASkyInsteadOfTheClearColor(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview();
        var Plain=At(Test.Render(Legacy(1)),Size/2,2);
        var Sky=Legacy(2);
        Sky.Ambient=[0f,0f,0f];
        Sky.Directionals=[new(){ Forward=[-0.3f,-0.8f,-0.2f],IlluminanceLux=3f,CastsShadows=true }];
        Sky.Environment=new(){ Atmosphere=true,SkyLightIntensity=1f,PhysicalExposure=true,ExposureEv100=0f,ExposureCompensation=1f,FilmicToneMapper=true,BloomIntensity=0.675f };
        var Frame=Test.Render(Sky);
        var Top=At(Frame,Size/2,2);
        Assert.NotEqual(Plain,Top);
        Assert.True(Top.B>=Top.R,$"sky pixel {Top}");
    }
    /// <summary>床の端と地平線のあいだに黒い帯が出ないこと（Outdoor を低い視点から見たときの不具合）。</summary>
    [Fact]
    public void WARP_OutdoorFloorMeetsTheSkyAtTheHorizonWithoutABlackBand(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview(Floor:true,FloorSize:8000f);
        var Eye=new Vector3(0f,0f,8f);
        var View=Matrix4x4.CreateLookAt(Eye,Eye+new Vector3(0f,0f,-1f),Vector3.UnitY);
        var Projection=Matrix4x4.CreatePerspectiveFieldOfView(0.6f,1f,0.05f,8000f);
        var Right=Vector3.Cross(Vector3.UnitY,-Vector3.UnitZ);
        var Rotation=Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(Right.X,Right.Y,Right.Z,0f,0f,1f,0f,0f,0f,0f,-1f,0f,0f,0f,0f,1f)));
        var Camera=new RenderPreviewCamera {
            ViewProjection=ToWire(View*Projection),Position=[Eye.X,Eye.Y,Eye.Z],Rotation=[Rotation.X,Rotation.Y,Rotation.Z,Rotation.W],
            FovYRadians=0.6f,NearZ=0.05f,FarZ=8000f,
        };
        var Sky=Legacy(2);
        Sky.Ambient=[0f,0f,0f];
        Sky.Directionals=[new(){ Forward=[-0.3f,-0.8f,-0.2f],IlluminanceLux=3f,CastsShadows=true }];
        Sky.Environment=new(){ Atmosphere=true,SkyLightIntensity=1f,PhysicalExposure=true,ExposureEv100=0f,ExposureCompensation=1f,FilmicToneMapper=true,BloomIntensity=0.675f };
        var Frame=Test.Frame(Sky);
        Frame.Camera=Camera;
        Test.Session.UpdateFrame(Frame);
        Test.Session.RenderForVerification();
        var Captured=Test.Session.LastCapturedFrame!;
        PreviewFramePng.SaveIfRequested(Captured,"outdoor-horizon");
        var Column=Enumerable.Range(0,Size).Select(Y=>Luma(At(Captured,Size/2+30,Y))).ToArray();
        Assert.True(Column.Take(Size/2-10).All(static Value=>Value>10),$"sky rows: {string.Join(",",Column)}");
        // 上の空と下の床のあいだに、ほぼ黒の行が続かない。
        var Run=0;var Longest=0;
        foreach(var Value in Column.Skip(Size/4)){ Run=Value<12?Run+1:0;Longest=Math.Max(Longest,Run); }
        Assert.True(Longest<=1,$"black rows between sky and floor: {Longest}; column {string.Join(",",Column)}");
    }
    [Fact]
    public void WARP_FloorWorldGridDrawsGridLinesOnTheFloor(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview(Floor:true,FloorSize:40f);
        var Lighting=new RenderPreviewLighting { EnvironmentRevision=1,Ambient=[1f,1f,1f] };
        var Frame=Test.Render(Lighting,RenderPreviewPresentationMode.Lit,"floor-grid");
        var Distinct=new HashSet<(int,int,int)>();
        for(var Y=Size*3/4;Y<Size;Y++){ for(var X=0;X<Size;X++){ Distinct.Add(At(Frame,X,Y)); } }
        Assert.True(Distinct.Count>2,$"the floor shows {Distinct.Count} distinct colours");
    }
    [Fact]
    public void WARP_ReplaceSceneKeepsTheEnvironmentOnTheFirstPresentedFrame(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview();
        var Lighting=Legacy(1);
        Lighting.Background=[0.1f,0.8f,0.2f];
        Test.Render(Lighting);
        Test.Session.ReplaceScene(Test.OpenRequest.Scene!,string.Empty);
        Test.Session.RenderForVerification();
        var Pixel=At(Test.Session.LastCapturedFrame!,2,2);
        Assert.True(Pixel.G>180&&Pixel.R<60,$"after ReplaceScene {Pixel}");
    }
    [Fact]
    public void WARP_TwoPreviewSessionsKeepIndependentEnvironments(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var First=new Preview();
        using var Second=new Preview();
        var A=Legacy(1);
        A.Background=[0.9f,0.1f,0.1f];
        var B=Legacy(1);
        B.Background=[0.1f,0.1f,0.9f];
        var PixelA=At(First.Render(A),2,2);
        var PixelB=At(Second.Render(B),2,2);
        var AgainA=At(First.Render(A),2,2);
        Assert.True(PixelA.R>200&&PixelA.B<60);
        Assert.True(PixelB.B>200&&PixelB.R<60);
        Assert.Equal(PixelA,AgainA);
    }
    [Fact]
    public void WARP_InvalidLightingIsRejectedBeforeItTouchesTheRenderer(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview();
        var Bad=Legacy(1);
        Bad.Directionals[0].Forward=[0f,0f,0f];
        Assert.Throws<ArgumentException>(()=>Test.Session.UpdateFrame(Test.Frame(Bad)));
        // 失敗した後も、次の正しいフレームで描ける。
        Assert.NotNull(Test.Render(Legacy(2)));
    }
    [Fact]
    public void WARP_MissingAtmosphereArtifactEndsSessionAndFreshDefaultSessionRenders(){
        if(!OperatingSystem.IsWindows()){ return; }
        var DirectoryPath=Path.Combine(Path.GetTempPath(),$"CgePreviewLighting-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
        var StagePath=Path.Combine(DirectoryPath,"Stage.cgeshaderartifact");
        try {
            File.Copy(ShaderArtifactPath,StagePath);
            using var First=new Win32Window(new Win32WindowOptions { Title="Preview failure",Width=Size,Height=Size,Visible=false });
            using var Second=new Win32Window(new Win32WindowOptions { Title="Preview recovery",Width=Size,Height=Size,Visible=false });
            using var Table=new RenderPreviewHostSessionTable(Request=>RenderPreviewRenderSession.Open(Request,Options));
            var Open=CreateOpen(First,Floor:false);
            Open.ShaderArtifactPath=StagePath;
            Assert.Equal(RenderPreviewHostResultState.Succeeded,Table.Handle(Open).State);
            var Sky=Legacy(1);
            Sky.Environment=new(){ Atmosphere=true,SkyLightIntensity=1f };
            RenderPreviewFrame Frame(RenderPreviewHostRequest Source,RenderPreviewLighting Lighting)=>new(){
                Camera=Source.Scene!.Camera,Lighting=Lighting,
                Objects=Source.Scene.Objects.Select(static Object=>new RenderPreviewObjectTransform { ObjectId=Object.ObjectId,World=Object.World }).ToArray(),
            };
            var Update=new RenderPreviewHostRequest { RequestId=Guid.NewGuid(),SessionId=Open.SessionId,Sequence=2,Command=RenderPreviewHostCommand.UpdateFrame,Frame=Frame(Open,Sky) };
            Assert.Equal(RenderPreviewHostResultState.Succeeded,Table.Handle(Update).State);
            var Render=new RenderPreviewHostRequest { RequestId=Guid.NewGuid(),SessionId=Open.SessionId,Sequence=3,Command=RenderPreviewHostCommand.Render,CaptureForVerification=true };
            var Failed=Table.Handle(Render);
            Assert.Equal(RenderPreviewHostResultState.Failed,Failed.State);
            Assert.Equal("PREVIEW_COMMAND_FAILED",Failed.DiagnosticCode);
            Assert.Contains("Atmosphere.cgeshaderartifact",Failed.Message,StringComparison.OrdinalIgnoreCase);
            Render.Sequence=4;
            Assert.Equal(RenderPreviewHostResultState.SessionNotFound,Table.Handle(Render).State);
            var Fresh=CreateOpen(Second,Floor:false);
            Fresh.ShaderArtifactPath=StagePath;
            Assert.Equal(RenderPreviewHostResultState.Succeeded,Table.Handle(Fresh).State);
            Update.SessionId=Fresh.SessionId;
            Update.Frame=Frame(Fresh,Legacy(2));
            Assert.Equal(RenderPreviewHostResultState.Succeeded,Table.Handle(Update).State);
            Render.SessionId=Fresh.SessionId;
            Render.Sequence=3;
            var Recovered=Table.Handle(Render);
            Assert.Equal(RenderPreviewHostResultState.Succeeded,Recovered.State);
            Assert.NotEmpty(Recovered.FrameHash);
        } finally {
            File.Delete(StagePath);
            Directory.Delete(DirectoryPath);
        }
    }
}
internal static class RenderPreviewTestColors {
    internal static readonly (int R,int G,int B)DefaultClear=(89,140,217);
}
