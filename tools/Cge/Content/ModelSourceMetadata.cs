using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.ContentCompiler;
internal static class ModelSourceMetadata {
    internal sealed record Result(AssetMetadata Metadata,Hash256 Hash);
    /// <summary>
    /// 利用者が書いたメタデータを読む。ビルドは書き換えない（ADR-0098 §5 規則 1・2）。廃止したインポーター名は、Assets ウィンドウの明示の変換へ案内して止める。
    /// </summary>
    internal static async Task<Result>ReadAsync(string FullSource,string SourceUri,CancellationToken CancellationToken){
        var PathValue=FullSource+".meta";var Bytes=await ReadBytesAsync(PathValue,CancellationToken);
        if(!AssetMetadata.TryParse(Bytes,out var Metadata,out var Error)||Metadata is null||Metadata.SourceUri!=SourceUri){ throw new InvalidDataException("IMP007: canonical model metadata is required: "+Error.Message); }
        if(AssetMetadata.IsRetiredModelImporter(Metadata.Importer)){
            throw new InvalidDataException($"IMP008: {SourceUri}.meta names the retired importer '{Metadata.Importer}'. Convert it with 'Convert legacy metadata' in the Assets window; the build does not rewrite metadata.");
        }
        return new(Metadata,Hash256.Compute(Bytes));
    }
    internal static async Task<bool>UnchangedAsync(string PathValue,Hash256?Expected,CancellationToken CancellationToken){
        if(File.Exists(PathValue)!=Expected.HasValue){ return false; }
        return Expected is not{}Hash||Hash256.Compute(await ReadBytesAsync(PathValue,CancellationToken))==Hash;
    }
    private static async Task<byte[]>ReadBytesAsync(string PathValue,CancellationToken CancellationToken){
        if(new FileInfo(PathValue).Length>65536){ throw new InvalidDataException("IMP007: metadata exceeds its bound."); }
        var Bytes=await File.ReadAllBytesAsync(PathValue,CancellationToken);
        if(Bytes.Length>65536){ throw new InvalidDataException("IMP007: metadata exceeds its bound."); }
        return Bytes;
    }
}
