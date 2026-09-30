using System.IO.Compression;
using System.Reflection;
using CSharpGameEngine.Contracts;
using static CSharpGameEngine.VSExtension.NativeMethods;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// ADR-0070 B1: the Scene view's client opens the packaged preview host on a built game's runtime view
/// and streams the edited scene's JSON to it; value edits show live and structural edits ask for a build.
/// </summary>
public sealed class PackagedRuntimeViewTests {
    private const string EditedScene="UserEcsProject.Scenes.PrimitiveShowcaseBoxScene";

    [Fact]
    public void VsixPayload_OpensTheGamesRuntimeViewAndAppliesTheSceneDocument(){
        string Game=typeof(PackagedRuntimeViewTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static Attribute=>Attribute.Key=="CgeRuntimeViewGame").Value!;
        Assert.True(File.Exists(Game),Game);
        string Original=File.ReadAllText(Path.Combine(RepositoryRoot(),"UserEcsProject","Scenes","PrimitiveShowcaseBoxScene.scene.json"));
        string Moved=Original.Replace("\"Position\": { \"R\": 3, \"U\": 0.5, \"F\": 0 }","\"Position\": { \"R\": 3, \"U\": 0.5, \"F\": 3 }");
        string AddedChild=Original.Replace("\"children\": []","\"children\": [ { \"id\": \"7b1c2f4e-0d3a-4c7e-9f1b-2a6d8e5c3b10\", \"components\": [], \"children\": [] } ]");
        Assert.NotEqual(Original,Moved);
        Assert.NotEqual(Original,AddedChild);
        string Root=Path.Combine(Path.GetTempPath(),$"CgePackagedRuntimeView-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        ZipFile.ExtractToDirectory(Path.Combine(AppContext.BaseDirectory,"TestAssets","CSharpGameEngine.VSExtension.vsix"),Root);
        string HostPath=Path.Combine(Root,"PreviewHost","CSharpGameEngine.EditorPreview.Host.exe");
        string ShaderPath=Path.Combine(Root,"PreviewHost","Shaders","Stage.cgeshaderartifact");
        IntPtr Parent=IntPtr.Zero;
        try {
            Parent=CreateWindowEx(0,"STATIC",string.Empty,0,0,0,160,120,IntPtr.Zero,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero,Parent);
            using RenderSurfaceHostWindow SurfaceHost=new(Parent,160,120,InitiallyVisible:false);
            using RenderPreviewHostClient Client=RenderPreviewHostClient.Start(HostPath,TimeSpan.FromSeconds(120));
            RenderPreviewScene Scene=new() { Camera=Camera() };
            RenderPreviewHostResponse Opened=Client.Open(
                SurfaceHost.Surface.Handle.ToInt64(),160,120,ShaderPath,Scene,
                new RenderPreviewRuntimeView {
                    AssemblyPath=Game,ContentDirectory=Path.GetDirectoryName(Game)!,SceneName=EditedScene,UseWarp=true,
                },
                Original);
            Assert.True(Opened.State==RenderPreviewHostResultState.Succeeded,Opened.Message);
            Assert.False(Opened.BuildRequired);
            string Frame(){
                Assert.Equal(RenderPreviewHostResultState.Succeeded,Client.Render(captureForVerification:false).State);
                RenderPreviewHostResponse Captured=Client.Render(captureForVerification:true);
                Assert.True(Captured.State==RenderPreviewHostResultState.Succeeded,Captured.Message);
                return Captured.FrameHash;
            }
            string Before=Frame();

            RenderPreviewHostResponse MovedResponse=Client.ReplaceScene(Scene,Moved);
            string AfterMove=Frame();
            RenderPreviewHostResponse StructureResponse=Client.ReplaceScene(Scene,AddedChild);

            Assert.False(MovedResponse.BuildRequired);
            Assert.NotEqual(Before,AfterMove);
            Assert.True(StructureResponse.BuildRequired);
            Assert.False(Client.ReplaceScene(Scene,Original).BuildRequired);
            Assert.Equal(Before,Frame());
            Client.Close();
        } finally {
            if(Parent!=IntPtr.Zero){ _=DestroyWindow(Parent); }
            Directory.Delete(Root,recursive:true);
        }
    }

    private static RenderPreviewCamera Camera()=>new() {
        Position=[3f,0.5f,-4f],Rotation=[0f,0f,0f,1f],FovYRadians=0.7853982f,NearZ=0.1f,FarZ=300f,
    };

    private static string RepositoryRoot(){
        DirectoryInfo?Directory=new(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new DirectoryNotFoundException();
    }
}
