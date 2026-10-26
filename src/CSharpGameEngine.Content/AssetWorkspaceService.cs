using System.Security.Cryptography;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Content;
/// <summary>評価済みプロジェクトソースの読み取り専用ビュー。探索による書き込み、インポート、ID割り当ては行わない。</summary>
public static class AssetWorkspaceService {
    public static AssetWorkspaceResult Inspect(AssetWorkspaceRequest Request,CancellationToken CancellationToken=default){
        if(Request is null||Request.ProtocolVersion!=1||Request.WorkspaceCommand!=1||Request.RequestId==Guid.Empty
            ||Request.Sources is null||Request.Sources.Length>100_000){
            return new(){RequestId=Request?.RequestId??Guid.Empty,Diagnostic="ASSET_INVENTORY_REQUEST_INVALID"};
        }
        var Items=new List<AssetWorkspaceItem>();
        var Seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var Source in Request.Sources){
            CancellationToken.ThrowIfCancellationRequested();
            if(Source is null||string.IsNullOrWhiteSpace(Source.ProjectPath)||string.IsNullOrWhiteSpace(Source.ProjectRoot)
                ||string.IsNullOrWhiteSpace(Source.SourcePath)||Source.SourcePath.Length>32_768||string.IsNullOrWhiteSpace(Source.RelativePath)
                ||Source.Provider is null||Source.MetadataRoot is null){
                return new(){RequestId=Request.RequestId,Diagnostic="ASSET_INVENTORY_SOURCE_INVALID"};
            }
            if(AssetWorkspaceClassification.IsExcluded(Source.RelativePath)||AssetWorkspaceClassification.Classify(Source.RelativePath) is not { }Kind){ continue; }
            if(!Seen.Add(Source.ProjectPath+"\n"+Source.SourcePath)){ continue; }
            var Item=new AssetWorkspaceItem{Source=Source,Kind=Kind,State=AssetWorkspaceState.Source};
            try {
                var File=new FileInfo(Source.SourcePath);
                if(!File.Exists){ Item.State=AssetWorkspaceState.MissingSource; }
                else {
                    Item.SourceLength=File.Length;
                    Item.ModifiedUtcTicks=File.LastWriteTimeUtc.Ticks;
                    if(Kind==AssetWorkspaceKind.Texture&&!ImageImportSettings.Supports(Source.SourcePath)){ Item.Diagnostic="IMAGE_FORMAT_UNSUPPORTED: no registered decoder for this source format."; }
                    var MetaPath=Source.SourcePath+".meta";
                    if(System.IO.File.Exists(MetaPath)){
                        var Information=new FileInfo(MetaPath);
                        if(Information.Length<1||Information.Length>64*1024){ throw new InvalidDataException("Metadata exceeds its size limit."); }
                        var Bytes=System.IO.File.ReadAllBytes(MetaPath);
                        Item.MetadataSha256=Convert.ToHexStringLower(SHA256.HashData(Bytes));
                        if(AssetMetadata.TryParse(Bytes,out var Metadata,out var Error)&&Metadata is not null){
                            Item.AssetId=Metadata.AssetId.ToString();
                            Item.State=AssetWorkspaceState.Ready;
                            if(ImageImportSettings.Supports(Source.SourcePath)){
                                if(Metadata.Importer!=ImageImportSettings.Importer){ throw new InvalidDataException("IMAGE_IMPORTER_MISMATCH"); }
                                Item.ImageSettings=ImageImportSettings.FromOptions(Metadata.Options);
                                if(Item.ImageSettings.Outputs.Single(O=>O.Key==Item.ImageSettings.DefaultOutput).Kind==ImageOutputKind.Image){ Item.Kind=AssetWorkspaceKind.Image; }
                            }
                            var MetadataRoot=string.IsNullOrEmpty(Source.MetadataRoot)?Source.ProjectRoot:Source.MetadataRoot;
                            var Failure=AssetImportStatus.Read(MetadataRoot,Metadata.AssetId);
                            if(Failure.Length>0){ Item.State=AssetWorkspaceState.ImportFailed;Item.Diagnostic="最新設定の取り込み失敗: "+Failure; }
                            if(!Source.ReadOnly&&Metadata.SourceUri!=Path.GetRelativePath(MetadataRoot,Source.SourcePath).Replace('\\','/')){
                                Item.State=AssetWorkspaceState.InvalidMetadata;Item.Diagnostic="ASSET_SOURCE_MISMATCH";
                            }
                        } else {
                            Item.State=AssetWorkspaceState.InvalidMetadata;
                            Item.Diagnostic="ASSET_METADATA_INVALID: "+Error.Message;
                        }
                    } else if(Kind==AssetWorkspaceKind.Model||ImageImportSettings.Supports(Source.SourcePath)){ Item.State=AssetWorkspaceState.MissingMetadata; }
                    if(Source.ReadOnly){ Item.State=AssetWorkspaceState.ReadOnly; }
                }
            } catch(Exception Error)when(Error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException){
                Item.State=AssetWorkspaceState.InvalidMetadata;
                Item.Diagnostic=Error.Message;
            }
            Items.Add(Item);
        }
        foreach(var Group in Items.Where(static Item=>Item.AssetId.Length>0).GroupBy(static Item=>Item.Source.ProjectPath+"\n"+Item.AssetId,StringComparer.Ordinal).Where(static Group=>Group.Count()>1)){
            foreach(var Item in Group){ Item.State=AssetWorkspaceState.InvalidMetadata;Item.Diagnostic="ASSET_ID_DUPLICATED"; }
        }
        return new(){RequestId=Request.RequestId,Success=true,Items=Items.ToArray()};
    }
}
