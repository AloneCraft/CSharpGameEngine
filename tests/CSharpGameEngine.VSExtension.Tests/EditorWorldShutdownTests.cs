using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0095 3-6: stopping a Scene view's editor world (the preview host started with --assembly) ends the
/// process within seconds. The shutdown request used to wait forever: the host answered "ok" into a pipe
/// the extension opened write-only and never read, so neither side finished and the process stayed.
/// </summary>
public sealed class EditorWorldShutdownTests {
    [Fact]
    public async Task Stop_EndsTheEditorWorldWithinSeconds(){
        string Game=typeof(EditorWorldShutdownTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static Attribute=>Attribute.Key=="CgeRuntimeViewGame").Value!;
        string Root=Path.Combine(Path.GetTempPath(),$"CgeEditorWorld-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        ZipFile.ExtractToDirectory(Path.Combine(AppContext.BaseDirectory,"TestAssets","CSharpGameEngine.VSExtension.vsix"),Root);
        string HostPath=Path.Combine(Root,"PreviewHost","CSharpGameEngine.EditorPreview.Host.exe");
        var Start=PreviewHostProcessStartInfo.Create(HostPath,
            $"--assembly {PreviewHostProcessStartInfo.Quote(Game)} --scene UserEcsProject.Scenes.PrimitiveShowcaseBoxScene",
            Path.GetDirectoryName(Game));
        using var Host=Process.Start(Start)!;
        try {
            // Let the host reach its control pipe, as a Scene view's editor world has long before it is stopped.
            Assert.False(Host.WaitForExit(5000),"The editor world ended on its own.");
            var Clock=Stopwatch.StartNew();
            var Cleanup=Task.Run(()=>EditorWorldProcess.Stop(Host));
            Assert.True(await Task.WhenAny(Cleanup,Task.Delay(TimeSpan.FromSeconds(10)))==Cleanup,"Stopping the editor world did not finish within 10 s.");
            Assert.True(Clock.Elapsed<TimeSpan.FromSeconds(5),$"Stopping the editor world took {Clock.Elapsed.TotalMilliseconds} ms.");
        } finally {
            try { if(!Host.HasExited){ Host.Kill(); } } catch(InvalidOperationException){ }
            try { Directory.Delete(Root,recursive:true); } catch(IOException){ } catch(UnauthorizedAccessException){ }
        }
    }
}
