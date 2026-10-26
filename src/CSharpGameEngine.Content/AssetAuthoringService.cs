using System.Security.Cryptography;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Content;

public static class AssetAuthoringService {
    private const int MaximumMetadataBytes=64*1024;

    public static AssetAuthoringResult Execute(AssetAuthoringRequest Request){
        string?RequestError=AssetAuthoringProtocol.Validate(Request);
        if(RequestError is not null){ return Rejected(Request?.RequestId??Guid.Empty,"ASSET_REQUEST_INVALID",RequestError); }
        try {
            AuthoringContext Context=ResolveContext(Request);
            if(Request.Command==AssetAuthoringCommand.MigrateLegacyMetadata){
                var Failure=TryMigrateLegacy(Request,Context);
                if(Failure is not null){ return Failure; }
            }
            if(Request.Command==AssetAuthoringCommand.SaveMetadata){
                AssetAuthoringResult?Failure=TrySave(Request,Context);
                if(Failure is not null){ return Failure; }
            }
            return Inspect(Request.RequestId,Context);
        } catch(Exception Ex)when(Ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){
            return Rejected(Request.RequestId,"ASSET_AUTHORING_FAILED",Ex.Message);
        }
    }

    private static AuthoringContext ResolveContext(AssetAuthoringRequest Request){
        string Root=Path.GetFullPath(Request.RepositoryRoot);
        string Source=Path.GetFullPath(Request.SourcePath);
        string RootPrefix=Root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!Source.StartsWith(RootPrefix,StringComparison.OrdinalIgnoreCase)){ throw new ArgumentException("Asset source must be inside the repository root."); }
        if(!File.Exists(Source)){ throw new ArgumentException("Asset source does not exist."); }
        string Relative=Path.GetRelativePath(Root,Source).Replace(Path.DirectorySeparatorChar,'/').Replace(Path.AltDirectorySeparatorChar,'/');
        if(Relative.StartsWith("../",StringComparison.Ordinal)||Path.IsPathRooted(Relative)){ throw new ArgumentException("Asset source URI escapes the repository root."); }
        string Importer=Path.GetExtension(Source).ToLowerInvariant()switch {
            ".png" or ".jpg" or ".jpeg"=>ImageImportSettings.Importer,
            ".fbx"=>"AssimpNet",
            ".glb" or ".gltf"=>"SharpGLTF",
            _=>throw new NotSupportedException("Asset authoring supports .fbx, .glb, and .gltf sources."),
        };
        return new AuthoringContext(Source,Source+".meta",Relative,Importer);
    }

    private static AssetAuthoringResult Inspect(Guid RequestId,AuthoringContext Context){
        if(!File.Exists(Context.MetadataPath)){
            AssetId Candidate=new(StableId.New());
            return Success(RequestId,AssetAuthoringState.MissingMetadata,Context,Candidate,Context.Importer==ImageImportSettings.Importer?ImageImportSettings.Initial(Context.SourcePath).ToOptions():[],string.Empty,[Info("ASSET_METADATA_MISSING","Canonical metadata has not been created yet.")]);
        }
        byte[]Bytes=ReadBoundedMetadata(Context.MetadataPath);
        if(!AssetMetadata.TryParse(Bytes,out AssetMetadata?Metadata,out AssetMetadataError Error)||Metadata is null){
            var Failure=Rejected(RequestId,"ASSET_METADATA_INVALID",$"{Error.Field}: {Error.Message}",Context.MetadataPath);
            Failure.MetadataSha256=ComputeSha256(Bytes);
            return Failure;
        }
        if(Metadata.SourceUri!=Context.SourceUri){ return Rejected(RequestId,"ASSET_SOURCE_MISMATCH",$"Metadata sourceUri must be {Context.SourceUri}.",Context.MetadataPath); }
        if(Metadata.Importer!=Context.Importer){ return Rejected(RequestId,"ASSET_IMPORTER_MISMATCH",$"Metadata importer must be {Context.Importer} for this source type.",Context.MetadataPath); }
        return Success(RequestId,AssetAuthoringState.Ready,Context,Metadata.AssetId,Metadata.Options,ComputeSha256(Bytes),[]);
    }

    private static AssetAuthoringResult?TrySave(AssetAuthoringRequest Request,AuthoringContext Context){
        bool Exists=File.Exists(Context.MetadataPath);
        string ActualHash=Exists?ComputeSha256(ReadBoundedMetadata(Context.MetadataPath)):string.Empty;
        if(ActualHash!=Request.ExpectedMetadataSha256){ return Rejected(Request.RequestId,"ASSET_METADATA_CONFLICT","Metadata changed after it was inspected; refresh before saving.",Context.MetadataPath); }
        if(!AssetId.TryParse(Request.AssetId,out AssetId Asset)||!Asset.IsValid){ return Rejected(Request.RequestId,"ASSET_ID_INVALID","AssetId is invalid.",Context.MetadataPath); }
        if(Request.Importer!=Context.Importer){ return Rejected(Request.RequestId,"ASSET_IMPORTER_INVALID",$"Importer must be {Context.Importer}.",Context.MetadataPath); }
        if(Exists){
            byte[]Current=ReadBoundedMetadata(Context.MetadataPath);
            if(!AssetMetadata.TryParse(Current,out AssetMetadata?Existing,out AssetMetadataError Error)||Existing is null){ return Rejected(Request.RequestId,"ASSET_METADATA_INVALID",$"{Error.Field}: {Error.Message}",Context.MetadataPath); }
            if(Existing.AssetId!=Asset){ return Rejected(Request.RequestId,"ASSET_ID_IMMUTABLE","An existing AssetId cannot be changed.",Context.MetadataPath); }
        }
        IEnumerable<KeyValuePair<string,string>>Options=(Request.Options??[]).Select(static Option=>new KeyValuePair<string,string>(Option.Name,Option.Value));
        if(Context.Importer==ImageImportSettings.Importer){
            var Settings=ImageImportSettings.FromOptions(Options.ToDictionary(P=>P.Key,P=>P.Value,StringComparer.Ordinal));
            if(Exists){
                AssetMetadata.TryParse(ReadBoundedMetadata(Context.MetadataPath),out var Previous,out _);
                var Old=ImageImportSettings.FromOptions(Previous!.Options);
                foreach(var Output in Old.Outputs){
                    if(!Settings.Outputs.Any(O=>O.Key==Output.Key&&O.Kind==Output.Kind)){ return Rejected(Request.RequestId,"IMAGE_REFERENCE_REVIEW_REQUIRED","Removing or changing an output kind requires reference replacement.",Context.MetadataPath); }
                }
            }
        }
        AssetMetadata Metadata=AssetMetadata.Create(Asset,Context.SourceUri,Context.Importer,Options);
        byte[]Canonical=Metadata.ToCanonicalUtf8();
        AtomicWrite(Context.MetadataPath,Canonical);
        return null;
    }
    private static AssetAuthoringResult?TryMigrateLegacy(AssetAuthoringRequest Request,AuthoringContext Context){
        var Bytes=ReadBoundedMetadata(Context.MetadataPath);
        if(ComputeSha256(Bytes)!=Request.ExpectedMetadataSha256){ return Rejected(Request.RequestId,"ASSET_METADATA_CONFLICT","Metadata changed after inspection.",Context.MetadataPath); }
        var Text=new System.Text.UTF8Encoding(false,true).GetString(Bytes).Trim();
        if(!Text.StartsWith("guid: ",StringComparison.Ordinal)||!Guid.TryParse(Text[6..],out var LegacyId)||LegacyId==Guid.Empty){
            return Rejected(Request.RequestId,"ASSET_MIGRATION_FORMAT","Expected exactly one legacy guid line; no files were changed.",Context.MetadataPath);
        }
        var Metadata=AssetMetadata.Create(AssetId.Parse(LegacyId.ToString("D")),Context.SourceUri,Context.Importer);
        AtomicWrite(Context.MetadataPath,Metadata.ToCanonicalUtf8());
        return null;
    }

    private static byte[]ReadBoundedMetadata(string PathValue){
        FileInfo Information=new(PathValue);
        if(Information.Length<1||Information.Length>MaximumMetadataBytes){ throw new InvalidDataException("Asset metadata length is outside the supported range."); }
        byte[]Bytes=File.ReadAllBytes(PathValue);
        if(Bytes.Length!=Information.Length){ throw new IOException("Asset metadata changed while it was read."); }
        return Bytes;
    }

    private static void AtomicWrite(string PathValue,byte[]Bytes){
        string DirectoryPath=Path.GetDirectoryName(PathValue)??throw new InvalidOperationException("Asset metadata directory is unavailable.");
        Directory.CreateDirectory(DirectoryPath);
        string Temporary=Path.Combine(DirectoryPath,"."+Path.GetFileName(PathValue)+"."+Guid.NewGuid().ToString("N")+".tmp");
        try {
            using(FileStream Stream=new(Temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){
                Stream.Write(Bytes);
                Stream.Flush(true);
            }
            if(File.Exists(PathValue)){ File.Replace(Temporary,PathValue,null); }
            else { File.Move(Temporary,PathValue); }
        } finally {
            if(File.Exists(Temporary)){ File.Delete(Temporary); }
        }
    }

    private static AssetAuthoringResult Success(Guid RequestId,AssetAuthoringState State,AuthoringContext Context,AssetId Asset,IEnumerable<KeyValuePair<string,string>>Options,string Hash,AuthoringHostDiagnostic[]Diagnostics)=>new(){
        RequestId=RequestId,State=State,Success=true,SourceUri=Context.SourceUri,MetadataPath=Context.MetadataPath,
        AssetId=Asset.ToString(),AssetUri="asset://"+Asset,Importer=Context.Importer,
        Options=Options.OrderBy(static Pair=>Pair.Key,StringComparer.Ordinal).Select(static Pair=>new AssetAuthoringOption {Name=Pair.Key,Value=Pair.Value}).ToArray(),
        MetadataSha256=Hash,Diagnostics=Diagnostics,
    };

    private static AssetAuthoringResult Rejected(Guid RequestId,string Code,string Message,string MetadataPath="")=>new(){
        RequestId=RequestId,State=AssetAuthoringState.Rejected,Success=false,MetadataPath=MetadataPath,
        Diagnostics=[new AuthoringHostDiagnostic {Severity=AuthoringHostDiagnosticSeverity.Error,Code=Code,Message=Message}],
    };
    private static AuthoringHostDiagnostic Info(string Code,string Message)=>new(){Severity=AuthoringHostDiagnosticSeverity.Info,Code=Code,Message=Message};
    private static string ComputeSha256(byte[]Bytes)=>Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
    private sealed record AuthoringContext(string SourcePath,string MetadataPath,string SourceUri,string Importer);
}
