using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Content;
/// <summary>キャッシュ専用の最終試行診断情報。ソースメタデータと最終成功時のマニフェストが引き続き正本となる。</summary>
public static class AssetImportStatus {
    public static string RecordFailure(string Root,string Source,string Diagnostic){
        try {
            var PathValue=StatusPath(Root,Source);if(PathValue is null){ return Diagnostic; }
            Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);
            var Temporary=PathValue+"."+Guid.NewGuid().ToString("N")+".tmp";
            try { File.WriteAllText(Temporary,Diagnostic.Length>4096?Diagnostic[..4096]:Diagnostic);File.Move(Temporary,PathValue,true); }
            finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
            return Diagnostic;
        }catch(Exception Error)when(Error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException){ return Diagnostic+"; IMPORT_STATUS_WRITE_FAILED: "+Error.Message; }
    }
    public static string Clear(string Root,string Source){
        try { var PathValue=StatusPath(Root,Source);if(PathValue is not null&&File.Exists(PathValue)){ File.Delete(PathValue); }return string.Empty; }
        catch(Exception Error)when(Error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException){ return "IMPORT_STATUS_CLEAR_FAILED: "+Error.Message; }
    }
    public static string Read(string Root,AssetId Id){
        var PathValue=Path.Combine(Root,"obj","cge-content","import-status",Id+".txt");
        if(!File.Exists(PathValue)){ return string.Empty; }
        if(new FileInfo(PathValue).Length>32768){ throw new InvalidDataException("Import diagnostic exceeds its limit."); }
        return File.ReadAllText(PathValue);
    }
    private static string?StatusPath(string Root,string Source){
        var FullRoot=Path.GetFullPath(Root);var FullSource=Path.GetFullPath(Source);
        if(!FullSource.StartsWith(FullRoot.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(FullSource+".meta")){ return null; }
        if(new FileInfo(FullSource+".meta").Length>65536||!AssetMetadata.TryParse(File.ReadAllBytes(FullSource+".meta"),out var Metadata,out _)||Metadata is null){ return null; }
        return Path.Combine(FullRoot,"obj","cge-content","import-status",Metadata.AssetId+".txt");
    }
}
