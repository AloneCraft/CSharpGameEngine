using System.Security.Cryptography;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.EditorPreview.Host;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
using PreviewBridge=CSharpGameEngine.Generated.GeneratedEditorPreviewBridge;
namespace CSharpGameEngine.Windows.Tests.Runtime;
/// <summary>
/// ADR-0070 B1 step 3: the preview host opens a runtime view of a built game over the preview
/// protocol. It loads the game into a context of its own and draws exactly what the game's bridge
/// draws from the same camera.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class RenderPreviewRuntimeSessionTests {
    private const string EditedScene="UserEcsProject.Scenes.PrimitiveShowcaseBoxScene";
    private static string GameAssembly=>Path.Combine(AppContext.BaseDirectory,"UserEcsProject.dll");

    [WindowsOnlyFact]
    public void RuntimeSession_RendersWhatTheGamesBridgeRendersFromTheSameCamera(){
        using var Surface=CreateSurface();
        string BridgeHash;
        Assert.True(PreviewBridge.InitializeRuntimeView(EditedScene,AppContext.BaseDirectory,Surface.Handle,160,120,UseWarp:true));
        try {
            Assert.True(PreviewBridge.RenderRuntimeView(3f,0.5f,-4f,0f,0f,0f,1f,MathF.PI/4f,0.1f,300f));
            BridgeHash=Convert.ToHexString(SHA256.HashData(PreviewBridge.CaptureRuntimeView()!.Pixels));
        } finally {
            PreviewBridge.ShutdownRuntimeView();
        }

        using RenderPreviewHostSessionTable Sessions=new(RenderPreviewHostServer.Open);
        var Open=CreateOpen(Surface,GameAssembly,EditedScene);
        var Opened=Sessions.Handle(Open);
        Assert.True(Opened.State==RenderPreviewHostResultState.Succeeded,Opened.Message);
        var Framed=Sessions.Handle(new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),SessionId=Open.SessionId,Sequence=2,Command=RenderPreviewHostCommand.UpdateFrame,
            Frame=new RenderPreviewFrame { Camera=Camera(3f,0.5f,-4f) },
        });
        Assert.True(Framed.State==RenderPreviewHostResultState.Succeeded,Framed.Message);
        var Rendered=Sessions.Handle(new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),SessionId=Open.SessionId,Sequence=3,Command=RenderPreviewHostCommand.Render,
            CaptureForVerification=true,
        });

        Assert.True(Rendered.State==RenderPreviewHostResultState.Succeeded,Rendered.Message);
        Assert.Equal(BridgeHash,Rendered.FrameHash);
        var Closed=Sessions.Handle(new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),SessionId=Open.SessionId,Sequence=4,Command=RenderPreviewHostCommand.Close,
        });
        Assert.Equal(RenderPreviewHostResultState.Succeeded,Closed.State);
    }

    [WindowsOnlyFact]
    public void SceneDocument_EditsValuesLiveAndFlagsStructureForABuild(){
        using var Surface=CreateSurface();
        var Original=File.ReadAllText(Path.Combine(RepositoryRoot(),"UserEcsProject","Scenes","PrimitiveShowcaseBoxScene.scene.json"));
        var Moved=Original.Replace("\"Position\": { \"R\": 3, \"U\": 0.5, \"F\": 0 }","\"Position\": { \"R\": 3, \"U\": 0.5, \"F\": 3 }");
        Assert.NotEqual(Original,Moved);
        var Open=CreateOpen(Surface,GameAssembly,EditedScene);
        Open.SceneJson=Original;
        using var Session=RenderPreviewRuntimeSession.Open(Open);
        Session.UpdateFrame(new RenderPreviewFrame { Camera=Camera(3f,0.5f,-4f) });
        string Frame(){
            Session.Render(CaptureForVerification:true);
            return Session.LastFrameHash;
        }
        Frame();
        var Before=Frame();
        Assert.Equal(Before,Frame());
        Assert.False(Session.BuildRequired);

        Session.ReplaceScene(Open.Scene!,Moved);
        var AfterMove=Frame();
        Session.ReplaceScene(Open.Scene!,Original);
        var Restored=Frame();

        Assert.False(Session.BuildRequired);
        Assert.NotEqual(Before,AfterMove);
        Assert.Equal(Before,Restored);
        var AddedChild=Original.Replace("\"children\": []","\"children\": [ { \"id\": \"7b1c2f4e-0d3a-4c7e-9f1b-2a6d8e5c3b10\", \"components\": [], \"children\": [] } ]");
        Session.ReplaceScene(Open.Scene!,AddedChild);
        Assert.True(Session.BuildRequired);
        Session.ReplaceScene(Open.Scene!,Original);
        Assert.False(Session.BuildRequired);
        Session.ReplaceScene(Open.Scene!,"{ not json");
        Assert.False(Session.BuildRequired);
    }

    [WindowsOnlyFact]
    public void SelectionAndGizmos_AreDrawnOverTheRuntimeImage(){
        using var Surface=CreateSurface();
        var Open=CreateOpen(Surface,GameAssembly,EditedScene);
        Open.SceneJson=File.ReadAllText(Path.Combine(RepositoryRoot(),"UserEcsProject","Scenes","PrimitiveShowcaseBoxScene.scene.json"));
        using var Session=RenderPreviewRuntimeSession.Open(Open);
        string Frame(RenderPreviewFrame Update){
            Session.UpdateFrame(Update);
            Session.Render(CaptureForVerification:false);
            Session.Render(CaptureForVerification:true);
            return Session.LastFrameHash;
        }
        var Plain=Frame(new RenderPreviewFrame { Camera=Camera(3f,0.5f,-4f) });

        var Selected=Frame(new RenderPreviewFrame {
            Camera=Camera(3f,0.5f,-4f),
            Objects=[new RenderPreviewObjectTransform { ObjectId="(root)",SelectionWeight=1f }],
        });
        // A green triangle in world space, just in front of the camera at (3, 0.5, -4).
        var Gizmo=Frame(new RenderPreviewFrame {
            Camera=Camera(3f,0.5f,-4f),
            OverlayTriangles=[
                new RenderPreviewOverlayVertex { Position=[2.9f,0.4f,-3f],Color=[0f,1f,0f,1f] },
                new RenderPreviewOverlayVertex { Position=[3.1f,0.4f,-3f],Color=[0f,1f,0f,1f] },
                new RenderPreviewOverlayVertex { Position=[3f,0.6f,-3f],Color=[0f,1f,0f,1f] },
            ],
        });
        var Cleared=Frame(new RenderPreviewFrame { Camera=Camera(3f,0.5f,-4f) });

        Assert.NotEqual(Plain,Selected);
        Assert.NotEqual(Plain,Gizmo);
        Assert.Equal(Plain,Cleared);
    }

    [WindowsOnlyFact]
    public void RuntimeSession_ExplainsAMissingSceneOrAGameWithoutARuntimeView(){
        using var Surface=CreateSurface();
        var MissingScene=Assert.Throws<InvalidOperationException>(()=>
            RenderPreviewRuntimeSession.Open(CreateOpen(Surface,GameAssembly,"UserEcsProject.Scenes.NotBuiltYet")));
        Assert.Contains("build",MissingScene.Message,StringComparison.OrdinalIgnoreCase);
        var NoBridge=Assert.Throws<InvalidOperationException>(()=>
            RenderPreviewRuntimeSession.Open(CreateOpen(Surface,Path.Combine(AppContext.BaseDirectory,"CSharpGameEngine.Contracts.dll"),EditedScene)));
        Assert.Contains("runtime view",NoBridge.Message,StringComparison.Ordinal);
    }

    [Fact]
    public void Protocol_AllowsARuntimeViewOnlyOnOpenAndRequiresItsAssemblyAndScene(){
        var Open=CreateOpen(null,GameAssembly,EditedScene);
        Assert.Null(RenderPreviewHostProtocol.Validate(Open));
        Open.RuntimeView!.SceneName="";
        Assert.NotNull(RenderPreviewHostProtocol.Validate(Open));
        Open.RuntimeView=new RenderPreviewRuntimeView { AssemblyPath="Game.exe",SceneName=EditedScene };
        Assert.NotNull(RenderPreviewHostProtocol.Validate(Open));
        Assert.NotNull(RenderPreviewHostProtocol.Validate(new RenderPreviewHostRequest {
            RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=2,Command=RenderPreviewHostCommand.Render,
            RuntimeView=new RenderPreviewRuntimeView { AssemblyPath=GameAssembly,SceneName=EditedScene },
        }));
    }

    private static string RepositoryRoot(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new DirectoryNotFoundException();
    }

    private static Win32Window CreateSurface()=>new(new Win32WindowOptions {
        Title="CSharpGameEngine Runtime Preview Session Test",Width=160,Height=120,Visible=false,
    });

    private static RenderPreviewCamera Camera(float R,float U,float F)=>new() {
        Position=[R,U,F],Rotation=[0f,0f,0f,1f],FovYRadians=MathF.PI/4f,NearZ=0.1f,FarZ=300f,
    };

    private static RenderPreviewHostRequest CreateOpen(Win32Window?Surface,string AssemblyPath,string SceneName)=>new() {
        RequestId=Guid.NewGuid(),
        SessionId=Guid.NewGuid(),
        Sequence=1,
        Command=RenderPreviewHostCommand.Open,
        SurfaceHandle=Surface?.Handle??1,
        Width=Surface?.ClientWidth??160,
        Height=Surface?.ClientHeight??120,
        ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact"),
        Scene=new RenderPreviewScene { Camera=Camera(3f,0.5f,-4f) },
        RuntimeView=new RenderPreviewRuntimeView { AssemblyPath=AssemblyPath,SceneName=SceneName,UseWarp=true },
    };
}
