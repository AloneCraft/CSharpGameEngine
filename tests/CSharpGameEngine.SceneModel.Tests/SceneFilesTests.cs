using CSharpGameEngine.SceneAnalysis.Json;
using Xunit;

namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// ADR-0070（2026-10-01 改訂）：シーンはプロジェクトの <c>Scenes</c> フォルダー内にある <c>*.json</c> ファイル。ジェネレーター（ビルドの ProjectDir を使用）と Visual Studio 拡張機能（最も近いプロジェクトフォルダーを使用）はこの共通ルールで判定する。ビルド対象も <c>Scenes\**\*.json</c> である。
/// </summary>
public sealed class SceneFilesTests {
    [Theory]
    [InlineData(@"C:\Game\Scenes\Main.json",true)]
    [InlineData(@"C:\Game\Scenes\Levels\Lvl_ThirdPerson.json",true)]
    [InlineData(@"C:\Game\scenes\Main.JSON",true)]
    [InlineData(@"C:\Game\Scenes\Main.scene.json",true)]
    [InlineData(@"C:\Game\appsettings.json",false)]
    [InlineData(@"C:\Game\Assets\Scenes\Main.json",false)]
    [InlineData(@"C:\Game\Main.scene.json",false)]
    [InlineData(@"C:\Game\Scenes\Main.cs",false)]
    [InlineData(@"C:\Game\Scenes",false)]
    [InlineData(@"C:\Other\Scenes\Main.json",false)]
    [InlineData(@"C:\GameScenes\Main.json",false)]
    public void Scene_IsAJsonFileUnderTheProjectsScenesFolder(string Path,bool Expected){
        Assert.Equal(Expected,SceneFiles.IsScene(Path,@"C:\Game"));
        Assert.Equal(Expected,SceneFiles.IsScene(Path,@"C:\Game\"));
        Assert.Equal(Expected,SceneFiles.IsScene(Path.Replace('\\','/'),"C:/Game/"));
    }

    [Theory]
    [InlineData(@"C:\Game\Plugins\Weather\Scenes\Storm.json",true)]
    [InlineData(@"C:\Game\Plugins\Weather\Scenes\Deep\Storm.json",true)]
    [InlineData(@"C:\Game\Plugins\Scenes\Storm.json",false)]
    [InlineData(@"C:\Game\Plugins\Weather\Assets\Scenes\Storm.json",false)]
    [InlineData(@"C:\Game\Plugins\Weather\Deep\Scenes\Storm.json",false)]
    [InlineData(@"C:\Engine\plugins\CSharpGameEngine.Vehicles\Scenes\Car.json",true)]
    [InlineData(@"C:\Engine\plugins\CSharpGameEngine.Vehicles\Runtime\Car.json",false)]
    [InlineData(@"C:\Engine\plugins\CSharpGameEngine.Navigation\Scenes\Map.json",false)]
    public void APluginsScenesFolder_HoldsScenes(string Path,bool Expected){
        // ADR-0079：プロジェクトプラグインは自身のフォルダー（Plugins\<Name>\Scenes）で判定し、エンジンまたはパッケージのプラグインはビルドのルートで判定する。
        var Roots=PluginRoot.Parse(@"|Vehicles=C:\Engine\plugins\CSharpGameEngine.Vehicles\|=ignored|Broken");

        Assert.Equal(Expected,SceneFiles.IsScene(Path,@"C:\Game",Roots));
    }

    [Fact]
    public void PluginRoots_ParseNameFolderPairs_AndSkipMalformedEntries(){
        var Roots=PluginRoot.Parse(@" Vehicles = C:\A\ ||=C:\B\|NoFolder=|Weather=D:\W");

        Assert.Equal(["Vehicles","Weather"],Roots.Select(static Root=>Root.Name));
        Assert.Equal([@"C:\A\",@"D:\W"],Roots.Select(static Root=>Root.Directory));
        Assert.Empty(PluginRoot.Parse(null));
    }

    [Theory]
    [InlineData(null,@"C:\Game")]
    [InlineData(@"C:\Game\Scenes\Main.json",null)]
    [InlineData(@"C:\Game\Scenes\Main.json","")]
    public void MissingPathOrProject_IsNotAScene(string?Path,string?ProjectDirectory){
        Assert.False(SceneFiles.IsScene(Path,ProjectDirectory));
    }
}
