using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>ADR-0079: a scene under an enabled plugin's Scenes folder is built like a game scene, wherever the plugin is.</summary>
public sealed class PluginSceneTests {
    private const string Scene="""
        { "schemaVersion": 3, "namespace": "Cars.Scenes", "className": "Garage", "startupOrder": 5,
          "root": { "id": "11111111-1111-5111-8111-111111111111", "name": "Garage", "components": [
            { "type": "Sample.Position", "members": { "X": 2 } } ], "children": [] } }
        """;

    private static GeneratorDriverRunResult Run(AdditionalText File){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([GameSystemGeneratorTestHelper.CommonComponents,"""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct Any : ISystem
            {
                public void Update(ref Position position) { }
            }
            """]);
        return GameSystemGeneratorTestHelper.RunGenerator(Compilation,[File]);
    }

    [Theory]
    [InlineData(SceneProjectOptions.EnginePluginDirectory+@"Scenes\Garage.json")]
    [InlineData(SceneProjectOptions.ProjectDirectory+@"Plugins\Cars\Scenes\Garage.json")]
    public void APluginScene_IsBuiltAndStarts(string Path){
        var Result=Run(new InMemoryAdditionalText(Path,Scene));

        Assert.Empty(Result.Diagnostics);
        Assert.Contains(Result.GeneratedTrees,static Tree=>Tree.FilePath.EndsWith("Cars.Scenes.Garage.Scene.g.cs",StringComparison.Ordinal));
        Assert.Contains(Result.GeneratedTrees,static Tree=>Tree.FilePath.EndsWith("Cars.Scenes.Garage.StartupSystem.g.cs",StringComparison.Ordinal));
    }

    [Fact]
    public void ASceneOfAPluginLibrarysComponents_IsBuiltAndItsSystemRuns(){
        // The Lightning sample: the components live in a referenced DLL, the system and the scene in the game's compilation.
        var Library=Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("Lightning.Library",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("namespace Lightning { public struct Bolt { public int Count; } public struct Intensity { public float Value; } }")],
            GameSystemGeneratorTestHelper.References,new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var Image=new MemoryStream();
        Assert.True(Library.Emit(Image).Success);
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([GameSystemGeneratorTestHelper.CommonComponents,"""
            using CSharpGameEngine.Entities;

            namespace Lightning;

            internal struct Thunder : ISystem
            {
                public void Update(ref Bolt bolt, in Intensity intensity) { bolt.Count++; }
            }
            """]).AddReferences(MetadataReference.CreateFromImage(Image.ToArray()));
        const string Storm="""
            { "schemaVersion": 3, "namespace": "Lightning.Scenes", "className": "LightningScene", "startupOrder": 101,
              "root": { "id": "a24c7e19-3f6b-4d82-b05e-9e1c3a7d5f60", "name": "Storm Cell", "components": [
                { "type": "Lightning.Bolt", "members": {} }, { "type": "Lightning.Intensity", "members": { "Value": 2 } } ], "children": [] } }
            """;
        var Result=GameSystemGeneratorTestHelper.RunGenerator(Compilation,[new InMemoryAdditionalText(SceneProjectOptions.EnginePluginDirectory+@"Scenes\Lightning.json",Storm)]);

        Assert.Empty(Result.Diagnostics);
        Assert.Contains(Result.GeneratedTrees,static Tree=>Tree.FilePath.EndsWith("Lightning.Scenes.LightningScene.Scene.g.cs",StringComparison.Ordinal));
    }

    [Fact]
    public void ThePluginRoots_SurviveTheEditorConfigThatCarriesThemToTheGenerator(){
        // The build writes CompilerVisibleProperty values into a generated .editorconfig. Its reader cuts a value at ';' or '#', so
        // the roots are separated by '|' (a ';' list arrived empty and a package plugin's scenes were never built).
        var Config=Microsoft.CodeAnalysis.AnalyzerConfig.Parse(
            "is_global = true\nbuild_property.CgePluginRoots = |Weather=C:\\Game\\Plugins\\Weather\\|Lightning=C:\\Users\\me\\.nuget\\packages\\lightning\\1.0.0\\plugin\\\n"
            +"build_property.Semicolons = ;Weather=C:\\Game\\\n",
            @"C:\Game\obj\Game.GeneratedMSBuildEditorConfig.editorconfig");
        var Options=Microsoft.CodeAnalysis.AnalyzerConfigSet.Create(System.Collections.Immutable.ImmutableArray.Create(Config)).GlobalConfigOptions.AnalyzerOptions;

        Assert.Equal(["Weather","Lightning"],SceneAnalysis.Json.PluginRoot.Parse(Options["build_property.cgepluginroots"]).Select(static Root=>Root.Name));
        Assert.Empty(SceneAnalysis.Json.PluginRoot.Parse(Options.TryGetValue("build_property.semicolons",out var Cut)?Cut:null));
    }

    [Fact]
    public void AProjectAndPluginsUnderFoldersWithHashOrSemicolon_KeepTheirPaths(){
        // A game under C:\C#\ lost its ProjectDir (cut to "C:\C"), so it built without its scenes and Assets catalog. The build now also
        // writes the folders with '#' as '?h' and ';' as '?s' (Game.targets), and the generator reads those first.
        var Config=Microsoft.CodeAnalysis.AnalyzerConfig.Parse(
            "is_global = true\n"
            +"build_property.ProjectDir = C:\\C#\\My;Game\\\n"
            +"build_property.CgeProjectDirectory = C:\\C?hs\\My?sGame\\\n"
            +"build_property.CgePluginRootsEncoded = |Weather=C:\\C?hs\\My?sGame\\Plugins\\Weather\\\n",
            @"C:\Game\obj\Game.GeneratedMSBuildEditorConfig.editorconfig");
        var Options=new Global(Microsoft.CodeAnalysis.AnalyzerConfigSet.Create(System.Collections.Immutable.ImmutableArray.Create(Config)).GlobalConfigOptions.AnalyzerOptions);

        Assert.Equal(@"C:\C#s\My;Game\",SceneJsonDslAnalyzer.ProjectDirectoryOf(Options));
        Assert.Equal(@"C:\C#s\My;Game\Plugins\Weather\",Assert.Single(SceneJsonDslAnalyzer.PluginRootsOf(Options)).Directory);
        Assert.True(SceneJsonDslAnalyzer.IsSceneFile(@"C:\C#s\My;Game\Scenes\Main.json",Options));
        Assert.Equal("Assets/Sounds/Jump.wav",AssetCatalogEmitter.RelativeAssetPath(@"C:\C#s\My;Game\Assets\Sounds\Jump.wav",SceneJsonDslAnalyzer.ProjectDirectoryOf(Options)));
    }

    private sealed class Global(System.Collections.Immutable.ImmutableDictionary<string,string>Values):Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions {
        public override bool TryGetValue(string Key,[System.Diagnostics.CodeAnalysis.NotNullWhen(true)]out string?Value)=>Values.TryGetValue(Key,out Value);
    }

    [Fact]
    public void JsonOutsideAPluginsScenesFolder_IsNotAScene(){
        var Result=Run(new InMemoryAdditionalText(SceneProjectOptions.EnginePluginDirectory+@"Runtime\Garage.json",Scene));

        Assert.DoesNotContain(Result.GeneratedTrees,static Tree=>Tree.FilePath.EndsWith("Garage.Scene.g.cs",StringComparison.Ordinal));
    }
}
