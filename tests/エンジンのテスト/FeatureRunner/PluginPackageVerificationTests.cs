using Xunit;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

/// <summary>ADR-0079：ゲームの出力と生成コードに対するプラグインパッケージ検査の判定。</summary>
public sealed class PluginPackageVerificationTests {
    private const string Generated="internal Weather_Intensity Weather_Intensity; internal Lightning_Intensity Lightning_Intensity;";
    private const string BothPlugins="""
        CGE-PLUGIN-SAMPLE Weather updates=888 rainfall=2664.0 gusts=7
        CGE-PLUGIN-SAMPLE Lightning bolts=888 energy=17760.0
        """;

    [Fact]
    public void BothPluginsWithConsistentTotals_Pass(){
        Assert.Empty(PluginPackageVerification.Evaluate(BothPlugins,Generated,PluginAssetCopied:true,out var Summary));
        Assert.Contains("intensityNamedApart=True",Summary);
    }

    [Theory]
    [InlineData("CGE-PLUGIN-SAMPLE Weather updates=888 rainfall=2664.0 gusts=7","Lightning printed no totals")]
    [InlineData("CGE-PLUGIN-SAMPLE Lightning bolts=1 energy=20.0","Weather printed no totals")]
    [InlineData("CGE-PLUGIN-SAMPLE Weather updates=10 rainfall=31.0 gusts=7\nCGE-PLUGIN-SAMPLE Lightning bolts=1 energy=20.0","is not updates × 3")]
    [InlineData("CGE-PLUGIN-SAMPLE Weather updates=10 rainfall=30.0 gusts=6\nCGE-PLUGIN-SAMPLE Lightning bolts=1 energy=20.0","Gusts read 6")]
    [InlineData("CGE-PLUGIN-SAMPLE Weather updates=0 rainfall=0.0 gusts=7\nCGE-PLUGIN-SAMPLE Lightning bolts=1 energy=20.0","never ran")]
    [InlineData("CGE-PLUGIN-SAMPLE Weather updates=10 rainfall=30.0 gusts=7\nCGE-PLUGIN-SAMPLE Lightning bolts=2 energy=20.0","is not bolts × 20")]
    public void AMissingOrInconsistentPlugin_Fails(string Output,string Expected){
        var Failures=PluginPackageVerification.Evaluate(Output,Generated,PluginAssetCopied:true,out _);

        Assert.Contains(Failures,Failure=>Failure.Contains(Expected,StringComparison.Ordinal));
    }

    [Fact]
    public void AnEnginePluginTheGameDidNotName_OrIntensitiesNotNamedApart_OrAMissingAsset_Fail(){
        Assert.Contains(PluginPackageVerification.Evaluate(BothPlugins,Generated+" global::CSharpGameEngine.Vehicles.Vehicle",true,out _),
            static Failure=>Failure.Contains("Vehicles engine plugin",StringComparison.Ordinal));
        Assert.Contains(PluginPackageVerification.Evaluate(BothPlugins,"internal Intensity Intensity;",true,out _),
            static Failure=>Failure.Contains("Weather_Intensity",StringComparison.Ordinal));
        Assert.Contains(PluginPackageVerification.Evaluate(BothPlugins,Generated,false,out _),
            static Failure=>Failure.Contains("asset was not copied",StringComparison.Ordinal));
    }
    /// <summary>ADR-0099 で名前を変えた後も、確かめの道具が組み立てるパスが実在する(PackageId からパスを作って外れたことがある)。</summary>
    [Fact]
    public void 確かめの道具が使う見本とプロジェクトが実在する(){
        var 根=FeatureStageRepositoryLocator.Find();
        Assert.True(File.Exists(PluginPackageVerification.LightningProjectPath(根)),PluginPackageVerification.LightningProjectPath(根));
        foreach(var フォルダ in new[]{Path.Combine(根,"samples","Plugins","Weather"),Path.Combine(根,"Templates","ThirdPerson")}){
            Assert.True(Directory.Exists(フォルダ),フォルダ);
        }
        foreach(var 実行ファイル in new[]{DestructionSampleVerification.ExecutablePath(根,"Debug"),DestructionSampleVerification.PhysicsCliPath(根,"Debug")}){
            var プロジェクトの場所=Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(実行ファイル)))))!;
            Assert.True(Directory.EnumerateFiles(プロジェクトの場所,"*.csproj").Any(),プロジェクトの場所);
        }
    }
}
