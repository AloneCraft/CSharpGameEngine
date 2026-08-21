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
/// Restores only the visibility of C# Game Engine's auxiliary panes in an Experimental instance.
/// Visual Studio remains the sole owner of window placement, docking groups, sizes and document
/// tabs; recreating those values here would overwrite the user's saved .winprf layout.
/// </summary>
internal sealed class ExperimentalToolWindowState {
    private const string CollectionPath="CSharpGameEngine\\ExperimentalToolWindows";
    private const string InitializedProperty="Initialized";

    private static readonly IReadOnlyList<Entry> Entries=Array.AsReadOnly(new[]{
        new Entry("Hierarchy",typeof(HierarchyToolWindowPane)),
        new Entry("Inspector",typeof(InspectorToolWindowPane)),
        new Entry("ComponentPalette",typeof(ComponentPaletteToolWindowPane)),
    });

    private readonly AsyncPackage Package;

    internal ExperimentalToolWindowState(AsyncPackage Package)=>
        this.Package=Package??throw new ArgumentNullException(nameof(Package));

    /// <summary>
    /// Runs after a solution is available. A missing saved value deliberately means "leave the
    /// VS-restored layout alone"; this makes rollout safe for existing Experimental hives.
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
    /// Captures visibility during normal package disposal. The shell itself persists docking,
    /// size, floating position and document well state, so those values are intentionally not
    /// copied into extension settings.
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
            // Shutdown must not be delayed or failed because the VS shell is already disposing frames.
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