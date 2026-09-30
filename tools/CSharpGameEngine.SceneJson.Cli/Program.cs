using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
namespace CSharpGameEngine.SceneJson.Cli;
/// <summary>
/// <c>CSharpGameEngine.SceneJson.Cli convert &lt;game .csproj&gt; [--write] [--delete-source]</c> (ADR-0070).
/// Prints each C# scene's conversion; <c>--write</c> writes <c>&lt;ClassName&gt;.scene.json</c> next to
/// the scene's source file and <c>--delete-source</c> also deletes that file when it declares only the
/// scene. Nothing is written for a project with any unconvertible scene.
/// </summary>
internal static class Program {
    private static async Task<int>Main(string[] Arguments){
        if(Arguments.Length<2||Arguments[0]!="convert"){
            Console.Error.WriteLine("usage: CSharpGameEngine.SceneJson.Cli convert <game .csproj> [--write] [--delete-source]");
            return 2;
        }
        var ProjectPath=Path.GetFullPath(Arguments[1]);
        var Write=Arguments.Contains("--write");
        var DeleteSource=Arguments.Contains("--delete-source");
        MSBuildLocator.RegisterDefaults();
        return await Convert(ProjectPath,Write,DeleteSource);
    }
    private static async Task<int>Convert(string ProjectPath,bool Write,bool DeleteSource){
        using var Workspace=MSBuildWorkspace.Create();
        var Project=await Workspace.OpenProjectAsync(ProjectPath);
        var Compilation=await Project.GetCompilationAsync()??throw new InvalidOperationException("The project has no compilation.");
        var ProjectDirectory=Path.GetDirectoryName(ProjectPath)!;
        var SchemaPath=Path.Combine(ProjectDirectory,".cge",SceneJsonSchemaBuilder.FileName);
        List<(string SourcePath,string JsonPath,string Text,bool SceneOnlyFile)>Outputs=[];
        var Failed=false;
        foreach(var Scene in EnumerateScenes(Compilation.Assembly.GlobalNamespace)){
            var Declaration=Scene.DeclaringSyntaxReferences.FirstOrDefault();
            if(Declaration is null||!Declaration.SyntaxTree.FilePath.StartsWith(ProjectDirectory,StringComparison.OrdinalIgnoreCase)){ continue; }
            var SourcePath=Declaration.SyntaxTree.FilePath;
            var JsonPath=Path.Combine(Path.GetDirectoryName(SourcePath)!,Scene.Name+".scene.json");
            var Model=SceneDeclarationAnalyzer.Analyze(Scene,Compilation,CancellationToken.None);
            if(Model.HasErrors){
                Console.Error.WriteLine($"{SourcePath}: the C# scene has errors; fix them before converting.");
                Failed=true;
                continue;
            }
            var Reference=Path.GetRelativePath(Path.GetDirectoryName(JsonPath)!,SchemaPath).Replace('\\','/');
            var Result=SceneJsonConverter.Convert(Model,Reference);
            foreach(var Problem in Result.Problems){ Console.Error.WriteLine($"{SourcePath}: {Problem}"); }
            if(Result.Text is null){
                Failed=true;
                continue;
            }
            var Root=await Declaration.SyntaxTree.GetRootAsync();
            var SceneOnlyFile=Root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax>().Count()==1;
            Outputs.Add((SourcePath,JsonPath,Result.Text,SceneOnlyFile));
            Console.WriteLine($"CGE-SCENE-JSON {Scene.ToDisplayString()} -> {JsonPath} ({Result.Text.Length} chars)");
        }
        if(Failed){
            Console.Error.WriteLine("Nothing was written: fix the problems above and run again.");
            return 1;
        }
        if(!Write){ return 0; }
        foreach(var Output in Outputs){
            File.WriteAllText(Output.JsonPath,Output.Text);
            if(DeleteSource){
                if(Output.SceneOnlyFile){
                    File.Delete(Output.SourcePath);
                } else {
                    Console.Error.WriteLine($"{Output.SourcePath}: kept, it declares more than the scene.");
                }
            }
        }
        return 0;
    }
    private static IEnumerable<INamedTypeSymbol>EnumerateScenes(INamespaceSymbol Namespace){
        foreach(var Member in Namespace.GetMembers()){
            if(Member is INamespaceSymbol Child){
                foreach(var Scene in EnumerateScenes(Child)){ yield return Scene; }
            } else if(Member is INamedTypeSymbol Type&&SceneTypePredicates.ImplementsSceneInterface(Type)){
                yield return Type;
            }
        }
    }
}
