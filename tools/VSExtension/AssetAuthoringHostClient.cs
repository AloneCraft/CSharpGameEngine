using System;
using System.IO;
using System.Text.Json;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Content が所有するアセット作成ホスト用の薄い net472 クライアント。VSIX は型付き DTO だけを転送し、正規メタデータを解析したり書き込んだりしない。
/// </summary>
internal static class AssetAuthoringHostClient {
    private static readonly BridgeTool Bridge=BridgeTool.AssetAuthoring;
    private static readonly TimeSpan BridgeTimeout=TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions=new(){ PropertyNameCaseInsensitive=false };

    internal static AssetImageResult Image(AssetImageRequest Request){
        var Root=Path.Combine(Path.GetTempPath(),"CgeImageAuthoring",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
        try {
            var Context=CgeProjectContext.Resolve(Request.SourcePath)??throw new InvalidOperationException("Game project unavailable.");
            BindRequest(Request,Context);
            var RequestPath=Path.Combine(Root,"request.json");var ResultPath=Path.Combine(Root,"result.json");
            File.WriteAllText(RequestPath,JsonSerializer.Serialize(Request,JsonOptions));
            var Json=ExternalBridgeProcess.RunAndReadJson(Bridge,RequestPath,ResultPath,Context.WorkspaceRoot,BridgeTimeout,UseResidentServer:true);
            var Result=JsonSerializer.Deserialize<AssetImageResult>(Json,JsonOptions)??throw new InvalidDataException("Image result missing.");
            if(Result.RequestId!=Request.RequestId){ throw new InvalidDataException("Image response identity mismatch."); }return Result;
        }finally { if(File.Exists(Path.Combine(Root,"request.json"))){ File.Delete(Path.Combine(Root,"request.json")); }if(File.Exists(Path.Combine(Root,"result.json"))){ File.Delete(Path.Combine(Root,"result.json")); }Directory.Delete(Root); }
    }
    internal static AssetModelResult Model(AssetModelRequest Request,System.Threading.CancellationToken CancellationToken=default){
        var Root=Path.Combine(Path.GetTempPath(),"CgeModelAuthoring",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
        try {
            var Context=CgeProjectContext.Resolve(Request.SourcePath)??throw new InvalidOperationException("Game project unavailable.");
            BindRequest(Request,Context);
            var RequestPath=Path.Combine(Root,"request.json");var ResultPath=Path.Combine(Root,"result.json");File.WriteAllText(RequestPath,JsonSerializer.Serialize(Request,JsonOptions));
            var Json=ExternalBridgeProcess.RunAndReadJson(Bridge,RequestPath,ResultPath,Context.WorkspaceRoot,Request.ModelCommand==4?TimeSpan.FromMinutes(10):BridgeTimeout,UseResidentServer:true,CancellationToken);
            var Result=JsonSerializer.Deserialize<AssetModelResult>(Json,JsonOptions)??throw new InvalidDataException("Model result missing.");
            if(Result.RequestId!=Request.RequestId){ throw new InvalidDataException("Model response mismatch."); }return Result;
        }finally { foreach(var FileName in new[]{"request.json","result.json"}){ var PathValue=Path.Combine(Root,FileName);if(File.Exists(PathValue)){ File.Delete(PathValue); } }Directory.Delete(Root); }
    }
    internal static void BindRequest(AssetImageRequest Request,CgeProjectContext Context){ Request.RequestId=Guid.NewGuid();Request.Root=Context.AssetMetadataRoot(Request.SourcePath); }
    internal static SimpleMesh Geometry(RenderPreviewModelSource Source,string WorkspaceRoot){
        var Root=Path.Combine(Path.GetTempPath(),"CgeModelGeometry",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
        try {
            var Input=Path.Combine(Root,"request.json");var Output=Path.Combine(Root,"result.json");
            File.WriteAllText(Input,JsonSerializer.Serialize(new{ModelGeometry=Source},JsonOptions));
            var Json=ExternalBridgeProcess.RunAndReadJson(Bridge,Input,Output,WorkspaceRoot,BridgeTimeout,UseResidentServer:true);
            return SimpleMesh.FromPreparedScene(JsonSerializer.Deserialize<RenderPreviewScene>(Json,JsonOptions)??throw new InvalidDataException("Prepared model geometry missing."));
        }finally { foreach(var Name in new[]{"request.json","result.json"}){ var PathValue=Path.Combine(Root,Name);if(File.Exists(PathValue)){ File.Delete(PathValue); } }Directory.Delete(Root); }
    }
    internal static void BindRequest(AssetModelRequest Request,CgeProjectContext Context){ Request.RequestId=Guid.NewGuid();Request.Root=Request.ModelCommand==4?Context.ProjectDirectory:Context.AssetMetadataRoot(Request.SourcePath); }
    internal static AssetAuthoringRequest CreateRequest(CgeProjectContext Context,string SourcePath,AssetAuthoringCommand Command)=>new(){RequestId=Guid.NewGuid(),Command=Command,RepositoryRoot=Context.AssetMetadataRoot(SourcePath),SourcePath=SourcePath};
    public static AssetAuthoringResult Inspect(string SourcePath){
        var Context=ResolveContext(SourcePath);
        return Execute(CreateRequest(Context,SourcePath,AssetAuthoringCommand.Inspect));
    }
    public static AssetAuthoringResult MigrateLegacy(string SourcePath,string ExpectedHash){
        var Context=ResolveContext(SourcePath);
        var Request=CreateRequest(Context,SourcePath,AssetAuthoringCommand.MigrateLegacyMetadata);
        Request.ExpectedMetadataSha256=ExpectedHash;
        return Execute(Request);
    }
    internal static AssetWorkspaceResult InspectWorkspace(AssetWorkspaceRequest Request,string WorkspaceRoot){
        var Root=Path.Combine(Path.GetTempPath(),"CgeAssetWorkspace",Request.RequestId.ToString("N"));
        Directory.CreateDirectory(Root);
        try {
            var RequestPath=Path.Combine(Root,"request.json");
            var ResultPath=Path.Combine(Root,"result.json");
            File.WriteAllText(RequestPath,JsonSerializer.Serialize(Request,JsonOptions));
            var Json=ExternalBridgeProcess.RunAndReadJson(Bridge,RequestPath,ResultPath,WorkspaceRoot,BridgeTimeout,UseResidentServer:true);
            var Result=JsonSerializer.Deserialize<AssetWorkspaceResult>(Json,JsonOptions);
            if(Result is null||Result.ProtocolVersion!=1||Result.RequestId!=Request.RequestId||Result.Items is null||Result.Items.Length>100_000){ throw new InvalidDataException("Invalid asset inventory response."); }
            if(!Result.Success){ throw new InvalidDataException(Result.Diagnostic); }
            if(Request.WorkspaceCommand==2&&AssetFontPreview.Validate(Result.FontPreview) is not null){
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
            var Json=ExternalBridgeProcess.RunAndReadJson(Bridge,Input,Output,WorkspaceRoot,BridgeTimeout,UseResidentServer:true);
            var Result=JsonSerializer.Deserialize<AssetFileMutationResult>(Json,JsonOptions);
            if(Result is null||Result.ProtocolVersion!=1||Result.RequestId!=Request.RequestId){ throw new InvalidDataException("Invalid asset mutation response."); }
            if(!Result.Success){ throw new InvalidOperationException(Result.Diagnostic); }
            return Result;
        } finally { try { Directory.Delete(Root,true); }catch(IOException){ }catch(UnauthorizedAccessException){ } }
    }

    public static AssetAuthoringResult SaveMetadata(
        string SourcePath,AssetAuthoringResult Snapshot){
        if(Snapshot is null){ throw new ArgumentNullException(nameof(Snapshot)); }
        var Context=ResolveContext(SourcePath);
        var Request=CreateRequest(Context,SourcePath,AssetAuthoringCommand.SaveMetadata);
        Request.ExpectedMetadataSha256=Snapshot.MetadataSha256;
        Request.AssetId=Snapshot.AssetId;Request.Importer=Snapshot.Importer;Request.Options=CloneOptions(Snapshot.Options);
        return Execute(Request);
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
                Bridge,RequestPath,ResultPath,Request.RepositoryRoot,
                BridgeTimeout,UseResidentServer:true);
            return DeserializeValidatedResult(Request,Json);
        } finally {
            try { Directory.Delete(TemporaryRoot,true); }
            catch(IOException){ }
            catch(UnauthorizedAccessException){ }
        }
    }

    /// <summary><paramref name="SourcePath"/> を所有するゲームプロジェクトのワークスペース (ADR-0065)。</summary>
    private static CgeProjectContext ResolveContext(string SourcePath)=>CgeProjectContext.Resolve(SourcePath)
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
