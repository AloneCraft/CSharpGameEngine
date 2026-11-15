using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
internal sealed partial class AssetInspectorControl {
    private async Task ShowImageSettingsAsync(AssetWorkspaceItem Item,int Generation){
        try {
            var Result=await Task.Run(()=>AssetAuthoringHostClient.Image(new(){SourcePath=Item.Source.SourcePath}));
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(this.Disposed||Generation!=this.Generation){ return; }
            if(!Result.Success||Result.Settings is null){ this.Label(Result.Diagnostic);return; }
            this.Label($"{Result.Width} x {Result.Height}");
            if(Result.NormalCandidate){ this.Label(VsixText.Get("Assets_ImageNormalCandidate")+Result.NormalCandidateReason); }
            var Settings=Result.Settings;
            var Original=ImageImportSettings.FromOptions(Settings.ToOptions());
            var Replacements=new System.Collections.Generic.Dictionary<string,string>(StringComparer.Ordinal);
            var Editors=new StackPanel{IsEnabled=!Item.Source.ReadOnly};this.Panel.Children.Add(Editors);
            void Text(string Value)=>Editors.Children.Add(new TextBlock{Text=Value,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,2)});
            ComboBox Choice<T>(T[]Values,T Selected,Action<T>Changed){
                var Box=new ComboBox{ItemsSource=Values,SelectedItem=Selected};Box.SelectionChanged+=(_,_)=>{ if(Box.SelectedItem is T Value){ Changed(Value); } };Editors.Children.Add(Box);return Box;
            }
            void Check(string Label,bool Value,Action<bool>Changed){ var Box=new CheckBox{Content=Label,IsChecked=Value};Box.Click+=(_,_)=>Changed(Box.IsChecked==true);Editors.Children.Add(Box); }
            void Draw(){
                Editors.Children.Clear();Text(VsixText.Get("Assets_ImageDefault"));Choice(Settings.Outputs.Select(O=>O.Key).ToArray(),Settings.DefaultOutput,V=>Settings.DefaultOutput=V);
                foreach(var Output in Settings.Outputs){
                    Text(Output.Name+" ["+Output.Key+"] / "+Output.Kind);
                    var Name=new TextBox{Text=Output.Name};Name.TextChanged+=(_,_)=>Output.Name=Name.Text;Editors.Children.Add(Name);
                    var Compatible=Settings.Outputs.Where(O=>O.Key!=Output.Key&&O.Kind==Output.Kind&&(O.Kind==ImageOutputKind.Image||O.Usage==Output.Usage)).Select(O=>O.Key).ToArray();
                    if(Compatible.Length>0){
                        var Replacement=Choice(Compatible,Compatible[0],_=>{});
                        var Remove=new Button{Content=VsixText.Get("Assets_ImageReplaceDelete")};Remove.Click+=(_,_)=>{
                            var Key=(string)Replacement.SelectedItem;
                            foreach(var OldKey in Replacements.Where(P=>P.Value==Output.Key).Select(P=>P.Key).ToArray()){ Replacements[OldKey]=Key; }
                            if(Original.Outputs.Any(O=>O.Key==Output.Key)){ Replacements[Output.Key]=Key; }
                            if(Settings.DefaultOutput==Output.Key){ Settings.DefaultOutput=Key; }
                            Settings.Outputs=Settings.Outputs.Where(O=>O.Key!=Output.Key).ToArray();Draw();
                        };Editors.Children.Add(Remove);
                    }
                    if(Settings.Outputs.Length>1){
                        var RemoveUnused=new Button{Content=VsixText.Get("Assets_ImageRemoveUnused")};RemoveUnused.Click+=(_,_)=>{
                            foreach(var OldKey in Replacements.Where(P=>P.Value==Output.Key).Select(P=>P.Key).ToArray()){ Replacements[OldKey]=string.Empty; }
                            if(Original.Outputs.Any(O=>O.Key==Output.Key)){ Replacements[Output.Key]=string.Empty; }
                            Settings.Outputs=Settings.Outputs.Where(O=>O.Key!=Output.Key).ToArray();
                            if(Settings.DefaultOutput==Output.Key){ Settings.DefaultOutput=Settings.Outputs[0].Key; }
                            Draw();
                        };Editors.Children.Add(RemoveUnused);
                    }
                    if(Output.Kind!=ImageOutputKind.Texture){ continue; }
                    Text(VsixText.Get("Assets_ImageUsage"));var UsageChoice=Choice(Enum.GetValues(typeof(TextureUsage)).Cast<TextureUsage>().ToArray(),Output.Usage,V=>Output.Usage=V);UsageChoice.IsEnabled=!Original.Outputs.Any(O=>O.Key==Output.Key);
                    Check(VsixText.Get("Assets_ImageMips"),Output.Mipmaps,V=>Output.Mipmaps=V);
                    Check(VsixText.Get("Assets_ImageFlipGreen"),Output.FlipGreen,V=>Output.FlipGreen=V);
                    var Channels=new[]{"r","g","b","a","zero","one"};
                    Text(VsixText.Get("Assets_ImageChannel"));Choice(Channels,Output.Channel,V=>Output.Channel=V);
                    Text(VsixText.Get("Assets_ImagePackedChannels"));Choice(Channels,Output.OcclusionChannel,V=>Output.OcclusionChannel=V);
                    Choice(Channels,Output.RoughnessChannel,V=>Output.RoughnessChannel=V);Choice(Channels,Output.MetallicChannel,V=>Output.MetallicChannel=V);
                    Check(VsixText.Get("Assets_ImageInvertRoughness"),Output.InvertRoughness,V=>Output.InvertRoughness=V);
                }
                var AddUsage=new ComboBox{ItemsSource=Enum.GetValues(typeof(TextureUsage)),SelectedItem=TextureUsage.Normal};Editors.Children.Add(AddUsage);
                void Add(ImageOutputSettings Output){
                    var Base=Output.Key;var Number=2;while(Settings.Outputs.Any(O=>O.Key==Output.Key)){ Output.Key=Base+Number++; }
                    Settings.Outputs=Settings.Outputs.Concat(new[]{Output}).ToArray();Draw();
                }
                var AddButton=new Button{Content=VsixText.Get("Assets_ImageAddTexture")};AddButton.Click+=(_,_)=>Add(ImageOutputSettings.For((TextureUsage)AddUsage.SelectedItem));Editors.Children.Add(AddButton);
                var ImageButton=new Button{Content=VsixText.Get("Assets_ImageAsImage")};ImageButton.Click+=(_,_)=>{
                    var Image=Settings.Outputs.FirstOrDefault(O=>O.Kind==ImageOutputKind.Image);
                    if(Image is null){ Image=new(){Key="image",Name="Image",Kind=ImageOutputKind.Image,Mipmaps=false};Add(Image); }
                    Settings.DefaultOutput=Image.Key;Draw();
                };Editors.Children.Add(ImageButton);
                var Apply=new Button{Content=VsixText.Get("Assets_ImageApply")};Apply.Click+=(_,_)=>this.Run(async()=>{
                    Settings.Validate();
                    var Prepared=await Task.Run(()=>AssetAuthoringHostClient.Image(new(){ImageCommand=6,SourcePath=Item.Source.SourcePath,Settings=Settings,ExpectedMetadataHash=Result.MetadataHash}));
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if(!Prepared.Success){ throw new InvalidOperationException(Prepared.Diagnostic); }
                    if(this.Disposed||Generation!=this.Generation){ return; }
                    var PathValue=Item.Source.SourcePath+".meta";
                    if(File.Exists(PathValue)){
                        var Owner=AssetsToolWindowPane.Controller?.Owner??throw new InvalidOperationException("Asset workspace unavailable.");
                        var Plan=await ImageReferenceMigration.ReviewOutputsAsync(Item,Prepared.MetadataJson,Result.MetadataHash,Original,Settings,Replacements,System.Threading.CancellationToken.None);
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        if(new AssetChangeReviewDialog(Plan).ShowDialog()!=true){ return; }
                        AssetFileTransaction.Execute(Owner,Plan);
                    }else{
                        var Owner=AssetsToolWindowPane.Controller?.Owner??throw new InvalidOperationException("Asset workspace unavailable.");
                        var Legacy=Settings.Outputs.FirstOrDefault(O=>O.Kind==ImageOutputKind.Texture&&O.Usage==TextureUsage.Color)??Settings.Outputs.FirstOrDefault(O=>O.Kind==ImageOutputKind.Texture);
                        if(Legacy is null){ throw new InvalidOperationException(VsixText.Get("Assets_ImageKeepTexture")); }
                        var Member=string.Concat(Legacy.Key.Split(new[]{'_'},StringSplitOptions.RemoveEmptyEntries).Select(P=>char.ToUpperInvariant(P[0])+P.Substring(1)));
                        var Plan=await ImageReferenceMigration.PlanAsync(Item,Prepared.MetadataJson,Member,System.Threading.CancellationToken.None);
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        if(new AssetChangeReviewDialog(Plan).ShowDialog()!=true){ return; }
                        AssetFileTransaction.Execute(Owner,Plan);
                    }
                    var Imported=await Task.Run(()=>AssetAuthoringHostClient.Image(new(){ImageCommand=3,SourcePath=Item.Source.SourcePath}));
                    if(!Imported.Success){ throw new InvalidOperationException(Imported.Diagnostic); }
                    if(AssetsToolWindowPane.Controller is { }Controller){ await Controller.RefreshAsync(); }
                    this.RefreshSelection();
                });Editors.Children.Add(Apply);
                var Revert=new Button{Content=VsixText.Get("Assets_ImageRevert")};Revert.Click+=(_,_)=>this.RefreshSelection();Editors.Children.Add(Revert);
                Text(VsixText.Get("Assets_ImageRemovalHelp"));
            }
            Draw();
        }catch(Exception Error){
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();if(!this.Disposed&&Generation==this.Generation){ this.Label(Error.Message); }
        }
    }
}
