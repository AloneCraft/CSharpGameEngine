using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
namespace CSharpGameEngine.VSExtension;
internal static class AssetFileTransaction {
    private static readonly IPropertyOwner HistoryContext=new FileUndoContext();
    private static ITextUndoHistory?LastHistory;
    internal static ITextUndoHistory History=>LastHistory??AssetDocumentWorkspace.Histories.RegisterHistory(HistoryContext);
    private sealed class FileUndoContext:IPropertyOwner {
        public PropertyCollection Properties{get;}=new();
    }
    internal static void Execute(AsyncPackage Package,AssetReferencePlan Plan){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Plan.Mutation.Operation is AssetFileOperation.Move or AssetFileOperation.Rename
            &&string.Equals(Plan.Mutation.Source,Plan.Mutation.Destination,StringComparison.Ordinal)&&Plan.Edits.All(static Edit=>Edit.Before==Edit.After)){ return; }
        if(Plan.SolutionVersion is { }Version&&Version!=AssetDocumentWorkspace.Workspace.CurrentSolution.Version){ throw new InvalidOperationException(VsixText.Get("Assets_Conflict")); }
        foreach(var Read in Plan.ValidationReads){ if(AssetDocumentWorkspace.Read(Read.Key)!=Read.Value){ throw new InvalidOperationException(VsixText.Get("Assets_Conflict")); } }
        var Reopen=Plan.Mutation.Operation is AssetFileOperation.Move or AssetFileOperation.Rename or AssetFileOperation.Delete or AssetFileOperation.UpdateMetadata
            &&AssetDocumentWorkspace.CloseCleanDocument(Plan.Mutation.Source);
        if(Plan.Mutation.Operation is AssetFileOperation.Move or AssetFileOperation.Rename or AssetFileOperation.UpdateMetadata
            &&Plan.Edits.FirstOrDefault(Edit=>string.Equals(Edit.Path,Plan.Mutation.Source,StringComparison.OrdinalIgnoreCase)) is { }OwnEdit){
            Plan.Mutation.Content=OwnEdit.After;Plan.Edits.Remove(OwnEdit);
        }
        var Buffers=new List<(AssetReferenceEdit Edit,ITextBuffer Buffer)>();
        foreach(var Edit in Plan.Edits.Where(static Edit=>Edit.Before!=Edit.After)){
            var Buffer=AssetDocumentWorkspace.OpenBuffer(Edit.Path);
            if(Buffer.CurrentSnapshot.GetText()!=Edit.Before){ throw new InvalidOperationException(VsixText.Get("Assets_Conflict")); }
            if((File.GetAttributes(Edit.Path)&FileAttributes.ReadOnly)!=0){ throw new UnauthorizedAccessException(Edit.Path); }
            Buffers.Add((Edit,Buffer));
        }
        var Linked=Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsLinkedUndoTransactionManager)) as IVsLinkedUndoTransactionManager;
        if(Linked is null&&Buffers.Count>1){ throw new InvalidOperationException("Linked native Undo is unavailable."); }
        var Description=VsixText.Get("Assets_Organize")+": "+Plan.Mutation.Operation;
        Linked?.OpenLinkedUndo(0,Description);
        try {
            var History=Buffers.Count>0?AssetDocumentWorkspace.Histories.RegisterHistory(Buffers[0].Buffer):AssetDocumentWorkspace.Histories.RegisterHistory(HistoryContext);
            using(var Transaction=History.CreateTransaction(Description)){
                var Primitive=new FilePrimitive(Plan.Mutation,Plan.WorkspaceRoot,Reopen);
                Primitive.Do();Transaction.AddUndo(Primitive);
                foreach(var Entry in Buffers){
                    if(!new SceneTextBufferWriter(AssetDocumentWorkspace.Histories).TryApply(Entry.Buffer,
                        Microsoft.CodeAnalysis.Text.SourceText.From(Entry.Edit.Before),Microsoft.CodeAnalysis.Text.SourceText.From(Entry.Edit.After),Description)){
                        throw new InvalidOperationException(VsixText.Get("Assets_Conflict"));
                    }
                }
                Primitive.Commit();
                Transaction.Complete();
            }
            LastHistory=History;
            if(Linked is not null){ ErrorHandler.ThrowOnFailure(Linked.CloseLinkedUndo()); }
        }catch { Linked?.AbortLinkedUndo();throw; }
    }
    internal static void Create(AsyncPackage Package,string Destination,string?Text){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Context=CgeProjectContext.Resolve(Path.GetDirectoryName(Destination))??throw new InvalidOperationException("Game project unavailable.");
        var Request=new AssetFileMutationRequest{FileMutationCommand=2,RequestId=Guid.NewGuid(),OperationId=Guid.NewGuid(),Root=Context.ProjectDirectory,MetadataRoot=Context.WorkspaceRoot,
            Destination=Destination,Operation=AssetFileOperation.Create,Content=Text??string.Empty,Directory=Text is null};
        Execute(Package,new AssetReferencePlan{WorkspaceRoot=Context.WorkspaceRoot,Mutation=Request});
    }
    internal static void Copy(AsyncPackage Package,string Source,string Destination,CSharpGameEngine.Contracts.TextureUsage?TargetTextureUsage=null){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Context=CgeProjectContext.Resolve(Path.GetDirectoryName(Destination))??throw new InvalidOperationException("Game project unavailable.");
        var Request=new AssetFileMutationRequest{RequestId=Guid.NewGuid(),OperationId=Guid.NewGuid(),Root=Context.ProjectDirectory,MetadataRoot=Context.WorkspaceRoot,Source=Source,Destination=Destination,Operation=AssetFileOperation.Copy,TargetTextureUsage=TargetTextureUsage};
        var Snapshot=AssetAuthoringHostClient.MutateFiles(Request,Context.WorkspaceRoot);
        Request.ExpectedSourceHash=Snapshot.SourceHash;Request.ExpectedMetadataHash=Snapshot.MetadataHash;Request.FileMutationCommand=2;
        Execute(Package,new AssetReferencePlan{WorkspaceRoot=Context.WorkspaceRoot,Mutation=Request});
    }
    /// <summary>依存ファイルを先に作成し、ルートを最後に公開する。一式を1つのネイティブUndo取引にまとめる。</summary>
    internal static void CreateBundle(AsyncPackage Package,string Destination,IReadOnlyDictionary<string,string>Files){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Context=CgeProjectContext.Resolve(Path.GetDirectoryName(Destination))??throw new InvalidOperationException("Game project unavailable.");
        var Primary=Path.GetFullPath(Destination);
        if(Files.Count==0||!Files.Keys.Any(PathValue=>string.Equals(Path.GetFullPath(PathValue),Primary,StringComparison.OrdinalIgnoreCase))){ throw new ArgumentException("The graph bundle requires its root file."); }
        var Ordered=Files.OrderBy(File=>string.Equals(Path.GetFullPath(File.Key),Primary,StringComparison.OrdinalIgnoreCase)?1:0).ThenBy(File=>File.Key,StringComparer.OrdinalIgnoreCase).ToArray();
        foreach(var File in Ordered){
            var FullPath=Path.GetFullPath(File.Key);
            if(!AssetWorkspaceQuery.IsWithin(FullPath,Context.ProjectDirectory)){ throw new InvalidOperationException("Bundle destination is outside the game project."); }
            if(System.IO.File.Exists(FullPath)||Directory.Exists(FullPath)||System.IO.File.Exists(FullPath+".meta")){ throw new IOException("Bundle destination already exists: "+FullPath); }
        }
        var Linked=Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsLinkedUndoTransactionManager)) as IVsLinkedUndoTransactionManager;
        var Description=VsixText.Get("Assets_Organize")+": Create Graph";
        if(Linked is not null){ ErrorHandler.ThrowOnFailure(Linked.OpenLinkedUndo(0,Description)); }
        try {
            var History=AssetDocumentWorkspace.Histories.RegisterHistory(HistoryContext);
            using(var Transaction=History.CreateTransaction(Description)){
                var Primitives=new List<FilePrimitive>();
                try {
                    foreach(var File in Ordered){
                        var Request=new AssetFileMutationRequest {
                            FileMutationCommand=2,RequestId=Guid.NewGuid(),OperationId=Guid.NewGuid(),Root=Context.ProjectDirectory,
                            MetadataRoot=Context.WorkspaceRoot,Destination=Path.GetFullPath(File.Key),Operation=AssetFileOperation.Create,Content=File.Value,
                        };
                        var Primitive=new FilePrimitive(Request,Context.WorkspaceRoot,Reopen:false);
                        Primitive.Do();
                        try { Transaction.AddUndo(Primitive); }
                        catch { Primitive.Undo();throw; }
                        Primitives.Add(Primitive);
                    }
                    foreach(var Primitive in Primitives){ Primitive.Commit(); }
                } catch {
                    Transaction.Cancel();
                    throw;
                }
                Transaction.Complete();
            }
            LastHistory=History;
            if(Linked is not null){ ErrorHandler.ThrowOnFailure(Linked.CloseLinkedUndo()); }
        } catch { Linked?.AbortLinkedUndo();throw; }
    }
    private sealed class FilePrimitive(AssetFileMutationRequest Request,string WorkspaceRoot,bool Reopen):ITextUndoPrimitive {
        private bool Applied;
        private bool HasRun;
        public bool CanUndo=>this.Applied;
        public bool CanRedo=>!this.Applied;
        public ITextUndoTransaction Parent{get;set;}=null!;
        public void Do(){
            ThreadHelper.ThrowIfNotOnUIThread();
            if(Reopen&&File.Exists(Request.Source)){ AssetDocumentWorkspace.CloseCleanDocument(Request.Source); }
            Request.FileMutationCommand=2;Request.RequestId=Guid.NewGuid();Request.Reverse=false;AssetAuthoringHostClient.MutateFiles(Request,WorkspaceRoot);this.Applied=true;
            if(this.HasRun){ this.Commit(); }this.HasRun=true;
            if(Reopen&&Request.Operation!=AssetFileOperation.Delete){ VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider,Request.Destination); }
        }
        public void Undo(){
            ThreadHelper.ThrowIfNotOnUIThread();
            if(Reopen&&File.Exists(Request.Destination)){ AssetDocumentWorkspace.CloseCleanDocument(Request.Destination); }
            Request.FileMutationCommand=2;Request.RequestId=Guid.NewGuid();Request.Reverse=true;AssetAuthoringHostClient.MutateFiles(Request,WorkspaceRoot);this.Applied=false;
            if(Reopen){ VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider,Request.Source); }
        }
        public bool CanMerge(ITextUndoPrimitive Older)=>false;
        internal void Commit(){ Request.FileMutationCommand=3;Request.RequestId=Guid.NewGuid();AssetAuthoringHostClient.MutateFiles(Request,WorkspaceRoot); }
        public ITextUndoPrimitive Merge(ITextUndoPrimitive Older)=>throw new NotSupportedException();
    }
}
