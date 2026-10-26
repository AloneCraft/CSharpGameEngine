using CSharpGameEngine.SceneAnalysis.Json;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// ADR-0074：スキーマバージョン2以降、シーンJSONでは<c>[Angle]</c>メンバーに含まれる数値をすべて回転数で保存する。
/// C#メンバーはそれぞれの単位を保持するため、バージョン1とバージョン2の文書は同じ値をビルドする。
/// バージョン3以降では回転数を使うため、角度キーからC#の単位を示す語を省く（<c>YawDegrees</c>は<c>Yaw</c>）。
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
        // 文書にC#メンバー名を記述してもよい。どちらの場合も数値は回転数として扱う。
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
    /// <summary>
    /// エディターで編集する際、作成者の文書へエディターの正規テキストを統合する（M0095）。
    /// バージョン2の文書に適用すると、現在のバージョンと単位のないキーになり、同じ値をビルドする。
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
    /// <summary>アップグレード時は角度の数値、角度キー、バージョンを書き換え、それ以外のテキストの文字はすべて維持する。</summary>
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
    /// 回転数を float で表すと粗すぎて、度数やラジアンのすべての float 値へ変換できない（500度、1.5707965ラジアンなど）。変換には double を使うため、更新後も値を正確に維持す
    /// る。最初の float のみを使うバージョンでは、ここで失敗していた。
    /// </summary>
    /// <summary>バージョン2のドキュメントはすでに回転数を保持している。更新では角度キーの名前だけを変え、数値は変更しない。</summary>
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
