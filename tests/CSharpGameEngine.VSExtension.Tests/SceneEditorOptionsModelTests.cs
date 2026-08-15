using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="SceneEditorOptionsModel"/>の検証・クランプ・書式。Tools&gt;Options のページ本体
/// (<see cref="SceneEditorOptionsPage"/>)とXAMLは実VSホストが必要でユニットテストできないため、
/// テスト可能なロジックをこの純粋クラスへ寄せてある。ここが唯一の自動証跡になる。
///
/// <para>
/// <see cref="SceneEditorSettings"/>を読み書きするテストだけはUIスレッドと設定ストアが必要なので
/// <see cref="MockedVsFixture"/>を共有する(<see cref="SceneEditorSettingsTests"/>と同じ作法)。
/// </para>
/// </summary>
[Collection(MockedVsCollection.Name)]
public sealed class SceneEditorOptionsModelTests {
    private readonly FakeWritableSettingsStore Store=new();

    public SceneEditorOptionsModelTests(MockedVsFixture Fixture)=>
        SceneEditorSettings.ConfigureStoreProvider(()=>Store);

    [Fact]
    public void NewModel_StartsAtTheSettingsDefaults(){
        SceneEditorOptionsModel Model=new();

        Assert.Equal(SceneEditorSettings.DefaultNavigationSpeedMultiplier,Model.NavigationSpeedMultiplier);
        Assert.Equal(SceneEditorSettings.DefaultInvertLookX,Model.InvertLookX);
        Assert.Equal(SceneEditorSettings.DefaultInvertLookY,Model.InvertLookY);
    }

    [Fact]
    public void ClampNavigationSpeedMultiplier_BelowRange_ReturnsTheRuntimeMinimum()=>
        Assert.Equal(
            SceneViewState.MinNavigationSpeedMultiplier,
            SceneEditorOptionsModel.ClampNavigationSpeedMultiplier(0f));

    [Fact]
    public void ClampNavigationSpeedMultiplier_AboveRange_ReturnsTheRuntimeMaximum()=>
        Assert.Equal(
            SceneViewState.MaxNavigationSpeedMultiplier,
            SceneEditorOptionsModel.ClampNavigationSpeedMultiplier(1000f));

    [Fact]
    public void ClampNavigationSpeedMultiplier_InsideRange_PassesThroughUnchanged()=>
        Assert.Equal(2.5f,SceneEditorOptionsModel.ClampNavigationSpeedMultiplier(2.5f));

    /// <summary>
    /// クランプ範囲はRMB+Wheelでの実行時調整(<c>SceneViewState.ApplyNavigationSpeedAdjust</c>)と
    /// 同じ定数を使う。オプション画面だけ別の範囲を許すと、設定した値がホイール操作の直後に
    /// 勝手に変わることになる。
    /// </summary>
    [Fact]
    public void ClampNavigationSpeedMultiplier_UsesTheSameRangeAsTheRuntimeWheelAdjustment(){
        Assert.Equal(0.05f,SceneViewState.MinNavigationSpeedMultiplier);
        Assert.Equal(20f,SceneViewState.MaxNavigationSpeedMultiplier);
    }

    [Fact]
    public void ClampNavigationSpeedMultiplier_NotANumber_FallsBackToTheDefault()=>
        Assert.Equal(
            SceneEditorSettings.DefaultNavigationSpeedMultiplier,
            SceneEditorOptionsModel.ClampNavigationSpeedMultiplier(float.NaN));

    [Fact]
    public void ClampNavigationSpeedMultiplier_Infinity_FallsBackToTheDefault()=>
        Assert.Equal(
            SceneEditorSettings.DefaultNavigationSpeedMultiplier,
            SceneEditorOptionsModel.ClampNavigationSpeedMultiplier(float.PositiveInfinity));

    [Fact]
    public void ParseNavigationSpeedMultiplier_ValidText_ReturnsTheValue()=>
        Assert.Equal(3.5f,SceneEditorOptionsModel.ParseNavigationSpeedMultiplier("3.5"));

    [Fact]
    public void ParseNavigationSpeedMultiplier_OutOfRangeText_IsClamped()=>
        Assert.Equal(
            SceneViewState.MaxNavigationSpeedMultiplier,
            SceneEditorOptionsModel.ParseNavigationSpeedMultiplier("999"));

    /// <summary>入力欄を空にしたままOKを押しても壊れない(既定値へ倒す)。</summary>
    [Fact]
    public void ParseNavigationSpeedMultiplier_EmptyOrGarbage_FallsBackToTheDefault(){
        Assert.Equal(
            SceneEditorSettings.DefaultNavigationSpeedMultiplier,
            SceneEditorOptionsModel.ParseNavigationSpeedMultiplier(""));
        Assert.Equal(
            SceneEditorSettings.DefaultNavigationSpeedMultiplier,
            SceneEditorOptionsModel.ParseNavigationSpeedMultiplier("   "));
        Assert.Equal(
            SceneEditorSettings.DefaultNavigationSpeedMultiplier,
            SceneEditorOptionsModel.ParseNavigationSpeedMultiplier("not-a-float"));
        Assert.Equal(
            SceneEditorSettings.DefaultNavigationSpeedMultiplier,
            SceneEditorOptionsModel.ParseNavigationSpeedMultiplier(null));
    }

    /// <summary>
    /// 表示と永続化はどちらも<c>InvariantCulture</c>。ここが食い違うと、小数点にカンマを使う
    /// ロケールで書式化した文字列を読み戻せなくなる。
    /// </summary>
    [Fact]
    public void FormatThenParseNavigationSpeedMultiplier_RoundTrips(){
        var Text=SceneEditorOptionsModel.FormatNavigationSpeedMultiplier(2.25f);

        Assert.Equal(2.25f,SceneEditorOptionsModel.ParseNavigationSpeedMultiplier(Text));
    }

    [Fact]
    public void ResetToDefaults_RestoresEveryExposedValue(){
        SceneEditorOptionsModel Model=new(){
            NavigationSpeedMultiplier=7f,
            InvertLookX=!SceneEditorSettings.DefaultInvertLookX,
            InvertLookY=!SceneEditorSettings.DefaultInvertLookY,
        };

        Model.ResetToDefaults();

        Assert.Equal(SceneEditorSettings.DefaultNavigationSpeedMultiplier,Model.NavigationSpeedMultiplier);
        Assert.Equal(SceneEditorSettings.DefaultInvertLookX,Model.InvertLookX);
        Assert.Equal(SceneEditorSettings.DefaultInvertLookY,Model.InvertLookY);
    }

    [Fact]
    public void SaveToSettings_ThenLoadFromSettings_RoundTripsAllThreeValues()=>RunOnUiThread(()=>{
        SceneEditorOptionsModel Saved=new(){
            NavigationSpeedMultiplier=4.5f,
            InvertLookX=!SceneEditorSettings.DefaultInvertLookX,
            InvertLookY=!SceneEditorSettings.DefaultInvertLookY,
        };
        Saved.SaveToSettings();

        SceneEditorOptionsModel Loaded=new();
        Loaded.LoadFromSettings();

        Assert.Equal(4.5f,Loaded.NavigationSpeedMultiplier);
        Assert.Equal(!SceneEditorSettings.DefaultInvertLookX,Loaded.InvertLookX);
        Assert.Equal(!SceneEditorSettings.DefaultInvertLookY,Loaded.InvertLookY);
    });

    /// <summary>範囲外の値はストアへ書く前にクランプされる(壊れた値を永続化しない)。</summary>
    [Fact]
    public void SaveToSettings_ClampsBeforePersisting()=>RunOnUiThread(()=>{
        SceneEditorOptionsModel Model=new(){ NavigationSpeedMultiplier=1000f };

        Model.SaveToSettings();

        Assert.Equal(SceneViewState.MaxNavigationSpeedMultiplier,Model.NavigationSpeedMultiplier);
        Assert.Equal(SceneViewState.MaxNavigationSpeedMultiplier,SceneEditorSettings.ReadNavigationSpeedMultiplier());
    });

    /// <summary>
    /// オプション画面での保存が、**Scene Viewが実際に読む経路**
    /// (<see cref="SceneViewState.CreateFromSettings"/>)まで到達することを端から端まで固定する。
    /// 実機で「チェックしても反転しない」と報告されたため、UI層より下の経路が生きていることを
    /// 機械的に切り分けられるようにした。
    /// </summary>
    [Fact]
    public void SaveToSettings_ReachesTheStateTheSceneViewActuallyReads()=>RunOnUiThread(()=>{
        SceneEditorOptionsModel Model=new(){
            NavigationSpeedMultiplier=6f,
            InvertLookX=true,
            InvertLookY=true,
        };

        Model.SaveToSettings();
        SceneViewState State=SceneViewState.CreateFromSettings();

        Assert.True(State.InvertLookX);
        Assert.True(State.InvertLookY);
        Assert.Equal(6f,State.NavigationSpeedMultiplier);
    });

    /// <summary>
    /// 反転フラグがLook計算へ実際に効くこと(状態→カメラ数学の接続)。
    /// <c>SceneViewState.TickFly</c>はRMBドラッグのLookを担う経路。
    /// </summary>
    [Fact]
    public void InvertLookX_LoadedFromSettings_ReversesTheYawDirection()=>RunOnUiThread(()=>{
        SceneEditorOptionsModel Normal=new(){ InvertLookX=false };
        Normal.SaveToSettings();
        SceneViewState NormalState=SceneViewState.CreateFromSettings();
        NormalState.TickFly([],DeltaX:10f,DeltaY:0f,DeltaTime:0f,Shift:false,Ctrl:false);

        SceneEditorOptionsModel Inverted=new(){ InvertLookX=true };
        Inverted.SaveToSettings();
        SceneViewState InvertedState=SceneViewState.CreateFromSettings();
        InvertedState.TickFly([],DeltaX:10f,DeltaY:0f,DeltaTime:0f,Shift:false,Ctrl:false);

        Assert.NotEqual(0f,NormalState.Yaw);
        Assert.Equal(-NormalState.Yaw,InvertedState.Yaw,4);
    });

    private static void RunOnUiThread(System.Action Action)=>
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Action();
        });
}
