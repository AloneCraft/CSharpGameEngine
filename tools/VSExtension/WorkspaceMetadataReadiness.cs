using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.VSExtension;
/// <summary>Observes the existing workspace until one current project contains both saved metadata and its validated catalog.</summary>
internal static class WorkspaceMetadataReadiness {
    internal static async Task<Project>WaitAsync(Workspace Workspace,ProjectId ProjectId,string MetadataPath,string ExpectedText,
        Func<Project,CancellationToken,Task<bool>>Validate,CancellationToken CancellationToken){
        using var Changed=new SemaphoreSlim(0,1);
        var Gate=new object();
        var Finished=false;
        var LastState="WaitingForWorkspaceChange";
        var LastChange="None";
        var Wakes=0;
        var FreshnessRejected=0;
        void Signal(){
            lock(Gate){
                if(!Finished&&Changed.CurrentCount==0){ Changed.Release(); }
            }
        }
        EventHandler<WorkspaceChangeEventArgs>Handler=(_,Args)=>{ LastChange=Args.Kind.ToString();Signal(); };
        Workspace.WorkspaceChanged+=Handler;
        var ObservedMetadata=false;
        try {
            // Subscribe first: a save/project update between registration and this check must also wake the observer.
            Signal();
            while(true){
                await Changed.WaitAsync(CancellationToken).ConfigureAwait(false);
                Wakes++;
                CancellationToken.ThrowIfCancellationRequested();
                var Solution=Workspace.CurrentSolution;
                var Project=Solution.GetProject(ProjectId)??throw new InvalidOperationException("Game project was removed while waiting for its asset catalog.");
                var Documents=Project.AdditionalDocuments.Where(Document=>string.Equals(Document.FilePath,MetadataPath,StringComparison.OrdinalIgnoreCase)).ToArray();
                if(Documents.Length>1){ throw new InvalidOperationException("Image metadata has multiple workspace documents."); }
                if(Documents.Length==0){
                    LastState="MetadataMissing";
                    if(ObservedMetadata){ throw new InvalidOperationException("Image metadata was removed while waiting for its asset catalog."); }
                    continue;
                }
                ObservedMetadata=true;
                LastState="ReadingMetadata";
                var Text=await Documents[0].GetTextAsync(CancellationToken).ConfigureAwait(false);
                if(!string.Equals(Text.ToString(),ExpectedText,StringComparison.Ordinal)){ LastState="MetadataTextMismatch expectedLength="+ExpectedText.Length+" actualLength="+Text.Length;continue; }
                LastState="ValidatingCatalog";
                if(!await Validate(Project,CancellationToken).ConfigureAwait(false)){ LastState="ValidationRejected";continue; }
                CancellationToken.ThrowIfCancellationRequested();
                if(ReferenceEquals(Solution,Workspace.CurrentSolution)){ return Project; }
                // Never approve an obsolete compilation, even if its validation completed after the newer event.
                FreshnessRejected++;LastState="SolutionChanged";
                Signal();
            }
        }catch(OperationCanceledException Error){
            Error.Data["MetadataReadiness"]=LastState+"; wakes="+Wakes+"; lastChange="+LastChange+"; freshnessRejected="+FreshnessRejected;
            throw;
        }finally {
            Workspace.WorkspaceChanged-=Handler;
            lock(Gate){ Finished=true; }
        }
    }
}
