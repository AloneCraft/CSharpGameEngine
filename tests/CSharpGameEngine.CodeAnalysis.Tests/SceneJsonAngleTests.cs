using CSharpGameEngine.SceneAnalysis.Json;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// ADR-0074：シーンJSON（バージョン3）では<c>[Angle]</c>メンバーに含まれる数値をすべて回転数で保存し、角度キーからC#の単位を示す語を省く
///（<c>YawDegrees</c>は<c>Yaw</c>）。C#メンバーはそれぞれの単位を保持する。バージョン1・2とC#の名前のキーは、ビルドとエディターでは読まず
///（ADR-0098 §5 規則 2）、<c>SceneJson.Cli upgrade</c> だけがバージョン3へ書き換える。
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
    private static string Format(SceneJsonProblem Problem)=>string.Format(System.Globalization.CultureInfo.InvariantCulture,
        Problem.Descriptor.MessageFormat.ToString(System.Globalization.CultureInfo.InvariantCulture),Problem.Arguments);
    [Fact]
    public void VersionThree_BuildsTheMemberValuesFromTurns(){
        Assert.Equal("90|1.5707964|2|1.2|0.3|0",Describe(VersionThree("0.25","0.25","0.19098594","0.047746485")));
    }
    [Theory]
    [InlineData("1","90","1.5707964","1.2","0.3")]
    [InlineData("2","0.25","0.25","0.19098594","0.047746485")]
    public void EarlierVersions_AreRejectedWithTheUpgradeCommand(string Version,string Yaw,string Fov,string R,string U){
        // 変換（SceneJson.Cli upgrade）とその試験は SceneModel.Tests の SceneJsonUpgradeTests にある。生成器のビルドには入らない。
        var Problem=Assert.Single(SceneJsonSceneAnalyzer.Analyze(Document(Version,Yaw,Fov,R,U),Base()).Problems);
        Assert.Contains("is no longer read",Format(Problem),StringComparison.Ordinal);
        Assert.Contains(SceneJsonSceneAnalyzer.UpgradeCommand,Format(Problem),StringComparison.Ordinal);
    }
    /// <summary>ADR-0098 §5 規則 2：生成器のビルドには、旧版を読む入口も変換も入らない（<c>CGE_SCENE_UPGRADE</c> は SceneJson.Cli だけが定義する）。</summary>
    [Fact]
    public void TheGeneratorAssemblyContainsNoEarlierVersionReader(){
        var Assembly=typeof(SceneJsonSceneAnalyzer).Assembly;
        Assert.Equal("CodeAnalysis",Assembly.GetName().Name);
        Assert.Null(Assembly.GetType("CSharpGameEngine.SceneAnalysis.Json.SceneJsonAngleUpgrade"));
        Assert.Null(typeof(SceneJsonSceneAnalyzer).GetMethod("AnalyzeEarlierVersion",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public));
        Assert.NotNull(typeof(SceneJsonSceneAnalyzer).GetMethod("Analyze",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic,[typeof(string),typeof(Microsoft.CodeAnalysis.Compilation)]));
    }
    [Fact]
    public void MemberNameAngleKeys_AreRejectedInVersionThree(){
        var Old=Document("3","0.25","0.25","0.19098594","0.047746485");
        var Problems=SceneJsonSceneAnalyzer.Analyze(Old,Base()).Problems.Select(Format).ToArray();
        Assert.Equal(2,Problems.Length);
        Assert.Contains(Problems,static Message=>Message.Contains("'YawDegrees' is no longer read; write it as 'Yaw'",StringComparison.Ordinal));
        Assert.Contains(Problems,static Message=>Message.Contains("'FovRadians' is no longer read; write it as 'Fov'",StringComparison.Ordinal));
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
        Assert.Contains(Scene.Problems,static Problem=>Format(Problem).Contains("'Yaw' and 'YawDegrees'",StringComparison.Ordinal));
    }
    /// <summary>
    /// 回転数ならfloatに収まっても、メンバーの単位へ変換すると収まらない場合がある（1e37回転を度数で表す場合など）。
    /// その無限大をリテラルとして書式化すると例外となり、アナライザーは失敗（AD0001）、ジェネレーターはプロジェクト全体の出力を破棄する（CS8785）。
    /// 値の位置でSCENE203を報告しつつ、ジェネレーターは引き続きシーンを出力する。
    /// </summary>
    [Theory]
    [InlineData("1e37","0.25","0","degrees")]
    [InlineData("0.25","1e38","0","radians")]
    [InlineData("0.25","0.25","1e38","radians")]
    public void AngleBeyondFloatInItsUnit_IsAnErrorAtTheValue(string Yaw,string Fov,string R,string Unit){
        var Json=VersionThree(Yaw,Fov,R,"0");
        var Problem=Assert.Single(SceneJsonSceneAnalyzer.Analyze(Json,Base()).Problems);
        Assert.Contains("beyond the range of float in "+Unit,string.Format(System.Globalization.CultureInfo.InvariantCulture,
            Problem.Descriptor.MessageFormat.ToString(System.Globalization.CultureInfo.InvariantCulture),Problem.Arguments),StringComparison.Ordinal);
        var Result=GameSystemGeneratorTestHelper.RunGenerator(Base(),[new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",Json)]);
        Assert.DoesNotContain(Result.Diagnostics,static Diagnostic=>Diagnostic.Id=="CS8785");
    }
    [Fact]
    public void DoubleAnglesRemainInTurnsWhenAnUnrelatedValueIsEdited(){
        const string Types="""
            using CSharpGameEngine.Contracts;
            namespace Sample;
            public struct DoubleAim { [Angle(AngleUnit.Degrees)] public double YawDegrees; public double Position; }
            """;
        const string Source="""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new DoubleAim { YawDegrees=90d, Position=13.250000000000002d }] };
            }
            """;
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Types,Source]);
        var Model=CSharpGameEngine.SceneAnalysis.SceneDeclarationAnalyzer.Analyze(Compilation.GetTypeByMetadataName("Game.MainScene")!,Compilation,CancellationToken.None);
        var Converted=SceneJsonConverter.Convert(Model,null);
        Assert.Empty(Converted.Problems);
        Assert.Contains("\"Yaw\": 0.25",Converted.Text!,StringComparison.Ordinal);
        Assert.Contains("\"Position\": 13.250000000000002",Converted.Text!,StringComparison.Ordinal);
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
        // 「Pitch」もTiltのメンバーなので、その角度キーにはC#の名前を維持する。
        Assert.Contains("\"PitchDegrees\": 0.125,",Json,StringComparison.Ordinal);
        Assert.Contains("\"Outer\": 0.25",Json,StringComparison.Ordinal);
        Assert.Equal("90|1.5707964|2|1.2|0.3|0|45|7|90",Describe(Json));
    }
    [Fact]
    public void Schema_DescribesAngleMembersInTurnsUnderUnitFreeKeysAndAcceptsOnlyTheCurrentVersion(){
        var Schema=SceneJsonSchemaBuilder.Build(Base());
        Assert.Contains("\"enum\": [ 3 ]",Schema,StringComparison.Ordinal);
        Assert.Contains("SceneJson.Cli upgrade",Schema,StringComparison.Ordinal);
        Assert.Contains("\"Yaw\": {",Schema,StringComparison.Ordinal);
        Assert.DoesNotContain("\"YawDegrees\"",Schema,StringComparison.Ordinal);
        Assert.Contains("\"PitchDegrees\": {",Schema,StringComparison.Ordinal);
        Assert.Contains("Angle in turns (1 = one full rotation; the C# member YawDegrees is in degrees).",Schema,StringComparison.Ordinal);
        Assert.Contains("Angle in turns (1 = one full rotation; the C# member Value is in radians). Constructor parameter of Spin.",Schema,StringComparison.Ordinal);
    }
}
