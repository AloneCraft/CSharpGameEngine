using CSharpGameEngine.Content;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.ContentCompiler;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Import.AssetAuthoring.Cli;
internal static class ModelPreviewPreparation {
    internal static async Task<AssetModelResult>RunAsync(AssetModelRequest Request,CancellationToken CancellationToken){
        try {
            CancellationToken.ThrowIfCancellationRequested();
            if(Request.RequestId==Guid.Empty||Request.PreviewSourcePaths is null||Request.PreviewSourcePaths.Length>16){ throw new ArgumentException("Invalid model preview preparation request."); }
            var Root=Path.GetFullPath(Request.Root);var Selected=Request.PreviewSourcePaths.Append(Request.SourcePath).Where(static PathValue=>!string.IsNullOrWhiteSpace(PathValue)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var Assets=Path.Combine(Root,"Assets");
            string[]Sources=Directory.Exists(Assets)?Directory.EnumerateFiles(Assets,"*",new EnumerationOptions {RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint})
                .Where(static PathValue=>Path.GetExtension(PathValue).ToLowerInvariant() is ".glb" or ".gltf")
                .Where(PathValue=>!Path.GetRelativePath(Assets,PathValue).Replace('\\','/').StartsWith("Editor/",StringComparison.OrdinalIgnoreCase)).ToArray():[];
            Sources=[..Sources.Concat(Selected).Distinct(StringComparer.OrdinalIgnoreCase)];
            var Content=Path.Combine(Root,"obj","cge-content","models");
            var Tool=Path.Combine(AppContext.BaseDirectory,"Cook","CSharpGameEngine.Cook.Cli.dll");var Module=Path.Combine(AppContext.BaseDirectory,"CSharpGameEngine.Import.dll");
            if(!File.Exists(Tool)||!File.Exists(Module)){ throw new FileNotFoundException("Model production worker is unavailable."); }
            var Compiled=await new ModelProjectCompiler(Root,Tool,[(Module,true)],Content).CompileAsync(Sources,CancellationToken:CancellationToken);
            if(!Compiled.Succeeded){ throw new InvalidDataException(string.Join("\n",Compiled.Diagnostics.Select(static Diagnostic=>Diagnostic.ToString()))); }
            var Prepared=Selected.Select(PathValue=>{
                var Uri=Path.GetRelativePath(Root,PathValue).Replace('\\','/');var Reference=Compiled.Index.Resolve(Uri,ContentArtifactKind.Model);
                return new RenderPreviewModelSource {SourcePath=PathValue,ContentDirectory=Content,SourceUri=Uri,ModelHash=Reference.ContentHash.ToString()};
            }).ToArray();
            return new(){RequestId=Request.RequestId,Success=true,PreparedSources=Prepared};
        }catch(Exception Error)when(Error is IOException or InvalidDataException or ArgumentException or InvalidOperationException){ return new(){RequestId=Request.RequestId,Diagnostic=Error.Message}; }
    }
}
