using Xunit;
using System.Numerics;
using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// 仮想シャドウのページの描画（M0132 単位 3、ADR-0100 決定 5）。画面の要求を読み戻して計画し、置き場へ 1 ページずつ描いた深度が、
/// 同じ段の同じ範囲をまとめて 1 枚に描いた深度と一致することを確かめる。止まったカメラでは、置いたページを作り直さずに使い回す。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class VirtualShadowMapPageGpuTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Width=320;
    private const int Height=180;
    private const int PageTexels=VirtualShadowMapPlanner.PageTexels;
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_PagesMatchTheSameRangeDrawnAtOnce(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Virtual Shadow Map Page GPU Test",Width=Width,Height=Height,Visible=false });
        static StageMeshCreateInfo Mesh(ProceduralMeshData Data)=>new(){ Positions=Data.Positions,Normals=Data.Normals,
            Tangents=Enumerable.Repeat(new Vector4(1f,0f,0f,1f),Data.Positions.Length).ToArray(),Uvs=new Vector2[Data.Positions.Length],Indices=Data.Indices };
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            [Mesh(ProceduralMeshFactory.CreatePlane(Width:400f,Depth:400f)),Mesh(ProceduralMeshFactory.CreateBox(Vector3.One)),Mesh(ProceduralMeshFactory.CreateSphere(0.8f))],
            [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.7f,0.7f,0.7f,1f),RoughnessFactor=1f,MetallicFactor=0f }],
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 },
                new StageObjectCreateInfo { MeshIndex=2,MaterialIndex=0 }]);
        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(System.Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
        Renderer.VirtualShadowMapsForTesting=true;
        var View=Camera.CreateFirstPerson(new Vector3(0.3f,1.7f,-6f));
        View.Pitch=-0.12f;
        void Render(){
            Renderer.SetAmbientLight(new Vector3(0.3f));
            Renderer.SetCamera(View);
            Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.35f,0.8f,-0.45f)),Vector3.One,3f);
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(0.5f,0.5f,0f));
            Renderer.SetObjectTransform(2,Matrix4x4.CreateScale(2f,4f,2f)*Matrix4x4.CreateTranslation(-4f,2f,12f));
            Renderer.SetObjectTransform(3,Matrix4x4.CreateTranslation(2.5f,1.2f,3f));
            Renderer.RenderFrame();
        }
        // 要求は読み戻しの輪を 1 周してから届く。届いたフレームで、足りないページが半分を超えるので上限を外して全部を描く。
        for(var Frame=0;Frame<StageRenderer.FrameCount+3;Frame++){ Render(); }
        var Settled=Renderer.ReadVirtualShadowMapForTesting();
        Assert.True(Settled.Statistics.Requested>20,$"only {Settled.Statistics.Requested} pages were requested");
        Assert.Equal(0,Settled.Statistics.Missing);
        Assert.Equal(Settled.Statistics.Requested,Settled.Statistics.Resident);
        Render();
        var Still=Renderer.ReadVirtualShadowMapForTesting();
        Assert.Equal(0,Still.Statistics.Rendered);
        Assert.Equal(Still.Statistics.Requested,Still.Statistics.Reused);
        var Pages=ResidentPages(Still.Table,Still.Origins);
        var Levels=Pages.Select(static Page=>Page.Key.Level).Distinct().Order().ToArray();
        Assert.True(Levels.Length>=4,$"pages exist only on the levels {string.Join(",",Levels)}");
        var Report=new List<string>();
        foreach(var Level in new[]{ Levels[0],Levels[Levels.Length/2],Levels[^1] }.Distinct()){
            var OnLevel=Pages.Where(Page=>Page.Key.Level==Level).ToArray();
            var PageX=OnLevel.Min(static Page=>Page.Key.X);
            var PageY=OnLevel.Min(static Page=>Page.Key.Y);
            var Span=Math.Min(16,Math.Max(OnLevel.Max(static Page=>Page.Key.X)-PageX,OnLevel.Max(static Page=>Page.Key.Y)-PageY)+1);
            Renderer.VirtualShadowReferenceForTesting=(Level,PageX,PageY,Span);
            Render();
            var Result=Renderer.ReadVirtualShadowMapForTesting();
            Assert.Equal(0,Result.Statistics.Rendered);
            Assert.NotNull(Result.Reference);
            var Compared=0;
            var Different=0;
            var Covered=0;
            var Size=Span*PageTexels;
            foreach(var (Key,Slot) in OnLevel){
                if(Key.X-PageX>=Span||Key.Y-PageY>=Span){ continue; }
                var AtlasX=Slot%VirtualShadowMapPlanner.PhysicalPagesPerAxis*PageTexels;
                var AtlasY=Slot/VirtualShadowMapPlanner.PhysicalPagesPerAxis*PageTexels;
                var ReferenceX=(Key.X-PageX)*PageTexels;
                var ReferenceY=(PageY+Span-1-Key.Y)*PageTexels;
                for(var Y=0;Y<PageTexels;Y++){
                    for(var X=0;X<PageTexels;X++){
                        var Atlas=Result.Atlas[(AtlasY+Y)*Result.AtlasWidth+AtlasX+X];
                        var Reference=Result.Reference![(ReferenceY+Y)*Size+ReferenceX+X];
                        Compared++;
                        if(Reference<1f){ Covered++; }
                        if(MathF.Abs(Atlas-Reference)>1e-6f){ Different++; }
                    }
                }
            }
            Report.Add($"level {Level}: {Compared/(PageTexels*PageTexels)} pages, {Covered} texels covered, {Different} different");
            Assert.True(Compared>0,$"no page of level {Level} lies in the reference range");
            Assert.True(Covered>0,$"nothing casts a shadow into the compared pages of level {Level}");
            // 三角形の縁がテクセルの中心をちょうど通るときだけ、行列の最後の桁で隣のテクセルになってよい。
            Assert.True(Different*1000<=Compared,$"level {Level}: {Different} of {Compared} texels differ");
        }
        if(System.Environment.GetEnvironmentVariable("CGE_VIRTUAL_SHADOW_MEASURE") is{ Length:>0 }Folder){
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder,"pages.txt"),$"{Width}x{Height}: {Settled.Statistics.Requested} pages requested and resident, {Still.Statistics.Rendered} rendered on a still frame; "+string.Join("; ",Report));
        }
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    /// <summary>
    /// 実行時と同じくカメラを描画空間の原点にして描く（物はカメラから見た位置）。区画を渡すと、カメラだけが動く間に古くなるページは 0。
    /// 区画を渡さないと、物の描画空間の位置がフレームごとに変わるので、ページが古くなる（試験がこの違いを見分けられること）。
    /// </summary>
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Warp_ACameraThatOnlyMovesMakesNoPageStaleWithTheAnchor(){
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Virtual Shadow Anchor GPU Test",Width=Width,Height=Height,Visible=false });
        static StageMeshCreateInfo Mesh(ProceduralMeshData Data)=>new(){ Positions=Data.Positions,Normals=Data.Normals,
            Tangents=Enumerable.Repeat(new Vector4(1f,0f,0f,1f),Data.Positions.Length).ToArray(),Uvs=new Vector2[Data.Positions.Length],Indices=Data.Indices };
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            [Mesh(ProceduralMeshFactory.CreatePlane(Width:400f,Depth:400f)),Mesh(ProceduralMeshFactory.CreateBox(Vector3.One))],
            [new StageMaterialCreateInfo { BaseColorFactor=new Vector4(0.7f,0.7f,0.7f,1f),RoughnessFactor=1f,MetallicFactor=0f }],
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=1,MaterialIndex=0 }]);
        Renderer.SetEnvironmentLighting(EnvironmentLightingSettings.Legacy with { DirectionalShadows=new(DirectionalShadowMode.Virtual) });
        var View=Camera.CreateFirstPerson(Vector3.Zero);
        View.Pitch=-0.12f;
        // 世界の位置は区画 2 の中（2,048 m より先）。区画の大きさを跨がない範囲で動く。
        var Start=new Vector3(2100.3f,1.7f,-6f);
        (int Invalidated,int Reused)Render(Vector3 CameraWorld,bool Anchor){
            Renderer.SetVirtualShadowAnchor(Anchor?new VirtualShadowAnchor(2,0,-1,CameraWorld-new Vector3(2048f,0f,-1024f)):default);
            Renderer.SetAmbientLight(new Vector3(0.3f));
            Renderer.SetCamera(View);
            Renderer.SetDirectionalLight(Vector3.Normalize(new Vector3(0.35f,0.8f,-0.45f)),Vector3.One,3f,CastsShadows:true,ShadowBiasMeters:0.005f);
            Renderer.SetObjectTransform(0,Matrix4x4.CreateTranslation(new Vector3(2100f,0f,0f)-CameraWorld));
            Renderer.SetObjectTransform(1,Matrix4x4.CreateTranslation(new Vector3(2100.5f,0.5f,0f)-CameraWorld));
            Renderer.SetObjectTransform(2,Matrix4x4.CreateScale(2f,4f,2f)*Matrix4x4.CreateTranslation(new Vector3(2096f,2f,12f)-CameraWorld));
            Renderer.RenderFrame();
            return (Renderer.VirtualShadowStatistics.Invalidated,Renderer.VirtualShadowStatistics.Reused);
        }
        for(var Frame=0;Frame<StageRenderer.FrameCount+3;Frame++){ _=Render(Start,Anchor:true); }
        var Moving=new List<(int Invalidated,int Reused)>();
        for(var Frame=1;Frame<=10;Frame++){ Moving.Add(Render(Start+new Vector3(0.37f,0.05f,0.21f)*Frame,Anchor:true)); }
        Assert.All(Moving,static Step=>Assert.Equal(0,Step.Invalidated));
        Assert.True(Moving.Sum(static Step=>Step.Reused)>0,"no page was reused while the camera moved");
        var WithoutAnchor=0;
        for(var Frame=11;Frame<=14;Frame++){ WithoutAnchor+=Render(Start+new Vector3(0.37f,0.05f,0.21f)*Frame,Anchor:false).Invalidated; }
        Assert.True(WithoutAnchor>0,"without the anchor the moving render space should make pages stale");
        Assert.DoesNotContain(Renderer.DebugMessages,static Message=>Message.IsErrorOrWorse);
    }
    /// <summary>ページ表の、置いたページとその置き場の番号。</summary>
    private static List<(VirtualShadowPageKey Key,int Slot)>ResidentPages(uint[]Table,(int X,int Y)[]Origins){
        var Result=new List<(VirtualShadowPageKey Key,int Slot)>();
        for(var Index=0;Index<Table.Length;Index++){
            if(Table[Index]==0){ continue; }
            var Level=Index/(VirtualShadowMapPlanner.PagesPerAxis*VirtualShadowMapPlanner.PagesPerAxis);
            var Y=Index/VirtualShadowMapPlanner.PagesPerAxis%VirtualShadowMapPlanner.PagesPerAxis;
            var X=Index%VirtualShadowMapPlanner.PagesPerAxis;
            Result.Add((new VirtualShadowPageKey(Level,Origins[Level].X+X,Origins[Level].Y+VirtualShadowMapPlanner.PagesPerAxis-1-Y),(int)Table[Index]-1));
        }
        return Result;
    }
}
