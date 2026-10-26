using System;
using System.IO;
using System.Text.Json;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Content が所有するアセット作成ホスト用の薄い net472 クライアント。VSIX は型付き DTO だけを転送し、正規メタデータを解析したり書き込んだりしない。
/// </summary>
internal static class AssetAuthoringHostClient {
    private const string BridgeProjectRelativePath=@"tools\CSharpGameEngine.Import.AssetAuthoring.Cli";
    private static readonly TimeSpan BridgeTimeout=TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions=new(){ PropertyNameCaseInsensitive=false };

    internal static AssetImageResult Image(AssetImageRequest Request){
        var Root=Path.Combine(Path.GetTempPath(),"CgeImageAuthoring",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
        try {
            Request.RequestId=Guid.NewGuid();
            var Context=CgeProjectContext.Resolve(Request.SourcePath)??throw new InvalidOperationException("Game project unavailable.");
            Request.Root=Context.WorkspaceRoot;
            var RequestPath=Path.Combine(Root,"request.json");var ResultPath=Path.Combine(Root,"result.json");
            File.WriteAllText(RequestPath,JsonSerializer.Serialize(Request,JsonOptions));
            var Json=ExternalBridgeProcess.RunAndReadJson(BridgeProjectRelativePath,RequestPath,ResultPath,Context.WorkspaceRoot,BridgeTimeout,UseResidentServer:true);
            var Result=JsonSerializer.Deserialize<AssetImageResult>(Json,JsonOptions)??throw new InvalidDataException("Image result missing.");
            if(Result.RequestId!=Request.RequestId){ throw new InvalidDataException("Image response identity mismatch."); }return Result;
        }finally { if(File.Exists(Path.Combine(Root,"request.json"))){ File.Delete(Path.Combine(Root,"request.json")); }if(File.Exists(Path.Combine(Root,"result.json"))){ File.Delete(Path.Combine(Root,"result.json")); }Directory.Delete(Root); }
    }
    internal static AssetModelResult Model(AssetModelRequest Request){
        var Root=Path.Combine(Path.GetTempPath(),"CgeModelAuthoring",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
        try {
            Request.RequestId=Guid.NewGuid();var Context=CgeProjectContext.Resolve(Request.SourcePath)??throw new InvalidOperationException("Game project unavailable.");Request.Root=Context.WorkspaceRoot;
            var RequestPath=Path.Combine(Root,"request.json");var ResultPath=Path.Combine(Root,"result.json");File.WriteAllText(RequestPath,JsonSerializer.Serialize(Request,JsonOptions));
            var Json=ExternalBridgeProcess.RunAndReadJson(BridgeProjectRelativePath,RequestPath,ResultPath,Context.WorkspaceRoot,BridgeTimeout,UseResidentServer:true);
            var Result=JsonSerializer.Deserialize<AssetModelResult>(Json,JsonOptions)??throw new InvalidDataException("Model result missing.");
            if(Result.RequestId!=Request.RequestId){ throw new InvalidDataException("Model response mismatch."); }return Result;
        }finally { foreach(var FileName in new[]{"request.json","result.json"}){ var PathValue=Path.Combine(Root,FileName);if(File.Exists(PathValue)){ File.Delete(PathValue); } }Directory.Delete(Root); }
    }
    public static AssetAuthoringResult Inspect(string SourcePath){
        string RepoRoot=ResolveWorkspaceRoot(SourcePath);
        return Execute(new AssetAuthoringRequest {
            RequestId=Guid.NewGuid(),Command=AssetAuthoringCommand.Inspect,
            RepositoryRoot=RepoRoot,SourcePath=SourcePath,
        });
    }
    public static AssetAuthoringResult MigrateLegacy(string SourcePath,string ExpectedHash)=>Execute(new AssetAuthoringRequest{
        RequestId=Guid.NewGuid(),Command=AssetAuthoringCommand.MigrateLegacyMetadata,
        RepositoryRoot=ResolveWorkspaceRoot(SourcePath),SourcePath=SourcePath,ExpectedMetadataSha256=ExpectedHash,
    });
    internal static AssetWorkspaceResult InspectWorkspace(AssetWorkspaceRequest Request,string WorkspaceRoot){
        var Root=Path.Combine(Path.GetTempPath(),"CgeAssetWorkspace",Request.RequestId.ToString("N"));
        Directory.CreateDirectory(Root);
        try {
            var RequestPath=Path.Combine(Root,"request.json");
            var ResultPath=Path.Combine(Root,"result.json");
            File.WriteAllText(RequestPath,JsonSerializer.Serialize(Request,JsonOptions));
            var Json=ExternalBridgeProcess.RunAndReadJson(BridgeProjectRelativePath,RequestPath,ResultPath,WorkspaceRoot,BridgeTimeout,UseResidentServer:true);
            var Result=JsonSerializer.Deserialize<AssetWorkspaceResult>(Json,JsonOptions);
            if(Result is null||Result.ProtocolVersion!=1||Result.RequestId!=Request.RequestId||Result.Items is null||Result.Items.Length>100_000){ throw new InvalidDataException("Invalid asset inventory response."); }
            if(!Result.Success){ throw new InvalidDataException(Result.Diagnostic); }
            if(Request.WorkspaceCommand==2&&(Result.FontPreview is not {Width:>0 and <=512,Height:>0 and <=512,SourceWidth:>0 and <=8192,SourceHeight:>0 and <=8192,GlyphCount:>0 and <=65536,Distance:{ }Pixels}Preview||Pixels.Length!=Preview.Width*Preview.Height)){
                throw new InvalidDataException("Invalid asset font preview response.");
            }
            if(Request.WorkspaceCommand==3&&Result.AudioPreview is not {SampleRate:>0,Channels:1 or 2,FrameCount:>0,Peaks:{Length:>0 and <=AssetAudioPreview.MaximumPeaks}}){
                throw new InvalidDataException("Invalid asset audio preview response.");
            }
            return Result;
        } finally {
            try { Directory.Delete(Root,true); }catch(IOException){ }catch(UnauthorizedAccessException){ }
        }
    }
    internal static AssetFileMutationResult MutateFiles(AssetFileMutationRequest Request,string WorkspaceRoot){
        var Root=Path.Combine(Path.GetTempPath(),"CgeAssetFiles",Request.RequestId.ToString("N"));
        Directory.CreateDirectory(Root);
        try {
            var Input=Path.Combine(Root,"request.json");var Output=Path.Combine(Root,"result.json");
            File.WriteAllText(Input,JsonSerializer.Serialize(Request,JsonOptions));
            var Json=ExternalBridgeProcess.RunAndReadJson(BridgeProjectRelativePath,Input,Output,WorkspaceRoot,BridgeTimeout,UseResidentServer:true);
            var Result=JsonSerializer.Deserialize<AssetFileMutationResult>(Json,JsonOptions);
            if(Result is null||Result.ProtocolVersion!=1||Result.RequestId!=Request.RequestId){ throw new InvalidDataException("Invalid asset mutation response."); }
            if(!Result.Success){ throw new InvalidOperationException(Result.Diagnostic); }
            return Result;
        } finally { try { Directory.Delete(Root,true); }catch(IOException){ }catch(UnauthorizedAccessException){ } }
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

    /// <summary><paramref name="SourcePath"/> を所有するゲームプロジェクトのワークスペース (ADR-0065)。</summary>
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
