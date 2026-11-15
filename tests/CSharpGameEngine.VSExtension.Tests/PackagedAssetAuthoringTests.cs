using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class PackagedAssetAuthoringTests {
    [Fact]
    public Task EmptyFontProducesADiagnosticThroughThePackagedBridge()=>WithPackageAsync(async(Root,PackageRoot)=>{
        var Source=Path.Combine(Root,"Empty.cgefont");File.WriteAllBytes(Source,[]);
        var Request=new AssetWorkspaceRequest { WorkspaceCommand=2,RequestId=Guid.NewGuid(),Sources=[new(){ SourcePath=Source }] };
        var Input=Path.Combine(Root,"request.json");var Output=Path.Combine(Root,"response.json");File.WriteAllText(Input,JsonSerializer.Serialize(Request));
        await RunAsync(PackageRoot,Input,Output);
        var Result=JsonSerializer.Deserialize<AssetWorkspaceResult>(File.ReadAllText(Output))!;
        Assert.Equal(Request.RequestId,Result.RequestId);Assert.False(Result.Success);Assert.Contains("ASSET_FONT_PREVIEW_SIZE_INVALID",Result.Diagnostic);Assert.Empty(File.ReadAllBytes(Source));
    });
    [Fact]
    public Task PublishedModelPreviewUsesItsOwnCookWorkerOutsideTheRepository()=>WithPackageAsync(async(Root,PackageRoot)=>{
        var Assets=Path.Combine(Root,"Game","Assets");Directory.CreateDirectory(Assets);var Source=Path.Combine(Assets,"Box.glb");
        var Sample=Directory.EnumerateFiles(Path.Combine(PackageRoot,"ProjectTemplateSources","ThirdPerson","Assets","Stage"),"SM_Cube_*.glb",SearchOption.AllDirectories).First();File.Copy(Sample,Source);
        var Request=new AssetModelRequest{ModelCommand=4,RequestId=Guid.NewGuid(),Root=Path.GetDirectoryName(Assets)!,SourcePath=Source,PreviewSourcePaths=[Source]};
        var Input=Path.Combine(Root,"request.json");var Output=Path.Combine(Root,"response.json");File.WriteAllText(Input,JsonSerializer.Serialize(Request));
        await RunAsync(PackageRoot,Input,Output);
        var Result=JsonSerializer.Deserialize<AssetModelResult>(File.ReadAllText(Output))!;
        Assert.Equal(Request.RequestId,Result.RequestId);Assert.True(Result.Success,Result.Diagnostic);
        var Prepared=Assert.Single(Result.PreparedSources);Assert.Equal(Source,Prepared.SourcePath);Assert.NotEmpty(Prepared.ModelHash);Assert.True(Directory.Exists(Prepared.ContentDirectory));Assert.Null(RenderPreviewSkeletalValidation.ValidateModel(Prepared,Source));
        File.Delete(Source);File.WriteAllText(Input,JsonSerializer.Serialize(new{ModelGeometry=Prepared}));await RunAsync(PackageRoot,Input,Output);
        var Scene=JsonSerializer.Deserialize<RenderPreviewScene>(File.ReadAllText(Output))!;Assert.NotEmpty(Scene.Objects);Assert.NotEmpty(Scene.Meshes);Assert.NotEmpty(Scene.Materials);
        var Mesh=SimpleMesh.FromPreparedScene(Scene);Assert.NotEmpty(Mesh.Positions);Assert.True(Mesh.BoundsRadius>0f);
    });
    private static async Task WithPackageAsync(Func<string,string,Task>Verify){
        var Root=Path.Combine(Path.GetTempPath(),"CgePackagedAuthoring-"+Guid.NewGuid().ToString("N"));
        var PackageRoot=Path.Combine(Root,"package");
        Directory.CreateDirectory(Root);
        try {
            ZipFile.ExtractToDirectory(Path.Combine(AppContext.BaseDirectory,"TestAssets","VSExtension.vsix"),PackageRoot);
            await Verify(Root,PackageRoot);
        } finally {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(Root),StringComparison.OrdinalIgnoreCase);
            Directory.Delete(Root,true);
        }
    }
    private static async Task RunAsync(string PackageRoot,string Input,string Output){
        var Start=new ProcessStartInfo(Path.Combine(PackageRoot,"Bridges","Import.AssetAuthoring.Cli","Import.AssetAuthoring.Cli.exe")){
            Arguments="\""+Input+"\" \""+Output+"\"",WorkingDirectory=Path.GetDirectoryName(Input),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
        };
        using var Process=System.Diagnostics.Process.Start(Start)??throw new InvalidOperationException("Packaged bridge did not start.");
        try {
            var StandardOutput=Process.StandardOutput.ReadToEndAsync();var StandardError=Process.StandardError.ReadToEndAsync();
            Assert.True(await Task.Run(()=>Process.WaitForExit(120000)),"Packaged bridge timed out.");Assert.True(Process.ExitCode==0,(await StandardOutput)+(await StandardError));
        } finally { if(!Process.HasExited){ Process.Kill();Process.WaitForExit(5000); } }
    }
}
