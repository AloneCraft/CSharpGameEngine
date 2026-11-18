using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class SceneImageOutputPreconditionTests {
    private const string MetadataPath=@"C:\Game\Assets\Photo.png.meta";
    private const string AssetPath="Assets/Photo.png:normal%20map";
    private const string TypeName="CSharpGameEngine.Authoring.TextureAsset";
    [Theory]
    [InlineData("before","before","after",true)]
    [InlineData("before","before","before",true)]
    [InlineData("before","before","external",false)]
    [InlineData("before","after","after",false)]
    [InlineData("before","external","before",false)]
    [InlineData(null,"before","after",false)]
    public void RollbackPersistence_RequiresRestoredBufferAndOwnedDiskText(string?Before,string Buffer,string Disk,bool Expected){
        var Precondition=new SceneImageOutputPrecondition(MetadataPath,"after",TypeName,"global::Assets.Photo.Normal",AssetPath,Before);
        Assert.Equal(Expected,Precondition.CanPersistRollback(Buffer,Disk));
    }
    [Theory]
    [InlineData("public static readonly TextureAsset Normal=new(\"Assets/Photo.png:normal%20map\");","expected",TypeName,true)]
    [InlineData("public static readonly TextureAsset Normal=new(\"Assets/Photo.png:normal%20map\");","old",TypeName,false)]
    [InlineData("public static readonly TextureAsset Normal=new(\"Assets/Other.png:normal%20map\");","expected",TypeName,false)]
    [InlineData("public static readonly ImageAsset Normal=new(\"Assets/Photo.png:normal%20map\");","expected",TypeName,false)]
    [InlineData("public static TextureAsset Normal=new(\"Assets/Photo.png:normal%20map\");","expected",TypeName,false)]
    [InlineData("public static TextureAsset Normal=>new(\"Assets/Photo.png:normal%20map\");","expected",TypeName,false)]
    [InlineData("public static readonly TextureAsset Value=new(\"Assets/Photo.png:normal%20map\");public static TextureAsset Normal=>Value;","expected",TypeName,true)]
    public async Task RequiresExactMetadataAndCanonicalTypedCatalogPath(string Catalog,string Metadata,string AssetType,bool Expected){
        using var Workspace=new AdhocWorkspace();
        var Project=CreateProject(Workspace,Catalog,Metadata);
        var Compilation=(await Project.GetCompilationAsync())!;
        Assert.DoesNotContain(Compilation.GetDiagnostics(),Diagnostic=>Diagnostic.Severity==DiagnosticSeverity.Error);
        var Precondition=new SceneImageOutputPrecondition(MetadataPath,"expected",AssetType,"global::Assets.Photo.Normal",AssetPath);
        Assert.Equal(Expected,await Precondition.MatchesAsync(Project,Compilation,"Game.Scene",CancellationToken.None));
    }
    [Fact]
    public async Task MissingProjectOrMetadata_RejectsWithoutChangingSources(){
        using var Workspace=new AdhocWorkspace();
        var Project=CreateProject(Workspace,"public static readonly TextureAsset Normal=new(\"Assets/Photo.png:normal%20map\");","expected");
        var Compilation=(await Project.GetCompilationAsync())!;
        var Precondition=new SceneImageOutputPrecondition(MetadataPath,"expected",TypeName,"global::Assets.Photo.Normal",AssetPath);
        Assert.False(await Precondition.MatchesAsync(null,Compilation,"Game.Scene",CancellationToken.None));
        var Missing=Project.Solution.RemoveAdditionalDocument(System.Linq.Enumerable.Single(Project.AdditionalDocumentIds)).GetProject(Project.Id)!;
        Assert.False(await Precondition.MatchesAsync(Missing,Compilation,"Game.Scene",CancellationToken.None));
        Assert.Same(Project.Solution,Workspace.CurrentSolution);
    }
    private static Project CreateProject(AdhocWorkspace Workspace,string Catalog,string Metadata){
        var Project=Workspace.AddProject("Game",LanguageNames.CSharp).WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Preview))
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        const string AssetTypes="namespace CSharpGameEngine.Authoring { public readonly struct TextureAsset(string Path) { public readonly string Path=Path; } public readonly struct ImageAsset(string Path) { public readonly string Path=Path; } }";
        Project=Project.AddDocument("AssetReferences.cs",SourceText.From(AssetTypes)).Project;
        var Source="using CSharpGameEngine.Authoring;namespace Game { public class Scene {} }internal static class Assets { internal static class Photo { "+Catalog+" } }";
        Project=Project.AddDocument("Catalog.cs",SourceText.From(Source)).Project;
        Project=Project.AddAdditionalDocument("Photo.png.meta",SourceText.From(Metadata),filePath:MetadataPath).Project;
        Assert.True(Workspace.TryApplyChanges(Project.Solution));
        return Workspace.CurrentSolution.GetProject(Project.Id)!;
    }
}
