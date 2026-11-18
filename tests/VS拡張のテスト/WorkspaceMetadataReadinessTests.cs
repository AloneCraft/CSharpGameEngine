using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension.Tests;
[Collection(MockedVsCollection.Name)]
public sealed class WorkspaceMetadataReadinessTests {
    public WorkspaceMetadataReadinessTests(MockedVsFixture Fixture){ }
    private const string MetadataPath=@"C:\Game\Assets\Photo.png.meta";
    [Fact]
    public async Task OldMetadata_DoesNotValidateUntilExpectedTextArrives(){
        using var Workspace=new AdhocWorkspace();
        var Threading=ThreadHelper.JoinableTaskContext;
        var Project=Workspace.AddProject("Game",LanguageNames.CSharp);
        var Metadata=AddMetadata(Workspace,Project.Id,"old");
        using var Stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var Calls=0;
        var Ready=Threading.Factory.RunAsync(()=>WorkspaceMetadataReadiness.WaitAsync(Workspace,Project.Id,MetadataPath,"expected",async(Snapshot,Token)=>{
            Interlocked.Increment(ref Calls);
            Assert.Equal("expected",(await Snapshot.GetAdditionalDocument(Metadata.Id)!.GetTextAsync(Token)).ToString());
            return true;
        },Stop.Token));
        Assert.Equal(0,Calls);
        Assert.False(Ready.IsCompleted);
        Assert.True(Workspace.TryApplyChanges(Workspace.CurrentSolution.WithAdditionalDocumentText(Metadata.Id,SourceText.From("expected"))));
        var Observed=await Ready;
        Assert.Equal(Project.Id,Observed.Id);
        Assert.Equal(1,Calls);
    }
    [Fact]
    public async Task MatchingMetadata_WaitsForCatalogChange(){
        using var Workspace=new AdhocWorkspace();
        var Threading=ThreadHelper.JoinableTaskContext;
        var Project=Workspace.AddProject("Game",LanguageNames.CSharp);
        AddMetadata(Workspace,Project.Id,"expected");
        var Catalog=Workspace.AddDocument(Project.Id,"Assets.g.cs",SourceText.From("old"));
        using var Stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var FirstCheck=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var Ready=Threading.Factory.RunAsync(()=>WorkspaceMetadataReadiness.WaitAsync(Workspace,Project.Id,MetadataPath,"expected",async(Snapshot,Token)=>{
            var Text=await Snapshot.GetDocument(Catalog.Id)!.GetTextAsync(Token);
            FirstCheck.TrySetResult(true);
            return Text.ToString()=="ready";
        },Stop.Token));
        Assert.Same(FirstCheck.Task,await Task.WhenAny(FirstCheck.Task,Ready.Task));
        Assert.False(Ready.IsCompleted);
        Assert.True(Workspace.TryApplyChanges(Workspace.CurrentSolution.WithDocumentText(Catalog.Id,SourceText.From("ready"))));
        Assert.Equal(Project.Id,(await Ready).Id);
    }
    [Fact]
    public async Task SnapshotChangedDuringValidation_RechecksLatestProject(){
        using var Workspace=new AdhocWorkspace();
        var Threading=ThreadHelper.JoinableTaskContext;
        var Project=Workspace.AddProject("Game",LanguageNames.CSharp);
        AddMetadata(Workspace,Project.Id,"expected");
        var Catalog=Workspace.AddDocument(Project.Id,"Assets.g.cs",SourceText.From("old"));
        using var Stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var Entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var Release=new SemaphoreSlim(0,1);
        var Rechecked=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var Ready=Threading.Factory.RunAsync(()=>WorkspaceMetadataReadiness.WaitAsync(Workspace,Project.Id,MetadataPath,"expected",async(Snapshot,Token)=>{
            if(Entered.TrySetResult(true)){ await Release.WaitAsync(Token);return true; }
            Assert.Equal("new",(await Snapshot.GetDocument(Catalog.Id)!.GetTextAsync(Token)).ToString());
            Rechecked.TrySetResult(true);
            return false;
        },Stop.Token));
        Assert.Same(Entered.Task,await Task.WhenAny(Entered.Task,Ready.Task));
        Assert.True(Workspace.TryApplyChanges(Workspace.CurrentSolution.WithDocumentText(Catalog.Id,SourceText.From("new"))));
        Release.Release();
        Assert.Same(Rechecked.Task,await Task.WhenAny(Ready.Task,Rechecked.Task));
        Assert.False(Ready.IsCompleted);
        Stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await Ready);
    }
    [Fact]
    public async Task ProjectRemoved_StopsPendingReadiness(){
        using var Workspace=new AdhocWorkspace();
        var Threading=ThreadHelper.JoinableTaskContext;
        var Project=Workspace.AddProject("Game",LanguageNames.CSharp);
        AddMetadata(Workspace,Project.Id,"old");
        using var Stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var Ready=Threading.Factory.RunAsync(()=>WorkspaceMetadataReadiness.WaitAsync(Workspace,Project.Id,MetadataPath,"expected",(Snapshot,Token)=>Task.FromResult(true),Stop.Token));
        Assert.True(Workspace.TryApplyChanges(Workspace.CurrentSolution.RemoveProject(Project.Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await Ready);
    }
    [Fact]
    public async Task MetadataRemoved_StopsPendingReadiness(){
        using var Workspace=new AdhocWorkspace();
        var Threading=ThreadHelper.JoinableTaskContext;
        var Project=Workspace.AddProject("Game",LanguageNames.CSharp);
        var Metadata=AddMetadata(Workspace,Project.Id,"old");
        using var Stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var Ready=Threading.Factory.RunAsync(()=>WorkspaceMetadataReadiness.WaitAsync(Workspace,Project.Id,MetadataPath,"expected",(Snapshot,Token)=>Task.FromResult(true),Stop.Token));
        Assert.True(Workspace.TryApplyChanges(Workspace.CurrentSolution.RemoveAdditionalDocument(Metadata.Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await Ready);
    }
    private static TextDocument AddMetadata(AdhocWorkspace Workspace,ProjectId ProjectId,string Text){
        var Id=DocumentId.CreateNewId(ProjectId);
        Assert.True(Workspace.TryApplyChanges(Workspace.CurrentSolution.AddAdditionalDocument(Id,"Photo.png.meta",SourceText.From(Text),filePath:MetadataPath)));
        return Workspace.CurrentSolution.GetAdditionalDocument(Id)!;
    }
    [Fact]
    public async Task Cancellation_StopsPendingReadiness(){
        using var Workspace=new AdhocWorkspace();
        var Threading=ThreadHelper.JoinableTaskContext;
        var Project=Workspace.AddProject("Game",LanguageNames.CSharp);
        AddMetadata(Workspace,Project.Id,"old");
        using var Stop=new CancellationTokenSource();
        var Ready=Threading.Factory.RunAsync(()=>WorkspaceMetadataReadiness.WaitAsync(Workspace,Project.Id,MetadataPath,"expected",(Snapshot,Token)=>Task.FromResult(true),Stop.Token));
        Stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await Ready);
    }
}
