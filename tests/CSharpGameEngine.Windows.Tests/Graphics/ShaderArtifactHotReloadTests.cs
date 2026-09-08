#if DEBUG
using System.Diagnostics;
using System.Numerics;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;
using CSharpGameEngine.ShaderArtifacts;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
public sealed class ShaderArtifactHotReloadTests {
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void RenderServer_DebugHotReload_SwapsSuccessAndRetainsLastKnownGoodOnFailure() {
        var Source=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
        var DirectoryPath=Path.Combine(Path.GetTempPath(),"CgeShaderHotReload",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        var ArtifactPath=Path.Combine(DirectoryPath,"Stage.cgeshaderartifact");
        File.Copy(Source,ArtifactPath);
        try {
            using Win32Window Window=new(new Win32WindowOptions { Title="CGE Shader Hot Reload Test",Width=128,Height=128,Visible=false });
            StageMaterialCreateInfo Material=new(){ BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f };
            (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,
                StageObjectCreateInfo[]Objects)=RenderServer.BuildSingleMeshRoster(
                [new Vector3(0f,0.6f,0f),new Vector3(-0.6f,-0.6f,0f),new Vector3(0.6f,-0.6f,0f)],
                [Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ],
                [new Vector2(0.5f,0f),new Vector2(0f,1f),new Vector2(1f,1f)],
                [0u,1u,2u],Material);
            using var Renderer=RenderServer.Create(
                Window.Handle,Window.ClientWidth,Window.ClientHeight,
                new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },
                ArtifactPath,Meshes,Materials,Objects);
            Assert.True(Renderer.ShaderArtifactLoaded);
            var InitialHash=Renderer.ShaderSemanticHash;
            var Artifact=ReadArtifact(ArtifactPath);
            Artifact.SemanticHash="HOT-RELOAD-1";
            WriteArtifactAtomic(ArtifactPath,Artifact);
            RenderUntil(Window,Renderer,()=>Renderer.ShaderSemanticHash=="HOT-RELOAD-1");
            Assert.NotEqual(InitialHash,Renderer.ShaderSemanticHash);
            var LastKnownGood=Renderer.ShaderSemanticHash;
            WriteBytesAtomic(ArtifactPath,[1,2,3,4]);
            RenderUntil(Window,Renderer,()=>Renderer.ShaderArtifactDiagnostics.Count>0);
            Assert.True(Renderer.ShaderArtifactLoaded);
            Assert.Equal(LastKnownGood,Renderer.ShaderSemanticHash);
            var FramesAfterFailure=Renderer.FramesRendered;
            Renderer.RenderFrame();
            Assert.True(Renderer.FramesRendered>FramesAfterFailure);
            Artifact.SemanticHash="HOT-RELOAD-2";
            WriteArtifactAtomic(ArtifactPath,Artifact);
            RenderUntil(Window,Renderer,()=>Renderer.ShaderSemanticHash=="HOT-RELOAD-2");
            Assert.Empty(Renderer.ShaderArtifactDiagnostics);
            Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
        } finally { Directory.Delete(DirectoryPath,true); }
    }
    private static ShaderArtifactDocument ReadArtifact(string PathValue) {
        using var Stream=File.OpenRead(PathValue);
        Assert.True(ShaderArtifactSerializer.TryRead(Stream,ShaderArtifactTargetKind.Stage,ShaderArtifactAbi.GetHash(ShaderArtifactTargetKind.Stage),out var Artifact,out var Error),Error);
        return Artifact!;
    }
    private static void WriteArtifactAtomic(string PathValue,ShaderArtifactDocument Artifact) {
        using var Buffer=new MemoryStream();
        ShaderArtifactSerializer.Write(Buffer,in Artifact,out var Error);
        Assert.Empty(Error);
        WriteBytesAtomic(PathValue,Buffer.ToArray());
    }
    private static void WriteBytesAtomic(string PathValue,byte[]Bytes) {
        var Temporary=PathValue+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllBytes(Temporary,Bytes);
        File.Move(Temporary,PathValue,true);
    }
    private static void RenderUntil(Win32Window Window,RenderServer Renderer,Func<bool>Predicate) {
        var Timeout=Stopwatch.StartNew();
        while(!Predicate()&&Timeout.Elapsed<TimeSpan.FromSeconds(5)) {
            Window.PumpEvents();
            Renderer.RenderFrame();
            Thread.Sleep(10);
        }
        Assert.True(Predicate(),string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));
    }
}
#endif
