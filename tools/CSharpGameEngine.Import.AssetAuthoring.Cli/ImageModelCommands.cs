using System.Text.Json;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Import.Images;
using CSharpGameEngine.Import.Fbx;
namespace CSharpGameEngine.Import.AssetAuthoring.Cli;
internal static class ImageModelCommands {
    internal static int?Run(string[]Args){
        if(Args.Length==0||Args[0] is not("image" or "model")){ return null; }
        try {
            if(Args.Length<4){ throw new ArgumentException("Usage: image|model inspect|register|import|apply|register-tree <project-root> <source-or-folder> [settings.json]"); }
            var Root=Path.GetFullPath(Args[2]);var Source=Path.GetFullPath(Args[3]);
            if(Args[1]=="register-tree"){
                if(Args[0]!="image"||Args.Length!=4){ throw new ArgumentException("register-tree supports image sources only."); }
                var Files=Directory.EnumerateFiles(Source,"*",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint}).Where(ImageImportSettings.Supports)
                    .Where(P=>!AssetWorkspaceClassification.IsExcluded(Path.GetRelativePath(Root,P))).OrderBy(P=>P,StringComparer.Ordinal).ToArray();
                var Success=true;foreach(var File in Files){
                    var Snapshot=AssetAuthoringService.Execute(new(){RequestId=Guid.NewGuid(),RepositoryRoot=Root,SourcePath=File});
                    var Result=ImageOutputCompiler.Execute(new(){RequestId=Guid.NewGuid(),Root=Root,SourcePath=File,ImageCommand=4,TargetUsage=TextureUsage.Color,ExpectedMetadataHash=Snapshot.MetadataSha256});
                    Console.WriteLine(JsonSerializer.Serialize(Result));Success&=Result.Success;
                }return Success?0:1;
            }
            if(Args[0]=="image"){
                var Settings=Args.Length==5?JsonSerializer.Deserialize<ImageImportSettings>(File.ReadAllBytes(Args[4])):null;
                var Snapshot=AssetAuthoringService.Execute(new(){RequestId=Guid.NewGuid(),RepositoryRoot=Root,SourcePath=Source});
                var Command=Args[1] switch {"inspect"=>1,"register"=>4,"import"=>3,"apply"=>2,_=>throw new ArgumentException("Unknown image command.")};
                var Result=ImageOutputCompiler.Execute(new(){RequestId=Guid.NewGuid(),Root=Root,SourcePath=Source,ImageCommand=Command,Settings=Settings,ExpectedMetadataHash=Snapshot.MetadataSha256});
                Console.WriteLine(JsonSerializer.Serialize(Result));return Result.Success?0:1;
            }
            if(Args[1]=="apply"){
                if(Args.Length!=5){ throw new ArgumentException("model apply requires settings.json."); }
                var Settings=JsonSerializer.Deserialize<ModelImportSettings>(File.ReadAllBytes(Args[4]))??throw new ArgumentException("Model settings required.");
                _=ModelImportSettings.FromOptions(Settings.ToOptions());
                var Snapshot=AssetAuthoringService.Execute(new(){RequestId=Guid.NewGuid(),RepositoryRoot=Root,SourcePath=Source});
                if(!Snapshot.Success){ throw new InvalidDataException(string.Join(";",Snapshot.Diagnostics.Select(D=>D.Message))); }
                var Options=Snapshot.Options.Where(O=>!O.Name.StartsWith("model.",StringComparison.Ordinal)).Concat(Settings.ToOptions().Select(P=>new AssetAuthoringOption{Name=P.Key,Value=P.Value})).ToArray();
                var Saved=AssetAuthoringService.Execute(new(){RequestId=Guid.NewGuid(),RepositoryRoot=Root,SourcePath=Source,Command=AssetAuthoringCommand.SaveMetadata,AssetId=Snapshot.AssetId,Importer=Snapshot.Importer,ExpectedMetadataSha256=Snapshot.MetadataSha256,Options=Options});
                if(!Saved.Success){ throw new InvalidDataException(string.Join(";",Saved.Diagnostics.Select(D=>D.Message))); }
            }
            if(Args[1]=="register"){
                var Snapshot=AssetAuthoringService.Execute(new(){RequestId=Guid.NewGuid(),RepositoryRoot=Root,SourcePath=Source});
                if(!Snapshot.Success){ throw new InvalidDataException(string.Join("; ",Snapshot.Diagnostics.Select(D=>D.Message))); }
                if(Snapshot.State==AssetAuthoringState.MissingMetadata){
                    var Settings=new ModelImportSettings();
                    Snapshot.Options=Settings.ToOptions().Select(P=>new AssetAuthoringOption{Name=P.Key,Value=P.Value}).ToArray();
                    var Saved=AssetAuthoringService.Execute(new(){RequestId=Guid.NewGuid(),Command=AssetAuthoringCommand.SaveMetadata,RepositoryRoot=Root,SourcePath=Source,
                        AssetId=Snapshot.AssetId,Importer=Snapshot.Importer,ExpectedMetadataSha256=Snapshot.MetadataSha256,Options=Snapshot.Options});
                    if(!Saved.Success){ throw new InvalidDataException(string.Join("; ",Saved.Diagnostics.Select(D=>D.Message))); }
                }
            }
            var Model=FbxModelCompiler.Execute(new(){RequestId=Guid.NewGuid(),Root=Root,SourcePath=Source,ModelCommand=Args[1] switch {"inspect"=>1,"import" or "register" or "apply"=>2,_=>throw new ArgumentException("Unknown model command.")}});
            Console.WriteLine(JsonSerializer.Serialize(Model));return Model.Success?0:1;
        }catch(Exception Error)when(Error is IOException or InvalidDataException or ArgumentException or JsonException or UnauthorizedAccessException){ Console.Error.WriteLine(Error.Message);return 1; }
    }
}
