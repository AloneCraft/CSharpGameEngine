using System.Numerics;
using CSharpGameEngine.Rendering.Server;
using CSharpGameEngine.Rendering.View;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;
using CSharpGameEngine.Lighting;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
[Collection(D3D12GpuCollection.Name)]
public sealed class VirtualShadowGpuTests {
    [WindowsOnlyTheory]
    [InlineData(DirectionalShadowMode.Cascaded,1f)]
    [InlineData(DirectionalShadowMode.Virtual,1f)]
    [InlineData(DirectionalShadowMode.Virtual,0.01f)]
    [Trait("Category","GPU")]
    public void SolarDiameterAndBlockerSeparationWidenPenumbra(DirectionalShadowMode Mode,float TransitionMeters){
        const int Size=384;
        using var Window=new Win32Window(new(){Title="Directional PCSS distance and angle",Width=Size,Height=Size,Visible=false});
        using var Renderer=StageRenderer.Create(Window.Handle,Size,Size,new(){UseWarp=true,EnableDebugLayer=true},Artifact,
            [Mesh(ProceduralMeshFactory.CreatePlane(40f,40f)),Mesh(ProceduralMeshFactory.CreateBox(new(2f,.2f,6f)))],
            [new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f }],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }]);
        var Camera=RenderCamera.CreateDefault(new(-3f,0f,0f));Camera.Pitch=-MathF.PI/2f+.001f;Camera.Distance=18f;Camera.FarZ=100f;Camera.RadialTransitionMeters=TransitionMeters;
        byte[]Draw(float Height,float Diameter,float Intensity,bool Shadows){
            Renderer.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy with { DirectionalShadows=new(Mode,SoftShadows:true,SourceAngularDiameterDegrees:Diameter) });
            Renderer.SetCamera(Camera);Renderer.SetAmbientLight(new(.05f));Renderer.SetDirectionalLight(Vector3.Normalize(new(1f,1f,0f)),Vector3.One,Intensity,CastsShadows:Shadows,ShadowBiasMeters:.002f);
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(0f,Height,0f));Renderer.RenderFrame(true);
            if(Shadows&&Mode==DirectionalShadowMode.Virtual){Assert.Equal(0,Renderer.VirtualShadowStatistics.Missing);}
            return Renderer.LastCapturedFrame!.Pixels.ToArray();
        }
        var Lit=Draw(6f,0f,3f,false);var Ambient=Draw(6f,0f,0f,false);
        Matrix4x4.Invert(Camera.GetView()*Camera.GetProjection(1f),out var Inverse);var Eye=Camera.GetEyePosition();
        int Transition(float Height,float Diameter){
            var Pixels=Draw(Height,Diameter,3f,true);var Count=0;
            for(var Y=Size/2-8;Y<Size/2+8;Y++){for(var X=0;X<Size;X++){
                var Clip=Vector4.Transform(new Vector4((X+.5f)*2f/Size-1f,1f-(Y+.5f)*2f/Size,1f,1f),Inverse);var Ray=Vector3.Normalize(new Vector3(Clip.X,Clip.Y,Clip.Z)/Clip.W-Eye);
                var Point=Eye-Ray*(Eye.Y/Ray.Y);
                // 外縁には、投影されたボディや反対側の縁に覆われていない、見える地面がある。
                if(MathF.Abs(Point.X-(-Height-1f))>.8f){continue;}
                var I=(Y*Size+X)*4;var Range=(float)Lit[I]-Ambient[I];var Visibility=(Pixels[I]-Ambient[I])/MathF.Max(Range,1f);
                if(Visibility>.12f&&Visibility<.88f){Count++;}
            }}
            return Count;
        }
        var Hard=Transition(6f,0f);var Near=Transition(2f,8f);var Far=Transition(6f,8f);var SmallSun=Transition(6f,2f);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,$"soft-shadow-{Mode}-{TransitionMeters}.json"),System.Text.Json.JsonSerializer.Serialize(new { Hard,Near,Far,SmallSun,Mode=Mode.ToString(),TransitionMeters }));
        Assert.True(Far>Hard+16,$"hard {Hard}, near {Near}, far {Far}, small sun {SmallSun}");
        Assert.True(Far>Near+16,$"near {Near}, far {Far}");
        Assert.True(Far>SmallSun+16,$"small sun {SmallSun}, far {Far}");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }
    private static readonly string Artifact=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static StageMeshCreateInfo Mesh(ProceduralMeshData Mesh)=>new(){Positions=Mesh.Positions,Normals=Mesh.Normals,Tangents=Enumerable.Repeat(new Vector4(1f,0f,0f,1f),Mesh.Positions.Length).ToArray(),Uvs=new Vector2[Mesh.Positions.Length],Indices=Mesh.Indices};
    private static StageRenderer CreateRenderer(Win32Window Window)=>StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },Artifact,
        [Mesh(ProceduralMeshFactory.CreatePlane(20f,20f)),Mesh(ProceduralMeshFactory.CreateBox(new(2f,.2f,2f)))],
        [new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f }],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }]);
    [WindowsOnlyTheory]
    [InlineData(1f)]
    [InlineData(0.01f)]
    [Trait("Category","GPU")]
    public void VirtualDepthCastsShadowCachesAndInvalidates(float TransitionMeters){
        using var Window=new Win32Window(new(){ Title="Virtual directional shadow oracle",Width=128,Height=128,Visible=false });
        using var Renderer=CreateRenderer(Window);
        var Camera=RenderCamera.CreateDefault(Vector3.Zero);Camera.Pitch=-89f*MathF.PI/180f;Camera.Distance=12f;Camera.FarZ=100f;Camera.RadialTransitionMeters=TransitionMeters;
        Renderer.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy with { DirectionalShadows=new(DirectionalShadowMode.Virtual) });
        void Draw(float X){
            Renderer.SetCamera(Camera);Renderer.SetAmbientLight(new(.05f));Renderer.SetDirectionalLight(Vector3.Normalize(new(.6f,1f,0f)),Vector3.One,3f,CastsShadows:true,ShadowBiasMeters:.005f);
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(X,2f,0f));Renderer.RenderFrame(true);
        }
        Draw(0f);Assert.True(Renderer.VirtualShadowStatistics.Rendered>0);Assert.Equal(0,Renderer.VirtualShadowStatistics.Missing);
        Draw(0f);Draw(0f);Assert.Equal(0,Renderer.VirtualShadowStatistics.Rendered);Assert.True(Renderer.VirtualShadowStatistics.Reused>0);
        var Capture=Renderer.LastCapturedFrame!;
        int Luma(int X,int Y){var I=(int)(Y*Capture.RowPitch)+X*4;return Capture.Pixels[I]+Capture.Pixels[I+1]+Capture.Pixels[I+2];}
        var Far=Luma(10,64);var Beside=Math.Min(Luma(40,64),Luma(88,64));
        Assert.True(Far>60&&Beside<Far*.65,$"far={Far} beside={Beside}");
        var Before=Capture.Pixels.ToArray();Draw(3f);Assert.True(Renderer.VirtualShadowStatistics.Invalidated>0);Assert.False(Before.SequenceEqual(Renderer.LastCapturedFrame!.Pixels));
        Renderer.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy with { DirectionalShadows=new(DirectionalShadowMode.Virtual,1) });
        Draw(3f);Assert.True(Renderer.VirtualShadowStatistics.Missing>0);
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void NonuniformScaleNormalMatchesBakedGeometry(){
        var Positions=new[]{new Vector3(-2f,0f,-2f),new Vector3(-2f,2f,2f),new Vector3(2f,0f,-2f)};
        var Normal=Vector3.Normalize(Vector3.Cross(Positions[1]-Positions[0],Positions[2]-Positions[0]));
        var World=Matrix4x4.CreateScale(3f,2f,4f)*Matrix4x4.CreateRotationY(.4f);
        var Baked=Positions.Select(P=>Vector3.Transform(P,World)).ToArray();
        var BakedNormal=Vector3.Normalize(Vector3.Cross(Baked[1]-Baked[0],Baked[2]-Baked[0]));
        byte[]Draw(bool Bake){
            using var Window=new Win32Window(new(){ Title="Geometric normal oracle",Width=128,Height=128,Visible=false });
            var Tangent=Vector3.Normalize(Positions[2]-Positions[0]);if(Bake){ Tangent=Vector3.Normalize(Vector3.TransformNormal(Tangent,World)); }
            var Mesh=new StageMeshCreateInfo { Positions=Bake?Baked:Positions,Normals=Enumerable.Repeat(Bake?BakedNormal:Normal,3).ToArray(),Tangents=Enumerable.Repeat(new Vector4(Tangent,1f),3).ToArray(),Uvs=new Vector2[3],Indices=[0,1,2] };
            using var Renderer=StageRenderer.Create(Window.Handle,128,128,new(){ UseWarp=true,EnableDebugLayer=true },Artifact,[Mesh],[new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f }],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
            var View=RenderCamera.CreateDefault(new(0f,1f,0f));View.Pitch=-1.1f;View.Distance=24f;
            for(var Frame=0;Frame<3;Frame++){
                Renderer.SetCamera(View);Renderer.SetPresentationMode(RenderPresentationMode.Normal);Renderer.SetObjectTransform(0,Bake?Matrix4x4.Identity:World);Renderer.RenderFrame(Frame==2);
            }
            Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);return Renderer.LastCapturedFrame!.Pixels;
        }
        var Transformed=Draw(false);var Reference=Draw(true);var Interior=0;
        for(var Y=35;Y<90;Y++){for(var X=35;X<90;X++){
            var I=(Y*128+X)*4;
            if(Reference[I]==StageRenderer.ClearColor.R&&Reference[I+1]==StageRenderer.ClearColor.G){ continue; }
            Interior++;for(var C=0;C<3;C++){Assert.InRange(Math.Abs(Transformed[I+C]-Reference[I+C]),0,2);}
        }}
        Assert.True(Interior>50);
    }
    [WindowsOnlyTheory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category","GPU")]
    public void ComparisonSwitchResizeAndDeviceRecoveryRetainPolicyAndPixels(bool Soft){
        using var Window=new Win32Window(new(){ Title="Virtual shadow recovery",Width=256,Height=128,Visible=false });
        using var Server=D3D12RenderServer.Create(Window.Handle,256,128,new(){ UseWarp=true,EnableDebugLayer=true },Artifact,
            [Mesh(ProceduralMeshFactory.CreatePlane(20f,20f)),Mesh(ProceduralMeshFactory.CreateBox(new(2f,.2f,2f)))],
            [new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f }],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }]);
        var View=RenderCamera.CreateDefault(Vector3.Zero);View.Pitch=-1.5f;View.Distance=12f;
        Server.SetCamera(View);Server.SetAmbientLight(new(.05f));Server.SetObjectTransform(0,Matrix4x4.Identity);Server.SetObjectTransform(1,Matrix4x4.CreateTranslation(0f,2f,0f));
        Server.SetDirectionalLight(new DirectionalLightSubmission(-Vector3.Normalize(new(.6f,1f,0f)),new LinearLightColor(1f,1f,1f),new Illuminance(3f),new LightShadowSettings(0,new ShadowBias(.005f))));
        var Policy=new DirectionalShadowSettings(DirectionalShadowMode.Virtual,1024,true,Soft,1.5f,2f);
        Server.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy with { DirectionalShadows=Policy });
        for(var I=0;I<3;I++){ Server.RenderFrame(true); }
        var Before=Server.LastCapturedFrame!.Pixels.ToArray();var Differences=0;
        for(var Y=20;Y<110;Y++){for(var X=5;X<123;X++){
            var Left=(Y*256+X)*4;var Right=Left+128*4;if(Before[Left]!=Before[Right]){ Differences++; }
        }}
        Assert.True(Differences>0,$"Independent shadow edges: differences={Differences}, settings={Server.DirectionalShadows}, pages={Server.VirtualShadowStatistics}");
        Server.RemoveDeviceForTesting();Server.RenderFrame(true);
        Assert.Equal(Policy,Server.DirectionalShadows);
        Assert.Equal(Before,Server.LastCapturedFrame!.Pixels);Assert.True(Server.DirectionalShadows.Compare);Assert.Equal(DirectionalShadowMode.Virtual,Server.DirectionalShadows.Mode);
        Server.Resize(128,128);Server.RenderFrame(true);Assert.Equal(128,Server.LastCapturedFrame!.Width);
        Server.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy);Server.RenderFrame(true);Assert.Equal(DirectionalShadowMode.Cascaded,Server.DirectionalShadows.Mode);
        Server.RemoveObject(1);Server.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy with { DirectionalShadows=new(DirectionalShadowMode.Virtual) });Server.RenderFrame(true);
        Assert.True(Server.VirtualShadowStatistics.Rendered>0);Assert.Equal(0,Server.ErrorOrWorseMessageCount);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void ThinCasterVisibilityIsCloserToAnalyticRayReference(){
        using var Window=new Win32Window(new(){ Title="Thin shadow analytic reference",Width=256,Height=256,Visible=false });
        using var Renderer=StageRenderer.Create(Window.Handle,256,256,new(){ UseWarp=true,EnableDebugLayer=true },Artifact,
            [Mesh(ProceduralMeshFactory.CreatePlane(20f,20f)),Mesh(ProceduralMeshFactory.CreateBox(new(.05f,2f,4f)))],
            [new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f }],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }]);
        var View=RenderCamera.CreateDefault(Vector3.Zero);View.Pitch=-1.55f;View.Distance=12f;View.FarZ=100f;
        var Sun=Vector3.Normalize(new Vector3(.6f,1f,0f));
        byte[]Capture(DirectionalShadowMode Mode,float Intensity,bool Shadows){
            Renderer.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy with { DirectionalShadows=new(Mode) });
            Renderer.SetCamera(View);Renderer.SetAmbientLight(new(.05f));Renderer.SetDirectionalLight(Sun,Vector3.One,Intensity,CastsShadows:Shadows,ShadowBiasMeters:.002f);
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(0f,1f,0f));Renderer.RenderFrame(true);return Renderer.LastCapturedFrame!.Pixels.ToArray();
        }
        var Lit=Capture(DirectionalShadowMode.Cascaded,3f,false);var Ambient=Capture(DirectionalShadowMode.Cascaded,0f,false);
        var Csm=Capture(DirectionalShadowMode.Cascaded,3f,true);var Virtual=Capture(DirectionalShadowMode.Virtual,3f,true);
        Assert.Equal(0,Renderer.VirtualShadowStatistics.Missing);
        Matrix4x4.Invert(View.GetView()*View.GetProjection(1f),out var Inverse);var Eye=View.GetEyePosition();
        bool Hit(Vector3 Origin,Vector3 Direction,float Limit){
            var Min=new Vector3(-.025f,0f,-2f);var Max=new Vector3(.025f,2f,2f);var Near=0f;var Far=Limit;
            for(var Axis=0;Axis<3;Axis++){
                if(MathF.Abs(Direction[Axis])<1e-8f){ if(Origin[Axis]<Min[Axis]||Origin[Axis]>Max[Axis]){ return false; }continue; }
                var A=(Min[Axis]-Origin[Axis])/Direction[Axis];var B=(Max[Axis]-Origin[Axis])/Direction[Axis];Near=MathF.Max(Near,MathF.Min(A,B));Far=MathF.Min(Far,MathF.Max(A,B));
            }
            return Near<Far;
        }
        var CsmError=0d;var VirtualError=0d;var ShadowSamples=0;
        for(var Y=32;Y<224;Y++){for(var X=32;X<224;X++){
            var Far=Vector4.Transform(new Vector4((X+.5f)/128f-1f,1f-(Y+.5f)/128f,1f,1f),Inverse);var Ray=Vector3.Normalize(new Vector3(Far.X,Far.Y,Far.Z)/Far.W-Eye);
            var Distance=-Eye.Y/Ray.Y;var Point=Eye+Ray*Distance;
            if(Hit(Eye,Ray,Distance)){ continue; }
            var Occluded=Hit(Point+Vector3.UnitY*.0001f,Sun,100f);if(Occluded){ ShadowSamples++; }
            var Index=(Y*256+X)*4;var Expected=Occluded?Ambient[Index]:Lit[Index];
            CsmError+=Math.Abs(Csm[Index]-Expected);VirtualError+=Math.Abs(Virtual[Index]-Expected);
        }}
        Assert.True(ShadowSamples>50);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"virtual-shadow-reference.json"),System.Text.Json.JsonSerializer.Serialize(new { Reference="analytic ray/AABB visibility",Adapter="WARP",Width=256,Height=256,CsmAbsoluteError=CsmError,VirtualAbsoluteError=VirtualError,ShadowSamples,ShaderSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Artifact))) }));
        Assert.True(VirtualError<CsmError*.8,$"analytic L1 error: CSM={CsmError}, virtual={VirtualError}, shadow pixels={ShadowSamples}");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void SkinnedCasterOutsideBindBoundsDoesNotLeaveItsOldShadow(){
        using var Window=new Win32Window(new(){ Title="Skinned virtual shadow",Width=128,Height=128,Visible=false });
        var Box=Mesh(ProceduralMeshFactory.CreateBox(new(2f,.2f,2f)));var Count=Box.Positions.Count;
        var Weights=new float[Count*8];for(var I=0;I<Count;I++){ Weights[I*8]=1f; }
        var Skinned=new StageMeshCreateInfo { Positions=Box.Positions,Normals=Box.Normals,Tangents=Box.Tangents,Uvs=Box.Uvs,Indices=Box.Indices,Skin=new StageSkinCreateInfo { JointCount=1,JointIndices=new ushort[Count*8],Weights=Weights } };
        using var Renderer=StageRenderer.Create(Window.Handle,128,128,new(){ UseWarp=true,EnableDebugLayer=true },Artifact,[Mesh(ProceduralMeshFactory.CreatePlane(20f,20f)),Skinned],
            [new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f }],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }]);
        var View=RenderCamera.CreateDefault(Vector3.Zero);View.Pitch=-89f*MathF.PI/180f;View.Distance=12f;View.FarZ=100f;
        Renderer.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy with { DirectionalShadows=new(DirectionalShadowMode.Virtual) });
        void Draw(float X){
            Renderer.SetSkinPalette(1,[Matrix4x4.CreateTranslation(X,0f,0f)]);Renderer.SetCamera(View);Renderer.SetAmbientLight(new(.05f));
            Renderer.SetDirectionalLight(Vector3.Normalize(new(.6f,1f,0f)),Vector3.One,3f,CastsShadows:true,ShadowBiasMeters:.005f);
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(0f,2f,0f));Renderer.RenderFrame(true);
        }
        int Luma(int X){var Capture=Renderer.LastCapturedFrame!;var I=(int)(64*Capture.RowPitch)+X*4;return Capture.Pixels[I]+Capture.Pixels[I+1]+Capture.Pixels[I+2];}
        Draw(0f);var X=Luma(40)<Luma(88)?40:88;var Before=Luma(X);
        Draw(4f);Assert.True(Renderer.VirtualShadowStatistics.Invalidated>0);Assert.True(Luma(X)>Before*1.5,$"old shadow {Before}, moved {Luma(X)}");
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }
}
