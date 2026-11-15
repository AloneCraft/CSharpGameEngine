using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
namespace CSharpGameEngine.SceneJson.Cli;
/// <summary>
/// <c>CSharpGameEngine.SceneJson.Cli convert &lt;game .csproj&gt; [--write] [--delete-source]</c> (ADR-0070)。各 C# シーンの変換結果を出力する。<c>--write</c> を指定すると <c>&lt;ClassName&gt;.json</c> をプロジェクトの <c>Scenes</c> フォルダーに書き込む。シーンファイルはそこに置く必要があり (ADR-0070)、ソースがすでに <c>Scenes</c> 以下ならその隣、そうでなければ <c>Scenes</c> 直下に配置する。<c>--delete-source</c> も指定すると、ソースファイルからシーンクラスを削除し、ほかに宣言がないファイルは削除する。変換できないシーンがプロジェクトに含まれる場合は、何も書き込まない。
/// <para>
/// <c>upgrade &lt;game .csproj&gt; [--write]</c> (ADR-0074) は、スキーマバージョン 1 または 2 のすべてのシーンファイルを現行のバージョン 3 に書き換える。角度は回転数に変換し、角度のキーでは C# の単位語を省略する。それ以外のテキストは維持する。いずれかのファイルに問題がある場合は何も書き込まない。
/// </para>
/// </summary>
internal static class Program {
    private static async Task<int>Main(string[] Arguments){
        if(Arguments.Length<2||Arguments[0] is not("convert" or "upgrade")){
            Console.Error.WriteLine("usage: CSharpGameEngine.SceneJson.Cli convert <game .csproj> [--write] [--delete-source]");
            Console.Error.WriteLine("       CSharpGameEngine.SceneJson.Cli upgrade <game .csproj> [--write]");
            return 2;
        }
        var ProjectPath=Path.GetFullPath(Arguments[1]);
        var Write=Arguments.Contains("--write");
        var DeleteSource=Arguments.Contains("--delete-source");
        MSBuildLocator.RegisterDefaults();
        return Arguments[0]=="upgrade"?await Upgrade(ProjectPath,Write):await Convert(ProjectPath,Write,DeleteSource);
    }
    private static async Task<int>Upgrade(string ProjectPath,bool Write){
        using var Workspace=MSBuildWorkspace.Create();
        var Project=await Workspace.OpenProjectAsync(ProjectPath);
        var Compilation=await Project.GetCompilationAsync()??throw new InvalidOperationException("The project has no compilation.");
        var ProjectDirectory=Path.GetDirectoryName(ProjectPath)!;
        var Scenes=Path.Combine(ProjectDirectory,SceneFiles.FolderName);
        List<(string Path,string Text)>Outputs=[];
        var Failed=false;
        IEnumerable<string>Files=Directory.Exists(Scenes)?Directory.EnumerateFiles(Scenes,"*"+SceneFiles.Extension,SearchOption.AllDirectories).Order(StringComparer.Ordinal):[];
        foreach(var ScenePath in Files){
            var Result=SceneJsonAngleUpgrade.Upgrade(File.ReadAllText(ScenePath),Compilation);
            foreach(var Problem in Result.Problems){ Console.Error.WriteLine($"{ScenePath}: {Problem}"); }
            if(Result.Text is null){
                Failed=true;
                continue;
            }
            Console.WriteLine($"CGE-SCENE-JSON {(Result.Changed?"upgraded":"already version "+SceneJsonSceneAnalyzer.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))} {ScenePath}");
            if(Result.Changed){ Outputs.Add((ScenePath,Result.Text!)); }
        }
        if(Failed){
            Console.Error.WriteLine("Nothing was written: fix the problems above and run again.");
            return 1;
        }
        if(Write){
            foreach(var Output in Outputs){ File.WriteAllText(Output.Path,Output.Text); }
        }
        return 0;
    }
    /// <summary>変換したシーンの出力先。シーンファイルはプロジェクトの Scenes フォルダー以下にある <c>*.json</c>。</summary>
    internal static string SceneJsonPath(string ProjectDirectory,string SourcePath,string SceneName){
        var Beside=Path.Combine(Path.GetDirectoryName(SourcePath)!,SceneName+SceneFiles.Extension);
        return SceneFiles.IsScene(Beside,ProjectDirectory)
            ?Beside
            :Path.Combine(ProjectDirectory,SceneFiles.FolderName,SceneName+SceneFiles.Extension);
    }
    private static async Task<int>Convert(string ProjectPath,bool Write,bool DeleteSource){
        using var Workspace=MSBuildWorkspace.Create();
        var Project=await Workspace.OpenProjectAsync(ProjectPath);
        var Compilation=await Project.GetCompilationAsync()??throw new InvalidOperationException("The project has no compilation.");
        var ProjectDirectory=Path.GetDirectoryName(ProjectPath)!;
        var SchemaPath=Path.Combine(ProjectDirectory,".cge",SceneJsonSchemaBuilder.FileName);
        List<(string JsonPath,string Text)>Outputs=[];
        Dictionary<SyntaxTree,List<SyntaxNode>>SceneDeclarations=[];
        var Failed=false;
        foreach(var Scene in EnumerateScenes(Compilation.Assembly.GlobalNamespace)){
            var Declaration=Scene.DeclaringSyntaxReferences.FirstOrDefault();
            if(Declaration is null||!Declaration.SyntaxTree.FilePath.StartsWith(ProjectDirectory,StringComparison.OrdinalIgnoreCase)){ continue; }
            var SourcePath=Declaration.SyntaxTree.FilePath;
            var JsonPath=SceneJsonPath(ProjectDirectory,SourcePath,Scene.Name);
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
            // ビルドでドキュメントが受け入れられなければならない。ジェネレーターの規則で読み戻して確認する。
            foreach(var Problem in SceneJsonSceneAnalyzer.Analyze(Result.Text,Compilation).Problems){
                Console.Error.WriteLine($"{JsonPath}: {Problem.Descriptor.Id} {string.Format(Problem.Descriptor.MessageFormat.ToString(),Problem.Arguments)}");
                Failed=true;
            }
            foreach(var Part in Scene.DeclaringSyntaxReferences.Where(static Part=>File.Exists(Part.SyntaxTree.FilePath)&&!Part.SyntaxTree.FilePath.EndsWith(".g.cs",StringComparison.OrdinalIgnoreCase))){
                if(!SceneDeclarations.TryGetValue(Part.SyntaxTree,out var Nodes)){ SceneDeclarations[Part.SyntaxTree]=Nodes=[]; }
                Nodes.Add(await Part.GetSyntaxAsync());
            }
            Outputs.Add((JsonPath,Result.Text));
            Console.WriteLine($"CGE-SCENE-JSON {Scene.ToDisplayString()} -> {JsonPath} ({Result.Text.Length} chars)");
        }
        if(Failed){
            Console.Error.WriteLine("Nothing was written: fix the problems above and run again.");
            return 1;
        }
        if(!Write){ return 0; }
        foreach(var Output in Outputs){
            Directory.CreateDirectory(Path.GetDirectoryName(Output.JsonPath)!);
            File.WriteAllText(Output.JsonPath,Output.Text);
        }
        if(!DeleteSource){ return 0; }
        foreach(var Pair in SceneDeclarations){
            var Root=await Pair.Key.GetRootAsync();
            var Remaining=Root.RemoveNodes(Pair.Value,SyntaxRemoveOptions.KeepNoTrivia)!;
            if(!Remaining.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax>().Any()){
                File.Delete(Pair.Key.FilePath);
                Console.WriteLine($"CGE-SCENE-JSON deleted {Pair.Key.FilePath}");
            } else {
                File.WriteAllText(Pair.Key.FilePath,Remaining.ToFullString());
                Console.WriteLine($"CGE-SCENE-JSON removed the scene classes from {Pair.Key.FilePath}");
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
