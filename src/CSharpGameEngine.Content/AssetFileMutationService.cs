using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Content;
/// <summary>物理アセットとサイドカーの変更を担当する。参照テキストとネイティブなUndoはVSが担当する。</summary>
public static class AssetFileMutationService {
    public static AssetFileMutationResult Execute(AssetFileMutationRequest Request){
        try {
            Validate(Request);
            var OperationsRoot=Path.Combine(Path.GetFullPath(Request.Root),".vs","CgeAssetOperations");
            if(Request.FileMutationCommand==4){
                var Pending=new List<AssetFileRecoveryInfo>();
                if(Directory.Exists(OperationsRoot)){
                    foreach(var Folder in Directory.EnumerateDirectories(OperationsRoot)){
                        CheckPath(Request.Root,Path.Combine(Folder,"state"),false);
                        if(!File.Exists(Path.Combine(Folder,"state"))||File.ReadAllText(Path.Combine(Folder,"state"))!="Applying"){ continue; }
                        var Original=JsonSerializer.Deserialize<AssetFileMutationRequest>(File.ReadAllText(Path.Combine(Folder,"operation.json")));
                        if(Original is not null&&Original.Root==Request.Root){ Pending.Add(new(){OperationId=Original.OperationId,Description=Original.Operation+": "+Original.Source+" → "+Original.Destination}); }
                    }
                }
                return new(){RequestId=Request.RequestId,Success=true,RecoveryOperations=Pending.ToArray()};
            }
            if(Request.FileMutationCommand==1){
                return new(){RequestId=Request.RequestId,Success=true,SourceHash=Hash(Request.Source),MetadataHash=Hash(Sidecar(Request.Source))};
            }
            var Recovery=Path.Combine(Path.GetFullPath(Request.Root),".vs","CgeAssetOperations",Request.OperationId.ToString("N"));
            CheckPath(Request.Root,Path.Combine(Recovery,"operation.json"),false);
            if(Request.FileMutationCommand==3){
                File.WriteAllText(Path.Combine(Recovery,"state"),"Complete");return new(){RequestId=Request.RequestId,Success=true,OperationId=Request.OperationId};
            }
            if(Request.FileMutationCommand==5){
                var Original=JsonSerializer.Deserialize<AssetFileMutationRequest>(File.ReadAllText(Path.Combine(Recovery,"operation.json")))??throw new InvalidDataException("Missing recovery request.");
                if(Original.Root!=Request.Root||Original.OperationId!=Request.OperationId||File.ReadAllText(Path.Combine(Recovery,"state"))!="Applying"){ throw new InvalidOperationException("Recovery identity or state mismatch."); }
                Validate(Original);
                Recover(Original,Recovery);
                return new(){RequestId=Request.RequestId,Success=true,OperationId=Request.OperationId};
            }
            if(Request.Reverse){ Reverse(Request,Recovery); }
            else { Apply(Request,Recovery); }
            return new(){RequestId=Request.RequestId,Success=true,OperationId=Request.OperationId};
        }catch(Exception Error)when(Error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException){
            return new(){RequestId=Request?.RequestId??Guid.Empty,Diagnostic=Error.Message};
        }
    }
    private static void Validate(AssetFileMutationRequest Request){
        if(Request is null||Request.ProtocolVersion!=1||Request.RequestId==Guid.Empty||Request.FileMutationCommand is <1 or >5||!Enum.IsDefined(Request.Operation)
            ||Request.Root is null||Request.Source is null||Request.Destination is null||Request.Content is null||Request.ExpectedSourceHash is null||Request.ExpectedMetadataHash is null){ throw new ArgumentException("ASSET_FILE_REQUEST_INVALID"); }
        var Root=Path.GetFullPath(Request.Root);
        if(!System.IO.Directory.Exists(Root)){ throw new ArgumentException("Asset project root is missing."); }
        CheckPath(Root,Request.Destination,true);
        CheckPath(Root,Request.Source,Request.Operation is not(AssetFileOperation.Duplicate or AssetFileOperation.Copy));
        if(Request.Destination.Length>0){ CheckPath(Root,Sidecar(Request.Destination),true); }
        if(Request.Source.Length>0){ CheckPath(Root,Sidecar(Request.Source),Request.Operation is not(AssetFileOperation.Duplicate or AssetFileOperation.Copy)); }
        if(Request.FileMutationCommand is 2 or 3 or 5&&Request.OperationId==Guid.Empty){ throw new ArgumentException("Operation identity is required."); }
        if(Request.Content.Length>4*1024*1024){ throw new ArgumentException("Asset source text exceeds its limit."); }
        if(Request.Source.Length>0&&AssetWorkspaceClassification.Classify(Request.Source) is null&&!(Request.Operation==AssetFileOperation.UpdateMetadata&&Request.Source.EndsWith(".meta",StringComparison.OrdinalIgnoreCase))){ throw new ArgumentException("Unsupported asset source."); }
        if(Request.Operation==AssetFileOperation.Inspect&&Request.FileMutationCommand==2){ throw new ArgumentException("Inspect cannot mutate sources."); }
    }
    private static void CheckPath(string Root,string Value,bool RequireInside){
        if(string.IsNullOrEmpty(Value)){ return; }
        var Full=Path.GetFullPath(Value);
        if(RequireInside&&(!Full.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||AssetWorkspaceClassification.IsExcluded(Path.GetRelativePath(Root,Full)))){ throw new ArgumentException("Asset operation escapes the editable project sources."); }
        for(var Current=Path.GetDirectoryName(Full);Current is not null;Current=Path.GetDirectoryName(Current)){
            if(System.IO.Directory.Exists(Current)&&(File.GetAttributes(Current)&FileAttributes.ReparsePoint)!=0){ throw new ArgumentException("Asset operations cannot traverse a reparse point."); }
        }
        if(File.Exists(Full)&&(File.GetAttributes(Full)&(FileAttributes.ReadOnly|FileAttributes.ReparsePoint))!=0&&RequireInside){ throw new UnauthorizedAccessException("Asset is read-only."); }
    }
    private static void Apply(AssetFileMutationRequest Request,string Recovery){
        if(Hash(Request.Source)!=Request.ExpectedSourceHash||Hash(Sidecar(Request.Source))!=Request.ExpectedMetadataHash){ throw new IOException("ASSET_FILE_CONFLICT: source or metadata changed."); }
        var IsDelete=Request.Operation==AssetFileOperation.Delete;
        var IsCreate=Request.Operation==AssetFileOperation.Create;
        var IsCopy=Request.Operation is AssetFileOperation.Copy or AssetFileOperation.Duplicate;
        if(Request.Operation==AssetFileOperation.UpdateMetadata){
            if(Request.Source!=Request.Destination||!Request.Source.EndsWith(".meta",StringComparison.OrdinalIgnoreCase)||Request.Content.Length==0){ throw new InvalidDataException("ASSET_METADATA_EDIT_INVALID"); }
            if(!AssetMetadata.TryParse(File.ReadAllBytes(Request.Source),out var Before,out _)||Before is null
                ||!AssetMetadata.TryParse(Encoding.UTF8.GetBytes(Request.Content),out var After,out _)||After is null||Before.AssetId!=After.AssetId||Before.Importer!=After.Importer||Before.SourceUri!=After.SourceUri){
                throw new InvalidDataException("ASSET_METADATA_IDENTITY_IMMUTABLE");
            }
            if(After.Importer==ImageImportSettings.Importer){ _=ImageImportSettings.FromOptions(After.Options); }
            if(After.Importer=="AssimpNet"){ _=ModelImportSettings.FromOptions(After.Options); }
        }
        if(!IsCreate&&!IsDelete&&Path.GetExtension(Request.Source).Equals(".gltf",StringComparison.OrdinalIgnoreCase)){
            using var Gltf=JsonDocument.Parse(File.ReadAllBytes(Request.Source));
            foreach(var Name in new[]{"buffers","images"}){
                if(!Gltf.RootElement.TryGetProperty(Name,out var Entries)){ continue; }
                foreach(var Entry in Entries.EnumerateArray()){
                    if(!Entry.TryGetProperty("uri",out var UriValue)||UriValue.GetString() is not { }UriText||UriText.StartsWith("data:",StringComparison.Ordinal)){ continue; }
                    if(Uri.TryCreate(UriText,UriKind.Absolute,out _)){ continue; }
                    var SourceDependency=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Request.Source)!,Uri.UnescapeDataString(UriText)));
                    var DestinationDependency=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Request.Destination)!,Uri.UnescapeDataString(UriText)));
                    if(Hash(SourceDependency).Length==0||Hash(SourceDependency)!=Hash(DestinationDependency)){ throw new InvalidDataException("ASSET_DEPENDENCY_RELOCATION_REQUIRED: "+UriText); }
                }
            }
        }
        if(!IsDelete&&(File.Exists(Request.Destination)||System.IO.Directory.Exists(Request.Destination))
            &&!string.Equals(Request.Source,Request.Destination,StringComparison.OrdinalIgnoreCase)){ throw new IOException("Asset destination already exists."); }
        if(!IsCreate&&!File.Exists(Request.Source)){ throw new FileNotFoundException("Asset source is missing.",Request.Source); }
        if(!IsDelete&&File.Exists(Sidecar(Request.Destination))&&!string.Equals(Request.Source,Request.Destination,StringComparison.OrdinalIgnoreCase)){ throw new IOException("Destination metadata already exists."); }
        byte[]?NewMetadata=null;
        if(File.Exists(Sidecar(Request.Source))){
            var Bytes=File.ReadAllBytes(Sidecar(Request.Source));
            if(!AssetMetadata.TryParse(Bytes,out var Metadata,out var Error)||Metadata is null){ throw new InvalidDataException("Convert metadata before organizing the asset: "+Error.Message); }
            var MetadataRoot=string.IsNullOrEmpty(Request.MetadataRoot)?Request.Root:Request.MetadataRoot;
            if(!IsCopy&&Metadata.SourceUri!=Path.GetRelativePath(MetadataRoot,Request.Source).Replace('\\','/')){ throw new InvalidDataException("ASSET_SOURCE_MISMATCH"); }
            if(!IsDelete){
                var NewId=IsCopy?AssetId.Parse(Guid.NewGuid().ToString("D")):Metadata.AssetId;
                NewMetadata=AssetMetadata.Create(NewId,Path.GetRelativePath(MetadataRoot,Request.Destination).Replace('\\','/'),Metadata.Importer,Metadata.Options).ToCanonicalUtf8();
                if(File.Exists(Path.Combine(Recovery,"metadata.after"))){ NewMetadata=File.ReadAllBytes(Path.Combine(Recovery,"metadata.after")); }
            }
        }
        if(IsCopy&&NewMetadata is null&&ImageImportSettings.Supports(Request.Destination)){
            var MetadataRoot=string.IsNullOrEmpty(Request.MetadataRoot)?Request.Root:Request.MetadataRoot;
            var Settings=ImageImportSettings.Initial(Request.Destination,Request.TargetTextureUsage);
            NewMetadata=AssetMetadata.Create(AssetId.Parse(Guid.NewGuid().ToString("D")),Path.GetRelativePath(MetadataRoot,Request.Destination).Replace('\\','/'),ImageImportSettings.Importer,Settings.ToOptions()).ToCanonicalUtf8();
            if(File.Exists(Path.Combine(Recovery,"metadata.after"))){ NewMetadata=File.ReadAllBytes(Path.Combine(Recovery,"metadata.after")); }
        }
        if(IsCopy&&NewMetadata is null&&Path.GetExtension(Request.Destination).Equals(".fbx",StringComparison.OrdinalIgnoreCase)){
            var MetadataRoot=string.IsNullOrEmpty(Request.MetadataRoot)?Request.Root:Request.MetadataRoot;
            NewMetadata=AssetMetadata.Create(AssetId.Parse(Guid.NewGuid().ToString("D")),Path.GetRelativePath(MetadataRoot,Request.Destination).Replace('\\','/'),"AssimpNet",new ModelImportSettings().ToOptions()).ToCanonicalUtf8();
            if(File.Exists(Path.Combine(Recovery,"metadata.after"))){ NewMetadata=File.ReadAllBytes(Path.Combine(Recovery,"metadata.after")); }
        }
        System.IO.Directory.CreateDirectory(Recovery);
        CheckPath(Request.Root,Path.Combine(Recovery,"operation.json"),false);
        File.WriteAllText(Path.Combine(Recovery,"operation.json"),JsonSerializer.Serialize(Request));
        if(File.Exists(Sidecar(Request.Source))){ File.Copy(Sidecar(Request.Source),Path.Combine(Recovery,"metadata.before"),true); }
        if(Request.Content.Length>0&&File.Exists(Request.Source)){ File.Copy(Request.Source,Path.Combine(Recovery,"source.before"),true); }
        var AfterSourceHash=Request.Content.Length>0||IsCreate&&!Request.Directory?Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Request.Content))):Request.ExpectedSourceHash;
        File.WriteAllText(Path.Combine(Recovery,"source.after.sha256"),AfterSourceHash);
        File.WriteAllText(Path.Combine(Recovery,"metadata.after.sha256"),NewMetadata is null?string.Empty:Convert.ToHexStringLower(SHA256.HashData(NewMetadata)));
        if(NewMetadata is not null){ File.WriteAllBytes(Path.Combine(Recovery,"metadata.after"),NewMetadata); }
        File.WriteAllText(Path.Combine(Recovery,"state"),"Applying");
        try {
            if(IsCreate){
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Request.Destination)!);
                if(Request.Directory){ System.IO.Directory.CreateDirectory(Request.Destination); }
                else {
                    var Staged=Path.Combine(Recovery,"source.staged");
                    using(var Output=new FileStream(Staged,FileMode.Create,FileAccess.Write,FileShare.None)){ Output.Write(Encoding.UTF8.GetBytes(Request.Content));Output.Flush(true); }
                    File.Move(Staged,Request.Destination,false);
                }
            } else if(IsDelete){ File.Move(Request.Source,Path.Combine(Recovery,"source")); }
            else {
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Request.Destination)!);
                if(IsCopy){
                    var Staged=Path.Combine(Recovery,"source.staged");File.Copy(Request.Source,Staged,true);
                    File.SetAttributes(Staged,File.GetAttributes(Staged)&~FileAttributes.ReadOnly);
                    if(Hash(Staged)!=Request.ExpectedSourceHash){ throw new IOException("ASSET_FILE_CONFLICT: source changed during copy."); }
                    File.Move(Staged,Request.Destination,false);
                }
                else if(string.Equals(Request.Source,Request.Destination,StringComparison.OrdinalIgnoreCase)){
                    var Temporary=Path.Combine(Recovery,"case-rename");File.Move(Request.Source,Temporary);File.Move(Temporary,Request.Destination);
                } else { File.Move(Request.Source,Request.Destination); }
                if(Request.Content.Length>0){ WriteAtomic(Request.Destination,Encoding.UTF8.GetBytes(Request.Content),Path.Combine(Recovery,"source.staged")); }
            }
            if(!IsCopy&&!IsCreate&&File.Exists(Sidecar(Request.Source))){ File.Delete(Sidecar(Request.Source)); }
            if(NewMetadata is not null){ WriteAtomic(Sidecar(Request.Destination),NewMetadata,Path.Combine(Recovery,"metadata.staged")); }
            if(Hash(IsDelete?Path.Combine(Recovery,"source"):Request.Destination)!=AfterSourceHash
                ||Hash(Sidecar(Request.Destination))!=(NewMetadata is null?string.Empty:Convert.ToHexStringLower(SHA256.HashData(NewMetadata)))){ throw new IOException("ASSET_FILE_CONFLICT: operation outputs changed during apply."); }
        }catch(Exception Error){
            try { Recover(Request,Recovery); }
            catch(Exception RecoveryError)when(RecoveryError is IOException or UnauthorizedAccessException){
                throw new IOException("ASSET_RECOVERY_REQUIRED: rollback could not safely restore this operation.",new AggregateException(Error,RecoveryError));
            }
            File.WriteAllText(Path.Combine(Recovery,"state"),"RolledBack");
            throw;
        }
    }
    private static void WriteAtomic(string Destination,byte[]Bytes,string Staged){
        using(var Stream=new FileStream(Staged,FileMode.Create,FileAccess.Write,FileShare.None)){ Stream.Write(Bytes);Stream.Flush(true); }
        File.Move(Staged,Destination,true);
    }
    private static void Recover(AssetFileMutationRequest Original,string Recovery){
        var BeforeMetadata=Path.Combine(Recovery,"metadata.before");
        var BeforeSource=Path.Combine(Recovery,"source.before");
        var AfterSource=File.ReadAllText(Path.Combine(Recovery,"source.after.sha256"));
        var AfterMetadata=File.ReadAllText(Path.Combine(Recovery,"metadata.after.sha256"));
        if(Hash(BeforeMetadata)!=Original.ExpectedMetadataHash||File.Exists(BeforeSource)&&Hash(BeforeSource)!=Original.ExpectedSourceHash){ throw new IOException("ASSET_RECOVERY_CONFLICT: recovery snapshot changed."); }
        var IsCopy=Original.Operation is AssetFileOperation.Copy or AssetFileOperation.Duplicate;
        var IsCreate=Original.Operation==AssetFileOperation.Create;
        var IsDelete=Original.Operation==AssetFileOperation.Delete;
        var SamePath=string.Equals(Original.Source,Original.Destination,StringComparison.OrdinalIgnoreCase);
        var Current=IsDelete?Path.Combine(Recovery,"source"):Original.Destination;
        var Intermediate=Path.Combine(Recovery,"case-rename");
        if(File.Exists(Path.Combine(Recovery,"case-recover"))){
            if(File.Exists(Intermediate)){ throw new IOException("ASSET_RECOVERY_CONFLICT: multiple intermediate sources."); }
            Intermediate=Path.Combine(Recovery,"case-recover");
        }
        if(File.Exists(Intermediate)){
            if(!SamePath||File.Exists(Current)||File.Exists(Original.Source)){ throw new IOException("ASSET_RECOVERY_CONFLICT: multiple operation sources."); }
            Current=Intermediate;
        }
        var CurrentExists=File.Exists(Current);
        var SourceExists=File.Exists(Original.Source);
        if(!IsCopy&&!IsCreate){
            if(CurrentExists&&SourceExists&&!SamePath){ throw new IOException("ASSET_RECOVERY_CONFLICT: original source was recreated."); }
            if(CurrentExists){
                var CurrentHash=Hash(Current);
                if(CurrentHash!=Original.ExpectedSourceHash&&CurrentHash!=AfterSource||CurrentHash!=Original.ExpectedSourceHash&&!File.Exists(BeforeSource)){ throw new IOException("ASSET_RECOVERY_CONFLICT: operation source changed."); }
            } else if(!SourceExists||Hash(Original.Source)!=Original.ExpectedSourceHash){ throw new IOException("ASSET_RECOVERY_CONFLICT: original source is unavailable."); }
        } else if(CurrentExists&&Hash(Current)!=AfterSource){ throw new IOException("ASSET_RECOVERY_CONFLICT: new source changed."); }
        var SourceMetadata=Sidecar(Original.Source);
        var DestinationMetadata=Sidecar(Original.Destination);
        if(!IsCopy&&!IsCreate&&File.Exists(SourceMetadata)&&Hash(SourceMetadata)!=Original.ExpectedMetadataHash
            &&(!SamePath||Hash(SourceMetadata)!=AfterMetadata)){ throw new IOException("ASSET_RECOVERY_CONFLICT: original metadata changed."); }
        if(File.Exists(DestinationMetadata)&&Hash(DestinationMetadata)!=AfterMetadata
            &&(!SamePath||Hash(DestinationMetadata)!=Original.ExpectedMetadataHash)){ throw new IOException("ASSET_RECOVERY_CONFLICT: output metadata changed."); }
        if(Original.Directory&&System.IO.Directory.Exists(Original.Destination)&&System.IO.Directory.EnumerateFileSystemEntries(Original.Destination).Any()){ throw new IOException("ASSET_RECOVERY_CONFLICT: created folder is not empty."); }
        // 最初の変更を行う前に、すべての参加対象を検査する。復旧時は、各アトミックファイル書き込みの既知の変更前状態と
        // 変更後状態（大文字小文字だけが異なる名前変更の中間状態を含む）を受け入れる。
        if(IsCopy||IsCreate){
            if(Original.Directory&&System.IO.Directory.Exists(Original.Destination)){ System.IO.Directory.Delete(Original.Destination,false); }
            else if(CurrentExists){ File.Delete(Current); }
        } else if(CurrentExists){
            if(SamePath&&string.Equals(Current,Original.Destination,StringComparison.Ordinal)){
                var Staged=Path.Combine(Recovery,"case-recover");File.Move(Current,Staged);File.Move(Staged,Original.Source);
            } else { File.Move(Current,Original.Source); }
            if(File.Exists(BeforeSource)){ WriteAtomic(Original.Source,File.ReadAllBytes(BeforeSource),Path.Combine(Recovery,"source.restore")); }
        }
        if(File.Exists(DestinationMetadata)){ File.Delete(DestinationMetadata); }
        if(!IsCopy&&!IsCreate&&File.Exists(BeforeMetadata)){
            WriteAtomic(SourceMetadata,File.ReadAllBytes(BeforeMetadata),Path.Combine(Recovery,"metadata.restore"));
        }
        File.WriteAllText(Path.Combine(Recovery,"state"),"Undone");
    }
    private static void Reverse(AssetFileMutationRequest Request,string Recovery){
        var Original=JsonSerializer.Deserialize<AssetFileMutationRequest>(File.ReadAllText(Path.Combine(Recovery,"operation.json")))??throw new InvalidDataException("Missing recovery request.");
        Validate(Original);
        if(Original.OperationId!=Request.OperationId||Original.Operation!=Request.Operation||Original.Directory!=Request.Directory
            ||Original.Source!=Request.Source||Original.Destination!=Request.Destination||Original.Root!=Request.Root
            ||Original.MetadataRoot!=Request.MetadataRoot||Original.ExpectedSourceHash!=Request.ExpectedSourceHash
            ||Original.ExpectedMetadataHash!=Request.ExpectedMetadataHash||Original.Content!=Request.Content){ throw new InvalidOperationException("Recovery identity mismatch."); }
        var IsDelete=Original.Operation==AssetFileOperation.Delete;
        var Current=IsDelete?Path.Combine(Recovery,"source"):Original.Destination;
        if(Hash(Current)!=File.ReadAllText(Path.Combine(Recovery,"source.after.sha256"))
            ||Hash(Sidecar(Original.Destination))!=File.ReadAllText(Path.Combine(Recovery,"metadata.after.sha256"))){ throw new IOException("ASSET_UNDO_CONFLICT: operation outputs changed."); }
        if(Original.Operation is AssetFileOperation.Copy or AssetFileOperation.Duplicate or AssetFileOperation.Create){
            if(Original.Directory){ System.IO.Directory.Delete(Original.Destination,false); }
            else { File.Delete(Original.Destination); }
        } else {
            if(File.Exists(Original.Source)&&!string.Equals(Original.Source,Current,StringComparison.OrdinalIgnoreCase)){ throw new IOException("Undo would overwrite an existing source."); }
            if(string.Equals(Current,Original.Source,StringComparison.OrdinalIgnoreCase)){
                var Temporary=Path.Combine(Recovery,"case-undo");File.Move(Current,Temporary);File.Move(Temporary,Original.Source);
            } else { File.Move(Current,Original.Source); }
            if(File.Exists(Path.Combine(Recovery,"source.before"))){ File.Copy(Path.Combine(Recovery,"source.before"),Original.Source,true); }
        }
        if(File.Exists(Sidecar(Original.Destination))){ File.Delete(Sidecar(Original.Destination)); }
        if(Original.Operation is not(AssetFileOperation.Copy or AssetFileOperation.Duplicate or AssetFileOperation.Create)
            &&File.Exists(Path.Combine(Recovery,"metadata.before"))){ File.Copy(Path.Combine(Recovery,"metadata.before"),Sidecar(Original.Source),false); }
        File.WriteAllText(Path.Combine(Recovery,"state"),"Undone");
    }
    private static string Sidecar(string Source)=>string.IsNullOrEmpty(Source)?string.Empty:Source+".meta";
    private static string Hash(string PathValue){
        if(string.IsNullOrEmpty(PathValue)||!File.Exists(PathValue)){ return string.Empty; }
        using var Stream=File.Open(PathValue,FileMode.Open,FileAccess.Read,FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(Stream));
    }
}
