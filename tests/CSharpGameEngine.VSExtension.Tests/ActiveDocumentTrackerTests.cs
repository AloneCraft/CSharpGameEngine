using CSharpGameEngine.VSExtension;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0050の実機不具合回帰テスト: <c>ActiveDocumentTracker</c>のScene候補判定が`.cs`限定のままだと、
/// ソリューションエクスプローラーで`.scene.json`を右クリックしてもコンテキストメニューの入り口
/// (<c>OnSelectionChanged</c>/<c>SetDocumentPath</c>)で握りつぶされ、Roslyn解析に依存しない
/// <c>OpenSceneJsonCommand</c>すら一切表示されなかった。
/// </summary>
public sealed class ActiveDocumentTrackerTests {
    [Theory]
    [InlineData(@"G:\Project\Scenes\MainScene.cs",true)]
    [InlineData(@"G:\Project\Scenes\MainScene.scene.json",true)]
    [InlineData(@"G:\Project\appsettings.json",false)]
    [InlineData(@"G:\Project\Readme.md",false)]
    public void IsSceneCandidatePath_AcceptsCSharpAndSceneJsonOnly(string Path,bool Expected){
        Assert.Equal(Expected,ActiveDocumentTracker.IsSceneCandidatePath(Path));
    }
}
