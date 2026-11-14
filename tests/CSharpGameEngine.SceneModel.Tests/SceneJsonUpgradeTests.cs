using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.SceneAnalysis.Tests;
/// <summary>
/// ADR-0074・ADR-0098 §5 規則 2：バージョン1・2と C# の名前の角度キーを、バージョン3へ書き換える変換（<c>SceneJson.Cli upgrade</c>）。
/// 旧版の読み方は <c>CGE_SCENE_UPGRADE</c> を定義したビルド（変換ツールとこの試験）にだけ入る。書き換えた文書は、現行版だけを読む
/// <see cref="SceneJsonSceneAnalyzer.Analyze"/> で、旧版と同じコンポーネントの値になる。
/// </summary>
public sealed class SceneJsonUpgradeTests {
    private const string Components="""
        namespace CSharpGameEngine.Contracts {
            public enum AngleUnit:byte { Radians, Degrees }
            [System.AttributeUsage(System.AttributeTargets.Field|System.AttributeTargets.Property|System.AttributeTargets.Parameter,Inherited=false)]
            public sealed class AngleAttribute:System.Attribute { public AngleAttribute(AngleUnit Unit){ this.Unit=Unit; } public AngleUnit Unit { get; } }
        }
        namespace Sample {
            using CSharpGameEngine.Contracts;
            public struct Euler { public Euler(float R,float U,float F){ this.R=R; this.U=U; this.F=F; } public float R; public float U; public float F; }
            public struct Spin { public Spin([Angle(AngleUnit.Radians)] Euler Value){ this.Value=Value; } public Euler Value; }
            public struct Aim { [Angle(AngleUnit.Degrees)] public float YawDegrees; [Angle(AngleUnit.Radians)] public float FovRadians; public float Plain; }
            public struct DoubleAim { [Angle(AngleUnit.Degrees)] public double YawDegrees; }
        }
        """;
    private static CSharpCompilation Base()=>SceneAnalysisTestHelper.CreateCompilation(Components);
    private static string Document(string Version,string Yaw,string Fov,string R,string U,string YawKey="YawDegrees",string FovKey="FovRadians")=>$$"""
        {
          "schemaVersion": {{Version}},
          "namespace": "Game",
          "className": "MainScene",
          "root": {
            "components": [
              { "type": "Sample.Aim", "members": { "{{YawKey}}": {{Yaw}}, "{{FovKey}}": {{Fov}}, "Plain": 2 } },
              { "type": "Sample.Spin", "members": { "Value": { "R": {{R}}, "U": {{U}}, "F": 0 } } }
            ]
          }
        }
        """;
    private static string VersionThree(string Yaw,string Fov,string R,string U)=>Document("3",Yaw,Fov,R,U,"Yaw","Fov");
    /// <summary>旧版を読む入口で読んだ元の文書と、現行版だけを読む入口で読んだ書き換え後の文書が、同じコンポーネントの式になる。</summary>
    private static void AssertSameValues(string Before,string After,CSharpCompilation Compilation){
        var Original=SceneJsonSceneAnalyzer.AnalyzeEarlierVersion(Before,Compilation);
        Assert.Empty(Original.Problems);
        var Current=SceneJsonSceneAnalyzer.Analyze(After,Compilation);
        Assert.Empty(Current.Problems);
        Assert.Equal(Original.Root!.Components.Select(static Component=>Component.EmitExpressionText),Current.Root!.Components.Select(static Component=>Component.EmitExpressionText));
    }
    [Theory]
    [InlineData("1","90","1.5707964","1.2","0.3")]
    [InlineData("2","0.25","0.25","0.19098594","0.047746485")]
    public void EarlierVersions_UpgradeToVersionThreeWithTheSameValues(string Version,string Yaw,string Fov,string R,string U){
        var Old=Document(Version,Yaw,Fov,R,U);
        var Result=SceneJsonAngleUpgrade.Upgrade(Old,Base());
        Assert.Empty(Result.Problems);
        Assert.True(Result.Changed);
        Assert.Equal(VersionThree("0.25","0.25","0.19098594","0.047746485"),Result.Text);
        AssertSameValues(Old,Result.Text!,Base());
    }
    [Fact]
    public void MemberNameAngleKeysInVersionThree_AreRenamedToUnitFreeKeys(){
        var Old=Document("3","0.25","0.25","0.19098594","0.047746485");
        var Result=SceneJsonAngleUpgrade.Upgrade(Old,Base());
        Assert.Empty(Result.Problems);
        Assert.True(Result.Changed);
        Assert.Equal(VersionThree("0.25","0.25","0.19098594","0.047746485"),Result.Text);
    }
    /// <summary>アップグレード時は角度の数値、角度キー、バージョンを書き換え、それ以外のテキストの文字はすべて維持する。2回目は何も変えない。</summary>
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
    /// <summary>回転数を float で表すと粗すぎて元の float 値へ戻せない値（500度、1.5707965ラジアンなど）も、double で変換して正確に維持する。</summary>
    [Fact]
    public void Upgrade_KeepsValuesWithoutAFloatPreimageInTurns(){
        var Before=Document("1","500","1.5707965","-3.1415925","1.5707961");
        var Result=SceneJsonAngleUpgrade.Upgrade(Before,Base());
        Assert.Empty(Result.Problems);
        AssertSameValues(Before,Result.Text!,Base());
    }
    [Fact]
    public void UpgradeVersionOnePreservesDoubleAnglePrecision(){
        const string Before="""
            {"schemaVersion":1,"namespace":"Game","className":"MainScene",
             "root":{"components":[{"type":"Sample.DoubleAim","members":{"YawDegrees":90.00000000000001}}]}}
            """;
        var Result=SceneJsonAngleUpgrade.Upgrade(Before,Base());
        Assert.Empty(Result.Problems);
        Assert.True(Result.Changed);
        AssertSameValues(Before,Result.Text!,Base());
        Assert.Contains("90.00000000000001d",SceneJsonSceneAnalyzer.Analyze(Result.Text!,Base()).Root!.Components[0].EmitExpressionText,StringComparison.Ordinal);
    }
    /// <summary>現行版だけを読む入口は、旧版を変換コマンドの案内つきで拒否する（変換ツールのビルドでも同じ）。</summary>
    [Theory]
    [InlineData("1","90","1.5707964","1.2","0.3")]
    [InlineData("2","0.25","0.25","0.19098594","0.047746485")]
    public void TheCurrentReaderRejectsEarlierVersionsEvenInTheUpgradeBuild(string Version,string Yaw,string Fov,string R,string U){
        var Problem=Assert.Single(SceneJsonSceneAnalyzer.Analyze(Document(Version,Yaw,Fov,R,U),Base()).Problems);
        Assert.Equal("SCENE204",Problem.Descriptor.Id);
    }
}
