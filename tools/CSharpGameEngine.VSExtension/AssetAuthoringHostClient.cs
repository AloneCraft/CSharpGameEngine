using System;
using System.IO;
using System.Text.Json;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Thin net472 client for the Content-owned asset authoring host. The VSIX transports typed DTOs
/// only; it never parses or writes canonical metadata itself.
/// </summary>
internal static class AssetAuthoringHostClient {
    private const string BridgeProjectRelativePath=@"tools\CSharpGameEngine.Import.AssetAuthoring.Cli";
    private static readonly TimeSpan BridgeTimeout=TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions=new(){ PropertyNameCaseInsensitive=false };

    public static AssetAuthoringResult Inspect(string SourcePath){
        string RepoRoot=ResolveWorkspaceRoot(SourcePath);
        return Execute(new AssetAuthoringRequest {
            RequestId=Guid.NewGuid(),Command=AssetAuthoringCommand.Inspect,
            RepositoryRoot=RepoRoot,SourcePath=SourcePath,
        });
    }

    public static AssetAuthoringResult SaveMetadata(
        string SourcePath,AssetAuthoringResult Snapshot){
        if(Snapshot is null){ throw new ArgumentNullException(nameof(Snapshot)); }
        string RepoRoot=ResolveWorkspaceRoot(SourcePath);
        return Execute(new AssetAuthoringRequest {
            RequestId=Guid.NewGuid(),Command=AssetAuthoringCommand.SaveMetadata,
            RepositoryRoot=RepoRoot,SourcePath=SourcePath,
            ExpectedMetadataSha256=Snapshot.MetadataSha256,
            AssetId=Snapshot.AssetId,Importer=Snapshot.Importer,
            Options=CloneOptions(Snapshot.Options),
        });
    }

    internal static AssetAuthoringResult DeserializeValidatedResult(
        AssetAuthoringRequest Request,string Json){
        AssetAuthoringResult?Result=JsonSerializer.Deserialize<AssetAuthoringResult>(Json,JsonOptions);
        string?Error=AssetAuthoringProtocol.Validate(Request,Result!);
        if(Error is not null){ throw new InvalidDataException(Error); }
        return Result!;
    }

    private static AssetAuthoringResult Execute(AssetAuthoringRequest Request){
        string?RequestError=AssetAuthoringProtocol.Validate(Request);
        if(RequestError is not null){ throw new InvalidOperationException(RequestError); }
        string TemporaryRoot=Path.Combine(
            Path.GetTempPath(),"CgeAssetAuthoring",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TemporaryRoot);
        string RequestPath=Path.Combine(TemporaryRoot,"request.json");
        string ResultPath=Path.Combine(TemporaryRoot,"result.json");
        try {
            File.WriteAllText(RequestPath,JsonSerializer.Serialize(Request,JsonOptions));
            string Json=ExternalBridgeProcess.RunAndReadJson(
                BridgeProjectRelativePath,RequestPath,ResultPath,Request.RepositoryRoot,
                BridgeTimeout,UseResidentServer:true);
            return DeserializeValidatedResult(Request,Json);
        } finally {
            try { Directory.Delete(TemporaryRoot,true); }
            catch(IOException){ }
            catch(UnauthorizedAccessException){ }
        }
    }

    /// <summary>The workspace of the game project that owns <paramref name="SourcePath"/> (ADR-0065).</summary>
    private static string ResolveWorkspaceRoot(string SourcePath)=>CgeProjectContext.Resolve(SourcePath)?.WorkspaceRoot
        ??throw new InvalidOperationException(VsixText.Get("Common_GameProjectNotFound"));

    private static AssetAuthoringOption[]CloneOptions(AssetAuthoringOption[]?Options){
        AssetAuthoringOption[]Source=Options??Array.Empty<AssetAuthoringOption>();
        AssetAuthoringOption[]Copy=new AssetAuthoringOption[Source.Length];
        for(var Index=0;Index<Source.Length;Index++){
            AssetAuthoringOption Option=Source[Index]
                ??throw new InvalidDataException("Asset authoring option is missing.");
            Copy[Index]=new AssetAuthoringOption {Name=Option.Name,Value=Option.Value};
        }
        return Copy;
    }
}
