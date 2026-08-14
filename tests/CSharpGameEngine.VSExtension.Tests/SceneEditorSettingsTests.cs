using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Sdk.TestFramework;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="GlobalServiceProvider"/>(Microsoft公式、Microsoft.VisualStudio.Sdk.TestFramework)は
/// 内部でWPFの<c>System.Windows.Application</c>を1つ生成し、その専用スレッドを
/// <c>ThreadHelper.JoinableTaskContext</c>のメインスレッドとして登録する。<c>Application</c>は
/// AppDomainにつき1個しか許されないため、このアセンブリ全体で1回だけ生成し、xunitの
/// コレクションフィクスチャ(<see cref="MockedVsCollection"/>)で複数のテストクラスに共有する
/// (<c>IClassFixture</c>だとテストクラスごとに別インスタンスが生成され、2個目の
/// <c>Application</c>生成で例外になる)。
/// </summary>
public sealed class MockedVsFixture:IDisposable {
    public MockedVsFixture()=>ServiceProvider.Reset();

    internal GlobalServiceProvider ServiceProvider { get; } = new();

    public void Dispose()=>ServiceProvider.Dispose();
}

/// <summary><see cref="MockedVsFixture"/>を共有する全テストクラスが<c>[Collection(Name)]</c>で参照する。</summary>
[CollectionDefinition(Name)]
public sealed class MockedVsCollection:ICollectionFixture<MockedVsFixture> {
    internal const string Name="MockedVS";
}

/// <summary>
/// <see cref="SceneEditorSettings"/>の永続化ロジックを<see cref="FakeWritableSettingsStore"/>で
/// 実VS無しに検証する。各テストは自分専用の<see cref="FakeWritableSettingsStore"/>を持つため
/// テスト間で状態が漏れない。
///
/// <para>
/// <c>ThreadHelper.ThrowIfNotOnUIThread()</c>はxunitのテスト実行スレッドを「UIスレッドではない」と
/// 正しく判定するため、<see cref="MockedVsFixture"/>が登録したメインスレッドへ
/// <c>JoinableTaskFactory.Run</c>+<c>SwitchToMainThreadAsync</c>で実際にホップしてから
/// <see cref="SceneEditorSettings"/>を呼ぶ(実VS無しでVS SDK型をテストするMicrosoft公式の手法)。
/// これは本番の呼び出し元(<c>SceneHwndHost</c>)が既にUIスレッド上にいる構造的保証を、
/// テスト側では明示的なスレッドホップで代替していることに相当する。
/// </para>
/// </summary>
[Collection(MockedVsCollection.Name)]
public sealed class SceneEditorSettingsTests {
    private readonly FakeWritableSettingsStore Store=new();

    public SceneEditorSettingsTests(MockedVsFixture Fixture)=>
        SceneEditorSettings.ConfigureStoreProvider(()=>Store);

    [Fact]
    public void ReadActiveTool_NoStoredValue_ReturnsSelectDefault()=>
        RunOnUiThread(()=>Assert.Equal(SceneActiveTool.Select,SceneEditorSettings.ReadActiveTool()));

    [Fact]
    public void WriteThenReadActiveTool_RoundTrips()=>RunOnUiThread(()=>{
        SceneEditorSettings.WriteActiveTool(SceneActiveTool.Rotate);

        Assert.Equal(SceneActiveTool.Rotate,SceneEditorSettings.ReadActiveTool());
    });

    [Fact]
    public void WriteThenReadTransformSpace_RoundTrips()=>RunOnUiThread(()=>{
        SceneEditorSettings.WriteTransformSpace(SceneTransformSpace.Local);

        Assert.Equal(SceneTransformSpace.Local,SceneEditorSettings.ReadTransformSpace());
    });

    [Fact]
    public void WriteThenReadProjectionMode_RoundTrips()=>RunOnUiThread(()=>{
        SceneEditorSettings.WriteProjectionMode(SceneProjectionMode.Orthographic);

        Assert.Equal(SceneProjectionMode.Orthographic,SceneEditorSettings.ReadProjectionMode());
    });

    [Fact]
    public void WriteThenReadNavigationPreset_RoundTrips()=>RunOnUiThread(()=>{
        SceneEditorSettings.WriteNavigationPreset(SceneNavigationPreset.Custom);

        Assert.Equal(SceneNavigationPreset.Custom,SceneEditorSettings.ReadNavigationPreset());
    });

    [Fact]
    public void ReadNavigationSpeedMultiplier_NoStoredValue_ReturnsOne()=>
        RunOnUiThread(()=>Assert.Equal(1.0f,SceneEditorSettings.ReadNavigationSpeedMultiplier()));

    [Fact]
    public void WriteThenReadNavigationSpeedMultiplier_RoundTripsWithFloatPrecision()=>RunOnUiThread(()=>{
        SceneEditorSettings.WriteNavigationSpeedMultiplier(2.5f);

        Assert.Equal(2.5f,SceneEditorSettings.ReadNavigationSpeedMultiplier());
    });

    [Fact]
    public void ReadNavigationSpeedMultiplier_CorruptedString_FallsBackToDefault()=>RunOnUiThread(()=>{
        var Store=new FakeWritableSettingsStore();
        SceneEditorSettings.ConfigureStoreProvider(()=>Store);
        Store.CreateCollection(@"CSharpGameEngine\SceneEditor");
        Store.SetString(@"CSharpGameEngine\SceneEditor","NavigationSpeedMultiplier","not-a-float");

        Assert.Equal(1.0f,SceneEditorSettings.ReadNavigationSpeedMultiplier());
    });

    [Fact]
    public void ReadInvertLookX_NoStoredValue_ReturnsFalse()=>
        RunOnUiThread(()=>Assert.False(SceneEditorSettings.ReadInvertLookX()));

    [Fact]
    public void WriteThenReadInvertLookX_RoundTrips()=>RunOnUiThread(()=>{
        SceneEditorSettings.WriteInvertLookX(true);

        Assert.True(SceneEditorSettings.ReadInvertLookX());
    });

    [Fact]
    public void WriteThenReadInvertLookY_RoundTrips()=>RunOnUiThread(()=>{
        SceneEditorSettings.WriteInvertLookY(true);

        Assert.True(SceneEditorSettings.ReadInvertLookY());
    });

    [Fact]
    public void ConfigureStoreProvider_Null_AllReadsReturnDefaults()=>RunOnUiThread(()=>{
        SceneEditorSettings.ConfigureStoreProvider(null);

        Assert.Equal(SceneActiveTool.Select,SceneEditorSettings.ReadActiveTool());
        Assert.Equal(1.0f,SceneEditorSettings.ReadNavigationSpeedMultiplier());
        Assert.False(SceneEditorSettings.ReadInvertLookX());
    });

    [Fact]
    public void ConfigureStoreProvider_Null_WritesDoNotThrow()=>RunOnUiThread(()=>{
        SceneEditorSettings.ConfigureStoreProvider(null);

        SceneEditorSettings.WriteActiveTool(SceneActiveTool.Move);
        SceneEditorSettings.WriteNavigationSpeedMultiplier(3f);
    });

    private static void RunOnUiThread(Action Action)=>
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Action();
        });
}
