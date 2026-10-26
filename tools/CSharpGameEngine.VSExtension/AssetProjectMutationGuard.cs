using System;
using System.Linq;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension;
/// <summary>生のプロジェクト変更がアセット参照計画を迂回するのを防ぐ。</summary>
internal sealed class AssetProjectMutationGuard:IVsTrackProjectDocumentsEvents2,IDisposable {
    private readonly AssetWorkspaceController Controller;
    private readonly IVsTrackProjectDocuments2 Tracker;
    private uint Cookie;
    private AssetProjectMutationGuard(AssetWorkspaceController Controller,IVsTrackProjectDocuments2 Tracker){ this.Controller=Controller;this.Tracker=Tracker; }
    internal static AssetProjectMutationGuard?Start(AssetWorkspaceController Controller){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Package.GetGlobalService(typeof(SVsTrackProjectDocuments)) is not IVsTrackProjectDocuments2 Tracker){ return null; }
        var Guard=new AssetProjectMutationGuard(Controller,Tracker);
        ErrorHandler.ThrowOnFailure(Tracker.AdviseTrackProjectDocumentsEvents(Guard,out Guard.Cookie));return Guard;
    }
    private bool Touches(string PathValue,bool Directory)=>this.Controller.Items.Any(Item=>Directory?AssetWorkspaceQuery.IsWithin(Item.Source.SourcePath,PathValue):string.Equals(Item.Source.SourcePath,PathValue,StringComparison.OrdinalIgnoreCase));
    private void Route(string Source,string Destination,AssetFileOperation Operation){
        var Item=this.Controller.Items.FirstOrDefault(Value=>string.Equals(Value.Source.SourcePath,Source,StringComparison.OrdinalIgnoreCase));
        if(Item is null){ this.Controller.Report(VsixText.Get("Assets_DirectoryGuard"));return; }
        this.Controller.Run(async()=>{
            var Plan=await AssetReferencePlanner.PlanAsync(Item,Destination,Operation,this.Controller.Owner.DisposalToken);
            await this.Controller.Owner.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(new AssetChangeReviewDialog(Plan).ShowDialog()==true){ AssetFileTransaction.Execute(this.Controller.Owner,Plan);await this.Controller.RefreshAsync(); }
        });
    }
    public int OnQueryAddFiles(IVsProject P0,Int32 P1,String[] P2,VSQUERYADDFILEFLAGS[] P3,VSQUERYADDFILERESULTS[] P4,VSQUERYADDFILERESULTS[] P5){
        return VSConstants.S_OK;
    }
    public int OnAfterAddFilesEx(Int32 P0,Int32 P1,IVsProject[] P2,Int32[] P3,String[] P4,VSADDFILEFLAGS[] P5){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Controller.SolutionChanged();
        return VSConstants.S_OK;
    }
    public int OnAfterAddDirectoriesEx(Int32 P0,Int32 P1,IVsProject[] P2,Int32[] P3,String[] P4,VSADDDIRECTORYFLAGS[] P5){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Controller.SolutionChanged();
        return VSConstants.S_OK;
    }
    public int OnAfterRemoveFiles(Int32 P0,Int32 P1,IVsProject[] P2,Int32[] P3,String[] P4,VSREMOVEFILEFLAGS[] P5){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Controller.SolutionChanged();
        return VSConstants.S_OK;
    }
    public int OnAfterRemoveDirectories(Int32 P0,Int32 P1,IVsProject[] P2,Int32[] P3,String[] P4,VSREMOVEDIRECTORYFLAGS[] P5){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Controller.SolutionChanged();
        return VSConstants.S_OK;
    }
    public int OnQueryRenameFiles(IVsProject P0,Int32 P1,String[] P2,String[] P3,VSQUERYRENAMEFILEFLAGS[] P4,VSQUERYRENAMEFILERESULTS[] P5,VSQUERYRENAMEFILERESULTS[] P6){
        ThreadHelper.ThrowIfNotOnUIThread();
        for(var Index=0;Index<P1;Index++){
            if(!this.Touches(P2[Index],false)){ continue; }
            if(P5.Length>0){ P5[0]=VSQUERYRENAMEFILERESULTS.VSQUERYRENAMEFILERESULTS_RenameNotOK; }
            if(P6.Length>Index){ P6[Index]=VSQUERYRENAMEFILERESULTS.VSQUERYRENAMEFILERESULTS_RenameNotOK; }
            this.Route(P2[Index],P3[Index],AssetFileOperation.Rename);
        }
        return VSConstants.S_OK;
    }
    public int OnAfterRenameFiles(Int32 P0,Int32 P1,IVsProject[] P2,Int32[] P3,String[] P4,String[] P5,VSRENAMEFILEFLAGS[] P6){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Controller.SolutionChanged();
        return VSConstants.S_OK;
    }
    public int OnQueryRenameDirectories(IVsProject P0,Int32 P1,String[] P2,String[] P3,VSQUERYRENAMEDIRECTORYFLAGS[] P4,VSQUERYRENAMEDIRECTORYRESULTS[] P5,VSQUERYRENAMEDIRECTORYRESULTS[] P6){
        ThreadHelper.ThrowIfNotOnUIThread();
        for(var Index=0;Index<P1;Index++){
            if(!this.Touches(P2[Index],true)){ continue; }
            if(P5.Length>0){ P5[0]=VSQUERYRENAMEDIRECTORYRESULTS.VSQUERYRENAMEDIRECTORYRESULTS_RenameNotOK; }
            if(P6.Length>Index){ P6[Index]=VSQUERYRENAMEDIRECTORYRESULTS.VSQUERYRENAMEDIRECTORYRESULTS_RenameNotOK; }
            this.Controller.Report(VsixText.Get("Assets_DirectoryGuard"));
        }
        return VSConstants.S_OK;
    }
    public int OnAfterRenameDirectories(Int32 P0,Int32 P1,IVsProject[] P2,Int32[] P3,String[] P4,String[] P5,VSRENAMEDIRECTORYFLAGS[] P6){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Controller.SolutionChanged();
        return VSConstants.S_OK;
    }
    public int OnQueryAddDirectories(IVsProject P0,Int32 P1,String[] P2,VSQUERYADDDIRECTORYFLAGS[] P3,VSQUERYADDDIRECTORYRESULTS[] P4,VSQUERYADDDIRECTORYRESULTS[] P5){
        return VSConstants.S_OK;
    }
    public int OnQueryRemoveFiles(IVsProject P0,Int32 P1,String[] P2,VSQUERYREMOVEFILEFLAGS[] P3,VSQUERYREMOVEFILERESULTS[] P4,VSQUERYREMOVEFILERESULTS[] P5){
        ThreadHelper.ThrowIfNotOnUIThread();
        for(var Index=0;Index<P1;Index++){
            if(!this.Touches(P2[Index],false)){ continue; }
            if(P4.Length>0){ P4[0]=VSQUERYREMOVEFILERESULTS.VSQUERYREMOVEFILERESULTS_RemoveNotOK; }
            if(P5.Length>Index){ P5[Index]=VSQUERYREMOVEFILERESULTS.VSQUERYREMOVEFILERESULTS_RemoveNotOK; }
            this.Route(P2[Index],string.Empty,AssetFileOperation.Delete);
        }
        return VSConstants.S_OK;
    }
    public int OnQueryRemoveDirectories(IVsProject P0,Int32 P1,String[] P2,VSQUERYREMOVEDIRECTORYFLAGS[] P3,VSQUERYREMOVEDIRECTORYRESULTS[] P4,VSQUERYREMOVEDIRECTORYRESULTS[] P5){
        ThreadHelper.ThrowIfNotOnUIThread();
        for(var Index=0;Index<P1;Index++){
            if(!this.Touches(P2[Index],true)){ continue; }
            if(P4.Length>0){ P4[0]=VSQUERYREMOVEDIRECTORYRESULTS.VSQUERYREMOVEDIRECTORYRESULTS_RemoveNotOK; }
            if(P5.Length>Index){ P5[Index]=VSQUERYREMOVEDIRECTORYRESULTS.VSQUERYREMOVEDIRECTORYRESULTS_RemoveNotOK; }
            this.Controller.Report(VsixText.Get("Assets_DirectoryGuard"));
        }
        return VSConstants.S_OK;
    }
    public int OnAfterSccStatusChanged(Int32 P0,Int32 P1,IVsProject[] P2,Int32[] P3,String[] P4,UInt32[] P5){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Controller.SolutionChanged();
        return VSConstants.S_OK;
    }
    public void Dispose(){ ThreadHelper.ThrowIfNotOnUIThread();this.Tracker.UnadviseTrackProjectDocumentsEvents(this.Cookie); }
}
