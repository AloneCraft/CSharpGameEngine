using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>ADR-0079: a data asset (*.cgeasset) is one value of a [DataAsset] type, compiled into the Assets catalog like a scene component.</summary>
public sealed class DataAssetTests {
    private const string Root=SceneProjectOptions.ProjectDirectory;
    private static readonly string Attribute=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"TestAssets","Core","DataAssetAttribute.cs"));

    private const string Types="""
        using CSharpGameEngine.Authoring;

        namespace Game;

        public enum Drive { Front, Rear, All }

        [DataAsset]
        public struct CarSetup
        {
            public float Mass;
            public int Gears;
            public Drive Drive;
            public float[] Ratios;
        }

        public struct NotData { public float Mass; }
        """;

    private const string AnySystem="""
        using CSharpGameEngine.Entities;
        using Sample;

        namespace Game;

        internal struct Any : ISystem
        {
            public void Update(ref Position position) { }
        }
        """;

    private static AdditionalText Asset(string RelativePath,string Text)=>new InMemoryAdditionalText(Root+RelativePath.Replace('/','\\'),Text);

    private const string Sport="""
        { "schemaVersion": 3, "type": "Game.CarSetup", "members": { "Mass": 1500, "Gears": 5, "Drive": "Rear", "Ratios": [4.25, 2.52, 1.66] } }
        """;

    [Fact]
    public void ADataAsset_IsItsValueInTheCatalog(){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([GameSystemGeneratorTestHelper.CommonComponents,Attribute,Types,AnySystem]);
        var Result=GameSystemGeneratorTestHelper.RunGenerator(Compilation,[Asset("Assets/Cars/Sport.cgeasset",Sport)]);

        Assert.Empty(Result.Diagnostics);
        var Catalog=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Assets.g.cs");
        Assert.Contains("internal static class Cars {",Catalog);
        Assert.Contains("public static readonly global::Game.CarSetup Sport = ",Catalog);
    }

    [Fact]
    public void ADataAssetsValue_ReadsAtRuntime(){
        var Source="""
            namespace Game;

            internal static class TestEntry
            {
                internal static string RunScenario()
                {
                    var Car = Assets.Cars.Sport;
                    return $"{Car.Mass}|{Car.Gears}|{Car.Drive}|{string.Join(",", Car.Ratios)}";
                }
            }
            """;
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([GameSystemGeneratorTestHelper.CommonComponents,Attribute,Types,AnySystem,Source]);
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(Compilation,[Asset("Assets/Cars/Sport.cgeasset",Sport)]);

        Assert.Equal("1500|5|Rear|4.25,2.52,1.66",GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario"));
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 3, "type": "Game.NotData", "members": {} }""","SCENE201")]
    [InlineData("""{ "schemaVersion": 3, "type": "Game.Missing" }""","SCENE201")]
    [InlineData("""{ "schemaVersion": 3, "type": "Game.CarSetup", "members": { "Weight": 1 } }""","SCENE202")]
    [InlineData("""{ "schemaVersion": 3, "type": "Game.CarSetup", "members": { "Gears": "five" } }""","SCENE203")]
    [InlineData("""{ "type": "Game.CarSetup" }""","SCENE204")]
    [InlineData("""{ "schemaVersion": 3, "type": "Game.CarSetup", "className": "X" }""","SCENE204")]
    [InlineData("""{ "schemaVersion": 3, """,null)]
    public void AnInvalidDataAsset_IsReportedAndLeftOutOfTheCatalog(string Text,string?Expected){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([GameSystemGeneratorTestHelper.CommonComponents,Attribute,Types,AnySystem]);
        var Result=GameSystemGeneratorTestHelper.RunGenerator(Compilation,[Asset("Assets/Cars/Bad.cgeasset",Text),Asset("Assets/Sounds/Horn.wav","")]);

        Assert.NotEmpty(Result.Diagnostics);
        if(Expected is not null){ Assert.Contains(Result.Diagnostics,Diagnostic=>Diagnostic.Id==Expected); }
        Assert.All(Result.Diagnostics,Diagnostic=>Assert.EndsWith("Bad.cgeasset",Diagnostic.Location.GetLineSpan().Path));
        var Catalog=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Assets.g.cs");
        Assert.DoesNotContain("Bad",Catalog);
        Assert.Contains("SoundAsset Horn",Catalog);
    }
}
