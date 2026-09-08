using System.Text.Json;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Import.AssetAuthoring.Cli;

internal static class AssetAuthoringBridgeWorker {
    private const int MaximumRequestBytes=256*1024;
    private static readonly JsonSerializerOptions JsonOptions=new(){ PropertyNameCaseInsensitive=false };

    public static(int ExitCode,string Output)Run(string RequestPath,string ResultPath){
        try {
            byte[]RequestBytes=ReadBoundedRequest(RequestPath);
            AssetAuthoringRequest?Request=JsonSerializer.Deserialize<AssetAuthoringRequest>(
                RequestBytes,JsonOptions);
            if(Request is null){ return(1,"Asset authoring request is empty."); }

            AssetAuthoringResult Result=AssetAuthoringService.Execute(Request);
            WriteResultAtomically(ResultPath,JsonSerializer.SerializeToUtf8Bytes(Result,JsonOptions));
            return(0,string.Empty);
        } catch(Exception Ex)when(Ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or NotSupportedException){
            return(1,Ex.Message);
        }
    }

    private static byte[]ReadBoundedRequest(string PathValue){
        FileInfo Information=new(PathValue);
        if(Information.Length<1||Information.Length>MaximumRequestBytes){
            throw new InvalidDataException("Asset authoring request length is outside the supported range.");
        }
        byte[]Bytes=File.ReadAllBytes(PathValue);
        if(Bytes.Length!=Information.Length){
            throw new IOException("Asset authoring request changed while it was read.");
        }
        return Bytes;
    }

    private static void WriteResultAtomically(string PathValue,byte[]Bytes){
        string FullPath=Path.GetFullPath(PathValue);
        string DirectoryPath=Path.GetDirectoryName(FullPath)
            ??throw new ArgumentException("Asset authoring result directory is unavailable.");
        Directory.CreateDirectory(DirectoryPath);
        string Temporary=Path.Combine(
            DirectoryPath,"."+Path.GetFileName(FullPath)+"."+Guid.NewGuid().ToString("N")+".tmp");
        try {
            using(FileStream Stream=new(
                Temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,
                FileOptions.WriteThrough)){
                Stream.Write(Bytes);
                Stream.Flush(true);
            }
            if(File.Exists(FullPath)){ File.Replace(Temporary,FullPath,null); }
            else { File.Move(Temporary,FullPath); }
        } finally {
            if(File.Exists(Temporary)){ File.Delete(Temporary); }
        }
    }
}
