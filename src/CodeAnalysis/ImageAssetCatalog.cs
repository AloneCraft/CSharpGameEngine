using System.Collections.Immutable;
using CSharpGameEngine.CodeAnalysis.ImageContract;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.CodeAnalysis;
internal static partial class AssetCatalogEmitter {
    private static readonly DiagnosticDescriptor InvalidImage=new("ASSET001","Invalid image metadata","{0}: {1}","CSharpGameEngine.Entities.CodeGen",DiagnosticSeverity.Error,true);
    private static Dictionary<string,ImageImportSettings>ReadImages(SourceProductionContext Context,ImmutableArray<(string Path,string Json)>Metadata){
        var Result=new Dictionary<string,ImageImportSettings>(StringComparer.Ordinal);
        if(Metadata.IsDefault){ return Result; }
        foreach(var (Path,Json) in Metadata){
            if(!ImageImportSettings.Supports(Path)){ continue; }
            try {
                var Root=SceneJsonReader.Parse(Json);
                if(Root.Get("schemaVersion")?.Text!="1"||Root.Get("importer")?.Text!=ImageImportSettings.Importer
                    ||!Guid.TryParse(Root.Get("assetId")?.Text,out var Id)||Id==Guid.Empty||Root.Get("options") is not {Kind:SceneJsonKind.Object}Options){
                    throw new ArgumentException("Expected canonical image metadata schema 1 and cge.image.");
                }
                var Values=Options.Properties.ToDictionary(P=>P.Name,P=>P.Value.Kind==SceneJsonKind.String?P.Value.Text!:throw new ArgumentException("Image option values must be strings."),StringComparer.Ordinal);
                Result.Add(Path,ImageImportSettings.FromOptions(Values));
            }catch(Exception Error)when(Error is ArgumentException or SceneJsonException){ Context.ReportDiagnostic(Diagnostic.Create(InvalidImage,Location.None,Path,Error.Message)); }
        }
        return Result;
    }
    private static void WriteImage(string Name,string Path,ImageImportSettings Image,string Indent,List<string>Lines){
        static string Member(string Key)=>string.Concat(Key.Split(new[]{'_'},StringSplitOptions.RemoveEmptyEntries).Select(Part=>char.ToUpperInvariant(Part[0])+Part.Substring(1)));
        Lines.Add(Indent+"internal static class "+Name+" {");
        foreach(var Output in Image.Outputs){
            var Type=Output.Kind==ImageOutputKind.Image?"ImageAsset":"TextureAsset";
            Lines.Add(Indent+"    public static readonly global::CSharpGameEngine.Authoring."+Type+" "+Member(Output.Key)+" = new(\""+Path+":"+Uri.EscapeDataString(Output.Key)+"\");");
        }
        var Default=Image.Outputs.Single(O=>O.Key==Image.DefaultOutput);
        Lines.Add(Indent+"    public static global::CSharpGameEngine.Authoring."+(Default.Kind==ImageOutputKind.Image?"ImageAsset":"TextureAsset")+" Default=>"+Member(Default.Key)+";");
        Lines.Add(Indent+"}");
    }
}
