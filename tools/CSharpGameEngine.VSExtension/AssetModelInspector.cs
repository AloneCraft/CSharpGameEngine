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
    private async Task ShowModelSettingsAsync(AssetWorkspaceItem Item,int Generation){
        try {
            var Result=await Task.Run(()=>AssetAuthoringHostClient.Model(new(){SourcePath=Item.Source.SourcePath}));
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(this.Disposed||Generation!=this.Generation){ return; }
            if(!Result.Success||Result.Settings is null){ this.Label(Result.Diagnostic);return; }
            var Settings=Result.Settings;
            this.Label("FBX / Generic");
            foreach(var Group in Result.Subassets.GroupBy(S=>S.Kind)){
                var List=new StackPanel();foreach(var Part in Group){ List.Children.Add(new TextBlock{Text=Part.Name,ToolTip=Part.Id}); }
                this.Panel.Children.Add(new Expander{Header=VsixText.Format("Assets_ModelGroupCount",Group.Key,Group.Count()),Content=List});
            }
            void Check(string Name,bool Value,Action<bool>Changed){ var Box=new CheckBox{Content=Name,IsChecked=Value,IsEnabled=!Item.Source.ReadOnly};Box.Click+=(_,_)=>Changed(Box.IsChecked==true);this.Panel.Children.Add(Box); }
            Check(VsixText.Get("Assets_ModelMeshes"),Settings.Meshes,V=>Settings.Meshes=V);Check(VsixText.Get("Assets_ModelMaterials"),Settings.Materials,V=>Settings.Materials=V);
            Check(VsixText.Get("Assets_ModelSkeleton"),Settings.Skeleton,V=>Settings.Skeleton=V);Check(VsixText.Get("Assets_ModelTakes"),Settings.Animations,V=>Settings.Animations=V);
            var TakeChoice=new ComboBox{ItemsSource=Result.Subassets.Where(S=>S.Kind=="clip"&&!Settings.Clips.Any(C=>C.Name==S.Name)).Select(S=>S.Name).ToArray(),SelectedIndex=0,IsEnabled=!Item.Source.ReadOnly};this.Panel.Children.Add(TakeChoice);
            var ClipName=new TextBox{Text=VsixText.Get("Assets_ModelNewClip"),IsReadOnly=Item.Source.ReadOnly};var First=new TextBox{Text=0.ToString(System.Globalization.CultureInfo.InvariantCulture),IsReadOnly=Item.Source.ReadOnly};var Last=new TextBox{Text=30.ToString(System.Globalization.CultureInfo.InvariantCulture),IsReadOnly=Item.Source.ReadOnly};
            this.Label(VsixText.Get("Assets_ModelClipRange"));this.Panel.Children.Add(ClipName);this.Panel.Children.Add(First);this.Panel.Children.Add(Last);
            var RangeList=new TextBlock{Text=string.Join(Environment.NewLine,Settings.Clips.Select(C=>C.Name+": "+C.Take+" ["+C.StartFrame+", "+C.EndFrame+"]")),TextWrapping=TextWrapping.Wrap};this.Panel.Children.Add(RangeList);
            var Add=new Button{Content=VsixText.Get("Assets_ModelAddClip"),IsEnabled=!Item.Source.ReadOnly&&TakeChoice.Items.Count>0};Add.Click+=(_,_)=>this.Run(()=>{
                if(!int.TryParse(First.Text,out var Start)||!int.TryParse(Last.Text,out var End)||TakeChoice.SelectedItem is not string Take){ throw new InvalidOperationException(VsixText.Get("Assets_ModelInvalidRange")); }
                var Proposed=Settings.Clips.Concat(new[]{new ModelClipRange{Name=ClipName.Text,Take=Take,StartFrame=Start,EndFrame=End}}).ToArray();
                var Previous=Settings.Clips;Settings.Clips=Proposed;try{_=Settings.ToOptions();}catch{Settings.Clips=Previous;throw;}
                RangeList.Text=string.Join(Environment.NewLine,Settings.Clips.Select(C=>C.Name+": "+C.Take+" ["+C.StartFrame+", "+C.EndFrame+"]"));return Task.CompletedTask;
            });this.Panel.Children.Add(Add);
            this.Label(VsixText.Get("Assets_ModelExplicitOperations"));
            this.Button("Assets_Apply",async()=>{
                var Prepared=await Task.Run(()=>AssetAuthoringHostClient.Model(new(){ModelCommand=3,SourcePath=Item.Source.SourcePath,Settings=Settings,ExpectedMetadataHash=Result.MetadataHash}));
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(!Prepared.Success){ throw new InvalidOperationException(Prepared.Diagnostic); }if(this.Disposed||Generation!=this.Generation){ return; }
                var PathValue=Item.Source.SourcePath+".meta";
                if(File.Exists(PathValue)){
                    var Before=AssetDocumentWorkspace.Read(PathValue);if(Before!=File.ReadAllText(PathValue)){ throw new InvalidOperationException(VsixText.Get("Assets_Conflict")); }
                    if(!AssetDocumentWorkspace.Write(PathValue,Before,Prepared.MetadataJson,VsixText.Get("Assets_ModelSettings"))){ throw new InvalidOperationException(VsixText.Get("Assets_Conflict")); }AssetDocumentWorkspace.Save(PathValue);
                }else{ AssetFileTransaction.Create(AssetsToolWindowPane.Controller!.Owner,PathValue,Prepared.MetadataJson); }
                var Imported=await Task.Run(()=>AssetAuthoringHostClient.Model(new(){ModelCommand=2,SourcePath=Item.Source.SourcePath}));
                if(!Imported.Success){ throw new InvalidOperationException(Imported.Diagnostic); }
                if(AssetsToolWindowPane.Controller is { }Controller){ await Controller.RefreshAsync(); }this.RefreshSelection();
            }).IsEnabled=!Item.Source.ReadOnly;
            this.Button("Assets_Cancel",()=>{ this.RefreshSelection();return Task.CompletedTask; });
        }catch(Exception Error){ await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();if(!this.Disposed&&Generation==this.Generation){ this.Label(Error.Message); } }
    }
}
