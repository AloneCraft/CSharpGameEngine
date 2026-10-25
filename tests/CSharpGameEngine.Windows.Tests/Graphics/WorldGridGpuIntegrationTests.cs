using CSharpGameEngine.Rendering;
using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// WARP oracles for the world-space procedural grid (M0098, ADR-0075): Unreal's M_PrototypeGrid draws a
/// 1 m grid with 20 cm sub lines by projecting a wire texture along the world axes, so the lines must
/// follow world coordinates (floor and walls alike) and never the mesh UVs.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class WorldGridGpuIntegrationTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private static readonly StageWorldGrid PrototypeGrid=new(1f,5,5f/512f,new Vector3(0.026f),new Vector3(0.0885f));
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void WorldGrid_Warp_FloorLinesFollowTheWorldGridAndSubGridHierarchy(){
        using var Scene=GridScene.Create(PrototypeGrid,FloorUvs.Zero);
        Scene.Render();
        var Surface=Scene.Luminance(new Vector3(0.1f,0f,0.3f));
        var SubLine=Scene.Luminance(new Vector3(0.4f,0f,0.3f));
        var MainLineX=Scene.Luminance(new Vector3(1f,0f,0.3f));
        var MainLineZ=Scene.Luminance(new Vector3(0.5f,0f,1f));
        var Diagnostic=$"surface={Surface} sub={SubLine} mainX={MainLineX} mainZ={MainLineZ}";
        Assert.True(MainLineX<Surface*0.5f,Diagnostic);
        Assert.True(MainLineZ<Surface*0.5f,Diagnostic);
        Assert.True(SubLine<Surface*0.97f,Diagnostic);
        Assert.True(MainLineX<SubLine,Diagnostic);
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void WorldGrid_Warp_IsIndependentOfTheMeshUvs(){
        using var Zero=GridScene.Create(PrototypeGrid,FloorUvs.Zero);
        using var Scrambled=GridScene.Create(PrototypeGrid,FloorUvs.Scrambled);
        Zero.Render();
        Scrambled.Render();
        Assert.Equal(Zero.Pixels(),Scrambled.Pixels());
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void WorldGrid_Warp_WithoutAGridTheFloorIsOneFlatColor(){
        using var Scene=GridScene.Create(null,FloorUvs.Zero);
        Scene.Render();
        var Surface=Scene.Luminance(new Vector3(0.1f,0f,0.3f));
        Assert.Equal(Surface,Scene.Luminance(new Vector3(1f,0f,0.3f)));
        Assert.Equal(Surface,Scene.Luminance(new Vector3(0.4f,0f,0.3f)));
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void WorldGrid_Warp_WallsGetHorizontalAndVerticalLinesFromTheSameWorldGrid(){
        using var Scene=GridScene.Create(PrototypeGrid,FloorUvs.Zero,Wall:true);
        Scene.Render();
        // The wall's -Z face is the plane z=2.5: a vertical line at x=1 and a horizontal line at y=1.
        var VerticalLine=Scene.Luminance(new Vector3(1f,0.3f,2.5f));
        var HorizontalLine=Scene.Luminance(new Vector3(0.3f,1f,2.5f));
        var Reference=Scene.Luminance(new Vector3(0.3f,0.5f,2.5f));
        var Diagnostic=$"reference={Reference} vertical={VerticalLine} horizontal={HorizontalLine}";
        Assert.True(VerticalLine<Reference*0.5f,Diagnostic);
        Assert.True(HorizontalLine<Reference*0.5f,Diagnostic);
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void WorldGrid_Warp_CameraRelativeRenderingKeepsTheLinesOnTheWorldGrid(){
        // The runtime renders camera-relative: the camera sits at the render origin and every object is moved
        // by minus the camera position, so the lines must still fall on the world grid (not on a grid that
        // follows the camera).
        using var Scene=GridScene.Create(PrototypeGrid,FloorUvs.Zero,RenderOrigin:new Vector3(0.7f,1.5f,0.5f));
        Scene.Render();
        var Surface=Scene.Luminance(new Vector3(0.1f,0f,0.3f));
        var SubLine=Scene.Luminance(new Vector3(0.4f,0f,0.3f));
        var MainLineX=Scene.Luminance(new Vector3(1f,0f,0.3f));
        var MainLineZ=Scene.Luminance(new Vector3(0.5f,0f,1f));
        var Diagnostic=$"surface={Surface} sub={SubLine} mainX={MainLineX} mainZ={MainLineZ}";
        Assert.True(MainLineX<Surface*0.5f,Diagnostic);
        Assert.True(MainLineZ<Surface*0.5f,Diagnostic);
        Assert.True(SubLine<Surface*0.97f,Diagnostic);
        Assert.True(MainLineX<SubLine,Diagnostic);
        Scene.AssertNoDebugLayerErrors();
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void LegacyFog_Warp_CameraRelativeRenderingThinsWithTheWorldHeight(){
        // The simplified height fog thins with the receiver's world height. A floor 3 m above the world origin,
        // seen from 1.5 m above it, must look the same whether the frame is rendered in world coordinates or
        // camera-relative (as the runtime does).
        var Fog=new FogParams(0.1f,0.5f,new Vector3(0.8f,0.8f,0.8f));
        var Eye=new Vector3(0.7f,4.5f,0.5f);
        using var World=GridScene.Create(null,FloorUvs.Zero,FloorHeight:3f,Fog:Fog,Eye:Eye,PitchDegrees:-20f);
        using var CameraRelative=GridScene.Create(null,FloorUvs.Zero,FloorHeight:3f,Fog:Fog,Eye:Eye,PitchDegrees:-20f,RenderOrigin:Eye);
        World.Render();
        CameraRelative.Render();
        var Expected=World.Pixels();
        var Actual=CameraRelative.Pixels();
        var Largest=0;
        for(var Index=0;Index<Expected.Length;Index++){
            Largest=Math.Max(Largest,Math.Abs(Expected[Index]-Actual[Index]));
        }
        Assert.True(Largest<=2,$"largest channel difference {Largest}");
        CameraRelative.AssertNoDebugLayerErrors();
    }
    private enum FloorUvs {
        Zero,
        Scrambled,
    }
    private sealed class GridScene:IDisposable {
        private GridScene(Win32Window Window,StageRenderer Renderer,Camera Camera,bool HasWall,Vector3 RenderOrigin,float FloorHeight,FogParams?Fog){
            this.Window=Window;
            this.Renderer=Renderer;
            this.Camera=Camera;
            this.HasWall=HasWall;
            this.RenderOrigin=RenderOrigin;
            this.FloorHeight=FloorHeight;
            this.Fog=Fog;
        }
        public Win32Window Window { get; }
        public StageRenderer Renderer { get; }
        /// <summary>The camera in world coordinates; it is also used to find the pixel of a world point.</summary>
        public Camera Camera { get; }
        public bool HasWall { get; }
        /// <summary>World position of the render-space origin: zero renders in world coordinates, the eye renders camera-relative.</summary>
        public Vector3 RenderOrigin { get; }
        public float FloorHeight { get; }
        public FogParams?Fog { get; }
        public static GridScene Create(StageWorldGrid?Grid,FloorUvs Uvs,bool Wall=false,Vector3 RenderOrigin=default,float FloorHeight=0f,FogParams?Fog=null,Vector3?Eye=null,float PitchDegrees=-85f){
            var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine World Grid GPU Test",Width=320,Height=180,Visible=false });
            StageMeshCreateInfo ToMesh(ProceduralMeshData Data){
                var Tangents=new Vector4[Data.Positions.Length];
                Array.Fill(Tangents,new Vector4(1f,0f,0f,1f));
                var UvValues=new Vector2[Data.Positions.Length];
                if(Uvs==FloorUvs.Scrambled){
                    for(var Index=0;Index<UvValues.Length;Index++){
                        UvValues[Index]=new Vector2(MathF.Sin(Index*12.9898f)*43758.5453f%1f,MathF.Sin(Index*78.233f)*12345.6789f%1f);
                    }
                }
                return new StageMeshCreateInfo { Positions=Data.Positions,Normals=Data.Normals,Tangents=Tangents,Uvs=UvValues,Indices=Data.Indices };
            }
            var Material=new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.18f,0.18f,0.18f,1f),RoughnessFactor=1f,MetallicFactor=0f,WorldGrid=Grid };
            List<StageMeshCreateInfo>Meshes=[ToMesh(ProceduralMeshFactory.CreatePlane(Width:40f,Depth:40f))];
            List<StageObjectCreateInfo>Objects=[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }];
            if(Wall){
                Meshes.Add(ToMesh(ProceduralMeshFactory.CreateBox(new Vector3(6f,6f,0.5f))));
                Objects.Add(new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 });
            }
            var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,[.. Meshes],[Material],[.. Objects]);
            // What the runtime does (EngineHost): the render origin's altitude, and the render origin modulo the grid size.
            Renderer.SetRenderOriginAltitude(RenderOrigin.Y);
            if(Grid is{ }WorldGrid){
                static float Reduce(float Value,float Cell)=>((Value%Cell)+Cell)%Cell;
                Renderer.SetWorldGridAnchor(0,new Vector3(Reduce(RenderOrigin.X,WorldGrid.GridSizeMeters),Reduce(RenderOrigin.Y,WorldGrid.GridSizeMeters),Reduce(RenderOrigin.Z,WorldGrid.GridSizeMeters)));
            }
            Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
            // Floor scene: straight down at 1.5 m (about 0.7 cm per pixel); wall scene: facing the wall's -Z face.
            var View=Wall
                ?Camera.CreateFirstPerson(new Vector3(1f,1f,0f))
                :Camera.CreateFirstPerson(Eye??new Vector3(0.7f,1.5f,0.5f));
            if(!Wall){
                View.Pitch=PitchDegrees*MathF.PI/180f;
            }
            return new GridScene(Window,Renderer,View,Wall,RenderOrigin,FloorHeight,Fog);
        }
        public void Render(){
            var RenderCamera=this.Camera;
            RenderCamera.Target-=this.RenderOrigin;
            for(var Frame=0;Frame<2;Frame++){
                this.Renderer.SetAmbientLight(new Vector3(0.4f,0.4f,0.4f));
                this.Renderer.SetCamera(RenderCamera);
                this.Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.3f,0.8f,-0.4f)),Vector3.One,2f);
                if(this.Fog is{ }Fog){
                    this.Renderer.SetFog(Fog);
                }
                this.Renderer.SetObjectTransform(0,Matrix4x4.CreateTranslation(new Vector3(0f,this.FloorHeight,0f)-this.RenderOrigin));
                if(this.HasWall){
                    this.Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(new Vector3(1f,2.5f,2.75f)-this.RenderOrigin));
                }
                this.Renderer.RenderFrame(CaptureForVerification:Frame==1);
            }
        }
        public byte[]Pixels()=>this.Renderer.LastCapturedFrame!.Pixels.ToArray();
        /// <summary>The summed 8-bit RGB of the pixel that shows the world point.</summary>
        public int Luminance(Vector3 World){
            var Frame=this.Renderer.LastCapturedFrame!;
            var Clip=Vector4.Transform(new Vector4(World,1f),this.Camera.GetView()*this.Camera.GetProjection((float)Frame.Width/Frame.Height));
            var X=(int)((Clip.X/Clip.W*0.5f+0.5f)*Frame.Width);
            var Y=(int)((0.5f-Clip.Y/Clip.W*0.5f)*Frame.Height);
            Assert.InRange(X,0,Frame.Width-1);
            Assert.InRange(Y,0,Frame.Height-1);
            var Offset=(int)(Y*Frame.RowPitch)+X*4;
            return Frame.Pixels[Offset]+Frame.Pixels[Offset+1]+Frame.Pixels[Offset+2];
        }
        public void AssertNoDebugLayerErrors()=>
            Assert.DoesNotContain(this.Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
        public void Dispose(){
            this.Renderer.Dispose();
            this.Window.Dispose();
        }
    }
}
