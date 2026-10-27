using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Skeletal Mesh／Skeleton／Animation Sequence の編集部品（M0127、ADR-0090）。ビューポートの下に置くタイムライン（クリップ・再生・スクラブ・ループ・速度・ボーン表示）と、
/// Details に置くスケルトンのツリー（選択したジョイントを強調）を持つ。評価はホストが行い、ここは時刻を進めて <see cref="AssetViewportHost.SetAnimation"/> へ伝えるだけ。
/// スキンを持たないアニメーションだけの .glb は、プロジェクト内のスキン付きメッシュからプレビューに使うものを選ぶ（選択はこのセッションの間だけ覚え、ソースには書かない）。
/// </summary>
internal sealed class SkeletalEditorPanel:IDisposable {
    private const int MaximumMeshCandidates=200;
    /// <summary>プレビューするメッシュの選択（プロジェクトのディレクトリ → 選んだメッシュ）。セッション内だけの記憶。</summary>
    private static readonly Dictionary<string,string>RememberedMeshes=new(StringComparer.OrdinalIgnoreCase);
    private readonly AssetViewportHost Viewport;
    private string FilePath;
    private readonly string ProjectDirectory;
    private readonly bool AnimationOnly;
    private readonly AssetAnimationTimeline State=new();
    private readonly DispatcherTimer PlaybackTimer=new(DispatcherPriority.Render){ Interval=TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch Clock=new();
    private readonly Grid Strip=new(){ Margin=new Thickness(6,4,6,4) };
    private readonly Button PlayButton;
    private readonly Slider Scrubber=new(){ Minimum=0,Maximum=1,MinWidth=120,VerticalAlignment=VerticalAlignment.Center,Focusable=false,IsEnabled=false };
    private readonly TextBlock TimeText=new(){ VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,8,0),MinWidth=96 };
    private readonly ComboBox ClipBox=new(){ MinWidth=150,Focusable=false,Margin=new Thickness(4,0,4,0),ToolTip=VsixText.Get("Skeletal_Clip") };
    private readonly ComboBox SpeedBox=new(){ MinWidth=64,Focusable=false,Margin=new Thickness(4,0,4,0),ToolTip=VsixText.Get("Skeletal_Speed") };
    private readonly CheckBox LoopBox=new(){ Content=VsixText.Get("Skeletal_Loop"),IsChecked=true,VerticalAlignment=VerticalAlignment.Center,Focusable=false,Margin=new Thickness(4,0,4,0) };
    private readonly CheckBox BonesBox=new(){ Content=VsixText.Get("Skeletal_ShowBones"),IsChecked=false,VerticalAlignment=VerticalAlignment.Center,Focusable=false,Margin=new Thickness(4,0,4,0) };
    private readonly ComboBox MeshBox=new(){ MinWidth=170,Focusable=false,Margin=new Thickness(4,0,4,0),ToolTip=VsixText.Get("Skeletal_PreviewMesh") };
    private readonly TextBlock Message=new(){ TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8,4,8,0),Foreground=Brushes.OrangeRed,Visibility=Visibility.Collapsed };
    private readonly TextBlock TreeHeader=new(){ Margin=new Thickness(8,6,8,2),FontWeight=FontWeights.SemiBold };
    private readonly TreeView JointTree=new(){ Focusable=true,MaxHeight=260,Margin=new Thickness(4,0,4,4) };
    private readonly StackPanel DetailsPart=new();
    private RenderPreviewAssetSummary?Summary;
    private long LastTicks;
    private bool Updating;
    private bool Disposed;
    /// <param name="Viewport">対象のビューポート。この部品が <see cref="AssetViewportHost.SkeletalSource"/> を設定する。</param>
    /// <param name="Info">開いているファイルの glTF の判定結果。</param>
    internal SkeletalEditorPanel(AssetViewportHost Viewport,string FilePath,GltfSkeletalInfo Info){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Viewport=Viewport;
        this.FilePath=FilePath;
        this.ProjectDirectory=CgeProjectContext.Resolve(FilePath)?.ProjectDirectory??Path.GetDirectoryName(FilePath)??string.Empty;
        this.AnimationOnly=Info.IsAnimationOnly;
        this.PlayButton=AssetEditorShell.CreateButton(VsixText.Get("Skeletal_Play"),this.OnPlayClicked);
        this.PlayButton.MinWidth=56;
        foreach(var Speed in new[] { 0.25f,0.5f,1f,2f }){
            this.SpeedBox.Items.Add(new ComboBoxItem { Content=Speed.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"x",Tag=Speed });
        }
        this.SpeedBox.SelectedIndex=2;
        this.BuildLayout();
        this.WireEvents();
        this.TimeText.Text=this.State.Format();
        Viewport.AssetSummaryLoaded+=this.OnSummaryLoaded;
        if(this.AnimationOnly){
            Viewport.AwaitSkeletalSource=true;
            this.PopulateMeshCandidates();
        } else {
            Viewport.SkeletalSource=new RenderPreviewAssetSource { MeshPath=FilePath,AnimationPath=Info.AnimationNames.Count>0?FilePath:string.Empty };
        }
    }
    /// <summary>ビューポートの下に置くタイムラインの帯。</summary>
    internal UIElement Timeline=>this.Strip;
    /// <summary>次にプレビューメッシュを選び直す際も、名前変更後のクリップを使う。</summary>
    internal void Retarget(string FilePath){
        this.FilePath=FilePath;
        this.ResetSummary();
    }
    /// <summary>Details に置くスケルトンのツリーと案内。</summary>
    internal UIElement Details=>this.DetailsPart;
    internal AssetAnimationTimeline TimelineStateForTesting=>this.State;
    private void BuildLayout(){
        var Panel=new WrapPanel { Orientation=Orientation.Horizontal };
        if(this.AnimationOnly){
            Panel.Children.Add(this.MakeLabel("Skeletal_PreviewMesh"));
            Panel.Children.Add(this.MeshBox);
        }
        Panel.Children.Add(this.PlayButton);
        var ScrubberHost=new Border { Width=220,Child=this.Scrubber,Margin=new Thickness(6,0,0,0),VerticalAlignment=VerticalAlignment.Center };
        Panel.Children.Add(ScrubberHost);
        Panel.Children.Add(this.TimeText);
        Panel.Children.Add(this.ClipBox);
        Panel.Children.Add(this.LoopBox);
        Panel.Children.Add(this.MakeLabel("Skeletal_Speed"));
        Panel.Children.Add(this.SpeedBox);
        Panel.Children.Add(this.BonesBox);
        this.Strip.Children.Add(Panel);
        this.TreeHeader.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey);
        this.TimeText.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey);
        this.DetailsPart.Children.Add(this.Message);
        this.DetailsPart.Children.Add(this.TreeHeader);
        this.DetailsPart.Children.Add(this.JointTree);
    }
    private TextBlock MakeLabel(string Key){
        var Label=new TextBlock { Text=VsixText.Get(Key),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,2,0) };
        Label.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey);
        return Label;
    }
    private void WireEvents(){
        this.PlaybackTimer.Tick+=this.OnPlaybackTick;
        this.ClipBox.SelectionChanged+=(_,_)=>this.OnClipSelected();
        this.SpeedBox.SelectionChanged+=(_,_)=>{
            if(this.SpeedBox.SelectedItem is ComboBoxItem{ Tag:float Speed }){ this.State.Speed=Speed; }
        };
        // Click ではなく Checked／Unchecked で受ける（キーボードや UI オートメーションの切り替えでも状態が変わるため）。
        this.LoopBox.Checked+=(_,_)=>this.OnLoopChanged();
        this.LoopBox.Unchecked+=(_,_)=>this.OnLoopChanged();
        this.BonesBox.Checked+=(_,_)=>this.Viewport.ShowSkeleton=true;
        this.BonesBox.Unchecked+=(_,_)=>this.Viewport.ShowSkeleton=false;
        this.Scrubber.ValueChanged+=(_,_)=>this.OnScrubbed();
        this.JointTree.SelectedItemChanged+=(_,_)=>this.Viewport.SelectedJoint=this.JointTree.SelectedItem is TreeViewItem{ Tag:int Joint }?Joint:-1;
        this.MeshBox.SelectionChanged+=(_,_)=>this.OnMeshSelected();
    }
    private void OnLoopChanged(){
        this.State.Loop=this.LoopBox.IsChecked==true;
        this.PushAnimation();
    }
    /// <summary>アニメーションだけのファイル用に、プロジェクトのスキン付きメッシュを候補として並べる。見つからなければ案内を出す。</summary>
    private void PopulateMeshCandidates(){
        var Candidates=new List<string>();
        try {
            foreach(var Path in Directory.EnumerateFiles(this.ProjectDirectory,"*.*",SearchOption.AllDirectories)){
                if(Candidates.Count>=MaximumMeshCandidates){ break; }
                if(IsBuildOutput(Path)||!(Path.EndsWith(".glb",StringComparison.OrdinalIgnoreCase)||Path.EndsWith(".gltf",StringComparison.OrdinalIgnoreCase))){ continue; }
                if(string.Equals(Path,this.FilePath,StringComparison.OrdinalIgnoreCase)){ continue; }
                if(GltfSkeletalProbe.Probe(Path) is{ IsSkeletalMesh:true }){ Candidates.Add(Path); }
            }
        } catch(IOException) {
        } catch(UnauthorizedAccessException) {
        }
        Candidates.Sort(StringComparer.OrdinalIgnoreCase);
        this.Updating=true;
        try {
            foreach(var Candidate in Candidates){
                this.MeshBox.Items.Add(new ComboBoxItem { Content=System.IO.Path.GetFileName(Candidate),ToolTip=Candidate,Tag=Candidate });
            }
            var Remembered=RememberedMeshes.TryGetValue(this.ProjectDirectory,out var Last)&&Candidates.Contains(Last,StringComparer.OrdinalIgnoreCase)?Last:null;
            var Chosen=Remembered??Candidates.FirstOrDefault();
            this.MeshBox.SelectedIndex=Chosen is null?-1:Candidates.FindIndex(Item=>string.Equals(Item,Chosen,StringComparison.OrdinalIgnoreCase));
        } finally {
            this.Updating=false;
        }
        if(this.MeshBox.SelectedItem is ComboBoxItem{ Tag:string Selected }){
            this.UseMesh(Selected);
        } else {
            this.ShowMessage(VsixText.Get("Skeletal_NoSkinnedMesh"));
        }
    }
    private static bool IsBuildOutput(string Path){
        var Normalized=Path.Replace('/','\\');
        return Normalized.IndexOf("\\bin\\",StringComparison.OrdinalIgnoreCase)>=0||Normalized.IndexOf("\\obj\\",StringComparison.OrdinalIgnoreCase)>=0;
    }
    private void OnMeshSelected(){
        if(this.Updating||this.MeshBox.SelectedItem is not ComboBoxItem{ Tag:string Selected }){ return; }
        RememberedMeshes[this.ProjectDirectory]=Selected;
        this.UseMesh(Selected);
    }
    private void UseMesh(string MeshPath){
        this.HideMessage();
        var Current=this.Viewport.SkeletalSource;
        if(Current is null||!string.Equals(Current.MeshPath,MeshPath,StringComparison.Ordinal)||!string.Equals(Current.AnimationPath,this.FilePath,StringComparison.Ordinal)){
            this.ResetSummary();
        }
        this.Viewport.SkeletalSource=new RenderPreviewAssetSource { MeshPath=MeshPath,AnimationPath=this.FilePath };
    }
    private void ResetSummary(){
        this.PlaybackTimer.Stop();
        this.Clock.Reset();
        this.State.SetClip(0f);
        this.Summary=null;
        this.Updating=true;
        try {
            this.ClipBox.Items.Clear();
            this.JointTree.Items.Clear();
            this.TreeHeader.Text=string.Empty;
            this.Viewport.SelectedJoint=-1;
        } finally {
            this.Updating=false;
        }
        this.SyncControls();
    }
    /// <summary>ホストが読み込んだ要約から、クリップの一覧とスケルトンのツリーを作る。</summary>
    private void OnSummaryLoaded(RenderPreviewAssetSummary Summary){
        this.Summary=Summary;
        this.Updating=true;
        try {
            this.ClipBox.Items.Clear();
            this.ClipBox.Items.Add(new ComboBoxItem { Content=VsixText.Get("Skeletal_BindPose"),Tag=-1 });
            for(var Index=0;Index<Summary.Clips.Length;Index++){
                var Clip=Summary.Clips[Index];
                this.ClipBox.Items.Add(new ComboBoxItem { Content=Clip.Name,Tag=Index });
            }
            // アニメーションだけのファイルは最初のクリップ、メッシュ付きのファイルでクリップがあれば最初のクリップを選ぶ。なければバインド姿勢。
            this.ClipBox.SelectedIndex=Summary.Clips.Length>0?1:0;
        } finally {
            this.Updating=false;
        }
        this.BuildTree(Summary);
        this.OnClipSelected();
    }
    private void BuildTree(RenderPreviewAssetSummary Summary){
        this.JointTree.Items.Clear();
        this.TreeHeader.Text=VsixText.Format("Skeletal_Joints",Summary.JointNames.Length);
        var Nodes=new TreeViewItem[Summary.JointNames.Length];
        for(var Index=0;Index<Nodes.Length;Index++){
            Nodes[Index]=new TreeViewItem { Header=Summary.JointNames[Index],Tag=Index,IsExpanded=true };
        }
        for(var Index=0;Index<Nodes.Length;Index++){
            var Parent=Index<Summary.JointParents.Length?Summary.JointParents[Index]:-1;
            if(Parent>=0&&Parent<Nodes.Length&&Parent!=Index){ Nodes[Parent].Items.Add(Nodes[Index]); } else { this.JointTree.Items.Add(Nodes[Index]); }
        }
        this.Viewport.SelectedJoint=-1;
    }
    private void OnClipSelected(){
        if(this.Updating||this.Summary is not{ }Summary||this.ClipBox.SelectedItem is not ComboBoxItem{ Tag:int ClipIndex }){ return; }
        var HasClip=ClipIndex>=0&&ClipIndex<Summary.Clips.Length&&Summary.Clips[ClipIndex].BoundTrackCount>0;
        this.State.SetClip(HasClip?Summary.Clips[ClipIndex].DurationSeconds:0f);
        this.State.Loop=this.LoopBox.IsChecked==true;
        this.PlaybackTimer.Stop();
        this.Clock.Reset();
        if(ClipIndex>=0&&ClipIndex<Summary.Clips.Length&&!HasClip){
            this.ShowMessage(VsixText.Get("Skeletal_ClipUnbound"));
        } else if(this.AnimationOnly&&Summary.Clips.Length==0){
            this.ShowMessage(VsixText.Get("Skeletal_NoClips"));
        } else {
            this.HideMessage();
        }
        this.SyncControls();
        this.PushAnimation();
        // 動くクリップを選んだら、すぐ再生して結果が見えるようにする。
        if(HasClip){
            this.State.Play();
            this.StartPlayback();
        }
    }
    private void OnPlayClicked(){
        this.State.Toggle();
        if(this.State.Playing){ this.StartPlayback(); } else { this.PlaybackTimer.Stop(); }
        this.SyncControls();
    }
    /// <summary>キー（Space）からの再生・一時停止。クリップがなければ何もしない。</summary>
    internal bool TogglePlayback(){
        if(!this.State.HasClip){ return false; }
        this.OnPlayClicked();
        return true;
    }
    private void StartPlayback(){
        this.LastTicks=Stopwatch.GetTimestamp();
        this.PlaybackTimer.Start();
    }
    private void OnPlaybackTick(object?Sender,EventArgs Args){
        var Now=Stopwatch.GetTimestamp();
        var Elapsed=(Now-this.LastTicks)/(float)Stopwatch.Frequency;
        this.LastTicks=Now;
        if(this.State.Advance(Elapsed)){ this.PushAnimation(); }
        if(!this.State.Playing){ this.PlaybackTimer.Stop(); }
        this.SyncControls();
    }
    private void OnScrubbed(){
        if(this.Updating||!this.State.HasClip){ return; }
        this.State.Seek((float)this.Scrubber.Value);
        this.TimeText.Text=this.State.Format();
        this.PushAnimation();
    }
    /// <summary>モデルの状態をコントロールへ反映する（再生ボタンの表示、スライダーの位置、時刻の文字）。</summary>
    private void SyncControls(){
        this.Updating=true;
        try {
            this.PlayButton.Content=VsixText.Get(this.State.Playing?"Skeletal_Pause":"Skeletal_Play");
            this.Scrubber.IsEnabled=this.State.HasClip;
            this.Scrubber.Maximum=this.State.HasClip?this.State.DurationSeconds:1f;
            this.Scrubber.Value=this.State.TimeSeconds;
            this.TimeText.Text=this.State.Format();
        } finally {
            this.Updating=false;
        }
    }
    private void PushAnimation(){
        var ClipIndex=this.ClipBox.SelectedItem is ComboBoxItem{ Tag:int Selected }&&this.State.HasClip?Selected:-1;
        this.Viewport.SetAnimation(ClipIndex,this.State.TimeSeconds,this.State.Loop);
    }
    private void ShowMessage(string Text){
        this.Message.Text=Text;
        this.Message.Visibility=Visibility.Visible;
    }
    private void HideMessage(){
        this.Message.Text=string.Empty;
        this.Message.Visibility=Visibility.Collapsed;
    }
    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        this.PlaybackTimer.Stop();
        this.PlaybackTimer.Tick-=this.OnPlaybackTick;
        this.Viewport.AssetSummaryLoaded-=this.OnSummaryLoaded;
    }
}
