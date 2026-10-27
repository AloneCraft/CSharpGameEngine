using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
namespace CSharpGameEngine.VSExtension;
internal sealed partial class AssetInspectorControl:ScrollViewer,IDisposable {
    private readonly StackPanel Panel=new(){Margin=new Thickness(8)};
    private int Generation;
    private bool Disposed;
    private System.Windows.Media.MediaPlayer?Audio;
    private ITextBuffer?WatchedBuffer;
    private string WatchedPath=string.Empty;
    private bool RefreshQueued;
    private (string Path,string Name)?PendingFocus;
    private readonly bool FollowSelection;
    private IReadOnlyList<AssetWorkspaceItem>CurrentItems=Array.Empty<AssetWorkspaceItem>();
    internal event Action<bool>?ShowingChanged;
    /// <summary>アセットエディターの Details 欄など、グローバルな Assets 選択に追従せず <see cref="Show"/> で渡された項目だけを表示する場合は false。</summary>
    internal AssetInspectorControl(bool FollowSelection=true){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.FollowSelection=FollowSelection;
        AssetTheme.Apply(this);
        this.Content=this.Panel;this.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        if(FollowSelection){ AssetWorkspaceSelection.Changed+=this.OnSelection; }
        this.PreviewKeyDown+=this.OnKey;
    }
    /// <summary>明示的に項目を表示する（<c>FollowSelection=false</c> のとき用）。</summary>
    internal void Show(IReadOnlyList<AssetWorkspaceItem>Items){ ThreadHelper.ThrowIfNotOnUIThread();this.OnSelection(Items); }
    private void OnSelection(IReadOnlyList<AssetWorkspaceItem>Items){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed){ return; }
        this.CurrentItems=Items;
        this.Generation++;
        this.StopWatching();
        if(Items.Count!=1||this.PendingFocus is { }Pending&&Pending.Path!=Items[0].Source.SourcePath){ this.PendingFocus=null; }
        this.Audio?.Close();this.Audio=null;
        this.Panel.Children.Clear();this.ShowingChanged?.Invoke(Items.Count>0);
        if(Items.Count==0){ return; }
        this.Label(VsixText.Format("Assets_Count",Items.Count));
        if(Items.Count!=1){ this.Label(string.Join(Environment.NewLine,Items.Select(static Item=>Item.Source.RelativePath)));return; }
        var Item=Items[0];
        var Presentation=new AssetWorkspaceRow(Item);
        this.Label(Item.Source.RelativePath);this.IconLabel(Presentation.KindLabel,Presentation.KindMoniker);
        this.IconLabel(Presentation.StateLabel,Presentation.HasStateIcon?Presentation.StateMoniker:Item.State==AssetWorkspaceState.ReadOnly?Presentation.LockMoniker:default);
        if(Presentation.IsReadOnly&&(Item.State!=AssetWorkspaceState.ReadOnly||Presentation.HasStateIcon)){ this.IconLabel(Presentation.ReadOnlyLabel,Presentation.LockMoniker); }
        if(!string.IsNullOrEmpty(Item.AssetId)){ this.Label(Item.AssetId); }
        if(!string.IsNullOrEmpty(Item.Diagnostic)){ this.Label(Item.Diagnostic); }
        this.Button("Assets_Open",async()=>{ await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();AssetsToolWindowPane.Controller?.Open(Item); }).IsEnabled=(Item.GetCapabilities()&AssetWorkspaceCapabilities.Open)!=0;
        this.Button("Assets_References",()=>AssetWorkspaceCommands.ShowReferencesAsync(Item)).IsEnabled=Item.State!=AssetWorkspaceState.MissingSource;
        if(Item.State==AssetWorkspaceState.MissingSource){ return; }
        if(Item.Kind==AssetWorkspaceKind.Data){ _=this.ShowDataAsync(Item,this.Generation); }
        else if(Item.Kind==AssetWorkspaceKind.Font){ _=this.ShowFontAsync(Item,this.Generation); }
        else if(Item.Kind==AssetWorkspaceKind.Audio){
            this.Audio=new System.Windows.Media.MediaPlayer();
            var AudioGeneration=this.Generation;
            this.Audio.MediaFailed+=(_,Args)=>{
                if(!this.Disposed&&AudioGeneration==this.Generation){ this.Label(Args.ErrorException.Message); }
            };
            this.Audio.Open(new Uri(Item.Source.SourcePath));
            this.Button("Assets_Play",()=>{ this.Audio?.Play();return Task.CompletedTask; });
            this.Button("Assets_Stop",()=>{ this.Audio?.Stop();return Task.CompletedTask; });
        }
        else if(Item.Kind is AssetWorkspaceKind.Texture or AssetWorkspaceKind.Image&&Path.GetExtension(Item.Source.SourcePath).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg"){
            var Image=new Image{MaxHeight=180,Stretch=System.Windows.Media.Stretch.Uniform};
            Image.SetBinding(Image.SourceProperty,new System.Windows.Data.Binding(nameof(AssetWorkspaceRow.Thumbnail)){Source=new AssetWorkspaceRow(Item)});this.Panel.Children.Add(Image);
        }
        if(!Item.Source.ReadOnly&&(ImageImportSettings.Supports(Item.Source.SourcePath)||Path.GetExtension(Item.Source.SourcePath).Equals(".fbx",StringComparison.OrdinalIgnoreCase))){
            this.Button("Assets_Reimport",async()=>{
                string Diagnostic;bool Success;
                if(ImageImportSettings.Supports(Item.Source.SourcePath)){ var Result=await Task.Run(()=>AssetAuthoringHostClient.Image(new(){ImageCommand=3,SourcePath=Item.Source.SourcePath}));Success=Result.Success;Diagnostic=Result.Diagnostic; }
                else { var Result=await Task.Run(()=>AssetAuthoringHostClient.Model(new(){ModelCommand=2,SourcePath=Item.Source.SourcePath}));Success=Result.Success;Diagnostic=Result.Diagnostic; }
                if(AssetsToolWindowPane.Controller is { }Controller){ await Controller.RefreshAsync(); }
                if(!Success){ throw new InvalidOperationException(Diagnostic); }
            }).IsEnabled=Item.MetadataSha256.Length>0;
        }
        if(ImageImportSettings.Supports(Item.Source.SourcePath)){ _=this.ShowImageSettingsAsync(Item,this.Generation); }
        if(Item.Kind==AssetWorkspaceKind.Model&&Path.GetExtension(Item.Source.SourcePath).Equals(".fbx",StringComparison.OrdinalIgnoreCase)){ _=this.ShowModelSettingsAsync(Item,this.Generation); }
        if(Item.Kind==AssetWorkspaceKind.Model&&!Item.Source.ReadOnly){
            if(Item.State==AssetWorkspaceState.InvalidMetadata){ this.Button("Assets_Migrate",()=>this.RunMetadataAsync(Item,true)); }
            if(Item.State==AssetWorkspaceState.MissingMetadata){ this.Button("Assets_Register",()=>this.RunMetadataAsync(Item,false)); }
            if(!Path.GetExtension(Item.Source.SourcePath).Equals(".fbx",StringComparison.OrdinalIgnoreCase)){ var Reimport=this.Button("Assets_Reimport",()=>Task.CompletedTask);Reimport.IsEnabled=false;Reimport.ToolTip=VsixText.Get("Assets_Unavailable"); }
        }
    }
    internal void RefreshSelection(){ ThreadHelper.ThrowIfNotOnUIThread();this.OnSelection(this.FollowSelection?AssetWorkspaceSelection.Current:this.CurrentItems); }
    private async Task ShowFontAsync(AssetWorkspaceItem Item,int Generation){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        try {
            if(Path.GetExtension(Item.Source.SourcePath).Equals(".cgefont",StringComparison.OrdinalIgnoreCase)){
                var Context=CgeProjectContext.TryCreate(Item.Source.ProjectPath)??throw new InvalidOperationException(VsixText.Get("Assets_NoSelection"));
                var Result=await Task.Run(()=>AssetAuthoringHostClient.InspectWorkspace(new(){WorkspaceCommand=2,RequestId=Guid.NewGuid(),Sources=[Item.Source]},Context.WorkspaceRoot));
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(this.Disposed||Generation!=this.Generation){ return; }
                var Preview=Result.FontPreview!;
                var Bitmap=System.Windows.Media.Imaging.BitmapSource.Create(Preview.Width,Preview.Height,96,96,System.Windows.Media.PixelFormats.Gray8,null,Preview.Distance,Preview.Width);Bitmap.Freeze();
                this.Label(VsixText.Format("Assets_FontAtlas",Preview.SourceWidth,Preview.SourceHeight,Preview.GlyphCount));
                this.Panel.Children.Add(new Image{Source=Bitmap,MaxHeight=300,Stretch=System.Windows.Media.Stretch.Uniform});
            } else {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(this.Disposed||Generation!=this.Generation){ return; }
                var Typeface=new System.Windows.Media.GlyphTypeface(new Uri(Item.Source.SourcePath));
                var Name=Typeface.FamilyNames.Values.First();
                this.Label(Name);
                this.Panel.Children.Add(new TextBlock{Text=VsixText.Get("Assets_FontSample"),TextWrapping=TextWrapping.Wrap,FontSize=28,
                    FontFamily=new System.Windows.Media.FontFamily(new Uri(Path.GetDirectoryName(Item.Source.SourcePath)!+Path.DirectorySeparatorChar),"./#"+Name)});
            }
        }catch(Exception Error){
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(!this.Disposed&&Generation==this.Generation){ this.Label(Error.Message);this.Label(VsixText.Get("Assets_Unsupported")); }
        }
    }
    private async Task RunMetadataAsync(AssetWorkspaceItem Item,bool Migrate){
        var Result=await Task.Run(()=>{
            var Snapshot=AssetAuthoringHostClient.Inspect(Item.Source.SourcePath);
            return Migrate?AssetAuthoringHostClient.MigrateLegacy(Item.Source.SourcePath,Snapshot.MetadataSha256):AssetAuthoringHostClient.SaveMetadata(Item.Source.SourcePath,Snapshot);
        });
        if(!Result.Success){ throw new InvalidOperationException(string.Join(Environment.NewLine,Result.Diagnostics.Select(static Diagnostic=>Diagnostic.Code+": "+Diagnostic.Message))); }
        if(AssetsToolWindowPane.Controller is { }Controller){ await Controller.RefreshAsync(); }
    }
    private async Task ShowDataAsync(AssetWorkspaceItem Item,int Generation){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        try {
            this.WatchBuffer(Item.Source.SourcePath);
            var Text=AssetDocumentWorkspace.Read(Item.Source.SourcePath);
            var Compilation=await AssetDocumentWorkspace.CompilationAsync(Item.Source.ProjectPath,CancellationToken.None);
            var Rows=await Task.Run(()=>AssetDataEditing.Rows(Text,Compilation));
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(this.Disposed||Generation!=this.Generation){ return; }
            foreach(var Row in Rows){
                this.Label(Row.Name);
                var Choices=AssetDataEditing.EnumChoices(Text,Row,Compilation);
                Control Editor;
                if(Choices.Length>0){
                    var Choice=new ComboBox{ItemsSource=Choices,SelectedItem=SceneAnalysis.Json.SceneJsonReader.Parse(Row.Json).Text,IsEnabled=!Item.Source.ReadOnly};
                    Editor=Choice;
                    this.Panel.Children.Add(Choice);
                    if(!Item.Source.ReadOnly){ this.Button("Assets_Apply",()=>this.EditAsync(Item,Text,Row,System.Text.Json.JsonSerializer.Serialize(Choice.SelectedItem as string),Generation)); }
                } else if(Row.Kind is SceneAnalysis.Json.SceneJsonKind.True or SceneAnalysis.Json.SceneJsonKind.False){
                    var Check=new CheckBox{IsChecked=Row.Kind==SceneAnalysis.Json.SceneJsonKind.True,IsEnabled=!Item.Source.ReadOnly,HorizontalAlignment=HorizontalAlignment.Left};
                    Editor=Check;
                    Check.Click+=(_,_)=>this.Run(()=>this.EditAsync(Item,Text,Row,Check.IsChecked==true?"true":"false",Generation));this.Panel.Children.Add(Check);
                } else {
                    var IsString=Row.Kind==SceneAnalysis.Json.SceneJsonKind.String;
                    var Input=new TextBox{Text=IsString?SceneAnalysis.Json.SceneJsonReader.Parse(Row.Json).Text??string.Empty:Row.Json,IsReadOnly=Item.Source.ReadOnly,ToolTip=VsixText.Get("Assets_Value"),MaxHeight=100,AcceptsReturn=Row.Kind==SceneAnalysis.Json.SceneJsonKind.Array,TextWrapping=TextWrapping.Wrap};
                    Editor=Input;
                    this.Panel.Children.Add(Input);
                    if(!Item.Source.ReadOnly){ this.Button("Assets_Apply",()=>this.EditAsync(Item,Text,Row,IsString?System.Text.Json.JsonSerializer.Serialize(Input.Text):Input.Text??string.Empty,Generation)); }
                }
                if(this.PendingFocus is { }Focus&&Focus.Path==Item.Source.SourcePath&&Focus.Name==Row.Name){ Editor.Focus();this.PendingFocus=null; }
            }
        }catch(Exception Error){ if(!this.Disposed&&Generation==this.Generation){ this.Label(Error.Message);this.Label(VsixText.Get("Assets_Unsupported")); } }
    }
    private async Task EditAsync(AssetWorkspaceItem Item,string Before,AssetDataRow Row,string Json,int Generation){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var Compilation=await AssetDocumentWorkspace.CompilationAsync(Item.Source.ProjectPath,CancellationToken.None);
        var After=await Task.Run(()=>AssetDataEditing.Edit(Before,Row,Json,Compilation));
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if(this.Disposed||Generation!=this.Generation||AssetDocumentWorkspace.Read(Item.Source.SourcePath)!=Before){ throw new InvalidOperationException(VsixText.Get("Assets_Conflict")); }
        if(!AssetDocumentWorkspace.Write(Item.Source.SourcePath,Before,After,VsixText.Get("Assets_Properties"))){ throw new InvalidOperationException(VsixText.Get("Assets_Conflict")); }
        this.PendingFocus=(Item.Source.SourcePath,Row.Name);
        if(this.FollowSelection){ AssetWorkspaceSelection.Select([Item]); } else { this.OnSelection([Item]); }
    }
    private void WatchBuffer(string Path){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(AssetDocumentWorkspace.TryBuffer(Path,out var Buffer)){
            this.WatchedBuffer=Buffer;this.WatchedPath=Path;Buffer!.Changed+=this.OnBufferChanged;
        }
    }
    private void StopWatching(){
        if(this.WatchedBuffer is { }Buffer){ Buffer.Changed-=this.OnBufferChanged; }
        this.WatchedBuffer=null;this.WatchedPath=string.Empty;
    }
    private void OnBufferChanged(object Sender,TextContentChangedEventArgs Args){
        if(this.Disposed||this.RefreshQueued){ return; }
        this.RefreshQueued=true;
        this.Run(async()=>{
            await Task.Yield();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            this.RefreshQueued=false;
            var Items=this.FollowSelection?AssetWorkspaceSelection.Current:this.CurrentItems;
            if(!this.Disposed&&Items.Count==1&&Items[0].Source.SourcePath==this.WatchedPath){ this.OnSelection(Items); }
        });
    }
    private void OnKey(object Sender,KeyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Keyboard.Modifiers==ModifierKeys.Control&&this.HandleNativeShortcut(Args.Key)){ Args.Handled=true; }
    }
    internal bool HandleNativeShortcut(Key Key){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Items=this.FollowSelection?AssetWorkspaceSelection.Current:this.CurrentItems;
        if(!this.IsKeyboardFocusWithin||Items.Count!=1||Key is not(Key.S or Key.Z or Key.Y)){ return false; }
        var Item=Items[0];
        if(Item.Kind!=AssetWorkspaceKind.Data){ return false; }
        if(Key==Key.S){ this.Run(async()=>{ await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();AssetDocumentWorkspace.Save(Item.Source.SourcePath); });return true; }
        if(Keyboard.FocusedElement is not TextBox){
            var History=AssetDocumentWorkspace.Histories.RegisterHistory(AssetDocumentWorkspace.OpenBuffer(Item.Source.SourcePath));
            if(Key==Key.Z&&History.CanUndo){ History.Undo(1); }
            if(Key==Key.Y&&History.CanRedo){ History.Redo(1); }
            this.OnSelection([Item]);return true;
        }
        return false;
    }
    private void Label(string Text)=>this.Panel.Children.Add(new TextBlock{Text=Text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,3)});
    private void IconLabel(string Text,Microsoft.VisualStudio.Imaging.Interop.ImageMoniker Moniker){
        if(Moniker.Guid==Guid.Empty){ this.Label(Text);return; }
        var Row=new DockPanel{Margin=new Thickness(0,3,0,3)};
        var Icon=new Microsoft.VisualStudio.Imaging.CrispImage{Moniker=Moniker,Width=16,Height=16,Margin=new Thickness(0,0,4,0),VerticalAlignment=VerticalAlignment.Top,ToolTip=Text};
        System.Windows.Automation.AutomationProperties.SetName(Icon,Text);
        DockPanel.SetDock(Icon,Dock.Left);Row.Children.Add(Icon);
        Row.Children.Add(new TextBlock{Text=Text,TextWrapping=TextWrapping.Wrap});this.Panel.Children.Add(Row);
    }
    private Button Button(string Key,Func<Task>Operation){
        var Button=new Button{Content=VsixText.Get(Key),Margin=new Thickness(0,2,0,2),HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(6,2,6,2)};
        Button.Click+=(_,_)=>this.Run(Operation);this.Panel.Children.Add(Button);
        return Button;
    }
    private void Run(Func<Task>Operation)=>AssetsToolWindowPane.Controller?.Run(Operation);
    public void Dispose(){ this.Disposed=true;this.Generation++;this.StopWatching();this.Audio?.Close();if(this.FollowSelection){ AssetWorkspaceSelection.Changed-=this.OnSelection; } }
}
