using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// UE5 のアセットエディター風の共通レイアウト。上にツールバー、中央にビューポート等、右に Details、下にステータスを置く。
/// 種類固有の部分は <see cref="AssetEditorContent"/> が差し込む。
/// WindowPane は Content が <see cref="IDisposable"/> のときだけ破棄するので、この型が中身の破棄を引き受ける
/// （ネイティブ描画面の <c>DestroyWindowCore</c> とフォーカス解放の修正を確実に動かすため）。
/// </summary>
internal sealed class AssetEditorShell:Grid,IDisposable {
    private readonly AssetEditorContent Content;
    private readonly TextBlock Status=new(){ TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(8,3,8,3) };
    private readonly ProgressBar OperationProgressBar=new(){ Height=3,Minimum=0,Maximum=1,Visibility=Visibility.Collapsed };
    private bool Disposed;
    private JoinableTask?NavigationTask;
    internal AssetEditorShell(AssetEditorContent Content){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Content=Content;
        this.Focusable=true;
        AssetTheme.Apply(this);
        this.SetResourceReference(Panel.BackgroundProperty,VsBrushes.WindowKey);
        this.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        this.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        this.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var Toolbar=this.CreateToolbar();
        Grid.SetRow(Toolbar,0);
        this.Children.Add(Toolbar);
        var Body=this.CreateBody();
        Grid.SetRow(Body,1);
        this.Children.Add(Body);
        var StatusPanel=new StackPanel();
        StatusPanel.Children.Add(this.OperationProgressBar);
        StatusPanel.Children.Add(this.Status);
        var StatusBar=new Border { BorderThickness=new Thickness(0,1,0,0),Child=StatusPanel };
        StatusBar.SetResourceReference(Border.BorderBrushProperty,VsBrushes.InactiveBorderKey);
        this.Status.SetResourceReference(TextBlock.ForegroundProperty,VsBrushes.WindowTextKey);
        Grid.SetRow(StatusBar,2);
        this.Children.Add(StatusBar);
        Content.StatusChanged+=this.OnStatusChanged;
        this.OnStatusChanged(Content.CurrentStatus);
        Content.ProgressChanged+=this.OnProgressChanged;
        Content.FocusRequested+=this.OnFocusRequested;
        this.PreviewKeyDown+=this.OnPreviewKeyDown;
    }
    internal AssetEditorContent EditorContent=>this.Content;
    private Border CreateToolbar(){
        var Panel=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(4,3,4,3) };
        var Solution=CreateButton(VsixText.Get("AssetEditor_ShowInSolution"),()=>this.Navigate(()=>Task.FromResult(AssetEditorNavigation.ShowInSolution(ServiceProvider.GlobalProvider,this.Content.FilePath))));
        Solution.ToolTip=VsixText.Get("AssetEditor_ShowInSolutionHelp");
        Panel.Children.Add(Solution);
        var Assets=CreateButton(VsixText.Get("AssetEditor_ShowInAssets"),()=>this.Navigate(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(AssetsToolWindowPane.Controller is not { }Controller){ return VsixText.Get("AssetEditor_NavigationUnavailable"); }
            return await Controller.RevealAsync(this.Content.FilePath)?VsixText.Get("AssetEditor_RevealedInAssets"):VsixText.Get("AssetEditor_NotInAssets");
        }));
        Assets.ToolTip=VsixText.Get("AssetEditor_ShowInAssetsHelp");
        Panel.Children.Add(Assets);
        foreach(var Item in this.Content.ToolbarItems){
            Panel.Children.Add(Item);
        }
        var Bar=new Border { BorderThickness=new Thickness(0,0,0,1),Child=Panel };
        Bar.SetResourceReference(Border.BorderBrushProperty,VsBrushes.InactiveBorderKey);
        return Bar;
    }
    private Grid CreateBody(){
        var Body=new Grid();
        var Details=this.Content.Details;
        Body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star),MinWidth=160 });
        var Center=this.Content.Center;
        Grid.SetColumn(Center,0);
        Body.Children.Add(Center);
        if(Details is not null){
            Body.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            Body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(340),MinWidth=200 });
            var Splitter=new GridSplitter { Width=5,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,ResizeBehavior=GridResizeBehavior.PreviousAndNext,Focusable=false };
            Splitter.SetResourceReference(Control.BackgroundProperty,VsBrushes.InactiveBorderKey);
            Grid.SetColumn(Splitter,1);
            Body.Children.Add(Splitter);
            Grid.SetColumn(Details,2);
            Body.Children.Add(Details);
        }
        return Body;
    }
    internal static Button CreateButton(string Text,Action Click){
        var Button=new Button { Content=Text,Margin=new Thickness(2,0,2,0),Padding=new Thickness(8,2,8,2),Focusable=false };
        Button.Click+=(_,_)=>Click();
        return Button;
    }
    private void Navigate(Func<Task<string>> Action){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed||this.NavigationTask is { IsCompleted:false }){ return; }
        this.NavigationTask=ThreadHelper.JoinableTaskFactory.RunAsync(async()=>{
            string Message;
            try { Message=await Action(); }
            catch(Exception Error){ Message=VsixText.Format("AssetEditor_NavigationFailed",Error.Message); }
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(!this.Disposed){ this.OnStatusChanged(Message); }
        });
    }
    private void OnStatusChanged(string Text){
        this.Status.Text=Text;
        this.Status.ToolTip=Text;
    }
    private void OnProgressChanged(float Fraction){
        this.OperationProgressBar.Value=Fraction;
        this.OperationProgressBar.Visibility=Fraction>0f&&Fraction<1f?Visibility.Visible:Visibility.Collapsed;
    }
    private void OnFocusRequested(){
        if(!this.Disposed){ Keyboard.Focus(this); }
    }
    private void OnPreviewKeyDown(object Sender,KeyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Keyboard.FocusedElement is TextBox){ return; }
        if(this.Content.HandleKeyDown(Args.Key,Keyboard.Modifiers)){ Args.Handled=true; }
    }
    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        this.Content.StatusChanged-=this.OnStatusChanged;
        this.Content.ProgressChanged-=this.OnProgressChanged;
        this.Content.FocusRequested-=this.OnFocusRequested;
        this.Content.Dispose();
    }
}
