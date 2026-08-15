using System;
using System.Globalization;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene View のナビゲーション設定7項目(仕様§3の5項目+InvertLookX/Y、
/// Docs/Specifications/Scene-Editor-Design.md §10)をVSのユーザー設定ストアへ永続化する。
///
/// <para>
/// 唯一の消費者は<c>SceneHwndHost</c>/<c>SceneViewState</c>で、呼び出しは常にHwndHostの
/// ライフサイクルコールバックかWPFイベント内、つまり構造的にUIスレッド保証があるため
/// 同期APIにしている(<see cref="GameEngineLayout"/>のasyncパターンは機械的にコピーしない)。
/// </para>
///
/// <para>
/// <see cref="SceneDesignerToolWindowPane.ConfigureSessionProvider"/>と同じFunc&lt;&gt;提供
/// パターン: <c>AsyncPackage</c>への参照を持たないWPFコントロール/HwndHostからでも、
/// パッケージ起動時に登録したデリゲート経由でストアへ到達できる。
/// </para>
/// </summary>
internal static class SceneEditorSettings {
    private const string CollectionPath=@"CSharpGameEngine\SceneEditor";

    // 実機確認(2026-08): 一時的にX/Yとも既定を反転側(true)へ変更したが、実機で試した結果
    // 反転しない側(false)を既定として維持することにした。
    // これらは Tools>Options ページ(SceneEditorOptionsPage)の初期値/「既定へ戻す」の基準でもある。
    internal const bool DefaultInvertLookX=false;
    internal const bool DefaultInvertLookY=false;

    // 残り5項目の既定値も、オプション画面が基準として参照できるよう名前付き定数へ引き上げる
    // (以前は各Readのインラインリテラルだった)。
    internal const SceneActiveTool DefaultActiveTool=SceneActiveTool.Select;
    internal const SceneTransformSpace DefaultTransformSpace=SceneTransformSpace.World;
    internal const SceneProjectionMode DefaultProjectionMode=SceneProjectionMode.Perspective;
    internal const SceneNavigationPreset DefaultNavigationPreset=SceneNavigationPreset.Default;
    internal const float DefaultNavigationSpeedMultiplier=1.0f;

    private static Func<WritableSettingsStore?>?StoreProvider;

    /// <summary>
    /// 設定がこのクラス経由で書き換えられたことの通知。Tools&gt;Options ページで値を変えたとき、
    /// **既に開いている** Scene View へ反映するために使う。
    ///
    /// <para>
    /// これが無いと壊れる理由(設計上の必須要件): 7項目は<c>SceneViewState.CreateFromSettings</c>が
    /// <c>SceneHwndHost.BuildWindowCore</c>で1回だけ読む。さらに
    /// <c>SceneHwndHost.DestroyWindowCore</c>が<c>SaveToSettings</c>で7項目すべてを書き戻すため、
    /// Scene Viewを開いたままオプション画面で変更すると、ビュー破棄時にメモリ上の古い
    /// <c>ViewState</c>の値で**上書き消滅する**。購読側が生きた<c>ViewState</c>を更新することで、
    /// 書き戻される値が新しい値になり、この上書きも同時に解消される。
    /// </para>
    ///
    /// <para>
    /// <c>SceneEditorHub</c>のstaticイベント方式に倣う(購読・解除は購読側のライフサイクルで行う)。
    /// </para>
    /// </summary>
    internal static event Action?SettingsChanged;

    internal static void ConfigureStoreProvider(Func<WritableSettingsStore?>?Provider)=>StoreProvider=Provider;

    /// <summary>
    /// 一時診断用: 設定ストアが実際に取得できるか。Providerはパッケージ初期化時に登録されるため、
    /// これがfalseなら書き込みは黙って無視されている(<see cref="WriteBoolean"/>参照)。
    /// 原因確認後に<see cref="SceneEditorOptionsPage"/>の診断ログごと削除する。
    /// </summary>
    internal static bool IsStoreAvailable=>StoreProvider?.Invoke() is not null;

    /// <summary>一時診断用: 生きたScene Viewが購読しているか(0なら即時反映先が無い)。</summary>
    internal static int SettingsChangedSubscriberCount=>SettingsChanged?.GetInvocationList().Length??0;

    /// <summary>
    /// オプション画面が公開する3項目をまとめて書き、最後に<see cref="SettingsChanged"/>を
    /// **1回だけ**発火する(項目ごとに発火して生きたビューを3回叩かないため)。
    /// </summary>
    internal static void WriteNavigationSettings(float NavigationSpeedMultiplier,bool InvertLookX,bool InvertLookY){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteFloat("NavigationSpeedMultiplier",NavigationSpeedMultiplier);
        WriteBoolean("InvertLookX",InvertLookX);
        WriteBoolean("InvertLookY",InvertLookY);
        SettingsChanged?.Invoke();
    }

    internal static SceneActiveTool ReadActiveTool(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadEnum("ActiveTool",DefaultActiveTool);
    }

    internal static void WriteActiveTool(SceneActiveTool Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteEnum("ActiveTool",Value);
    }

    internal static SceneTransformSpace ReadTransformSpace(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadEnum("TransformSpace",DefaultTransformSpace);
    }

    internal static void WriteTransformSpace(SceneTransformSpace Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteEnum("TransformSpace",Value);
    }

    internal static SceneProjectionMode ReadProjectionMode(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadEnum("ProjectionMode",DefaultProjectionMode);
    }

    internal static void WriteProjectionMode(SceneProjectionMode Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteEnum("ProjectionMode",Value);
    }

    internal static SceneNavigationPreset ReadNavigationPreset(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadEnum("NavigationPreset",DefaultNavigationPreset);
    }

    internal static void WriteNavigationPreset(SceneNavigationPreset Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteEnum("NavigationPreset",Value);
    }

    internal static float ReadNavigationSpeedMultiplier(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadFloat("NavigationSpeedMultiplier",DefaultNavigationSpeedMultiplier);
    }

    internal static void WriteNavigationSpeedMultiplier(float Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteFloat("NavigationSpeedMultiplier",Value);
    }

    internal static bool ReadInvertLookX(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadBoolean("InvertLookX",DefaultInvertLookX);
    }

    internal static void WriteInvertLookX(bool Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteBoolean("InvertLookX",Value);
    }

    internal static bool ReadInvertLookY(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadBoolean("InvertLookY",DefaultInvertLookY);
    }

    internal static void WriteInvertLookY(bool Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteBoolean("InvertLookY",Value);
    }

    private static T ReadEnum<T>(string Name,T Default)where T:struct,Enum{
        if(TryGetStore()is not{ }Store||!Store.PropertyExists(CollectionPath,Name)){
            return Default;
        }

        string Raw=Store.GetString(CollectionPath,Name,Default.ToString());
        return Enum.TryParse(Raw,out T Value)?Value:Default;
    }

    private static void WriteEnum<T>(string Name,T Value)where T:struct,Enum{
        if(TryGetStore()is{ }Store){
            Store.SetString(CollectionPath,Name,Value.ToString());
        }
    }

    private static float ReadFloat(string Name,float Default){
        if(TryGetStore()is not{ }Store||!Store.PropertyExists(CollectionPath,Name)){
            return Default;
        }

        string Raw=Store.GetString(CollectionPath,Name,Default.ToString("G9",CultureInfo.InvariantCulture));
        return float.TryParse(Raw,NumberStyles.Float,CultureInfo.InvariantCulture,out float Value)?Value:Default;
    }

    private static void WriteFloat(string Name,float Value){
        if(TryGetStore()is{ }Store){
            Store.SetString(CollectionPath,Name,Value.ToString("G9",CultureInfo.InvariantCulture));
        }
    }

    private static bool ReadBoolean(string Name,bool Default)=>
        TryGetStore()is{ }Store&&Store.PropertyExists(CollectionPath,Name)
            ?Store.GetBoolean(CollectionPath,Name,Default)
            :Default;

    private static void WriteBoolean(string Name,bool Value){
        if(TryGetStore()is{ }Store){
            Store.SetBoolean(CollectionPath,Name,Value);
        }
    }

    private static WritableSettingsStore?TryGetStore(){
        WritableSettingsStore?Store=StoreProvider?.Invoke();
        if(Store is null){
            return null;
        }

        if(!Store.CollectionExists(CollectionPath)){
            Store.CreateCollection(CollectionPath);
        }

        return Store;
    }
}
