#if DEBUG
using System.Diagnostics;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.ShaderArtifacts;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
public sealed class ShaderArtifactHotReloadTests {
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Triangle_DebugHotReload_SwapsSuccessAndRetainsLastKnownGoodOnFailure() {
        var Source=Path.Combine(AppContext.BaseDirectory,"Shaders","Triangle.cgeshaderartifact");
        var DirectoryPath=Path.Combine(Path.GetTempPath(),"CgeShaderHotReload",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        var ArtifactPath=Path.Combine(DirectoryPath,"Triangle.cgeshaderartifact");
        File.Copy(Source,ArtifactPath);
        try {
            using Win32Window Window=new(new Win32WindowOptions { Title="CGE Shader Hot Reload Test",Width=128,Height=128,Visible=false });
            using var Renderer=TriangleRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ArtifactPath);
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
        Assert.True(ShaderArtifactSerializer.TryRead(Stream,ShaderArtifactTargetKind.Triangle,ShaderArtifactAbi.GetHash(ShaderArtifactTargetKind.Triangle),out var Artifact,out var Error),Error);
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
    private static void RenderUntil(Win32Window Window,TriangleRenderer Renderer,Func<bool>Predicate) {
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