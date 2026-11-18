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
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Args.LeftButton!=MouseButtonState.Pressed||Sender is not FrameworkElement{DataContext:ImageOutputSettings Output}Element){ return; }
        DependencyObject?Parent=Element;
        while(Parent is not null&&Parent is not ListViewItem){ Parent=VisualTreeHelper.GetParent(Parent); }
        if(Parent is not ListViewItem{DataContext:AssetWorkspaceRow Row}){ return; }
        var Data=new DataObject(typeof(AssetWorkspaceItem),Row.Item);Data.SetData(typeof(ImageOutputDrag),new ImageOutputDrag(Row.Item,Output));
        Args.Handled=true;AssetWorkspaceDrag.Run(()=>DragDrop.DoDragDrop(Element,Data,DragDropEffects.Copy));
    }
}
internal static class ImageOutputDrop {
    /// <summary>欄に合う種類の新しい出力。ImageAsset の欄にはテクスチャではなく、ミップを作らない画像の出力を足す。</summary>
    internal static ImageOutputSettings NewOutput(string AssetTypeName,TextureUsage Usage)=>AssetTypeName switch {
        "CSharpGameEngine.Authoring.ImageAsset"=>new(){Key="image",Name="Image",Kind=ImageOutputKind.Image,Mipmaps=false},
        "CSharpGameEngine.Authoring.TextureAsset"=>ImageOutputSettings.For(Usage),
        _=>throw new ArgumentException("Unsupported image output target type.",nameof(AssetTypeName)),
    };
    internal static string Member(string Key)=>string.Concat(Key.Split(new[]{'_'},StringSplitOptions.RemoveEmptyEntries).Select(P=>char.ToUpperInvariant(P[0])+P.Substring(1)));
    internal static bool Accepts(SceneInspectorMember Member,AssetWorkspaceItem Asset,ImageOutputDrag?Explicit){
        // Windows のパスは大文字小文字を区別しないので、シーンを開いたパスの綴りの違いで同じプロジェクトを別物にしない。
        var Context=CgeProjectContext.Resolve(SceneEditorHub.ActiveSceneModel?.FilePath);
        if(Context is null||Asset.Source.ReadOnly&&Asset.ImageSettings is null||!ImageImportSettings.Supports(Asset.Source.SourcePath)
            ||!string.Equals(Asset.Source.ProjectPath,Context.ProjectFilePath,StringComparison.OrdinalIgnoreCase)){ return false; }
        var Kind=Explicit?.Output.Kind??(Asset.Kind==AssetWorkspaceKind.Image?ImageOutputKind.Image:ImageOutputKind.Texture);
        return Member.AssetTypeName switch{
            "CSharpGameEngine.Authoring.TextureAsset"=>Kind==ImageOutputKind.Texture&&(Explicit is null||Member.TextureUsage is null||Explicit.Output.Usage==Member.TextureUsage),
            "CSharpGameEngine.Authoring.ImageAsset"=>Kind==ImageOutputKind.Image,
            _=>false,
        };
    }
    /// <summary>
    /// 欄に入れる生成カタログの名前を返す。出力が無ければ .meta に足して保存し、割り当ての前提(保存した本文とカタログの値)も返す。
    /// <paramref name="MetadataPrepared"/> は .meta を書く直前に呼ぶ(呼び手がシーンの本文が変わっていないことを確かめる)。
    /// </summary>
    internal static async Task<(string Expression,IVsLinkedUndoTransactionManager?Linked,SceneImageOutputPrecondition?Precondition)>PrepareAsync(SceneInspectorMember Member,AssetWorkspaceItem Asset,ImageOutputDrag?Explicit,IVsLinkedUndoTransactionManager?ExistingLinked=null,Action<SceneImageOutputPrecondition>?MetadataPrepared=null){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var Role=Member.TextureUsage??(Member.MemberName.IndexOf("normal",StringComparison.OrdinalIgnoreCase)>=0?TextureUsage.Normal:TextureUsage.Color);
        var Snapshot=await Task.Run(()=>AssetAuthoringHostClient.Image(new(){SourcePath=Asset.Source.SourcePath}));
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if(!Snapshot.Success||Snapshot.Settings is null||Snapshot.MetadataHash.Length==0){ throw new InvalidOperationException(VsixText.Get("Assets_ImageRegisterFirst")); }
        var Settings=Snapshot.Settings;
        var Output=Explicit is null?Member.AssetTypeName=="CSharpGameEngine.Authoring.ImageAsset"?Settings.Outputs.FirstOrDefault(O=>O.Kind==ImageOutputKind.Image)
            :Settings.Outputs.FirstOrDefault(O=>O.Kind==ImageOutputKind.Texture&&O.Usage==Role):Settings.Outputs.FirstOrDefault(O=>O.Key==Explicit.Output.Key&&O.Kind==Explicit.Output.Kind);
        IVsLinkedUndoTransactionManager?Linked=ExistingLinked;
        var OwnsLinked=false;
        var MetadataWritten=false;
        SceneImageOutputPrecondition?Precondition=null;
        string?Reference=null;
        var MetadataPath=Asset.Source.SourcePath+".meta";
        try {
            if(Output is null){
                if(Explicit is not null||Asset.Source.ReadOnly){ throw new InvalidOperationException(VsixText.Get("Assets_ImageAddFirst")); }
                Output=NewOutput(Member.AssetTypeName!,Role);var Base=Output.Key;var Suffix=2;while(Settings.Outputs.Any(O=>O.Key==Output.Key)){ Output.Key=Base+Suffix++; }
                Settings.Outputs=Settings.Outputs.Concat(new[]{Output}).ToArray();
                var Prepared=await Task.Run(()=>AssetAuthoringHostClient.Image(new(){ImageCommand=6,SourcePath=Asset.Source.SourcePath,Settings=Settings,ExpectedMetadataHash=Snapshot.MetadataHash}));
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();if(!Prepared.Success){ throw new InvalidOperationException(Prepared.Diagnostic); }
                Reference=Expression(Asset,Output);
                var Before=AssetDocumentWorkspace.Read(MetadataPath);
                if(!string.Equals(Before,File.ReadAllText(MetadataPath),StringComparison.Ordinal)){ throw new InvalidOperationException("Metadata changed before drop."); }
                Precondition=new SceneImageOutputPrecondition(MetadataPath,Prepared.MetadataJson,Member.AssetTypeName!,Reference,Asset.Source.RelativePath+":"+Uri.EscapeDataString(Output.Key),Before);
                MetadataPrepared?.Invoke(Precondition);
                Linked=ExistingLinked??Package.GetGlobalService(typeof(SVsLinkedUndoTransactionManager)) as IVsLinkedUndoTransactionManager??throw new InvalidOperationException("Linked native Undo is unavailable.");
                if(ExistingLinked is null){ ErrorHandler.ThrowOnFailure(Linked.OpenLinkedUndo(0,VsixText.Get("Assets_ImageAddAssign")));OwnsLinked=true; }
                MetadataWritten=true;
                if(!AssetDocumentWorkspace.Write(MetadataPath,Before,Prepared.MetadataJson,VsixText.Get("Assets_ImageAddUndo"))){ throw new InvalidOperationException("Metadata changed before drop."); }
                if(!string.Equals(File.ReadAllText(MetadataPath),Before,StringComparison.Ordinal)){ throw new InvalidOperationException("Metadata changed before save."); }
                AssetDocumentWorkspace.Save(MetadataPath);
                if(!string.Equals(File.ReadAllText(MetadataPath),Prepared.MetadataJson,StringComparison.Ordinal)){ throw new InvalidOperationException("Image output metadata was not saved."); }
            }
            Reference??=Expression(Asset,Output!);
            return(Reference,Linked,Precondition);
        }catch(Exception Failure){
            // 返すまでは、自分で開いた連動の Undo だけを持つ。呼び手から渡された Undo は呼び手が片付ける。
            if(OwnsLinked){
                try {
                    ErrorHandler.ThrowOnFailure(Linked!.AbortLinkedUndo());
                    if(MetadataWritten){
                        if(Precondition is null||!Precondition.CanPersistRollback(AssetDocumentWorkspace.Read(MetadataPath),File.ReadAllText(MetadataPath))){ throw new InvalidOperationException("Metadata rollback conflicts with the restored buffer or current disk text."); }
                        AssetDocumentWorkspace.Save(MetadataPath);
                        if(!string.Equals(File.ReadAllText(MetadataPath),Precondition.MetadataBefore,StringComparison.Ordinal)){ throw new InvalidOperationException("Restored image metadata was not saved."); }
                    }
                }catch(Exception RollbackFailure){ throw new AggregateException("Image output assignment failed and metadata rollback could not be persisted.",Failure,RollbackFailure); }
            }
            throw;
        }
    }
    private static string Expression(AssetWorkspaceItem Asset,ImageOutputSettings Output){
        var Items=AssetsToolWindowPane.Controller?.Items??[];
        var Names=AssetCatalogLayout.Map(Items.Where(I=>string.Equals(I.Source.ProjectPath,Asset.Source.ProjectPath,StringComparison.OrdinalIgnoreCase)&&AssetReferencePlanner.InCatalog(I.Source.RelativePath)).Select(I=>I.Source.RelativePath));
        if(!Names.TryGetValue(Asset.Source.RelativePath,out var Name)){ throw new InvalidOperationException("Asset catalog changed during drop."); }
        return "global::"+Name+"."+Member(Output.Key);
    }
}
