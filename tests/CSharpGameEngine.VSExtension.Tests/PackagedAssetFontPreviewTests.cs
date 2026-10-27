using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class PackagedAssetFontPreviewTests {
    [Fact]
    public async Task EmptyFontProducesADiagnosticThroughThePackagedBridge(){
        var Root=Path.Combine(Path.GetTempPath(),"CgePackagedFont-"+Guid.NewGuid().ToString("N"));
        var PackageRoot=Path.Combine(Root,"package");
        Directory.CreateDirectory(Root);
        try {
            ZipFile.ExtractToDirectory(Path.Combine(AppContext.BaseDirectory,"TestAssets","CSharpGameEngine.VSExtension.vsix"),PackageRoot);
            var Source=Path.Combine(Root,"Empty.cgefont");
            File.WriteAllBytes(Source,[]);
            var Request=new AssetWorkspaceRequest { WorkspaceCommand=2,RequestId=Guid.NewGuid(),Sources=[new(){ SourcePath=Source }] };
            var Input=Path.Combine(Root,"request.json");
            var Output=Path.Combine(Root,"response.json");
            File.WriteAllText(Input,JsonSerializer.Serialize(Request));
            var Start=new ProcessStartInfo(Path.Combine(PackageRoot,"Bridges","CSharpGameEngine.Import.AssetAuthoring.Cli","CSharpGameEngine.Import.AssetAuthoring.Cli.exe")){
                Arguments="\""+Input+"\" \""+Output+"\"",WorkingDirectory=Root,
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
            };
            using var Process=System.Diagnostics.Process.Start(Start)??throw new InvalidOperationException("Packaged bridge did not start.");
            try {
                var StandardOutput=Process.StandardOutput.ReadToEndAsync();
                var StandardError=Process.StandardError.ReadToEndAsync();
                Assert.True(await Task.Run(()=>Process.WaitForExit(120000)),"Packaged bridge timed out.");
                Assert.True(Process.ExitCode==0,(await StandardOutput)+(await StandardError));
                var Result=JsonSerializer.Deserialize<AssetWorkspaceResult>(File.ReadAllText(Output))!;
                Assert.Equal(Request.RequestId,Result.RequestId);
                Assert.False(Result.Success);
                Assert.Contains("ASSET_FONT_PREVIEW_SIZE_INVALID",Result.Diagnostic);
                Assert.Empty(File.ReadAllBytes(Source));
            } finally { if(!Process.HasExited){ Process.Kill();Process.WaitForExit(5000); } }
        } finally {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);
            Directory.Delete(Root,true);
        }
    }
}
