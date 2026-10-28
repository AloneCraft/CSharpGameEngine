using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CSharpGameEngine.Contracts;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Settings;
namespace CSharpGameEngine.VSExtension;
internal sealed class AssetWorkspaceController:IVsSelectionEvents,IDisposable {
    internal const int ShowCommandId=0x0140;
    internal const int FilterCommandId=0x0141;
    internal static readonly Guid CommandSet=new("521655e5-0d1b-4255-8086-1039c5c2e3da");
    private readonly AsyncPackage Package;
    private readonly DTE Dte;
    private readonly IVsMonitorSelection Selection;
    private uint SelectionCookie;
    private bool Started;
    private bool Disposed;
    private int Generation;
    private AssetProjectMutationGuard?MutationGuard;
    private readonly List<FileSystemWatcher>Watchers=[];
    private System.Threading.Timer?RefreshTimer;
    internal AssetWorkspaceItem[]Items{get;private set;}=[];
    /// <summary>読み込み済みのゲーム プロジェクトの Assets 一覧にあるファイルか。</summary>
    internal bool IsListed(string SourcePath)=>this.Items.Any(Item=>string.Equals(Item.Source.SourcePath,SourcePath,StringComparison.OrdinalIgnoreCase));
    internal CgeProjectContext[]Projects{get;private set;}=[];
    internal AssetWorkspaceQuery Query{get;}=new();
    internal event Action?Changed;
    internal AsyncPackage Owner=>this.Package;
    internal string Diagnostic{get;private set;}=string.Empty;
    private AssetWorkspaceController(AsyncPackage Package,DTE Dte,IVsMonitorSelection Selection){ this.Package=Package;this.Dte=Dte;this.Selection=Selection; }
    internal static async Task<AssetWorkspaceController>CreateAsync(AsyncPackage Package){
        await Package.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        var Dte=(DTE)(await Package.GetServiceAsync(typeof(SDTE))??throw new InvalidOperationException("DTE unavailable."));
        var Selection=(IVsMonitorSelection)(await Package.GetServiceAsync(typeof(SVsShellMonitorSelection))??throw new InvalidOperationException("Selection service unavailable."));
        var Result=new AssetWorkspaceController(Package,Dte,Selection);
        ErrorHandler.ThrowOnFailure(Selection.AdviseSelectionEvents(Result,out Result.SelectionCookie));
        AssetsToolWindowPane.Controller=Result;
        Result.MutationGuard=AssetProjectMutationGuard.Start(Result);
        AssetWorkspaceSelection.RevealRequested+=Result.Reveal;
        if(await Package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService Commands){
            Commands.AddCommand(new MenuCommand((_,_)=>Result.Run(Result.ShowAsync),new CommandID(CommandSet,ShowCommandId)));
        }
        return Result;
    }
    internal async Task StartAsync(){
        await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);
        this.Started=true;
        await this.RefreshAsync();
        if(!this.Dte.Solution.IsOpen||this.Projects.Length==0){ return; }
        var Store=new ShellSettingsManager(this.Package).GetWritableSettingsStore(SettingsScope.UserSettings);
        const string Collection="CSharpGameEngine\\Assets";
        if(Store.CollectionExists(Collection)&&Store.PropertyExists(Collection,"InitiallyShown")&&Store.GetBoolean(Collection,"InitiallyShown")){ return; }
        var PreviousFocus=System.Windows.Input.Keyboard.FocusedElement;
        var Pane=await this.Package.FindToolWindowAsync(typeof(AssetsToolWindowPane),0,true,this.Package.DisposalToken);
        if(Pane?.Frame is IVsWindowFrame Frame){
            GameEngineLayout.DockAssets(Frame);
            ErrorHandler.ThrowOnFailure(Frame.ShowNoActivate());
            if(Pane is AssetsToolWindowPane Assets&&Assets.Control.IsKeyboardFocusWithin){ PreviousFocus?.Focus(); }
            if(!Store.CollectionExists(Collection)){ Store.CreateCollection(Collection); }
            Store.SetBoolean(Collection,"InitiallyShown",true);
        }
    }
    internal void SolutionChanged(){
        if(this.Disposed||!this.Started){ return; }
        this.Run(this.StartAsync);
    }
    internal void CancelRefresh(){ this.Generation++;this.Diagnostic=VsixText.Get("Assets_Cancelled");this.Changed?.Invoke(); }
    internal void Report(string Text){ this.Diagnostic=Text;this.Changed?.Invoke(); }
    internal AssetWorkspaceFavorite[]LoadFavorites(){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Store=new ShellSettingsManager(this.Package).GetWritableSettingsStore(SettingsScope.UserSettings);
        const string Collection="CSharpGameEngine\\Assets";
        if(!Store.CollectionExists(Collection)||!Store.PropertyExists(Collection,"Favorites")){ return []; }
        try { return System.Text.Json.JsonSerializer.Deserialize<AssetWorkspaceFavorite[]>(Store.GetString(Collection,"Favorites"))??[]; }
        catch(System.Text.Json.JsonException){ return []; }
    }
    internal void SaveFavorite(){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Favorite=new AssetWorkspaceFavorite{Project=this.Query.ProjectPath,Folder=this.Query.Folder,Search=this.Query.Search,Kind=this.Query.Kind,Provider=this.Query.Provider,State=this.Query.State,WholeProject=this.Query.WholeProject};
        var Favorites=this.LoadFavorites().Where(Value=>Value.Name!=Favorite.Name||Value.Project!=Favorite.Project).Concat(new[]{Favorite}).Take(100).ToArray();
        var Store=new ShellSettingsManager(this.Package).GetWritableSettingsStore(SettingsScope.UserSettings);
        const string Collection="CSharpGameEngine\\Assets";
        if(!Store.CollectionExists(Collection)){ Store.CreateCollection(Collection); }
        Store.SetString(Collection,"Favorites",System.Text.Json.JsonSerializer.Serialize(Favorites));
    }
    internal void Run(Func<Task>Operation)=>_=this.Package.JoinableTaskFactory.RunAsync(async()=>{
        try { await Operation(); }catch(OperationCanceledException){ }catch(Exception Error){
            await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(!this.Disposed){ this.Diagnostic=Error.Message;this.Changed?.Invoke(); }
            ActivityLog.LogError(nameof(AssetWorkspaceController),Error.ToString());
        }
    });
    internal async Task RefreshAsync(){
        await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);
        var Version=++this.Generation;
        var Sources=AssetWorkspaceProjectItems.Collect(this.Dte,out var Projects);
        this.Projects=Projects;
        if(!Projects.Any(Project=>Project.ProjectFilePath==this.Query.ProjectPath)){
            this.Query.ProjectPath=Projects.FirstOrDefault()?.ProjectFilePath??string.Empty;this.Query.Folder=Projects.FirstOrDefault()?.ProjectDirectory??string.Empty;
        }
        if(Projects.Length==0){ this.Items=[];this.ResetWatchers(Sources);AssetWorkspaceSelection.Clear();this.Changed?.Invoke();return; }
        var Context=Projects[0];
        var Request=new AssetWorkspaceRequest{RequestId=Guid.NewGuid(),Sources=Sources};
        var Result=await Task.Run(()=>AssetAuthoringHostClient.InspectWorkspace(Request,Context.WorkspaceRoot),this.Package.DisposalToken);
        await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);
        if(this.Disposed||Version!=this.Generation){ return; }
        this.Items=Result.Items;
        this.Diagnostic=string.Empty;
        this.ResetWatchers(Sources);
        this.Changed?.Invoke();
        var SelectedPaths=new HashSet<string>(AssetWorkspaceSelection.Current.Select(static Item=>Item.Source.SourcePath),StringComparer.OrdinalIgnoreCase);
        AssetWorkspaceSelection.Select(this.Items.Where(Item=>SelectedPaths.Contains(Item.Source.SourcePath)).ToArray());
    }
    private void ResetWatchers(AssetWorkspaceSource[]Sources){
        foreach(var Watcher in this.Watchers){ Watcher.Dispose(); }
        this.Watchers.Clear();
        foreach(var Root in Sources.Select(static Source=>Source.ProjectRoot).Distinct(StringComparer.OrdinalIgnoreCase)){
            if(!Directory.Exists(Root)){ continue; }
            var Watcher=new FileSystemWatcher(Root){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.DirectoryName};
            Watcher.Changed+=this.OnFileChanged;Watcher.Created+=this.OnFileChanged;Watcher.Deleted+=this.OnFileChanged;Watcher.Renamed+=this.OnFileChanged;
            Watcher.Error+=(_,_)=>this.ScheduleRefresh();
            Watcher.EnableRaisingEvents=true;
            this.Watchers.Add(Watcher);
        }
    }
    private void OnFileChanged(object Sender,FileSystemEventArgs Args){
        if(!AssetWorkspaceClassification.IsExcluded(Args.FullPath)
            &&(Args.FullPath.EndsWith(".meta",StringComparison.OrdinalIgnoreCase)||AssetWorkspaceClassification.Classify(Args.FullPath) is not null||Args.ChangeType==WatcherChangeTypes.Deleted)){ this.ScheduleRefresh(); }
    }
    private void ScheduleRefresh(){
        if(this.Disposed){ return; }
        this.RefreshTimer??=new System.Threading.Timer(_=>{ if(!this.Disposed){ this.Run(this.RefreshAsync); } },null,Timeout.Infinite,Timeout.Infinite);
        this.RefreshTimer.Change(250,Timeout.Infinite);
    }
    internal async Task ShowAsync(){
        var Pane=await this.Package.ShowToolWindowAsync(typeof(AssetsToolWindowPane),0,true,this.Package.DisposalToken);
        await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);
        if(Pane is AssetsToolWindowPane Assets){ Assets.Control.Attach(this); }
    }
    private void Reveal(string SourcePath)=>this.Run(async()=>{ await this.RevealAsync(SourcePath); });
    internal async Task<bool>RevealAsync(string SourcePath){
        await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);
        var Item=this.Items.FirstOrDefault(Candidate=>string.Equals(Candidate.Source.SourcePath,SourcePath,StringComparison.OrdinalIgnoreCase));
        if(Item is null){ this.Report(VsixText.Get("AssetEditor_NotInAssets"));return false; }
        await this.ShowAsync();
        await this.Package.JoinableTaskFactory.SwitchToMainThreadAsync(this.Package.DisposalToken);
        if(this.Disposed){ return false; }
        this.Query.Reveal(Item);
        this.Changed?.Invoke();
        AssetWorkspaceSelection.Select([Item]);
        return true;
    }
    internal void Open(AssetWorkspaceItem Item){
        ThreadHelper.ThrowIfNotOnUIThread();
        if((Item.GetCapabilities()&AssetWorkspaceCapabilities.Open)==0){ this.Report(VsixText.Get("Assets_State_"+Item.State));return; }
        VsShellUtilities.OpenDocument(this.Package,Item.Source.SourcePath);
    }
    /// <summary>
    /// Assets で選んだアセットをソリューション エクスプローラーでも選択する。フォーカスを奪わず、プロジェクトにないファイルでは何もしない。
    /// 選択イベントが自分の選択の反響として戻ってきたときに、フォルダーの切り替えや再選択をしないよう短時間だけ無視する。
    /// </summary>
    internal void SyncSolutionSelection(AssetWorkspaceItem Item){
        ThreadHelper.ThrowIfNotOnUIThread();
        var PathValue=Item.Source.SourcePath;
        this.SolutionSyncPath=PathValue;
        this.SolutionSyncUntil=DateTime.UtcNow.AddMilliseconds(500);
        if(!AssetEditorNavigation.SelectInSolution(this.Package,PathValue)){ this.SolutionSyncPath=null; }
    }
    private string?SolutionSyncPath;
    private DateTime SolutionSyncUntil;
    public int OnSelectionChanged(IVsHierarchy OldHierarchy,uint OldItemId,IVsMultiItemSelect OldMulti,ISelectionContainer OldContainer,
        IVsHierarchy NewHierarchy,uint NewItemId,IVsMultiItemSelect NewMulti,ISelectionContainer NewContainer){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Query.Locked||NewHierarchy is null||NewItemId==VSConstants.VSITEMID_NIL){ return VSConstants.S_OK; }
        if(ErrorHandler.Succeeded(NewHierarchy.GetCanonicalName(NewItemId,out var PathValue))&&!string.IsNullOrEmpty(PathValue)){
            if(this.SolutionSyncPath is { }Echo){
                var Expected=DateTime.UtcNow<=this.SolutionSyncUntil&&string.Equals(Echo,PathValue,StringComparison.OrdinalIgnoreCase);
                this.SolutionSyncPath=null;
                if(Expected){ return VSConstants.S_OK; }
            }
            var Plan=AssetSelectionPlanner.Decide(this.Items,this.Projects.Select(static Project=>(Project.ProjectFilePath,Project.ProjectDirectory)).ToArray(),PathValue,Directory.Exists);
            // 場所が変わったときだけ一覧を作り直す（同じファイルの選択通知でサムネイルを消さない）。
            if(Plan.Kind!=AssetSelectionPlanner.Kind.Other&&this.Query.MoveTo(Plan.ProjectPath,Plan.Folder)){ this.Changed?.Invoke(); }
            if(Plan.Item is { }Selected){ AssetWorkspaceSelection.Select([Selected]); }
            else if(AssetWorkspaceSelection.Current.Count>0){ AssetWorkspaceSelection.Clear(); }
        }
        return VSConstants.S_OK;
    }
    public int OnElementValueChanged(uint ElementId,object OldValue,object NewValue)=>VSConstants.S_OK;
    public int OnCmdUIContextChanged(uint Cookie,int Active)=>VSConstants.S_OK;
    public void Dispose(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Disposed=true;this.Generation++;this.RefreshTimer?.Dispose();
        foreach(var Watcher in this.Watchers){ Watcher.Dispose(); }
        this.Watchers.Clear();
        this.Selection.UnadviseSelectionEvents(this.SelectionCookie);
        this.MutationGuard?.Dispose();
        AssetWorkspaceSelection.RevealRequested-=this.Reveal;
        AssetWorkspaceSelection.Clear();
        AssetsToolWindowPane.Controller=null;
    }
}
