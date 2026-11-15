using System.IO.Compression;
using System.Reflection;
using CSharpGameEngine.Contracts;
using static CSharpGameEngine.VSExtension.NativeMethods;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>ADR-0070 B1: the packaged host renders the built game's world and accepts live scene edits.</summary>
public sealed class PackagedRuntimeViewTests {
    private const string EditedScene="UserEcsProject.Scenes.PrimitiveShowcaseBoxScene";
    [Fact]
    public void VsixPayload_OpensTheGamesRuntimeViewAndAppliesTheSceneDocument(){
        var Original=File.ReadAllText(Path.Combine(RepositoryRoot(),"UserEcsProject","Scenes","PrimitiveShowcaseBoxScene.json"));
        var Moved=Original.Replace("\"Position\": { \"R\": 3, \"U\": 0.5, \"F\": 0 }","\"Position\": { \"R\": 3, \"U\": 0.5, \"F\": 3 }");
        var AddedChild=Original.Replace("\"children\": []","\"children\": [ { \"id\": \"7b1c2f4e-0d3a-4c7e-9f1b-2a6d8e5c3b10\", \"components\": [], \"children\": [] } ]");
        Assert.NotEqual(Original,Moved);Assert.NotEqual(Original,AddedChild);
        WithRuntimeView(EditedScene,Original,TimeSpan.FromSeconds(120),(Client,Scene)=>{
            string Frame(){
                Assert.Equal(RenderPreviewHostResultState.Succeeded,Client.Render(captureForVerification:false).State);
                var Captured=Client.Render(captureForVerification:true);
                Assert.True(Captured.State==RenderPreviewHostResultState.Succeeded,Captured.Message);
                return Captured.FrameHash;
            }
            var Before=Frame();
            var MovedResponse=Client.ReplaceScene(Scene,Moved);var AfterMove=Frame();
            var StructureResponse=Client.ReplaceScene(Scene,AddedChild);
            Assert.False(MovedResponse.BuildRequired);Assert.NotEqual(Before,AfterMove);Assert.True(StructureResponse.BuildRequired);
            Assert.False(Client.ReplaceScene(Scene,Original).BuildRequired);Assert.Equal(Before,Frame());
        });
    }
    [Fact]
    public void TexturedStageRendersItsFirstFrameWhileTheEditorWorldRuns(){
        var Document=File.ReadAllText(Path.Combine(RepositoryRoot(),"UserEcsProject","Scenes","FirstPersonStageScene.json"));
        WithRuntimeView("UserEcsProject.Scenes.FirstPersonStageScene",Document,TimeSpan.FromSeconds(30),(Client,Scene)=>{
            var Presented=Client.Render(captureForVerification:false);
            Assert.True(Presented.State==RenderPreviewHostResultState.Succeeded,Presented.Message);
            Assert.NotEqual(Guid.Empty,Presented.EditorSpatialWorldId);
            var Captured=Client.Render(captureForVerification:true);
            Assert.True(Captured.State==RenderPreviewHostResultState.Succeeded,Captured.Message);
            Assert.Equal(64,Captured.FrameHash.Length);Assert.True(Captured.CompletedFence>0);
            Assert.False(Client.ReplaceScene(Scene,Document).BuildRequired);
        });
    }
    private static void WithRuntimeView(string SceneName,string Document,TimeSpan Timeout,Action<RenderPreviewHostClient,RenderPreviewScene>Verify){
        var Game=typeof(PackagedRuntimeViewTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(static Attribute=>Attribute.Key=="CgeRuntimeViewGame").Value!;
        Assert.True(File.Exists(Game),Game);
        var Root=Path.Combine(Path.GetTempPath(),$"CgePackagedRuntimeView-{Guid.NewGuid():N}");Directory.CreateDirectory(Root);
        ZipFile.ExtractToDirectory(Path.Combine(AppContext.BaseDirectory,"TestAssets","VSExtension.vsix"),Root);
        var HostPath=Path.Combine(Root,"PreviewHost","EditorPreview.Host.exe");var ShaderPath=Path.Combine(Root,"PreviewHost","Shaders","Stage.cgeshaderartifact");
        var Parent=IntPtr.Zero;
        try {
            Parent=CreateWindowEx(0,"STATIC",string.Empty,0,0,0,160,120,IntPtr.Zero,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero);Assert.NotEqual(IntPtr.Zero,Parent);
            using var SurfaceHost=new RenderSurfaceHostWindow(Parent,160,120,InitiallyVisible:false);
            using var Client=RenderPreviewHostClient.Start(HostPath,Timeout);
            var Scene=new RenderPreviewScene{Camera=Camera()};
            var Opened=Client.Open(SurfaceHost.Surface.Handle.ToInt64(),160,120,ShaderPath,Scene,
                new RenderPreviewRuntimeView{AssemblyPath=Game,ContentDirectory=Path.GetDirectoryName(Game)!,SceneName=SceneName,UseWarp=true},Document);
            Assert.True(Opened.State==RenderPreviewHostResultState.Succeeded,Opened.Message);Assert.False(Opened.BuildRequired);
            Verify(Client,Scene);Client.Close();
        }finally{
            if(Parent!=IntPtr.Zero){_=DestroyWindow(Parent);}Directory.Delete(Root,recursive:true);
        }
    }
    private static RenderPreviewCamera Camera()=>new(){Position=[3f,0.5f,-4f],Rotation=[0f,0f,0f,1f],FovYRadians=0.7853982f,NearZ=0.1f,FarZ=300f};
    private static string RepositoryRoot(){
        DirectoryInfo?Directory=new(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){Directory=Directory.Parent;}
        return Directory?.FullName??throw new DirectoryNotFoundException();
    }
}
