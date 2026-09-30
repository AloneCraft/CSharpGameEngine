using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// ADR-0070: scene JSON documents (<c>Scenes\**\*.json</c>) are compiled to the same scenes as C# scene classes, and
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
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",JsonScene)]);
        Assert.Equal("1.5|-2|0.2|3|Bright|a \"b\"|True|7|-5|-3.25",Describe(FromCSharp));
        Assert.Equal(Describe(FromCSharp),Describe(FromJson));
    }
    /// <summary>
    /// A scene is a JSON file under the project's Scenes folder (ADR-0070, revised 2026-10-01): the
    /// same document elsewhere, or with the old <c>.scene.json</c> name outside Scenes, is not built.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Game\Scenes\MainScene.json",true)]
    [InlineData(@"C:\Game\Scenes\Levels\MainScene.json",true)]
    [InlineData(@"C:\Game\Assets\MainScene.json",false)]
    [InlineData(@"C:\Game\MainScene.scene.json",false)]
    public void OnlyJsonUnderScenes_IsBuiltAsAScene(string Path,bool Built){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components]),
            [new InMemoryAdditionalText(Path,JsonScene)]);
        var Generated=string.Join("\n",Result.GeneratedTrees.Select(static Tree=>Tree.ToString()));
        Assert.Equal(Built,Generated.Contains("MainScene",StringComparison.Ordinal));
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
        Assert.EndsWith(@"Scenes\MainScene.json",Diagnostic.Location.GetLineSpan().Path);
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
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",Broken)]);
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
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",Json)]);
        Assert.Equal(Describe(GameSystemGeneratorTestHelper.CompileAndLoad(CSharpCompilation)),Describe(FromJson));
        Assert.Equal(Json,CSharpGameEngine.SceneAnalysis.Json.SceneJsonConverter.Convert(Model,"../.cge/scene.schema.json").Text);
    }
    /// <summary>
    /// <c>default</c> is written as the member type's own JSON form: <c>false</c>, <c>0</c>, the enum member
    /// whose value is zero, an empty object for a struct and <c>null</c> for a reference.
    /// </summary>
    [Fact]
    public void Converter_WritesDefaultValuesInTheirMemberTypesJsonForm(){
        const string DefaultsComponent="""
            namespace Sample {
                public enum Shade { Dark,Bright }
                public struct Pair { public float X; public float Y; }
                public struct Defaults { public bool Enabled; public int Count; public float Scale; public Shade Mode; public Pair Offset; public string? Label; }
            }
            """;
        const string Scene="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class DefaultsScene:IScene {
                public Node Root=>new() { Components=[new Sample.Defaults { Enabled=default,Count=default,Scale=default,Mode=default,Offset=default,Label=default }] };
            }
            """;
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,DefaultsComponent,Scene]);
        var Model=CSharpGameEngine.SceneAnalysis.SceneDeclarationAnalyzer.Analyze(Compilation.GetTypeByMetadataName("Game.DefaultsScene")!,Compilation,CancellationToken.None);
        Assert.False(Model.HasErrors,string.Join("\n",Model.Diagnostics));

        var Converted=CSharpGameEngine.SceneAnalysis.Json.SceneJsonConverter.Convert(Model,null);

        Assert.Empty(Converted.Problems);
        var Json=Converted.Text!;
        Assert.Contains("\"Enabled\": false",Json);
        Assert.Contains("\"Count\": 0",Json);
        Assert.Contains("\"Scale\": 0",Json);
        Assert.Contains("\"Mode\": \"Dark\"",Json);
        Assert.Contains("\"Offset\": {}",Json);
        Assert.Contains("\"Label\": null",Json);
        var Base=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,DefaultsComponent]);
        Assert.Empty(CSharpGameEngine.SceneAnalysis.Json.SceneJsonSceneAnalyzer.Analyze(Json,Base).Problems);
    }
    /// <summary>
    /// The Scene Designer reads a JSON scene through the C# class it is equivalent to, and writes it back
    /// with the converter: the model matches the C# scene's and the round trip is byte-identical.
    /// </summary>
    [Fact]
    public void DesignerSource_YieldsTheCSharpScenesModelAndConvertsBackByteIdentically(){
        var Base=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components]);
        var CSharpCompilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components,CSharpScene]);
        var CSharpModel=CSharpGameEngine.SceneAnalysis.SceneDeclarationAnalyzer.Analyze(CSharpCompilation.GetTypeByMetadataName("Game.MainScene")!,CSharpCompilation,CancellationToken.None);
        var Json=CSharpGameEngine.SceneAnalysis.Json.SceneJsonConverter.Convert(CSharpModel,"../.cge/scene.schema.json").Text!;
        var Scene=CSharpGameEngine.SceneAnalysis.Json.SceneJsonSceneAnalyzer.Analyze(Json,Base);
        var Source=CSharpGameEngine.SceneAnalysis.Json.SceneJsonDesignerSource.Build(Scene)!;
        const string JsonPath=@"C:\Game\Scenes\MainScene.json";
        var WithDesigner=Base.AddSyntaxTrees(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(Source,(Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)Base.SyntaxTrees.First().Options,JsonPath));
        var Model=CSharpGameEngine.SceneAnalysis.SceneDeclarationAnalyzer.Analyze(WithDesigner.GetTypeByMetadataName("Game.MainScene")!,WithDesigner,CancellationToken.None);
        Assert.False(Model.HasErrors,string.Join("\n",Model.Diagnostics));
        Assert.Equal(JsonPath,Model.FilePath);
        Assert.Equal(Semantic(CSharpModel),Semantic(Model));
        var Ids=Scene.NodeIdsByPath();
        Assert.Equal(Json,CSharpGameEngine.SceneAnalysis.Json.SceneJsonConverter.Convert(Model,"../.cge/scene.schema.json",Path=>Ids.TryGetValue(Path,out var Id)?Id:null).Text);
    }
    /// <summary>Node names, component types and member values, independent of how the source was written.</summary>
    private static string Semantic(CSharpGameEngine.SceneAnalysis.SceneModel Model){
        var Builder=new System.Text.StringBuilder();
        string Dump(CSharpGameEngine.SceneAnalysis.SceneValue Item)=>Item.Kind switch {
            CSharpGameEngine.SceneAnalysis.SceneValueKind.EnumMember=>Item.ReferencedSymbol!.Name,
            CSharpGameEngine.SceneAnalysis.SceneValueKind.ObjectCreation=>"("+string.Join(",",Item.Arguments.Select(Dump))+"){"+string.Join(",",Item.Members.Select(Member=>Member.MemberName+"="+Dump(Member.Value)))+"}",
            CSharpGameEngine.SceneAnalysis.SceneValueKind.Collection=>"["+string.Join(",",Item.Elements.Select(Dump))+"]",
            _=>Convert.ToString(Item.ConstantValue,System.Globalization.CultureInfo.InvariantCulture)??"null",
        };
        void Walk(CSharpGameEngine.SceneAnalysis.SceneNode Node){
            Builder.Append(Node.Path.ToDisplayString()).Append(' ').AppendLine(Node.Name);
            foreach(var Component in Node.Components){
                Builder.Append("  ").Append(Component.TypeFullyQualifiedName).Append('(').Append(string.Join(",",Component.ConstructorArguments.Select(Dump))).Append(')');
                foreach(var Member in Component.Members){ Builder.Append(' ').Append(Member.MemberName).Append('=').Append(Dump(Member.Value)); }
                Builder.AppendLine();
            }
            foreach(var Child in Node.Children){ Walk(Child); }
        }
        Walk(Model.Root!);
        return Builder.ToString();
    }
    private static ImmutableArray<Diagnostic>Analyze(string Json){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components]);
        var Options=SceneProjectOptions.Analyzer([new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",Json)]);
        return Compilation.WithAnalyzers([new SceneJsonDslAnalyzer()],Options).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }
}
