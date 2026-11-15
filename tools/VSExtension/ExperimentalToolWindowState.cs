using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Experimental インスタンス内で C# Game Engine 補助ペインの表示状態だけを復元します。
/// ウィンドウ配置、ドッキンググループ、サイズ、ドキュメントタブは Visual Studio が管理します。ここで再作成すると、ユーザーが保存した .winprf レイアウトを上書きします。
/// </summary>
internal sealed class ExperimentalToolWindowState {
    private const string CollectionPath="CSharpGameEngine\\ExperimentalToolWindows";
    private const string InitializedProperty="Initialized";

    private static readonly IReadOnlyList<Entry> Entries=Array.AsReadOnly(new[]{
        new Entry("Hierarchy",typeof(HierarchyToolWindowPane)),
        new Entry("Inspector",typeof(InspectorToolWindowPane)),
        new Entry("ComponentPalette",typeof(ComponentPaletteToolWindowPane)),
        new Entry("Assets",typeof(AssetsToolWindowPane)),
    });

    private readonly AsyncPackage Package;

    internal ExperimentalToolWindowState(AsyncPackage Package)=>
        this.Package=Package??throw new ArgumentNullException(nameof(Package));

    /// <summary>
    /// ソリューションが使用可能になった後に実行します。保存値がなければ、既定で「VS が復元したレイアウトをそのまま使う」とします。既存の Experimental hive に安全に展開できます。
    /// </summary>
    internal async Task RestoreAsync(CancellationToken CancellationToken){
        await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        WritableSettingsStore Store=GetStore();
        if(!Store.CollectionExists(CollectionPath)
            ||!Store.PropertyExists(CollectionPath,InitializedProperty)
            ||!Store.GetBoolean(CollectionPath,InitializedProperty)){
            ActivityLog.LogInformation(nameof(ExperimentalToolWindowState),
                "No saved C# Game Engine pane visibility exists; preserving Visual Studio's current Experimental layout.");
            return;
        }

        foreach(Entry Entry in Entries){
            if(!ReadVisible(Store,Entry)){
                continue;
            }

            ToolWindowPane?Pane=await this.Package.FindToolWindowAsync(
                Entry.PaneType,0,true,CancellationToken);
            if(Pane?.Frame is IVsWindowFrame Frame){
                ErrorHandler.ThrowOnFailure(Frame.ShowNoActivate());
            }
        }

        ActivityLog.LogInformation(nameof(ExperimentalToolWindowState),
            "Restored saved C# Game Engine pane visibility without changing the Visual Studio window layout.");
    }

    /// <summary>
    /// package の通常の破棄時に表示状態を取得します。shell はドッキング、サイズ、フローティング位置、document well の状態を保存するため、それらは extension settings へコピーしません。
    /// </summary>
    internal void Capture(){
        try {
            this.Package.JoinableTaskFactory.Run(async()=>{
                await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken.None);
                WritableSettingsStore Store=GetStore();
                if(!Store.CollectionExists(CollectionPath)){
                    Store.CreateCollection(CollectionPath);
                }

                foreach(Entry Entry in Entries){
                    ToolWindowPane?Pane=await this.Package.FindToolWindowAsync(
                        Entry.PaneType,0,false,CancellationToken.None);
                    bool IsVisible=Pane?.Frame is IVsWindowFrame Frame
                        &&ErrorHandler.Succeeded(Frame.IsVisible());
                    Store.SetBoolean(CollectionPath,Entry.VisiblePropertyName,IsVisible);
                }

                Store.SetBoolean(CollectionPath,InitializedProperty,true);
                ActivityLog.LogInformation(nameof(ExperimentalToolWindowState),
                    "Saved C# Game Engine pane visibility; Visual Studio will retain the previous window placement.");
            });
        } catch(Exception Exception) when(Exception is COMException||Exception is OperationCanceledException){
            // VS shell がすでに frame を破棄中の場合でも、シャットダウンを遅らせたり失敗させたりしてはいけません。
        }
    }
    private WritableSettingsStore GetStore()=>
        new ShellSettingsManager(this.Package).GetWritableSettingsStore(SettingsScope.UserSettings);

    private static bool ReadVisible(WritableSettingsStore Store,Entry Entry)=>
        Store.PropertyExists(CollectionPath,Entry.VisiblePropertyName)
        &&Store.GetBoolean(CollectionPath,Entry.VisiblePropertyName);

    private sealed class Entry {
        internal Entry(string Name,Type PaneType){
            this.PaneType=PaneType;
            this.VisiblePropertyName=Name+"Visible";
        }

        internal Type PaneType { get; }

        internal string VisiblePropertyName { get; }
    }
}
