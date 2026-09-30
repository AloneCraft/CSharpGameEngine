using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// ADR-0070: <c>*.scene.json</c> documents are compiled to the same scenes as C# scene classes, and
/// undefined types, members and values fail the build with errors at the JSON line and column.
/// </summary>
public sealed class SceneJsonGeneratorTests {
    private const string Components="""
        namespace Sample;
        public enum Mode { Off, Bright }
        public readonly struct Vec {
            public Vec(float X,float Y){ this.X=X; this.Y=Y; }
            public readonly float X;
            public readonly float Y;
        }
        public struct Transform { public Vec Position; public float Scale; }
        public struct Light { public float Intensity; public Mode Mode; public string Label; public bool Enabled; }
        public struct Tag { public int Count; public short Small; public Scalar Offset; }
        public readonly struct Scalar {
            private Scalar(float Value){ this.Value=Value; }
            public readonly float Value;
            public static implicit operator Scalar(float Value)=>new(Value);
        }
        internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem {
            public bool Update(ref Transform transform, ref CSharpGameEngine.Entities.State state) => true;
        }
        """;
    private const string Driver="""
        using System.Globalization;
        using CSharpGameEngine.Entities;
        using Sample;
        namespace Game;
        public static class Driver {
            public static string Describe(){
                World World=new();
                SceneInstance Instance=World.InstantiateScene<MainScene>();
                Entity Root=World.GetSceneRoot(Instance);
                Entity Child=new(Root.Id+1);
                if(!World.TryGetComponent(Root,out Transform T)||!World.TryGetComponent(Child,out Light L)||!World.TryGetComponent(Child,out Tag G)){
                    return "missing component";
                }
                return string.Join("|",
                    T.Position.X.ToString("R",CultureInfo.InvariantCulture),T.Position.Y.ToString("R",CultureInfo.InvariantCulture),T.Scale.ToString("R",CultureInfo.InvariantCulture),
                    L.Intensity.ToString("R",CultureInfo.InvariantCulture),L.Mode,L.Label,L.Enabled,G.Count,G.Small,G.Offset.Value.ToString("R",CultureInfo.InvariantCulture));
            }
        }
        """;
    private const string CSharpScene="""
        using CSharpGameEngine.Entities;
        using Sample;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() {
                Name="Root",
                Components=[new Transform { Position=new Vec(1.5f,-2f),Scale=0.2f }],
                Children=[
                    new Node { Name="Lamp",Components=[new Light { Intensity=3f,Mode=Mode.Bright,Label="a \"b\"",Enabled=true },new Tag { Count=7,Small=-5,Offset=-3.25f }] },
                ],
            };
        }
        """;
    private const string JsonScene="""
        {
          "$schema": "../.cge/scene.schema.json",
          "schemaVersion": 1,
          "namespace": "Game",
          "className": "MainScene",
          "root": {
            "id": "7c0c2c9e-6c56-4f46-9d0f-0c6f2c8f0001",
            "name": "Root",
            "components": [
              {
                "type": "Sample.Transform",
                "members": {
                  "Position": { "X": 1.5, "Y": -2 },
                  "Scale": 0.2
                }
              }
            ],
            "children": [
              {
                "id": "7c0c2c9e-6c56-4f46-9d0f-0c6f2c8f0002",
                "name": "Lamp",
                "components": [
                  { "type": "Sample.Light", "members": { "Intensity": 3, "Mode": "Bright", "Label": "a \"b\"", "Enabled": true } },
                  { "type": "Sample.Tag", "members": { "Count": 7, "Small": -5, "Offset": -3.25 } }
                ]
              }
            ]
          }
        }
        """;
    private static string Describe(Assembly Assembly)=>(string)GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.Driver","Describe")!;
    [Fact]
    public void JsonScene_BuildsTheSameWorldAsTheEquivalentCSharpScene(){
        var FromCSharp=GameSystemGeneratorTestHelper.CompileAndLoad(
            GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components,CSharpScene,Driver]));
        var FromJson=GameSystemGeneratorTestHelper.CompileAndLoad(
            GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components,Driver]),
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.scene.json",JsonScene)]);
        Assert.Equal("1.5|-2|0.2|3|Bright|a \"b\"|True|7|-5|-3.25",Describe(FromCSharp));
        Assert.Equal(Describe(FromCSharp),Describe(FromJson));
    }
    [Fact]
    public void JsonWithoutProblems_ReportsNoDiagnostics(){
        Assert.Empty(Analyze(JsonScene));
    }
    [Theory]
    [InlineData("\"Sample.Transform\"","\"Sample.Missing\"","SCENE201",11,17)]
    [InlineData("\"Scale\": 0.2","\"Size\": 0.2","SCENE202",14,11)]
    [InlineData("\"Mode\": \"Bright\"","\"Mode\": \"Dim\"","SCENE203",23,74)]
    [InlineData("\"Count\": 7","\"Count\": 7.5","SCENE203",24,57)]
    [InlineData("\"className\": \"MainScene\"","\"className\": \"MainScene\", \"extra\": 1","SCENE204",5,29)]
    [InlineData("\"Offset\": -3.25 } }","\"Offset\": -3.25 } }, { \"type\": \"Sample.Tag\" }","SCENE205",24,104)]
    [InlineData("\"schemaVersion\": 1","\"schemaVersion\": 2","SCENE204",3,20)]
    public void EachKindOfProblem_IsAnErrorAtItsJsonPosition(string Original,string Replacement,string ExpectedId,int Line,int Column){
        Assert.Contains(Original,JsonScene);
        var Diagnostic=Assert.Single(Analyze(JsonScene.Replace(Original,Replacement)));
        Assert.Equal(ExpectedId,Diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error,Diagnostic.Severity);
        var Position=Diagnostic.Location.GetLineSpan().StartLinePosition;
        Assert.Equal((Line,Column),(Position.Line+1,Position.Character+1));
        Assert.EndsWith("MainScene.scene.json",Diagnostic.Location.GetLineSpan().Path);
    }
    [Fact]
    public void MissingConstructorArgument_NamesTheMissingParameterAndTheUnknownKey(){
        var Diagnostics=Analyze(JsonScene.Replace("\"Position\": { \"X\": 1.5, \"Y\": -2 }","\"Position\": { \"X\": 1.5, \"Z\": -2 }"));
        Assert.Equal(["SCENE202","SCENE203"],Diagnostics.Select(static Diagnostic=>Diagnostic.Id).OrderBy(static Id=>Id));
        Assert.Contains("also needs 'Y'",Diagnostics.Single(static Diagnostic=>Diagnostic.Id=="SCENE203").GetMessage());
        Assert.Contains("'Z'",Diagnostics.Single(static Diagnostic=>Diagnostic.Id=="SCENE202").GetMessage());
    }
    [Fact]
    public void MalformedJson_IsReportedAndGeneratesNoScene(){
        var Broken=JsonScene.Replace("\"Scale\": 0.2","\"Scale\": 0.2,");
        var Diagnostic=Assert.Single(Analyze(Broken));
        Assert.Equal("SCENE204",Diagnostic.Id);
        var Result=GameSystemGeneratorTestHelper.RunGenerator(GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components]),
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.scene.json",Broken)]);
        Assert.DoesNotContain(Result.Results[0].GeneratedSources,Source=>Source.HintName.Contains("MainScene"));
    }
    [Fact]
    public void GeneratedSchema_ListsComponentTypesAndTheirMembersForEditorWarnings(){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components]);
        var Schema=CSharpGameEngine.SceneAnalysis.Json.SceneJsonReader.Parse(CSharpGameEngine.SceneAnalysis.Json.SceneJsonSchemaBuilder.Build(Compilation));
        var Definitions=Schema.Get("definitions")!;
        var TypeNames=Definitions.Get("component")!.Get("properties")!.Get("type")!.Get("enum")!.Items.Select(static Item=>Item.Text).ToArray();
        Assert.Contains("Sample.Transform",TypeNames);
        Assert.Contains("Sample.Light",TypeNames);
        Assert.Contains("Sample.Tag",TypeNames);
        Assert.DoesNotContain("Sample.Mode",TypeNames);
        var Light=Definitions.Get("type:Sample.Light")!;
        Assert.Equal(CSharpGameEngine.SceneAnalysis.Json.SceneJsonKind.False,Light.Get("additionalProperties")!.Kind);
        var LightMembers=Light.Get("properties")!;
        Assert.Equal("number",LightMembers.Get("Intensity")!.Get("type")!.Text);
        Assert.Equal(["Off","Bright"],LightMembers.Get("Mode")!.Get("enum")!.Items.Select(static Item=>Item.Text));
        Assert.Equal("boolean",LightMembers.Get("Enabled")!.Get("type")!.Text);
        var Vec=Definitions.Get("type:Sample.Vec")!.Get("properties")!;
        Assert.Equal(["X","Y"],Vec.Properties.Select(static Property=>Property.Name));
        var Offset=Definitions.Get("type:Sample.Tag")!.Get("properties")!.Get("Offset")!.Get("anyOf")!;
        Assert.Equal("number",Offset.Items[0].Get("type")!.Text);
        // Each component's members are tied to its type with if/then so unknown members warn.
        Assert.Contains(Definitions.Get("component")!.Get("allOf")!.Items,Condition=>
            Condition.Get("if")!.Get("properties")!.Get("type")!.Get("const")!.Text=="Sample.Light"
            &&Condition.Get("then")!.Get("properties")!.Get("members")!.Get("$ref")!.Text=="#/definitions/type:Sample.Light");
    }
    [Fact]
    public void Generator_EmbedsTheSchemaAsAConstant(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components]),[]);
        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.SceneSchema.g.cs");
        Assert.Contains("internal static class CgeGeneratedSceneSchema",Text);
        Assert.Contains("type:Sample.Light",Text);
    }
    [Fact]
    public void SchemaTool_CopiesTheEmbeddedSchemaFromMetadataAndSkipsUnchangedWrites(){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components]);
        Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(new GameSystemGenerator()).RunGeneratorsAndUpdateCompilation(Compilation,out var Output,out _);
        var Directory=Path.Combine(Path.GetTempPath(),"cge-scene-schema-"+Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        try {
            var AssemblyPath=Path.Combine(Directory,"Game.dll");
            var Emit=Output.Emit(AssemblyPath);
            Assert.True(Emit.Success,string.Join("\n",Emit.Diagnostics));
            Assert.True(CSharpGameEngine.SceneSchema.Cli.SceneSchemaExtractor.TryRead(AssemblyPath,out var Schema));
            Assert.Equal(CSharpGameEngine.SceneAnalysis.Json.SceneJsonSchemaBuilder.Build(Compilation),Schema);
            var SchemaPath=Path.Combine(Directory,".cge","scene.schema.json");
            Assert.True(CSharpGameEngine.SceneSchema.Cli.SceneSchemaExtractor.WriteIfChanged(SchemaPath,Schema));
            var WriteTime=File.GetLastWriteTimeUtc(SchemaPath);
            Assert.False(CSharpGameEngine.SceneSchema.Cli.SceneSchemaExtractor.WriteIfChanged(SchemaPath,Schema));
            Assert.Equal(WriteTime,File.GetLastWriteTimeUtc(SchemaPath));
            var Plain=Path.Combine(Directory,"Plain.dll");
            Assert.True(Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("Plain",[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("class A{}")],GameSystemGeneratorTestHelper.References,
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).Emit(Plain).Success);
            Assert.False(CSharpGameEngine.SceneSchema.Cli.SceneSchemaExtractor.TryRead(Plain,out _));
        } finally {
            System.IO.Directory.Delete(Directory,recursive:true);
        }
    }
    [Fact]
    public void Converter_TurnsTheCSharpSceneIntoJsonThatBuildsTheSameWorld(){
        var CSharpCompilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components,CSharpScene,Driver]);
        var SceneType=CSharpCompilation.GetTypeByMetadataName("Game.MainScene")!;
        var Model=CSharpGameEngine.SceneAnalysis.SceneDeclarationAnalyzer.Analyze(SceneType,CSharpCompilation,CancellationToken.None);
        var Converted=CSharpGameEngine.SceneAnalysis.Json.SceneJsonConverter.Convert(Model,"../.cge/scene.schema.json");
        Assert.Empty(Converted.Problems);
        var Json=Converted.Text!;
        Assert.Contains("\"Position\": { \"X\": 1.5, \"Y\": -2 }",Json);
        Assert.Contains("\"Mode\": \"Bright\"",Json);
        Assert.Empty(Analyze(Json));
        var FromJson=GameSystemGeneratorTestHelper.CompileAndLoad(
            GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components,Driver]),
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.scene.json",Json)]);
        Assert.Equal(Describe(GameSystemGeneratorTestHelper.CompileAndLoad(CSharpCompilation)),Describe(FromJson));
        Assert.Equal(Json,CSharpGameEngine.SceneAnalysis.Json.SceneJsonConverter.Convert(Model,"../.cge/scene.schema.json").Text);
    }
    private static ImmutableArray<Diagnostic>Analyze(string Json){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components]);
        var Options=new AnalyzerOptions([new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.scene.json",Json)]);
        return Compilation.WithAnalyzers([new SceneJsonDslAnalyzer()],Options).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }
}
