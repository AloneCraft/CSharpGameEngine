using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.VSExtension;
internal sealed class LightConversionDialog:Window {
    private readonly Dictionary<string,TextBox>Inputs=new(StringComparer.Ordinal);
    internal SceneComponent?Replacement{get;private set;}
    internal LightConversionDialog(SceneComponent Source,IReadOnlyList<INamedTypeSymbol>Targets,Compilation Compilation){
        this.Title=VsixText.Get("LightConversionTitle");
        this.Width=540;this.SizeToContent=SizeToContent.Height;this.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var Panel=new StackPanel{Margin=new Thickness(16)};
        var Choice=new ComboBox{ItemsSource=Targets,DisplayMemberPath="Name",Margin=new Thickness(0,8,0,8)};
        var Review=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
        var Fields=new StackPanel();
        var Error=new TextBlock{TextWrapping=TextWrapping.Wrap};
        var Apply=new Button{Content=VsixText.Get("LightConversionApply"),IsDefault=true,Margin=new Thickness(0,12,0,0),IsEnabled=false};
        var Cancel=new Button{Content=VsixText.Get("LightConversionCancel"),IsCancel=true,Margin=new Thickness(0,8,0,0)};
        Panel.Children.Add(new TextBlock{Text=VsixText.Get("LightConversionInstructions"),TextWrapping=TextWrapping.Wrap});
        Panel.Children.Add(Choice);Panel.Children.Add(Review);Panel.Children.Add(Fields);Panel.Children.Add(Error);Panel.Children.Add(Apply);Panel.Children.Add(Cancel);
        this.Content=Panel;
        Choice.SelectionChanged+=(_,_)=>{
            Fields.Children.Clear();this.Inputs.Clear();
            if(Choice.SelectedItem is not INamedTypeSymbol Target){return;}
            var Suggestions=LightConversionPlanner.Suggest(Source,Target,Compilation);
            var Plan=SceneComponentInitializerPolicy.CreatePlan(Target,SceneTypePredicates.ToFullyQualifiedName(Target),Compilation);
            Review.Text=VsixText.Format("LightConversionReview",Source.TypeSymbol?.Name,Target.Name,
                string.Join(", ",SceneComponentInitializerPolicy.CreatePlan(Source.TypeSymbol!,Source.TypeFullyQualifiedName,Compilation).Members.Select(Member=>Member.MemberName).Except(Suggestions.Keys)));
            foreach(var Member in Plan.Members){
                var Contract=LightValueContract.Find(Member.Value.Type)!;
                var Initial=Suggestions.TryGetValue(Member.MemberName,out var Text)?Text:string.Empty;
                Fields.Children.Add(new TextBlock{Text=VsixText.Format("LightPhysicalValueLabel",Member.MemberName,Contract.Unit,string.Join(", ",Contract.Parameters)),Margin=new Thickness(0,6,0,2)});
                if(Initial.Length==0&&Contract.TryRead(Member.Value,out var DefaultValues,out _)){
                    Fields.Children.Add(new TextBlock{Text=VsixText.Format("LightConversionDefault",string.Join(", ",DefaultValues.Select(Value=>Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture))))});
                }
                var Input=new TextBox{Text=Initial,IsReadOnly=Initial.Length!=0};
                this.Inputs.Add(Member.MemberName,Input);Fields.Children.Add(Input);
                Input.TextChanged+=(_,_)=>Validate();
            }
            Validate();
        };
        Apply.Click+=(_,_)=>{
            if(Choice.SelectedItem is INamedTypeSymbol Target&&LightConversionPlanner.TryBuild(Source,Target,Compilation,this.Values(),out var Replacement)){
                this.Replacement=Replacement;this.DialogResult=true;
            }
        };
        void Validate(){
            Apply.IsEnabled=Choice.SelectedItem is INamedTypeSymbol Target&&LightConversionPlanner.TryBuild(Source,Target,Compilation,this.Values(),out _);
            Error.Text=Apply.IsEnabled?string.Empty:VsixText.Get("LightConversionInputRequired");
        }
        Choice.SelectedIndex=0;
    }
    private Dictionary<string,string>Values()=>this.Inputs.ToDictionary(Pair=>Pair.Key,Pair=>Pair.Value.Text,StringComparer.Ordinal);
}
