using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Animation;
using CSharpGameEngine.Testing;
namespace CSharpGameEngine.Runtime.Windows.Tests;
[CollectionDefinition("Model artifact content root",DisableParallelization=true)]
public sealed class ModelArtifactContentRootCollection {
}
public sealed class MannequinArtifactFixture:IAsyncLifetime {
    private readonly Dictionary<string,ModelArtifactTestContent>Templates=[];
    private readonly string PreviousDirectory=ContentRoot.Directory;
    private readonly string PreviousModels=ContentRoot.ModelDirectory;
    public Task InitializeAsync(){
        var Repository=new DirectoryInfo(AppContext.BaseDirectory);
        while(Repository is not null&&!File.Exists(Path.Combine(Repository.FullName,"CSharpGameEngine.slnx"))){ Repository=Repository.Parent; }
        if(Repository is null){ throw new InvalidOperationException("Repository root was not found."); }
        foreach(var Template in new[]{"FirstPerson","ThirdPerson","TopDown"}){
            var Project=Path.Combine(Repository.FullName,"Templates",""+Template);
            var Sources=Directory.EnumerateFiles(Path.Combine(Project,"Assets","Characters","Mannequins"),"*.glb",SearchOption.AllDirectories).ToArray();
            this.Templates.Add(Template,new ModelArtifactTestContent(Sources,Project));
        }
        return Task.CompletedTask;
    }
    public void Select(string Template){
        var Value=this.Templates[Template];ContentRoot.Use(Value.ProjectDirectory,Value.ContentDirectory);
    }
    public Task DisposeAsync(){
        SkeletalAssetCache.Clear();ContentRoot.Use(this.PreviousDirectory,this.PreviousModels);
        foreach(var Content in this.Templates.Values){ Content.Dispose(); }
        return Task.CompletedTask;
    }
}
