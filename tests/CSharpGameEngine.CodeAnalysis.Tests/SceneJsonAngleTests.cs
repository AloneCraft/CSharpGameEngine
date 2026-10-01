using CSharpGameEngine.SceneAnalysis.Json;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// ADR-0074: scene JSON stores every number inside an <c>[Angle]</c> member in turns from schema version 2 on; the C#
/// member keeps its own unit, so a version 1 document and its version 2 form build the same values.
/// </summary>
public sealed class SceneJsonAngleTests {
    private const string Components="""
        using CSharpGameEngine.Contracts;
        namespace Sample;
        public struct Euler { public Euler(float R,float U,float F){ this.R=R; this.U=U; this.F=F; } public float R; public float U; public float F; }
        public struct Spin { public Spin([Angle(AngleUnit.Radians)] Euler Value){ this.Value=Value; } public Euler Value; }
        public struct Aim { [Angle(AngleUnit.Degrees)] public float YawDegrees; [Angle(AngleUnit.Radians)] public float FovRadians; public float Plain; }
        internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem {
            public void Update(ref Aim aim, ref CSharpGameEngine.Entities.State state) { }
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
                Entity Root=World.GetSceneRoot(World.InstantiateScene<MainScene>());
                if(!World.TryGetComponent(Root,out Aim A)||!World.TryGetComponent(Root,out Spin S)){ return "missing component"; }
                return string.Join("|",new[]{A.YawDegrees,A.FovRadians,A.Plain,S.Value.R,S.Value.U,S.Value.F}.Select(static Value=>Value.ToString("R",CultureInfo.InvariantCulture)));
            }
        }
        """;
    private static string Document(string Version,string Yaw,string Fov,string R,string U)=>$$"""
        {
          "schemaVersion": {{Version}},
          "namespace": "Game",
          "className": "MainScene",
          "root": {
            "components": [
              { "type": "Sample.Aim", "members": { "YawDegrees": {{Yaw}}, "FovRadians": {{Fov}}, "Plain": 2 } },
              { "type": "Sample.Spin", "members": { "Value": { "R": {{R}}, "U": {{U}}, "F": 0 } } }
            ]
          }
        }
        """;
    private static Microsoft.CodeAnalysis.CSharp.CSharpCompilation Base()=>
        GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components]);
    private static string Describe(string Json)=>(string)GameSystemGeneratorTestHelper.Invoke(
        GameSystemGeneratorTestHelper.CompileAndLoad(
            GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components,Driver]),
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",Json)]),
        "Game.Driver","Describe")!;
    [Fact]
    public void TurnsDocument_BuildsTheSameValuesAsItsVersionOneForm(){
        var VersionOne=Describe(Document("1","90","1.5707964","1.2","0.3"));
        Assert.Equal("90|1.5707964|2|1.2|0.3|0",VersionOne);
        Assert.Equal(VersionOne,Describe(Document("2","0.25","0.25","0.19098594","0.047746485")));
    }
    [Fact]
    public void Converter_WritesVersionTwoWithAnglesInTurns(){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components,"""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new Aim { YawDegrees=90f,FovRadians=1.5707964f,Plain=2f },new Spin(new Euler(1.2f,0.3f,0f))] };
            }
            """]);
        var Model=CSharpGameEngine.SceneAnalysis.SceneDeclarationAnalyzer.Analyze(Compilation.GetTypeByMetadataName("Game.MainScene")!,Compilation,CancellationToken.None);
        var Converted=SceneJsonConverter.Convert(Model,null);
        Assert.Empty(Converted.Problems);
        var Json=Converted.Text!;
        Assert.Contains("\"schemaVersion\": 2",Json,StringComparison.Ordinal);
        Assert.Contains("\"YawDegrees\": 0.25,",Json,StringComparison.Ordinal);
        Assert.Contains("\"FovRadians\": 0.25,",Json,StringComparison.Ordinal);
        Assert.Contains("\"Plain\": 2",Json,StringComparison.Ordinal);
        Assert.Contains("\"Value\": { \"R\": 0.19098594, \"U\": 0.047746485, \"F\": 0 }",Json,StringComparison.Ordinal);
        Assert.Equal("90|1.5707964|2|1.2|0.3|0",Describe(Json));
    }
    /// <summary>An upgrade rewrites the angle numbers and the version and leaves every other character of the text.</summary>
    [Fact]
    public void Upgrade_RewritesOnlyTheAngleNumbersAndTheVersion(){
        var Before=Document("1","90","1.5707964","1.2","0.3").Replace("\"Plain\": 2","\"Plain\":2",StringComparison.Ordinal);
        var Result=SceneJsonAngleUpgrade.Upgrade(Before,Base());
        Assert.Empty(Result.Problems);
        Assert.True(Result.Changed);
        Assert.Equal(Document("2","0.25","0.25","0.19098594","0.047746485").Replace("\"Plain\": 2","\"Plain\":2",StringComparison.Ordinal),Result.Text);
        var Again=SceneJsonAngleUpgrade.Upgrade(Result.Text!,Base());
        Assert.False(Again.Changed);
        Assert.Equal(Result.Text,Again.Text);
    }
    /// <summary>
    /// A float in turns is too coarse to reach every float in degrees or radians (500 degrees, 1.5707965 radians); the
    /// conversion goes through double, so an upgrade keeps every value exactly. The first float-only version failed here.
    /// </summary>
    [Fact]
    public void Upgrade_KeepsValuesWithoutAFloatPreimageInTurns(){
        var Before=Document("1","500","1.5707965","-3.1415925","1.5707961");
        var Result=SceneJsonAngleUpgrade.Upgrade(Before,Base());
        Assert.Empty(Result.Problems);
        Assert.Equal(Describe(Before),Describe(Result.Text!));
    }
    [Fact]
    public void Schema_DescribesAngleMembersInTurnsAndAcceptsBothVersions(){
        var Schema=SceneJsonSchemaBuilder.Build(Base());
        Assert.Contains("\"enum\": [ 1, 2 ]",Schema,StringComparison.Ordinal);
        Assert.Contains("Angle in turns (1 = one full rotation; the C# member is in degrees).",Schema,StringComparison.Ordinal);
        Assert.Contains("Angle in turns (1 = one full rotation; the C# member is in radians). Constructor parameter of Spin.",Schema,StringComparison.Ordinal);
    }
}
