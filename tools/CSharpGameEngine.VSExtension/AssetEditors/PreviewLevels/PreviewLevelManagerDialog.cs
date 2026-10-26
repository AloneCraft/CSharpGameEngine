using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// プレビューレベルの管理（複製・名前変更・削除・既定の指定・JSON で編集）。組み込みは変更できず、複製してから編集する（M0127）。
/// ダイアログは <see cref="PreviewLevelStore"/> と <see cref="PreviewLevelPreferences"/> だけを操作し、開いているプレビューの適用は呼び出し側が結果（<see cref="Changed"/>）を見て行う。
/// </summary>
internal sealed class PreviewLevelManagerDialog:DialogWindow {
    private sealed class Row(PreviewLevelEntry Entry,string Text) {
        internal PreviewLevelEntry Entry { get;}=Entry;
        public override string ToString()=>Text;
    }
    private readonly PreviewLevelStore Store;
    private readonly PreviewLevelPreferences Preferences;
    private readonly ListBox List=new(){ Margin=new Thickness(0,0,8,0) };
    private readonly TextBlock Details=new(){ TextWrapping=TextWrapping.Wrap };
    private readonly Button DuplicateButton;
    private readonly Button RenameButton;
    private readonly Button DeleteButton;
    private readonly Button DefaultButton;
    private readonly Button EditButton;
    private readonly TextBlock Message=new(){ TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) };
    /// <summary>テンプレートの追加・名前変更・削除・既定の変更があった。</summary>
    internal bool Changed { get;private set;}
    /// <summary>JSON エディターを開くために閉じた。</summary>
    internal bool OpenedForEditing { get;private set;}
    internal PreviewLevelManagerDialog(PreviewLevelStore Store,PreviewLevelPreferences Preferences,string?SelectedId){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Store=Store;
        this.Preferences=Preferences;
        AssetTheme.Apply(this);
        this.Title=VsixText.Get("PreviewLevel_ManagerTitle");
        this.Width=720;
        this.Height=460;
        this.MinWidth=520;
        this.MinHeight=320;
        var Root=new Grid { Margin=new Thickness(12) };
        Root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        Root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(250) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        Grid.SetRow(this.List,0);
        Root.Children.Add(this.List);
        var Side=new StackPanel();
        Side.Children.Add(this.Details);
        this.DuplicateButton=this.Button(Side,"PreviewLevel_Duplicate",this.OnDuplicate);
        this.RenameButton=this.Button(Side,"PreviewLevel_Rename",this.OnRename);
        this.DeleteButton=this.Button(Side,"PreviewLevel_Delete",this.OnDelete);
        this.DefaultButton=this.Button(Side,"PreviewLevel_SetDefault",this.OnSetDefault);
        this.EditButton=this.Button(Side,"PreviewLevel_EditJson",this.OnEditJson);
        Side.Children.Add(this.Message);
        Grid.SetRow(Side,0);
        Grid.SetColumn(Side,1);
        Root.Children.Add(Side);
        var Close=new Button { Content=VsixText.Get("PreviewLevel_Close"),MinWidth=90,IsDefault=true,IsCancel=true,Margin=new Thickness(0,10,0,0),HorizontalAlignment=HorizontalAlignment.Right };
        Grid.SetRow(Close,1);
        Grid.SetColumnSpan(Close,2);
        Root.Children.Add(Close);
        this.Content=Root;
        this.List.SelectionChanged+=(_,_)=>this.UpdateSelection();
        this.Refresh(SelectedId);
    }
    private Button Button(Panel Parent,string Key,Action Click){
        var Result=new Button { Content=VsixText.Get(Key),Margin=new Thickness(0,4,0,0),HorizontalAlignment=HorizontalAlignment.Left,MinWidth=160,Padding=new Thickness(8,3,8,3) };
        Result.Click+=(_,_)=>{
            ThreadHelper.ThrowIfNotOnUIThread();
            try { Click(); }
            catch(Exception Error) when(Error is IOException or UnauthorizedAccessException or PreviewLevelProtectedException or ArgumentException or InvalidOperationException){
                this.ShowMessage(Error is PreviewLevelProtectedException?VsixText.Get("PreviewLevel_ProtectedHint"):Error.Message);
            }
        };
        Parent.Children.Add(Result);
        return Result;
    }
    private PreviewLevelEntry?Selected=>(this.List.SelectedItem as Row)?.Entry;
    private void Refresh(string?SelectId){
        var Entries=this.Store.Load();
        var Default=this.Preferences.DefaultId;
        this.List.Items.Clear();
        Row?ToSelect=null;
        foreach(var Entry in Entries){
            var Text=Entry.DisplayName;
            if(Entry.BuiltIn){ Text+=" "+VsixText.Get("PreviewLevel_BuiltInTag"); }
            if(!Entry.IsUsable){ Text+=" "+VsixText.Get("PreviewLevel_BrokenTag"); }
            if(string.Equals(Entry.Id,Default??PreviewLevelBuiltIns.DefaultId,StringComparison.OrdinalIgnoreCase)){ Text+=" "+VsixText.Get("PreviewLevel_DefaultTag"); }
            var Item=new Row(Entry,Text);
            this.List.Items.Add(Item);
            if(string.Equals(Entry.Id,SelectId,StringComparison.OrdinalIgnoreCase)){ ToSelect=Item; }
        }
        this.List.SelectedItem=ToSelect??(this.List.Items.Count>0?this.List.Items[0]:null);
        this.UpdateSelection();
    }
    private void UpdateSelection(){
        var Entry=this.Selected;
        this.DuplicateButton.IsEnabled=Entry?.IsUsable==true;
        this.RenameButton.IsEnabled=Entry is{ BuiltIn:false,IsUsable:true };
        this.DeleteButton.IsEnabled=Entry is{ BuiltIn:false };
        this.DefaultButton.IsEnabled=Entry?.IsUsable==true;
        this.EditButton.IsEnabled=Entry is{ BuiltIn:false,FilePath:not null };
        this.Details.Text=Entry is null?string.Empty:Entry.Error is{ }Error?Error:(Entry.Template?.Description??string.Empty)+(Entry.BuiltIn?Environment.NewLine+VsixText.Get("PreviewLevel_ProtectedHint"):string.Empty);
        this.Message.Text=string.Empty;
    }
    private void ShowMessage(string Text)=>this.Message.Text=Text;
    private void OnDuplicate(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Selected is not{ Template:{ }Source }Entry){ return; }
        var Name=NamePromptDialog.Ask(this,VsixText.Format("PreviewLevel_CopyName",Entry.DisplayName));
        if(Name is null){ return; }
        var Created=this.Store.Duplicate(Source,Name);
        this.Changed=true;
        this.Refresh(Created.Id);
    }
    private void OnRename(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Selected is not{ BuiltIn:false }Entry){ return; }
        var Name=NamePromptDialog.Ask(this,Entry.DisplayName);
        if(Name is null){ return; }
        this.Store.Rename(Entry.Id,Name);
        this.Changed=true;
        this.Refresh(Entry.Id);
    }
    private void OnDelete(){
        if(this.Selected is not{ BuiltIn:false }Entry){ return; }
        if(MessageBox.Show(this,VsixText.Format("PreviewLevel_ConfirmDelete",Entry.DisplayName),this.Title,MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes){ return; }
        this.Store.Delete(Entry.Id);
        if(string.Equals(this.Preferences.LastUsedId,Entry.Id,StringComparison.OrdinalIgnoreCase)){ this.Preferences.LastUsedId=null; }
        if(string.Equals(this.Preferences.DefaultId,Entry.Id,StringComparison.OrdinalIgnoreCase)){ this.Preferences.DefaultId=null; }
        this.Changed=true;
        this.Refresh(null);
    }
    private void OnSetDefault(){
        if(this.Selected is not{ IsUsable:true }Entry){ return; }
        this.Preferences.DefaultId=Entry.Id;
        this.Changed=true;
        this.Refresh(Entry.Id);
    }
    private void OnEditJson(){
        if(this.Selected is not{ BuiltIn:false,FilePath:{ }Path }){ return; }
        VsShellUtilities.OpenDocumentWithSpecificEditor(ServiceProvider.GlobalProvider,Path,VSConstants.VsEditorFactoryGuid.TextEditor_guid,VSConstants.LOGVIEWID.TextView_guid);
        this.OpenedForEditing=true;
        this.DialogResult=true;
    }
}
/// <summary>テンプレート名の入力ダイアログ。空・空白だけの名前は受け付けない。</summary>
internal sealed class NamePromptDialog:DialogWindow {
    private readonly TextBox Input=new(){ MinWidth=320 };
    internal NamePromptDialog(string InitialName){
        ThreadHelper.ThrowIfNotOnUIThread();
        AssetTheme.Apply(this);
        this.Title=VsixText.Get("PreviewLevel_NamePrompt");
        this.SizeToContent=SizeToContent.WidthAndHeight;
        this.ResizeMode=ResizeMode.NoResize;
        var Root=new StackPanel { Margin=new Thickness(12) };
        this.Input.Text=InitialName;
        Root.Children.Add(this.Input);
        var Buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0) };
        var Ok=new Button { Content=VsixText.Get("PreviewLevel_Ok"),MinWidth=80,IsDefault=true,Margin=new Thickness(4,0,0,0) };
        var Cancel=new Button { Content=VsixText.Get("PreviewLevel_Cancel"),MinWidth=80,IsCancel=true,Margin=new Thickness(4,0,0,0) };
        Ok.Click+=(_,_)=>{
            if(!string.IsNullOrWhiteSpace(this.Input.Text)){ this.DialogResult=true; }
        };
        Buttons.Children.Add(Ok);
        Buttons.Children.Add(Cancel);
        Root.Children.Add(Buttons);
        this.Content=Root;
        this.Loaded+=(_,_)=>{
            this.Input.Focus();
            this.Input.SelectAll();
        };
    }
    internal string?EnteredName=>string.IsNullOrWhiteSpace(this.Input.Text)?null:this.Input.Text.Trim();
    /// <summary>名前を尋ねる。キャンセルなら null。</summary>
    internal static string?Ask(Window Owner,string InitialName){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Dialog=new NamePromptDialog(InitialName){ Owner=Owner,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        return Dialog.ShowModal()==true?Dialog.EnteredName:null;
    }
}
