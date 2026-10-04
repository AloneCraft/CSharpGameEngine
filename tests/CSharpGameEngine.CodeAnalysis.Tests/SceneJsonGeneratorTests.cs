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
    /// A node name is any JSON string, line breaks included. The generator wrote it into a one-line comment, so a name
    /// with a line break (\n, or U+2028 and U+0085, at which C# also ends a line) put the rest of the name into the code.
    /// </summary>
    [Theory]
    [InlineData("La\\nthrow null;mp")]
    [InlineData("La\\u2028throw null;mp")]
    [InlineData("La\\r\\u0085mp */ \\\" end")]
    public void NodeNameWithALineBreak_Builds(string EscapedName){
        var Json=JsonScene.Replace("\"name\": \"Lamp\"","\"name\": \""+EscapedName+"\"");
        Assert.Contains(EscapedName,Json);
        var FromJson=GameSystemGeneratorTestHelper.CompileAndLoad(
            GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components,Driver]),
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",Json)]);
        Assert.Equal("1.5|-2|0.2|3|Bright|a \"b\"|True|7|-5|-3.25",Describe(FromJson));
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
    /// <summary>
    /// Two scene files that declare the same class (a copied file whose className was kept) build the first by path and
    /// report SCENE207 at the later file's className. The generator added the same scene twice, which threw for the
    /// duplicate hint name and dropped its whole output (CS8785).
    /// </summary>
    [Fact]
    public async Task TwoFilesDeclaringTheSameScene_BuildOneAndReportTheOther(){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components,Driver]);
        AdditionalText[]Files=[new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",JsonScene),new InMemoryAdditionalText(@"C:\Game\Scenes\Copy.json",JsonScene)];
        var Built=GameSystemGeneratorTestHelper.CompileAndLoad(Compilation,Files);
        Assert.Equal("1.5|-2|0.2|3|Bright|a \"b\"|True|7|-5|-3.25",Describe(Built));
        var Diagnostics=await Compilation.WithAnalyzers([new SceneJsonDslAnalyzer()],SceneProjectOptions.Analyzer([.. Files])).GetAnalyzerDiagnosticsAsync();
        var Duplicate=Assert.Single(Diagnostics);
        Assert.Equal("SCENE207",Duplicate.Id);
        Assert.Equal(@"C:\Game\Scenes\MainScene.json",Duplicate.Location.GetLineSpan().Path);
        Assert.Contains("Copy.json",Duplicate.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
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
    [InlineData("\"schemaVersion\": 1","\"schemaVersion\": 4","SCENE204",3,20)]
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
    /// <summary>
    /// A <c>ulong</c> member takes the whole range: the converter writes it, the document builds the same value, the
    /// Scene Designer reads it back unchanged, a negative number is an error and the schema calls it an integer. The
    /// JSON reader used to reject every ulong value that the converter and the Inspector write.
    /// </summary>
    [Fact]
    public void UInt64Member_ConvertsAndBuildsItsWholeRange(){
        const string Counter="""
            namespace Sample {
                public struct Counter { public ulong Big; public ulong Small; }
                internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem {
                    public bool Update(ref Counter counter, ref CSharpGameEngine.Entities.State state) => true;
                }
            }
            """;
        const string Scene="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new Sample.Counter { Big=18446744073709551615UL,Small=5UL }] };
            }
            """;
        const string CounterDriver="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public static class Driver {
                public static string Describe(){
                    World World=new();
                    Entity Root=World.GetSceneRoot(World.InstantiateScene<MainScene>());
                    return World.TryGetComponent(Root,out Sample.Counter C)?C.Big+"|"+C.Small:"missing component";
                }
            }
            """;
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Counter,Scene]);
        var Model=CSharpGameEngine.SceneAnalysis.SceneDeclarationAnalyzer.Analyze(Compilation.GetTypeByMetadataName("Game.MainScene")!,Compilation,CancellationToken.None);
        var Converted=CSharpGameEngine.SceneAnalysis.Json.SceneJsonConverter.Convert(Model,null);
        Assert.Empty(Converted.Problems);
        var Json=Converted.Text!;
        Assert.Contains("\"Big\": 18446744073709551615",Json);

        Assert.Empty(Analyze(Json,Counter));
        var Built=GameSystemGeneratorTestHelper.CompileAndLoad(
            GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Counter,CounterDriver]),
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",Json)]);
        Assert.Equal("18446744073709551615|5",Describe(Built));
        var Base=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Counter]);
        var Designer=Base.AddSyntaxTrees(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            CSharpGameEngine.SceneAnalysis.Json.SceneJsonDesignerSource.Build(CSharpGameEngine.SceneAnalysis.Json.SceneJsonSceneAnalyzer.Analyze(Json,Base))!,
            (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)Base.SyntaxTrees.First().Options));
        var DesignerModel=CSharpGameEngine.SceneAnalysis.SceneDeclarationAnalyzer.Analyze(Designer.GetTypeByMetadataName("Game.MainScene")!,Designer,CancellationToken.None);
        Assert.False(DesignerModel.HasErrors,string.Join("\n",DesignerModel.Diagnostics));
        Assert.Equal(Json,CSharpGameEngine.SceneAnalysis.Json.SceneJsonConverter.Convert(DesignerModel,null).Text);
        Assert.Equal("SCENE203",Assert.Single(Analyze(Json.Replace("\"Small\": 5","\"Small\": -1"),Counter)).Id);
        Assert.Equal("SCENE203",Assert.Single(Analyze(Json.Replace("18446744073709551615","18446744073709551616"),Counter)).Id);
        var Schema=CSharpGameEngine.SceneAnalysis.Json.SceneJsonReader.Parse(CSharpGameEngine.SceneAnalysis.Json.SceneJsonSchemaBuilder.Build(
            GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Counter])));
        Assert.Equal("integer",Schema.Get("definitions")!.Get("type:Sample.Counter")!.Get("properties")!.Get("Big")!.Get("type")!.Text);
    }
    /// <summary>
    /// A C# constant without a JSON number (NaN, an infinity) is reported like any other value JSON cannot hold. The
    /// converter used to throw for a float and to write an unreadable document for a double.
    /// </summary>
    [Theory]
    [InlineData("Scale=2f","Scale=float.PositiveInfinity","Infinity")]
    [InlineData("Scale=2f","Scale=float.NaN","NaN")]
    [InlineData("Far=1e300","Far=double.NegativeInfinity","-Infinity")]
    [InlineData("YawDegrees=90f","YawDegrees=float.NaN","NaN")]
    public void Converter_ReportsANumberJsonCannotHold(string Original,string Replacement,string Text){
        const string Lens="""
            namespace Sample {
                public struct Lens { public float Scale; public double Far; [CSharpGameEngine.Contracts.Angle(CSharpGameEngine.Contracts.AngleUnit.Degrees)] public float YawDegrees; }
            }
            """;
        const string Scene="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new Sample.Lens { Scale=2f,Far=1e300,YawDegrees=90f }] };
            }
            """;
        Assert.Empty(Convert(Scene).Problems);
        var Converted=Convert(Scene.Replace(Original,Replacement));

        Assert.Null(Converted.Text);
        Assert.Contains(Text+" has no JSON form",Assert.Single(Converted.Problems));

        CSharpGameEngine.SceneAnalysis.Json.SceneJsonConverter.Result Convert(string Source){
            var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Lens,Source]);
            var Model=CSharpGameEngine.SceneAnalysis.SceneDeclarationAnalyzer.Analyze(Compilation.GetTypeByMetadataName("Game.MainScene")!,Compilation,CancellationToken.None);
            Assert.False(Model.HasErrors,string.Join("\n",Model.Diagnostics));
            return CSharpGameEngine.SceneAnalysis.Json.SceneJsonConverter.Convert(Model,null);
        }
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
    /// <summary>
    /// A schema the post-build step cannot write fails the build with its cause: the line is in MSBuild's canonical form at
    /// the schema. It was "CGE-SCENE-SCHEMA failed: ...", which Exec logs as a message, so the error list showed only MSB3073.
    /// </summary>
    [Fact]
    public void SchemaTool_ReportsAFailedWriteAsABuildError(){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components]);
        Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(new GameSystemGenerator()).RunGeneratorsAndUpdateCompilation(Compilation,out var Output,out _);
        var Directory=Path.Combine(Path.GetTempPath(),"cge-scene-schema-"+Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        var OriginalError=Console.Error;
        try {
            var AssemblyPath=Path.Combine(Directory,"Game.dll");
            Assert.True(Output.Emit(AssemblyPath).Success);
            using var Error=new StringWriter();
            Console.SetError(Error);
            // The schema path is a directory, so writing it fails.
            Assert.Equal(1,CSharpGameEngine.SceneSchema.Cli.Program.Run([AssemblyPath,Directory]));
            Assert.StartsWith(Directory+": error CGE-SCENE-SCHEMA: ",Error.ToString(),StringComparison.Ordinal);
        } finally {
            Console.SetError(OriginalError);
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
    /// <summary>The designer reads a startup scene as one (its <c>startupOrder</c> survives the round trip back to JSON).</summary>
    [Fact]
    public void DesignerSource_KeepsTheStartupOrder(){
        // The raw string has the checkout's line endings (CRLF in a fresh clone on Windows).
        var Json=JsonScene.Replace("\r\n","\n").Replace("  \"className\": \"MainScene\",\n","  \"className\": \"MainScene\",\n  \"startupOrder\": -3,\n");
        Assert.Contains("\"startupOrder\": -3",Json);
        var Base=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components]);
        var Scene=CSharpGameEngine.SceneAnalysis.Json.SceneJsonSceneAnalyzer.Analyze(Json,Base);
        var Designer=Base.AddSyntaxTrees(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(CSharpGameEngine.SceneAnalysis.Json.SceneJsonDesignerSource.Build(Scene)!,
            (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)Base.SyntaxTrees.First().Options,@"C:\Game\Scenes\MainScene.json"));
        var Model=CSharpGameEngine.SceneAnalysis.SceneDeclarationAnalyzer.Analyze(Designer.GetTypeByMetadataName("Game.MainScene")!,Designer,CancellationToken.None);

        Assert.True(Model.IsStartupScene);
        Assert.Equal(-3,Model.StartupOrder);
        var Ids=Scene.NodeIdsByPath();
        Assert.Contains("  \"className\": \"MainScene\",\n  \"startupOrder\": -3,\n",CSharpGameEngine.SceneAnalysis.Json.SceneJsonConverter.Convert(Model,"../.cge/scene.schema.json",Path=>Ids.TryGetValue(Path,out var Id)?Id:null).Text);
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
    private const string ShelfComponents="""
        namespace Sample {
            public struct Shelf {
                public System.Collections.Generic.List<int> Items; public System.Collections.Generic.HashSet<int> Tags;
                public System.Collections.Immutable.ImmutableArray<int> Fixed; public System.Collections.Generic.Queue<int> Waiting;
                public System.Collections.Generic.Dictionary<string,int> Counts; public (int A,int B) Pair; public int[,] Grid;
            }
            internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem {
                public bool Update(ref Shelf shelf, ref CSharpGameEngine.Entities.State state) => true;
            }
        }
        """;
    private const string ShelfScene="""
        {
          "schemaVersion": 3,
          "namespace": "Game",
          "className": "MainScene",
          "root": {
            "id": "7c0c2c9e-6c56-4f46-9d0f-0c6f2c8f0001",
            "name": "Root",
            "components": [
              { "type": "Sample.Shelf", "members": { "Items": [1, 2], "Tags": [3], "Fixed": [4] } }
            ],
            "children": []
          }
        }
        """;
    /// <summary>
    /// A JSON array is emitted as a collection expression (an array creation for an array) and an object as
    /// <c>new T(...) { ... }</c>. For a type C# cannot create that way the generated file did not compile (CS1061 for
    /// <c>Queue&lt;T&gt;</c>, CS9215 for a dictionary, CS8181 for a tuple, CS0029 for a multidimensional array) and the
    /// scene had no diagnostic; it is a SCENE203 at the value.
    /// </summary>
    [Theory]
    [InlineData("\"Waiting\": [1]","this collection type cannot be created")]
    [InlineData("\"Counts\": [{ \"key\": \"a\", \"value\": 1 }]","this collection type cannot be created")]
    [InlineData("\"Pair\": { \"A\": 1, \"B\": 2 }","a tuple cannot be written")]
    [InlineData("\"Grid\": [1, 2]","a multidimensional array")]
    public void ValuesTheGeneratedCodeCannotCreate_AreErrorsAtTheirJsonPosition(string Member,string Reason){
        Assert.Empty(Analyze(ShelfScene,ShelfComponents));
        GameSystemGeneratorTestHelper.CompileAndLoad(
            GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,ShelfComponents]),
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",ShelfScene)]);
        var Json=ShelfScene.Replace("\"Fixed\": [4]","\"Fixed\": [4], "+Member);
        var Diagnostic=Assert.Single(Analyze(Json,ShelfComponents));
        Assert.Equal("SCENE203",Diagnostic.Id);
        Assert.Contains(Reason,Diagnostic.GetMessage(),StringComparison.Ordinal);
        Assert.Equal(Json.IndexOf(Member,StringComparison.Ordinal)+Member.IndexOf(": ",StringComparison.Ordinal)+2,Diagnostic.Location.SourceSpan.Start);
    }
    private const string NestComponents="""
        namespace Sample {
            public struct Nest { public Must Must; public NoDefault Plain; public Shape Shape; public Old Old; public ByRef ByRef; }
            public struct Must { public required int A; public int B; }
            public sealed class NoDefault { public NoDefault(int A){ this.A=A; } public int A; }
            public abstract class Shape { public int A; }
            public struct Old { [System.Obsolete("Use B.",true)] public int A; public int B; }
            public struct ByRef { public ByRef(ref int A){ this.A=A; } public int A; }
            internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem {
                public bool Update(ref Nest nest, ref CSharpGameEngine.Entities.State state) => true;
            }
        }
        """;
    private const string NestScene="""
        {
          "schemaVersion": 3,
          "namespace": "Game",
          "className": "MainScene",
          "root": {
            "id": "7c0c2c9e-6c56-4f46-9d0f-0c6f2c8f0001",
            "name": "Root",
            "components": [
              { "type": "Sample.Nest", "members": { "Must": { "A": 1 }, "Plain": { "A": 2 }, "Old": { "B": 3 }, "ByRef": { "A": 4 } } }
            ],
            "children": []
          }
        }
        """;
    /// <summary>
    /// An object is emitted as <c>new T(...) { ... }</c>, which did not compile, with no scene diagnostic, for a required
    /// member left out (CS9035), a class without a public parameterless constructor (CS7036, CS0122), an abstract class
    /// (CS0144) or an obsolete-as-error member (CS0619); they are SCENE203 at the value. A constructor with a ref
    /// parameter was chosen for its key (CS1620); it is skipped, so the member is set instead.
    /// </summary>
    [Theory]
    [InlineData("\"Must\": { \"A\": 1 }","\"Must\": { \"B\": 1 }","the required member 'A' needs a value")]
    [InlineData("\"Plain\": { \"A\": 2 }","\"Plain\": { }","no public constructor without parameters")]
    [InlineData("\"Old\": { \"B\": 3 }","\"Old\": { \"A\": 3 }","'A' is obsolete")]
    [InlineData("\"ByRef\": { \"A\": 4 }","\"ByRef\": { \"A\": 4 }, \"Shape\": { \"A\": 5 }","an abstract type cannot be created")]
    public void ObjectsTheGeneratedCodeCannotCreate_AreErrorsAtTheirJsonPosition(string Original,string Replacement,string Reason){
        Assert.Empty(Analyze(NestScene,NestComponents));
        GameSystemGeneratorTestHelper.CompileAndLoad(
            GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,NestComponents]),
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",NestScene)]);
        Assert.Contains(Original,NestScene);
        var Diagnostic=Assert.Single(Analyze(NestScene.Replace(Original,Replacement),NestComponents));
        Assert.Equal("SCENE203",Diagnostic.Id);
        Assert.Contains(Reason,Diagnostic.GetMessage(),StringComparison.Ordinal);
    }
    /// <summary>
    /// A JSON null for a reference member the game declares non-nullable (the palette writes one for an unset string or
    /// array) builds without a warning. The generated file is #nullable enable, so the plain null was CS8625, an error
    /// in a project that treats warnings as errors.
    /// </summary>
    [Fact]
    public void NullForANonNullableReferenceMember_BuildsWithoutWarnings(){
        const string Component="""
            #nullable enable
            namespace Sample {
                public struct Labelled { public string Label; public int[] Ids; public string? Note; public int? Count; }
                internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem {
                    public bool Update(ref Labelled labelled, ref CSharpGameEngine.Entities.State state) => true;
                }
            }
            """;
        var Json=NestScene.Replace("""{ "type": "Sample.Nest", "members": { "Must": { "A": 1 }, "Plain": { "A": 2 }, "Old": { "B": 3 }, "ByRef": { "A": 4 } } }""",
            """{ "type": "Sample.Labelled", "members": { "Label": null, "Ids": null, "Note": null, "Count": null } }""");
        Assert.Contains("Sample.Labelled",Json);
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Component]);
        Assert.Empty(Analyze(Json,Component));
        Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create([new GameSystemGenerator().AsSourceGenerator()],[new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",Json)],
            optionsProvider:SceneProjectOptions.Instance).RunGeneratorsAndUpdateCompilation(Compilation,out var Output,out _);
        var Scene=Assert.Single(Output.SyntaxTrees,static Tree=>Tree.FilePath.Contains("Scene.g",StringComparison.Ordinal));
        Assert.Contains("Label = null!",Scene.ToString(),StringComparison.Ordinal);
        Assert.Empty(Output.GetDiagnostics().Where(Diagnostic=>Diagnostic.Severity>=DiagnosticSeverity.Warning&&Diagnostic.Location.SourceTree==Scene));
    }
    private const string NamingComponents="""
        namespace Sample {
            public struct Kit { public float Speed; }
            public partial class Shared { }
            internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem {
                public bool Update(ref Kit kit, ref CSharpGameEngine.Entities.State state) => true;
            }
        }
        namespace Game.Sub { public class Thing { } }
        """;
    private static string NamedScene(string Namespace,string ClassName)=>
        "{ \"schemaVersion\": 3, \"namespace\": \""+Namespace+"\", \"className\": \""+ClassName+"\", \"root\": { \"components\": [ { \"type\": \"Sample.Kit\", \"members\": { \"Speed\": 1 } } ] } }";
    /// <summary>
    /// A scene whose full name is already a type (not a partial class) or a namespace is an error at its className, and
    /// the scene is not generated. The generated partial class collided with it (CS0101, CS0260, CS0436) with no scene
    /// diagnostic, as when a copied scene is renamed after a component.
    /// </summary>
    [Theory]
    [InlineData("Sample","Kit","is already a type")]
    [InlineData("Game","Sub","is already a namespace")]
    [InlineData("System","String","is already a type")]
    public void SceneClassNamingAnExistingTypeOrNamespace_IsAnErrorAtTheClassName(string Namespace,string ClassName,string Reason){
        var Json=NamedScene(Namespace,ClassName);
        var Diagnostic=Assert.Single(Analyze(Json,NamingComponents));
        Assert.Equal("SCENE204",Diagnostic.Id);
        Assert.Contains(Reason,Diagnostic.GetMessage(),StringComparison.Ordinal);
        Assert.Equal(Json.IndexOf("\""+ClassName+"\"",StringComparison.Ordinal),Diagnostic.Location.SourceSpan.Start);
        GameSystemGeneratorTestHelper.CompileAndLoad(
            GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,NamingComponents]),
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",Json)]);
    }
    /// <summary>
    /// The analyzer in a build, and the editor, see the scene's own generated class; it is no conflict, nor is a user's
    /// partial class of the name, which the generated one joins.
    /// </summary>
    [Theory]
    [InlineData("Game","MainScene")]
    [InlineData("Sample","Shared")]
    public async Task SceneClassAndItsGeneratedFile_AreNotAConflict(string Namespace,string ClassName){
        var Json=NamedScene(Namespace,ClassName);
        AdditionalText[]Files=[new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",Json)];
        Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create([new GameSystemGenerator().AsSourceGenerator()],Files,optionsProvider:SceneProjectOptions.Instance)
            .RunGeneratorsAndUpdateCompilation(GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,NamingComponents]),out var Output,out _);
        Assert.Contains(Output.SyntaxTrees,Tree=>Tree.FilePath.EndsWith(Namespace+"."+ClassName+".Scene.g.cs",StringComparison.Ordinal));
        Assert.Empty(await Output.WithAnalyzers([new SceneJsonDslAnalyzer()],SceneProjectOptions.Analyzer([.. Files])).GetAnalyzerDiagnosticsAsync());
    }
    /// <summary>
    /// A nested component type builds by the name the converter, the schema and the palette write (Outer.Inner) as well as
    /// by its metadata name (Outer+Inner); only the metadata name resolved (SCENE201), and the schema left nested types out.
    /// </summary>
    [Theory]
    [InlineData("Sample.Outer.Inner")]
    [InlineData("Sample.Outer+Inner")]
    public void NestedComponentType_BuildsByEitherName(string TypeName){
        const string Nested="""
            namespace Sample {
                public static class Outer { public struct Inner { public float Speed; } }
                internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem {
                    public bool Update(ref Outer.Inner inner, ref CSharpGameEngine.Entities.State state) => true;
                }
            }
            """;
        var Json=NamedScene("Game","MainScene").Replace("Sample.Kit",TypeName);
        Assert.Empty(Analyze(Json,Nested));
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Nested]);
        GameSystemGeneratorTestHelper.CompileAndLoad(Compilation,[new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",Json)]);
        Assert.Contains("\"Sample.Outer.Inner\"",CSharpGameEngine.SceneAnalysis.Json.SceneJsonSchemaBuilder.Build(Compilation),StringComparison.Ordinal);
    }
    /// <summary>
    /// A scene class named like a C# contextual keyword is an error at its className and is not generated: 'var' turned
    /// the generated code's implicitly typed locals into the scene type, 'file' and 'required' parsed as modifiers,
    /// 'scoped' is an error and 'record' a warning, all in the generated file with no scene diagnostic.
    /// </summary>
    [Theory]
    [InlineData("var")]
    [InlineData("file")]
    [InlineData("required")]
    [InlineData("scoped")]
    [InlineData("record")]
    public void SceneClassNamedLikeAContextualKeyword_IsAnErrorAtTheClassName(string ClassName){
        var Json=NamedScene("Game",ClassName);
        var Diagnostic=Assert.Single(Analyze(Json,NamingComponents));
        Assert.Equal("SCENE204",Diagnostic.Id);
        Assert.Contains("contextual keyword",Diagnostic.GetMessage(),StringComparison.Ordinal);
        GameSystemGeneratorTestHelper.CompileAndLoad(
            GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,NamingComponents]),
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",Json)]);
    }
    /// <summary>
    /// The schema the editor warns with while typing agrees with the build: a value the build rejects (SCENE203) for a
    /// Queue, a dictionary, a tuple, a multidimensional array or an abstract type matches nothing, and an IEnumerable
    /// member takes an array. The schema offered an array or an object for the rejected ones and none for IEnumerable.
    /// </summary>
    [Fact]
    public void GeneratedSchema_AgreesWithTheBuildOnCollectionsAndObjects(){
        const string Source="""
            namespace Sample {
                public abstract class Shape { public int A; }
                public struct Bag {
                    public System.Collections.Generic.List<int> Items; public System.Collections.Generic.HashSet<int> Tags;
                    public System.Collections.Immutable.ImmutableArray<int> Fixed; public System.Collections.Generic.IEnumerable<int> Any;
                    public System.Collections.Generic.Queue<int> Waiting; public System.Collections.Generic.Dictionary<string,int> Counts;
                    public (int A,int B) Pair; public int[,] Grid; public Shape Outline;
                }
                internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem {
                    public bool Update(ref Bag bag, ref CSharpGameEngine.Entities.State state) => true;
                }
            }
            """;
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Source]);
        var Members=CSharpGameEngine.SceneAnalysis.Json.SceneJsonReader.Parse(CSharpGameEngine.SceneAnalysis.Json.SceneJsonSchemaBuilder.Build(Compilation))
            .Get("definitions")!.Get("type:Sample.Bag")!.Get("properties")!;
        foreach(var Name in new[]{"Items","Tags","Fixed","Any"}){
            Assert.Equal("array",Members.Get(Name)!.Get("type")!.Text);
        }
        foreach(var Name in new[]{"Waiting","Counts","Pair","Grid","Outline"}){
            Assert.NotNull(Members.Get(Name)!.Get("not"));
        }
    }
    /// <summary>
    /// A game whose systems do not run in the editor builds its generated files without a warning. The editor preview
    /// bridge declared and reset its system set without reading it then (CS0414), which stops a build that treats
    /// warnings as errors.
    /// </summary>
    [Fact]
    public void GameWithoutEditorSystems_BuildsItsGeneratedFilesWithoutWarnings(){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,NamingComponents]);
        Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create([new GameSystemGenerator().AsSourceGenerator()],
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",NamedScene("Game","MainScene"))],optionsProvider:SceneProjectOptions.Instance)
            .RunGeneratorsAndUpdateCompilation(Compilation,out var Output,out _);
        var Generated=Output.SyntaxTrees.Where(static Tree=>Tree.FilePath.EndsWith(".g.cs",StringComparison.Ordinal)).ToHashSet();
        Assert.Contains(Generated,static Tree=>Tree.ToString().Contains("GeneratedEditorPreviewBridge",StringComparison.Ordinal));
        Assert.Empty(Output.GetDiagnostics().Where(Diagnostic=>Diagnostic.Severity>=DiagnosticSeverity.Warning
            &&Diagnostic.Location.SourceTree is { }Tree&&Generated.Contains(Tree)).Select(static Diagnostic=>Diagnostic.ToString()));
    }
    /// <summary>Stand-ins with the engine's names: a validating physical light value and Q64.64's scalar.</summary>
    private const string RangedComponents="""
        namespace CSharpGameEngine.Lighting {
            public readonly struct LinearLightColor {
                public LinearLightColor(float R,float G,float B){ this.R=R; this.G=G; this.B=B; }
                public float R { get; }
                public float G { get; }
                public float B { get; }
            }
        }
        namespace CSharpGameEngine.Numerics {
            public readonly struct LogicalScalar {
                public static implicit operator LogicalScalar(float Value)=>default;
            }
        }
        namespace Sample {
            public struct Lamp { public CSharpGameEngine.Lighting.LinearLightColor Color; public CSharpGameEngine.Numerics.LogicalScalar Height; public float Scale; public double Far; public decimal Price; }
            internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem {
                public bool Update(ref Lamp lamp, ref CSharpGameEngine.Entities.State state) => true;
            }
        }
        """;
    private const string RangedScene="""
        {
          "schemaVersion": 1,
          "namespace": "Game",
          "className": "MainScene",
          "root": {
            "id": "7c0c2c9e-6c56-4f46-9d0f-0c6f2c8f0001",
            "name": "Root",
            "components": [
              { "type": "Sample.Lamp", "members": { "Color": { "R": 1, "G": 0.5, "B": 0 }, "Height": 9e18, "Scale": 3e38, "Far": -1e308, "Price": 7.9e28 } }
            ],
            "children": []
          }
        }
        """;
    [Theory]
    [InlineData("\"R\": 1,","\"R\": -1,","a LinearLightColor needs finite non-negative values")]
    [InlineData("\"Height\": 9e18","\"Height\": 1e19","beyond the Q64.64 range")]
    [InlineData("\"Scale\": 3e38","\"Scale\": 1e39","beyond the range of float")]
    [InlineData("\"Far\": -1e308","\"Far\": -1e309","beyond the range of double")]
    [InlineData("\"Price\": 7.9e28","\"Price\": 7.93e28","beyond the range of decimal")]
    public void ValuesTheRuntimeOrTheCompilerWouldReject_AreErrorsAtTheirJsonPosition(string Original,string Replacement,string Reason){
        // These build, then throw when the scene is instantiated (a light value's constructor, LogicalScalar's
        // conversion) or fail to compile in the generated file (a real literal beyond its type), so the build reports them.
        Assert.Empty(Analyze(RangedScene,RangedComponents));
        Assert.Contains(Original,RangedScene);
        var Diagnostic=Assert.Single(Analyze(RangedScene.Replace(Original,Replacement),RangedComponents));
        Assert.Equal("SCENE203",Diagnostic.Id);
        Assert.Contains(Reason,Diagnostic.GetMessage(),StringComparison.Ordinal);
    }
    private static ImmutableArray<Diagnostic>Analyze(string Json,string ComponentSource=Components){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,ComponentSource]);
        var Options=SceneProjectOptions.Analyzer([new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",Json)]);
        return Compilation.WithAnalyzers([new SceneJsonDslAnalyzer()],Options).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }
}
