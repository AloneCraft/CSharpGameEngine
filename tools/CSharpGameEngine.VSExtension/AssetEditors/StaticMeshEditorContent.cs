using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Static Mesh Editor（.glb／.gltf／.fbx）。左にオービット可能なビューポート、右に統計と取り込み設定（Details）。
/// 表示モードは Render Preview Host の <see cref="RenderPreviewPresentationMode"/>（Lit／Base Color／Normal／Wireframe）。
/// ツールバーの「プレビューレベル」で照明・背景・環境・床のテンプレート（<see cref="PreviewLevelTemplate"/>）を切り替える（M0127）。
/// 選択はプロジェクトごとに記憶し、次回開いたときに復元する。存在しない・壊れたテンプレートは標準の Default へ戻し、理由を表示する。
/// テンプレートのファイル（<c>Assets\PreviewLevels\*.cgepreview</c>）を編集すると、開いているプレビューへ即座に反映する。
/// </summary>
internal sealed class StaticMeshEditorContent:AssetEditorContent {
    private const string KindName="StaticMesh";
    private readonly AssetViewportHost Viewport;
    private readonly AssetDetailsView DetailsView;
    private readonly Grid CenterRoot=new();
    private readonly Grid DetailsRoot=new();
    private readonly StackPanel Statistics=new(){ Margin=new Thickness(8,6,8,6) };
    private readonly TextBlock EnvironmentMessage=new(){ TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8,6,8,0),Foreground=Brushes.OrangeRed,Visibility=Visibility.Collapsed };
    private readonly SkeletalEditorPanel?Skeletal;
    private readonly PreviewLevelStore Store;
    private readonly PreviewLevelPreferences Preferences;
    private readonly ComboBox LevelBox;
    private readonly UIElement[]Toolbar;
    private readonly DispatcherTimer ReloadTimer=new(DispatcherPriority.Background){ Interval=TimeSpan.FromMilliseconds(300) };
    private IReadOnlyList<PreviewLevelEntry>Entries;
    private FileSystemWatcher?Watcher;
    private string CurrentId;
    private string AppliedJson;
    private bool UpdatingLevelBox;
    private bool Disposed;
    private string OperationText=string.Empty;
    internal StaticMeshEditorContent(string FilePath):base(FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        var ProjectDirectory=CgeProjectContext.Resolve(FilePath)?.ProjectDirectory??Path.GetDirectoryName(FilePath)??string.Empty;
        this.Store=new PreviewLevelStore(ProjectDirectory);
        this.Preferences=new PreviewLevelPreferences(new VsPreviewLevelSettings(),ProjectDirectory);
        this.Entries=this.Store.Load();
        var Resolution=PreviewLevelResolver.Resolve(this.Entries,this.Preferences.LastUsedId,this.Preferences.KindDefaultId(KindName),this.Preferences.DefaultId);
        this.CurrentId=Resolution.Template.Id;
        this.AppliedJson=Resolution.Template.ToJson();
        this.Viewport=new AssetViewportHost(FilePath,Resolution.Template);
        this.Viewport.OperationChanged+=this.OnOperationChanged;
        this.Viewport.MeshLoaded+=this.OnMeshLoaded;
        this.Viewport.TemplateRejected+=this.OnTemplateRejected;
        this.Viewport.ViewportPressed+=this.RequestFocus;
        var SkeletalInfo=IsGltf(FilePath)?GltfSkeletalProbe.Probe(FilePath):null;
        if(SkeletalInfo is{ IsSkeletalMesh:true }||SkeletalInfo is{ IsAnimationOnly:true }){
            this.Skeletal=new SkeletalEditorPanel(this.Viewport,FilePath,SkeletalInfo);
        }
        this.CenterRoot.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        Grid.SetRow(this.Viewport,0);
        this.CenterRoot.Children.Add(this.Viewport);
        if(this.Skeletal is not null){
            this.CenterRoot.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            Grid.SetRow(this.Skeletal.Timeline,1);
            this.CenterRoot.Children.Add(this.Skeletal.Timeline);
        }
        this.DetailsView=new AssetDetailsView(FilePath);
        this.DetailsRoot.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        this.DetailsRoot.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        this.DetailsRoot.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        this.DetailsRoot.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        Grid.SetRow(this.EnvironmentMessage,0);
        Grid.SetRow(this.Statistics,1);
        Grid.SetRow(this.DetailsView,3);
        this.DetailsRoot.Children.Add(this.EnvironmentMessage);
        this.DetailsRoot.Children.Add(this.Statistics);
        if(this.Skeletal is not null){
            Grid.SetRow(this.Skeletal.Details,2);
            this.DetailsRoot.Children.Add(this.Skeletal.Details);
        }
        this.DetailsRoot.Children.Add(this.DetailsView);
        if(SkeletalInfo is{ SkinCount:>1 }){ this.ShowEnvironmentMessage(VsixText.Format("Skeletal_SkinCountUnsupported",SkeletalInfo.SkinCount)); }
        this.LevelBox=this.CreateLevelBox();
        var FrameButton=AssetEditorShell.CreateButton(VsixText.Get("AssetEditor_Frame"),this.Viewport.FrameMesh);
        FrameButton.ToolTip=VsixText.Get("AssetEditor_FrameHint");
        var LevelLabel=new TextBlock { Text=VsixText.Get("PreviewLevel_Label"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,4,0) };
        LevelLabel.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey);
        this.Toolbar=[this.CreateModeBox(),LevelLabel,this.LevelBox,AssetEditorShell.CreateButton(VsixText.Get("PreviewLevel_Manage"),this.OpenManager),FrameButton];
        this.RebuildLevelBox();
        if(Resolution.Error is{ }Problem){ this.ShowEnvironmentMessage(Problem); }
        this.ReloadTimer.Tick+=this.OnReloadTimer;
        this.EnsureWatcher();
        this.ShowStatisticsPlaceholder();
    }
    private static bool IsGltf(string FilePath)=>FilePath.EndsWith(".glb",StringComparison.OrdinalIgnoreCase)||FilePath.EndsWith(".gltf",StringComparison.OrdinalIgnoreCase);
    internal override UIElement Center=>this.CenterRoot;
    internal override UIElement?Details=>this.DetailsRoot;
    internal override IReadOnlyList<UIElement>ToolbarItems=>this.Toolbar;
    internal AssetViewportHost ViewportForTesting=>this.Viewport;
    private ComboBox CreateModeBox(){
        var Box=new ComboBox { Margin=new Thickness(8,0,2,0),MinWidth=110,Focusable=false,ToolTip=VsixText.Get("AssetEditor_ViewMode") };
        var Modes=new (string Key,RenderPreviewPresentationMode Mode)[] {
            ("SceneRenderModeLit",RenderPreviewPresentationMode.Lit),
            ("SceneRenderModeBaseColor",RenderPreviewPresentationMode.BaseColor),
            ("SceneRenderModeNormal",RenderPreviewPresentationMode.Normal),
            ("SceneRenderModeWireframe",RenderPreviewPresentationMode.Wireframe),
        };
        foreach(var Entry in Modes){
            Box.Items.Add(new ComboBoxItem { Content=VsixText.Get(Entry.Key),Tag=Entry.Mode });
        }
        Box.SelectedIndex=0;
        Box.SelectionChanged+=(_,_)=>{
            if(Box.SelectedItem is ComboBoxItem{ Tag:RenderPreviewPresentationMode Mode }){ this.Viewport.PresentationMode=Mode; }
        };
        return Box;
    }
    private ComboBox CreateLevelBox(){
        var Box=new ComboBox { Margin=new Thickness(2,0,2,0),MinWidth=140,Focusable=false,ToolTip=VsixText.Get("PreviewLevel_Tooltip") };
        Box.DropDownOpened+=(_,_)=>this.RefreshEntries();
        Box.SelectionChanged+=(_,_)=>this.OnLevelSelected(Box);
        return Box;
    }
    /// <summary>カタログからドロップダウンを作り直す。使えない項目は警告付きで選べない。</summary>
    private void RebuildLevelBox(){
        this.UpdatingLevelBox=true;
        try {
            this.LevelBox.Items.Clear();
            ComboBoxItem?Current=null;
            foreach(var Entry in this.Entries){
                var Text=Entry.IsUsable?Entry.DisplayName:Entry.DisplayName+" "+VsixText.Get("PreviewLevel_BrokenTag");
                var Item=new ComboBoxItem { Content=Text,Tag=Entry,IsEnabled=Entry.IsUsable,ToolTip=Entry.Error??Entry.Template?.Description };
                this.LevelBox.Items.Add(Item);
                if(string.Equals(Entry.Id,this.CurrentId,StringComparison.OrdinalIgnoreCase)){ Current=Item; }
            }
            this.LevelBox.SelectedItem=Current;
        } finally {
            this.UpdatingLevelBox=false;
        }
    }
    private void OnLevelSelected(ComboBox Box){
        if(this.UpdatingLevelBox||Box.SelectedItem is not ComboBoxItem{ Tag:PreviewLevelEntry Entry }){ return; }
        if(Entry.Template is not{ }Template){
            this.ShowEnvironmentMessage(VsixText.Format("PreviewLevel_Unavailable",Entry.Id,Entry.Error??VsixText.Get("PreviewLevel_Missing")));
            this.RebuildLevelBox();
            return;
        }
        this.Apply(Template,Persist:true);
    }
    /// <summary>テンプレートを適用する。対象モデルは読み直さず、カメラも維持する（ビューポート側の契約）。</summary>
    private void Apply(PreviewLevelTemplate Template,bool Persist){
        this.CurrentId=Template.Id;
        this.AppliedJson=Template.ToJson();
        this.Viewport.SetTemplate(Template);
        if(Persist){ this.Preferences.LastUsedId=Template.Id; }
        this.HideEnvironmentMessage();
        this.SetStatus(VsixText.Format("PreviewLevel_Applied",Template.DisplayName));
    }
    /// <summary>ホストがテンプレートを適用できず Default へ戻ったとき。ドロップダウンを合わせ、理由を表示する（保存済みの選択は変えない）。</summary>
    private void OnTemplateRejected(PreviewLevelTemplate Rejected,PreviewLevelTemplate Fallback,string Reason){
        this.CurrentId=Fallback.Id;
        this.AppliedJson=Fallback.ToJson();
        this.RebuildLevelBox();
        this.ShowEnvironmentMessage(VsixText.Format("PreviewLevel_Unavailable",Rejected.Id,Reason));
    }
    private void ShowEnvironmentMessage(string Text){
        this.EnvironmentMessage.Text=Text;
        this.EnvironmentMessage.Visibility=Visibility.Visible;
        this.SetStatus(Text);
    }
    private void HideEnvironmentMessage(){
        this.EnvironmentMessage.Text=string.Empty;
        this.EnvironmentMessage.Visibility=Visibility.Collapsed;
    }
    /// <summary>
    /// カタログを読み直す（ドロップダウンを開いたとき・管理ダイアログのあと・ファイル編集のとき）。いま使っているテンプレートが消えた場合は
    /// 標準の Default へ戻して理由を表示する。内容が変わった場合は即座に反映し、壊れた場合は直前の内容を保ったまま理由を表示する（編集の途中でちらつかせない）。
    /// </summary>
    private void RefreshEntries(){
        this.Entries=this.Store.Load();
        this.EnsureWatcher();
        var Entry=this.Entries.FirstOrDefault(Candidate=>string.Equals(Candidate.Id,this.CurrentId,StringComparison.OrdinalIgnoreCase));
        if(Entry is null){
            var Missing=this.CurrentId;
            var Fallback=PreviewLevelResolver.Resolve(this.Entries,this.Preferences.DefaultId);
            this.Apply(Fallback.Template,Persist:false);
            this.RebuildLevelBox();
            this.ShowEnvironmentMessage(VsixText.Format("PreviewLevel_Unavailable",Missing,VsixText.Get("PreviewLevel_Missing")));
            return;
        }
        this.RebuildLevelBox();
        if(Entry.Template is{ }Template){
            if(Template.ToJson()!=this.AppliedJson){ this.Apply(Template,Persist:false); }
        } else {
            this.ShowEnvironmentMessage(VsixText.Format("PreviewLevel_Unavailable",Entry.Id,Entry.Error??string.Empty));
        }
    }
    private void OpenManager(){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Dialog=new PreviewLevelManagerDialog(this.Store,this.Preferences,this.CurrentId);
        if(PresentationSource.FromVisual(this.CenterRoot) is HwndSource Source){ new WindowInteropHelper(Dialog).Owner=Source.Handle; }
        Dialog.ShowModal();
        if(Dialog.Changed){ this.RefreshEntries(); }
    }
    private void EnsureWatcher(){
        if(this.Watcher is not null||this.Disposed||!Directory.Exists(this.Store.Directory)){ return; }
        try {
            var Created=new FileSystemWatcher(this.Store.Directory,"*"+PreviewLevelStore.Extension){ NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size,EnableRaisingEvents=true };
            Created.Changed+=this.OnTemplateFileChanged;
            Created.Created+=this.OnTemplateFileChanged;
            Created.Deleted+=this.OnTemplateFileChanged;
            Created.Renamed+=this.OnTemplateFileChanged;
            this.Watcher=Created;
        } catch(Exception Error) when(Error is IOException or ArgumentException or UnauthorizedAccessException){
            this.Watcher=null;
        }
    }
    private void OnTemplateFileChanged(object Sender,FileSystemEventArgs Args){
        if(this.Disposed){ return; }
        // FileSystemWatcher はスレッドプールで呼ばれる。ShaderGraphEditorWindowPane と同じく Dispatcher で UI スレッドへ渡し、デバウンスのタイマーを再始動する。
#pragma warning disable VSTHRD001
        _=this.CenterRoot.Dispatcher.BeginInvoke(new Action(()=>{
            if(this.Disposed){ return; }
            this.ReloadTimer.Stop();
            this.ReloadTimer.Start();
        }));
#pragma warning restore VSTHRD001
    }
    private void OnReloadTimer(object?Sender,EventArgs Args){
        this.ReloadTimer.Stop();
        if(!this.Disposed){ this.RefreshEntries(); }
    }
    private void ShowStatisticsPlaceholder(){
        this.Statistics.Children.Clear();
        this.Statistics.Children.Add(Line(VsixText.Get("AssetEditor_MeshLoading")));
    }
    private void OnMeshLoaded(AssetMeshStatistics Mesh){
        this.Statistics.Children.Clear();
        var Culture=CultureInfo.CurrentCulture;
        this.Statistics.Children.Add(Line(VsixText.Format("AssetEditor_MeshVertices",Mesh.VertexCount.ToString("N0",Culture))));
        this.Statistics.Children.Add(Line(VsixText.Format("AssetEditor_MeshTriangles",Mesh.TriangleCount.ToString("N0",Culture))));
        var Size=Mesh.Size;
        this.Statistics.Children.Add(Line(VsixText.Format("AssetEditor_MeshSize",Size.X.ToString("0.###",Culture),Size.Y.ToString("0.###",Culture),Size.Z.ToString("0.###",Culture))));
        this.Statistics.Children.Add(Line(VsixText.Get(Path.GetExtension(this.FilePath).Equals(".fbx",StringComparison.OrdinalIgnoreCase)?"AssetEditor_MeshNoteFbx":"AssetEditor_MeshNoteBaseColor")));
    }
    private void OnOperationChanged(AuthoringOperationSnapshot Operation){
        var Diagnostics=string.Join(" ",Operation.Diagnostics.Select(static Diagnostic=>$"{Diagnostic.Code}: {Diagnostic.Message}"));
        this.OperationText=string.IsNullOrEmpty(Diagnostics)?Operation.Summary:Operation.Summary+" "+Diagnostics;
        this.SetStatus(this.OperationText);
        this.SetProgress(Operation.Fraction);
    }
    private static TextBlock Line(string Text){
        var Block=new TextBlock { Text=Text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,2) };
        Block.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey);
        return Block;
    }
    internal override bool HandleKeyDown(Key Key,ModifierKeys Modifiers){
        if(Key==Key.F&&Modifiers==ModifierKeys.None){
            this.Viewport.FrameMesh();
            return true;
        }
        if(Key==Key.Space&&Modifiers==ModifierKeys.None&&this.Skeletal is{ }Panel){ return Panel.TogglePlayback(); }
        return false;
    }
    protected override void OnReload(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.ShowStatisticsPlaceholder();
        this.Viewport.SetModelPath(this.FilePath);
        this.DetailsView.Retarget(this.FilePath);
    }
    public override void Dispose(){
        this.Disposed=true;
        this.Skeletal?.Dispose();
        this.ReloadTimer.Stop();
        this.ReloadTimer.Tick-=this.OnReloadTimer;
        if(this.Watcher is{ }Watching){
            Watching.EnableRaisingEvents=false;
            Watching.Dispose();
            this.Watcher=null;
        }
        this.Viewport.OperationChanged-=this.OnOperationChanged;
        this.Viewport.MeshLoaded-=this.OnMeshLoaded;
        this.Viewport.TemplateRejected-=this.OnTemplateRejected;
        this.Viewport.ViewportPressed-=this.RequestFocus;
        this.DetailsView.Dispose();
        this.Viewport.Dispose();
    }
}
