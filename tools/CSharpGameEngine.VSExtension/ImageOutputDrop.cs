using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
namespace CSharpGameEngine.VSExtension;
internal sealed record ImageOutputDrag(AssetWorkspaceItem Item,ImageOutputSettings Output);
public partial class AssetsToolWindowControl {
    private void OnOutputDragMove(object Sender,MouseEventArgs Args){
        if(Args.LeftButton!=MouseButtonState.Pressed||Sender is not FrameworkElement{DataContext:ImageOutputSettings Output}Element){ return; }
        DependencyObject?Parent=Element;
        while(Parent is not null&&Parent is not ListViewItem){ Parent=VisualTreeHelper.GetParent(Parent); }
        if(Parent is not ListViewItem{DataContext:AssetWorkspaceRow Row}){ return; }
        var Data=new DataObject(typeof(AssetWorkspaceItem),Row.Item);Data.SetData(typeof(ImageOutputDrag),new ImageOutputDrag(Row.Item,Output));
        Args.Handled=true;DragDrop.DoDragDrop(Element,Data,DragDropEffects.Copy);
    }
}
internal static class ImageOutputDrop {
    internal static string Member(string Key)=>string.Concat(Key.Split(new[]{'_'},StringSplitOptions.RemoveEmptyEntries).Select(P=>char.ToUpperInvariant(P[0])+P.Substring(1)));
    internal static bool Accepts(SceneInspectorMember Member,AssetWorkspaceItem Asset,ImageOutputDrag?Explicit){
        if(Asset.Source.ReadOnly&&Asset.ImageSettings is null||!ImageImportSettings.Supports(Asset.Source.SourcePath)
            ||Asset.Source.ProjectPath!=CgeProjectContext.Resolve(SceneEditorHub.ActiveSceneModel?.FilePath)?.ProjectFilePath){ return false; }
        var Kind=Explicit?.Output.Kind??(Asset.Kind==AssetWorkspaceKind.Image?ImageOutputKind.Image:ImageOutputKind.Texture);
        return Member.AssetTypeName switch{
            "CSharpGameEngine.Authoring.TextureAsset"=>Kind==ImageOutputKind.Texture&&(Explicit is null||Member.TextureUsage is null||Explicit.Output.Usage==Member.TextureUsage),
            "CSharpGameEngine.Authoring.ImageAsset"=>Kind==ImageOutputKind.Image,
            _=>false,
        };
    }
    internal static async Task<(string Expression,IVsLinkedUndoTransactionManager?Linked)>PrepareAsync(SceneInspectorMember Member,AssetWorkspaceItem Asset,ImageOutputDrag?Explicit,IVsLinkedUndoTransactionManager?ExistingLinked=null){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var Role=Member.TextureUsage??(Member.MemberName.IndexOf("normal",StringComparison.OrdinalIgnoreCase)>=0?TextureUsage.Normal:TextureUsage.Color);
        var Snapshot=await Task.Run(()=>AssetAuthoringHostClient.Image(new(){SourcePath=Asset.Source.SourcePath}));
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if(!Snapshot.Success||Snapshot.Settings is null||Snapshot.MetadataHash.Length==0){ throw new InvalidOperationException(VsixText.Get("Assets_ImageRegisterFirst")); }
        var Settings=Snapshot.Settings;
        var Output=Explicit is null?Member.AssetTypeName=="CSharpGameEngine.Authoring.ImageAsset"?Settings.Outputs.FirstOrDefault(O=>O.Kind==ImageOutputKind.Image)
            :Settings.Outputs.FirstOrDefault(O=>O.Kind==ImageOutputKind.Texture&&O.Usage==Role):Settings.Outputs.FirstOrDefault(O=>O.Key==Explicit.Output.Key&&O.Kind==Explicit.Output.Kind);
        IVsLinkedUndoTransactionManager?Linked=ExistingLinked;
        if(Output is null){
            if(Explicit is not null||Asset.Source.ReadOnly){ throw new InvalidOperationException(VsixText.Get("Assets_ImageAddFirst")); }
            Output=ImageOutputSettings.For(Role);var Base=Output.Key;var Suffix=2;while(Settings.Outputs.Any(O=>O.Key==Output.Key)){ Output.Key=Base+Suffix++; }
            Settings.Outputs=Settings.Outputs.Concat(new[]{Output}).ToArray();
            var Prepared=await Task.Run(()=>AssetAuthoringHostClient.Image(new(){ImageCommand=6,SourcePath=Asset.Source.SourcePath,Settings=Settings,ExpectedMetadataHash=Snapshot.MetadataHash}));
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();if(!Prepared.Success){ throw new InvalidOperationException(Prepared.Diagnostic); }
            Linked=ExistingLinked??Package.GetGlobalService(typeof(SVsLinkedUndoTransactionManager)) as IVsLinkedUndoTransactionManager??throw new InvalidOperationException("Linked native Undo is unavailable.");
            if(ExistingLinked is null){ Linked.OpenLinkedUndo(0,VsixText.Get("Assets_ImageAddAssign")); }
            try {
                var PathValue=Asset.Source.SourcePath+".meta";var Before=AssetDocumentWorkspace.Read(PathValue);
                if(Before!=File.ReadAllText(PathValue)||!AssetDocumentWorkspace.Write(PathValue,Before,Prepared.MetadataJson,VsixText.Get("Assets_ImageAddUndo"))){ throw new InvalidOperationException("Metadata changed before drop."); }
                AssetDocumentWorkspace.Save(PathValue);
            }catch{ Linked.AbortLinkedUndo();throw; }
        }
        var Items=AssetsToolWindowPane.Controller?.Items??[];
        var Names=AssetCatalogLayout.Map(Items.Where(I=>I.Source.ProjectPath==Asset.Source.ProjectPath&&AssetReferencePlanner.InCatalog(I.Source.RelativePath)).Select(I=>I.Source.RelativePath));
        if(!Names.TryGetValue(Asset.Source.RelativePath,out var Name)){ Linked?.AbortLinkedUndo();throw new InvalidOperationException("Asset catalog changed during drop."); }
        return("global::"+Name+"."+ImageOutputDrop.Member(Output!.Key),Linked);
    }
}
