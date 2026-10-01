using CSharpGameEngine.SceneAnalysis.Json;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// ADR-0074: scene JSON stores every number inside an <c>[Angle]</c> member in turns from schema version 2 on; the C#
/// member keeps its own unit, so a version 1 document and its version 2 form build the same values. From version 3 an
/// angle's key drops the C# unit word (<c>YawDegrees</c> is <c>Yaw</c>), because the number is in turns.
/// </summary>
public sealed class SceneJsonAngleTests {
    private const string Components="""
        using CSharpGameEngine.Contracts;
        namespace Sample;
        public struct Euler { public Euler(float R,float U,float F){ this.R=R; this.U=U; this.F=F; } public float R; public float U; public float F; }
        public struct Spin { public Spin([Angle(AngleUnit.Radians)] Euler Value){ this.Value=Value; } public Euler Value; }
        public struct Aim { [Angle(AngleUnit.Degrees)] public float YawDegrees; [Angle(AngleUnit.Radians)] public float FovRadians; public float Plain; }
        public struct Tilt { [Angle(AngleUnit.Degrees)] public float PitchDegrees; public float Pitch; }
        public struct Cone {
            public Cone([Angle(AngleUnit.Degrees)] float OuterDegrees){ this.OuterDegrees=OuterDegrees; }
            public float OuterDegrees { get; }
        }
        internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem {
            public void Update(ref Aim aim, ref Tilt tilt, ref Cone cone, ref CSharpGameEngine.Entities.State state) { }
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
                var Values=new System.Collections.Generic.List<float>{A.YawDegrees,A.FovRadians,A.Plain,S.Value.R,S.Value.U,S.Value.F};
                if(World.TryGetComponent(Root,out Tilt T)){ Values.Add(T.PitchDegrees); Values.Add(T.Pitch); }
                if(World.TryGetComponent(Root,out Cone C)){ Values.Add(C.OuterDegrees); }
                return string.Join("|",Values.Select(static Value=>Value.ToString("R",CultureInfo.InvariantCulture)));
            }
        }
        """;
    private static string Document(string Version,string Yaw,string Fov,string R,string U,string YawKey="YawDegrees",string FovKey="FovRadians",
        string Extra="")=>$$"""
        {
          "schemaVersion": {{Version}},
          "namespace": "Game",
          "className": "MainScene",
          "root": {
            "components": [
              { "type": "Sample.Aim", "members": { "{{YawKey}}": {{Yaw}}, "{{FovKey}}": {{Fov}}, "Plain": 2 } },
              { "type": "Sample.Spin", "members": { "Value": { "R": {{R}}, "U": {{U}}, "F": 0 } } }{{Extra}}
            ]
          }
        }
        """;
    private static string VersionThree(string Yaw,string Fov,string R,string U)=>Document("3",Yaw,Fov,R,U,"Yaw","Fov");
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
    public void UnitFreeKeys_BuildTheSameValuesAsTheMemberNames(){
        var MemberNames=Describe(Document("2","0.25","0.25","0.19098594","0.047746485"));
        Assert.Equal(MemberNames,Describe(VersionThree("0.25","0.25","0.19098594","0.047746485")));
        // A document may still name the C# member; the number is turns either way.
        Assert.Equal(MemberNames,Describe(Document("3","0.25","0.25","0.19098594","0.047746485")));
    }
    [Fact]
    public void UnitFreeKeys_ReachConstructorParametersAndKeepATakenName(){
        const string Extra="""
            ,
            { "type": "Sample.Tilt", "members": { "PitchDegrees": 0.125, "Pitch": 7 } },
            { "type": "Sample.Cone", "members": { "Outer": 0.25 } }
            """;
        Assert.Equal("90|1.5707964|2|1.2|0.3|0|45|7|90",
            Describe(Document("3","0.25","0.25","0.19098594","0.047746485","Yaw","Fov",Extra)));
    }
    [Fact]
    public void BothKeysOfOneMember_AreRejected(){
        var Json=VersionThree("0.25","0.25","0","0").Replace("\"Plain\": 2","\"YawDegrees\": 0.25, \"Plain\": 2",StringComparison.Ordinal);
        var Scene=SceneJsonSceneAnalyzer.Analyze(Json,Base());
        var Problem=Assert.Single(Scene.Problems);
        Assert.Contains("'Yaw' and 'YawDegrees'",string.Format(System.Globalization.CultureInfo.InvariantCulture,
            Problem.Descriptor.MessageFormat.ToString(System.Globalization.CultureInfo.InvariantCulture),Problem.Arguments),StringComparison.Ordinal);
    }
    [Fact]
    public void Converter_WritesVersionThreeWithAnglesInTurnsUnderUnitFreeKeys(){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components,"""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new Aim { YawDegrees=90f,FovRadians=1.5707964f,Plain=2f },new Spin(new Euler(1.2f,0.3f,0f)),
                    new Tilt { PitchDegrees=45f,Pitch=7f },new Cone(90f)] };
            }
            """]);
        var Model=CSharpGameEngine.SceneAnalysis.SceneDeclarationAnalyzer.Analyze(Compilation.GetTypeByMetadataName("Game.MainScene")!,Compilation,CancellationToken.None);
        var Converted=SceneJsonConverter.Convert(Model,null);
        Assert.Empty(Converted.Problems);
        var Json=Converted.Text!;
        Assert.Contains("\"schemaVersion\": 3",Json,StringComparison.Ordinal);
        Assert.Contains("\"Yaw\": 0.25,",Json,StringComparison.Ordinal);
        Assert.Contains("\"Fov\": 0.25,",Json,StringComparison.Ordinal);
        Assert.Contains("\"Plain\": 2",Json,StringComparison.Ordinal);
        Assert.Contains("\"Value\": { \"R\": 0.19098594, \"U\": 0.047746485, \"F\": 0 }",Json,StringComparison.Ordinal);
        // "Pitch" is another member of Tilt, so its angle keeps the C# name.
        Assert.Contains("\"PitchDegrees\": 0.125,",Json,StringComparison.Ordinal);
        Assert.Contains("\"Outer\": 0.25",Json,StringComparison.Ordinal);
        Assert.Equal("90|1.5707964|2|1.2|0.3|0|45|7|90",Describe(Json));
    }
    /// <summary>
    /// An edit merges the editor's canonical text into the author's (M0095). Over a version 2 document the result is
    /// the current version with unit-free keys, and it builds the same values.
    /// </summary>
    [Fact]
    public void EditMerge_OverAVersionTwoDocumentWritesTheCurrentForm(){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components,"""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new Aim { YawDegrees=90f,FovRadians=1.5707964f,Plain=2f },new Spin(new Euler(1.2f,0.3f,0f))] };
            }
            """]);
        var Model=CSharpGameEngine.SceneAnalysis.SceneDeclarationAnalyzer.Analyze(Compilation.GetTypeByMetadataName("Game.MainScene")!,Compilation,CancellationToken.None);
        var Old=Document("2","0.25","0.25","0.19098594","0.047746485");
        var Merged=SceneJsonTextMerge.Merge(Old,SceneJsonConverter.Convert(Model,null).Text!);
        Assert.Contains("\"schemaVersion\": 3",Merged,StringComparison.Ordinal);
        Assert.Contains("\"Yaw\": 0.25",Merged,StringComparison.Ordinal);
        Assert.DoesNotContain("YawDegrees",Merged,StringComparison.Ordinal);
        Assert.Equal(Describe(Old),Describe(Merged));
    }
    /// <summary>An upgrade rewrites the angle numbers, the angle keys and the version and leaves every other character of the text.</summary>
    [Fact]
    public void Upgrade_RewritesOnlyTheAngleNumbersKeysAndTheVersion(){
        var Before=Document("1","90","1.5707964","1.2","0.3").Replace("\"Plain\": 2","\"Plain\":2",StringComparison.Ordinal);
        var Result=SceneJsonAngleUpgrade.Upgrade(Before,Base());
        Assert.Empty(Result.Problems);
        Assert.True(Result.Changed);
        Assert.Equal(VersionThree("0.25","0.25","0.19098594","0.047746485").Replace("\"Plain\": 2","\"Plain\":2",StringComparison.Ordinal),Result.Text);
        var Again=SceneJsonAngleUpgrade.Upgrade(Result.Text!,Base());
        Assert.False(Again.Changed);
        Assert.Equal(Result.Text,Again.Text);
    }
    /// <summary>
    /// A float in turns is too coarse to reach every float in degrees or radians (500 degrees, 1.5707965 radians); the
    /// conversion goes through double, so an upgrade keeps every value exactly. The first float-only version failed here.
    /// </summary>
    /// <summary>A version 2 document already holds turns: the upgrade renames the angle keys and changes no number.</summary>
    [Fact]
    public void Upgrade_FromVersionTwoRenamesTheKeysOnly(){
        var Result=SceneJsonAngleUpgrade.Upgrade(Document("2","0.25","0.25","0.19098594","0.047746485"),Base());
        Assert.Empty(Result.Problems);
        Assert.True(Result.Changed);
        Assert.Equal(VersionThree("0.25","0.25","0.19098594","0.047746485"),Result.Text);
    }
    [Fact]
    public void Upgrade_KeepsValuesWithoutAFloatPreimageInTurns(){
        var Before=Document("1","500","1.5707965","-3.1415925","1.5707961");
        var Result=SceneJsonAngleUpgrade.Upgrade(Before,Base());
        Assert.Empty(Result.Problems);
        Assert.Equal(Describe(Before),Describe(Result.Text!));
    }
    [Fact]
    public void Schema_DescribesAngleMembersInTurnsUnderUnitFreeKeysAndAcceptsEveryVersion(){
        var Schema=SceneJsonSchemaBuilder.Build(Base());
        Assert.Contains("\"enum\": [ 1, 2, 3 ]",Schema,StringComparison.Ordinal);
        Assert.Contains("\"Yaw\": {",Schema,StringComparison.Ordinal);
        Assert.DoesNotContain("\"YawDegrees\"",Schema,StringComparison.Ordinal);
        Assert.Contains("\"PitchDegrees\": {",Schema,StringComparison.Ordinal);
        Assert.Contains("Angle in turns (1 = one full rotation; the C# member YawDegrees is in degrees).",Schema,StringComparison.Ordinal);
        Assert.Contains("Angle in turns (1 = one full rotation; the C# member Value is in radians). Constructor parameter of Spin.",Schema,StringComparison.Ordinal);
    }
}
