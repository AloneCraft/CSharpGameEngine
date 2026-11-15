using CSharpGameEngine.Content;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.ContentCompiler;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Import.Gltf;
namespace CSharpGameEngine.Testing;
internal sealed class ModelArtifactTestContent:IDisposable {
    private readonly string Root=Path.Combine(Path.GetTempPath(),"cge-preview-artifacts-"+Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string,RenderPreviewModelSource>Sources=new(StringComparer.OrdinalIgnoreCase);
    internal string ProjectDirectory{get;}
    internal string ContentDirectory=>this.Root;
    internal ModelArtifactTestContent(IEnumerable<string>Paths,string?ProjectRoot=null){
        var Selected=Paths.Where(static Value=>!string.IsNullOrEmpty(Value)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(Selected.Length==0&&ProjectRoot is null){ throw new ArgumentException("An empty input set requires its project directory.",nameof(Paths)); }
        var Repository=new DirectoryInfo(AppContext.BaseDirectory);
        while(Repository is not null&&!File.Exists(Path.Combine(Repository.FullName,"CSharpGameEngine.slnx"))){ Repository=Repository.Parent; }
        if(Repository is null){ throw new InvalidOperationException("Repository root was not found."); }
        var RepositoryPrefix=Repository.FullName.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        var Project=ProjectRoot is not null?Path.GetFullPath(ProjectRoot):Selected.All(Value=>Value.StartsWith(RepositoryPrefix,StringComparison.OrdinalIgnoreCase))?Repository.FullName:Path.GetDirectoryName(Selected[0])!;
        this.ProjectDirectory=Project;
#if DEBUG
        const string Configuration="Debug";
#else
        const string Configuration="Release";
#endif
        var Cook=Path.Combine(Repository.FullName,"tools","Cook.Cli","bin",Configuration,"net10.0","Cook.Cli.dll");
        try {
            var Result=Task.Run(()=>new ModelProjectCompiler(Project,Cook,[(typeof(GltfImporter).Assembly.Location,true)],this.Root).CompileAsync(Selected)).GetAwaiter().GetResult();
            if(!Result.Succeeded){ throw new InvalidDataException(string.Join("\n",Result.Diagnostics.Select(static Diagnostic=>Diagnostic.ToString()))); }
            foreach(var PathValue in Selected){
                var Uri=Path.GetRelativePath(Project,PathValue).Replace('\\','/');var Reference=Result.Index.Resolve(Uri,ContentArtifactKind.Model);
                this.Sources.Add(PathValue,new(){SourcePath=PathValue,ContentDirectory=this.Root,SourceUri=Uri,ModelHash=Reference.ContentHash.ToString()});
            }
        }catch{ this.Dispose();throw; }
    }
    internal RenderPreviewModelSource Source(string PathValue)=>this.Sources[Path.GetFullPath(PathValue)];
    internal void Prepare(RenderPreviewScene Scene){
        if(!string.IsNullOrEmpty(Scene.StaticAssetPath)){ Scene.StaticModel=this.Source(Scene.StaticAssetPath); }
        if(Scene.Asset is not{}Asset){ return; }
        Asset.MeshModel=this.Source(Asset.MeshPath);if(!string.IsNullOrEmpty(Asset.AnimationPath)){ Asset.AnimationModel=this.Source(Asset.AnimationPath); }
    }
    public void Dispose(){ if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); } }
}
