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

    private static Func<WritableSettingsStore?>?StoreProvider;

    internal static void ConfigureStoreProvider(Func<WritableSettingsStore?>?Provider)=>StoreProvider=Provider;

    internal static SceneActiveTool ReadActiveTool(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadEnum("ActiveTool",SceneActiveTool.Select);
    }

    internal static void WriteActiveTool(SceneActiveTool Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteEnum("ActiveTool",Value);
    }

    internal static SceneTransformSpace ReadTransformSpace(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadEnum("TransformSpace",SceneTransformSpace.World);
    }

    internal static void WriteTransformSpace(SceneTransformSpace Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteEnum("TransformSpace",Value);
    }

    internal static SceneProjectionMode ReadProjectionMode(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadEnum("ProjectionMode",SceneProjectionMode.Perspective);
    }

    internal static void WriteProjectionMode(SceneProjectionMode Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteEnum("ProjectionMode",Value);
    }

    internal static SceneNavigationPreset ReadNavigationPreset(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadEnum("NavigationPreset",SceneNavigationPreset.Default);
    }

    internal static void WriteNavigationPreset(SceneNavigationPreset Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteEnum("NavigationPreset",Value);
    }

    internal static float ReadNavigationSpeedMultiplier(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadFloat("NavigationSpeedMultiplier",1.0f);
    }

    internal static void WriteNavigationSpeedMultiplier(float Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteFloat("NavigationSpeedMultiplier",Value);
    }

    internal static bool ReadInvertLookX(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadBoolean("InvertLookX",false);
    }

    internal static void WriteInvertLookX(bool Value){
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteBoolean("InvertLookX",Value);
    }

    internal static bool ReadInvertLookY(){
        ThreadHelper.ThrowIfNotOnUIThread();
        return ReadBoolean("InvertLookY",false);
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
