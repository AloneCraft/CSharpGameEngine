using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
namespace CSharpGameEngine.VSExtension;
internal sealed class AssetChangeReviewDialog:DialogWindow {
    internal AssetChangeReviewDialog(AssetReferencePlan Plan){
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
        AssetTheme.Apply(this);
        this.Title=VsixText.Get("Assets_PreviewChanges");this.Width=1000;this.Height=650;this.MinWidth=600;this.MinHeight=300;
        var Grid=new Grid{Margin=new Thickness(12)};
        Grid.RowDefinitions.Add(new(){Height=GridLength.Auto});Grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});Grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        Grid.ColumnDefinitions.Add(new(){Width=new GridLength(280)});Grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var Header=new TextBlock{Text=Plan.Mutation.Operation+Environment.NewLine+Plan.Mutation.Source+Environment.NewLine+Plan.Mutation.Destination,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
        Grid.Children.Add(Header);System.Windows.Controls.Grid.SetColumnSpan(Header,2);
        var Edits=Plan.Edits.ToList();
        if(Plan.Mutation.Content.Length>0&&!Edits.Any(Edit=>string.Equals(Edit.Path,Plan.Mutation.Source,StringComparison.OrdinalIgnoreCase))){
            Edits.Insert(0,new AssetReferenceEdit(Plan.Mutation.Destination,System.IO.File.Exists(Plan.Mutation.Source)?AssetDocumentWorkspace.Read(Plan.Mutation.Source):string.Empty,Plan.Mutation.Content));
        }
        var Files=new ListBox{ItemsSource=Edits,DisplayMemberPath=nameof(AssetReferenceEdit.Path),Margin=new Thickness(0,0,8,0)};
        System.Windows.Controls.Grid.SetRow(Files,1);Grid.Children.Add(Files);
        var Preview=new TextBox{IsReadOnly=true,AcceptsReturn=true,AcceptsTab=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new System.Windows.Media.FontFamily("Consolas")};
        System.Windows.Controls.Grid.SetRow(Preview,1);System.Windows.Controls.Grid.SetColumn(Preview,1);Grid.Children.Add(Preview);
        Files.SelectionChanged+=(_,_)=>{
            if(Files.SelectedItem is AssetReferenceEdit Edit){ Preview.Text=VsixText.Get("Assets_Before")+Environment.NewLine+Edit.Before+Environment.NewLine+Environment.NewLine+VsixText.Get("Assets_After")+Environment.NewLine+Edit.After; }
        };
        if(Edits.Count>0){ Files.SelectedIndex=0; }else { Preview.Text=Plan.Description; }
        var Buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        var Apply=new Button{Content=VsixText.Get("Assets_Apply"),MinWidth=90,IsDefault=true,Margin=new Thickness(4,0,0,0)};
        var Cancel=new Button{Content=VsixText.Get("Assets_Cancel"),MinWidth=90,IsCancel=true,Margin=new Thickness(4,0,0,0)};
        Apply.Click+=(_,_)=>this.DialogResult=true;Buttons.Children.Add(Apply);Buttons.Children.Add(Cancel);
        System.Windows.Controls.Grid.SetRow(Buttons,2);System.Windows.Controls.Grid.SetColumnSpan(Buttons,2);Grid.Children.Add(Buttons);this.Content=Grid;
    }
}
