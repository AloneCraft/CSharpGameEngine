using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Content.Tests;
public sealed class AssetFileMutationTests:IDisposable {
    private readonly string Root=Path.Combine(Path.GetTempPath(),"cge-asset-mutation-"+Guid.NewGuid().ToString("N"));
    private AssetFileMutationRequest Plan(AssetFileOperation Operation,string Source,string Destination){
        var Request=new AssetFileMutationRequest{RequestId=Guid.NewGuid(),Root=this.Root,Source=Source,Destination=Destination,Operation=Operation,OperationId=Guid.NewGuid()};
        var Snapshot=AssetFileMutationService.Execute(Request);Assert.True(Snapshot.Success,Snapshot.Diagnostic);
        Request.ExpectedSourceHash=Snapshot.SourceHash;Request.ExpectedMetadataHash=Snapshot.MetadataHash;Request.FileMutationCommand=2;return Request;
    }
    private string CreateSource(){
        var Source=Path.Combine(this.Root,"Assets","Ship.glb");Directory.CreateDirectory(Path.GetDirectoryName(Source)!);File.WriteAllText(Source,"mesh");
        File.WriteAllBytes(Source+".meta",AssetMetadata.Create(AssetId.Parse(Guid.NewGuid().ToString("D")),"Assets/Ship.glb","SharpGLTF").ToCanonicalUtf8());return Source;
    }
    [Fact]
    public void ReviewedMetadataEditPreservesIdentityAndSupportsUndoRedo(){
        var Source=this.CreateSource();var Meta=Source+".meta";var Before=File.ReadAllBytes(Meta);
        AssetMetadata.TryParse(Before,out var Original,out _);
        var Settings=new ModelImportSettings{Animations=false};
        var After=AssetMetadata.Create(Original!.AssetId,Original.SourceUri,Original.Importer,Settings.ToOptions()).ToCanonicalUtf8();
        var Request=this.Plan(AssetFileOperation.UpdateMetadata,Meta,Meta);Request.Content=System.Text.Encoding.UTF8.GetString(After);
        var Result=AssetFileMutationService.Execute(Request);Assert.True(Result.Success,Result.Diagnostic);Assert.Equal(After,File.ReadAllBytes(Meta));
        Request.Reverse=true;Result=AssetFileMutationService.Execute(Request);Assert.True(Result.Success,Result.Diagnostic);Assert.Equal(Before,File.ReadAllBytes(Meta));
        Request.Reverse=false;Assert.True(AssetFileMutationService.Execute(Request).Success);Assert.Equal(After,File.ReadAllBytes(Meta));
        var Invalid=this.Plan(AssetFileOperation.UpdateMetadata,Meta,Meta);Invalid.Content=System.Text.Encoding.UTF8.GetString(AssetMetadata.Create(AssetId.Parse(Guid.NewGuid().ToString("D")),Original.SourceUri,Original.Importer).ToCanonicalUtf8());
        Assert.False(AssetFileMutationService.Execute(Invalid).Success);Assert.Equal(After,File.ReadAllBytes(Meta));
    }
    [Fact]
    public void MovePreservesIdentityAndNativeUndoRequestRestoresExactSidecar(){
        var Source=this.CreateSource();var Bytes=File.ReadAllBytes(Source+".meta");var Destination=Path.Combine(this.Root,"Assets","Moved.glb");
        var Request=this.Plan(AssetFileOperation.Move,Source,Destination);var Result=AssetFileMutationService.Execute(Request);
        Assert.True(Result.Success,Result.Diagnostic);Assert.False(File.Exists(Source));Assert.Equal("mesh",File.ReadAllText(Destination));
        Assert.True(AssetMetadata.TryParse(File.ReadAllBytes(Destination+".meta"),out var After,out _));Assert.Equal("Assets/Moved.glb",After!.SourceUri);
        Assert.True(AssetMetadata.TryParse(Bytes,out var Before,out _));Assert.Equal(Before!.AssetId,After.AssetId);
        Request.Reverse=true;Result=AssetFileMutationService.Execute(Request);Assert.True(Result.Success,Result.Diagnostic);
        Assert.Equal(Bytes,File.ReadAllBytes(Source+".meta"));Assert.False(File.Exists(Destination));
        Request.Reverse=false;Assert.True(AssetFileMutationService.Execute(Request).Success);
    }
    [Fact]
    public void DuplicateAllocatesNewIdentityWithoutMutatingOriginal(){
        var Source=this.CreateSource();var Bytes=File.ReadAllBytes(Source+".meta");var Destination=Path.Combine(this.Root,"Assets","Copy.glb");
        var Request=this.Plan(AssetFileOperation.Duplicate,Source,Destination);var Result=AssetFileMutationService.Execute(Request);
        Assert.True(Result.Success,Result.Diagnostic);Assert.Equal(Bytes,File.ReadAllBytes(Source+".meta"));
        AssetMetadata.TryParse(Bytes,out var Before,out _);AssetMetadata.TryParse(File.ReadAllBytes(Destination+".meta"),out var After,out _);
        Assert.NotEqual(Before!.AssetId,After!.AssetId);
        Request.Reverse=true;Assert.True(AssetFileMutationService.Execute(Request).Success);Assert.False(File.Exists(Destination));Assert.True(File.Exists(Source));
    }
    [Fact]
    public void CopyFromReadOnlyProviderCreatesEditableProjectSource(){
        var Source=this.CreateSource();File.SetAttributes(Source,File.GetAttributes(Source)|FileAttributes.ReadOnly);
        try {
            var Request=this.Plan(AssetFileOperation.Copy,Source,Path.Combine(this.Root,"Assets","Editable.glb"));
            var Result=AssetFileMutationService.Execute(Request);Assert.True(Result.Success,Result.Diagnostic);
            Assert.Equal(0,(int)(File.GetAttributes(Request.Destination)&FileAttributes.ReadOnly));
            Assert.NotEqual(0,(int)(File.GetAttributes(Source)&FileAttributes.ReadOnly));
            Request.Reverse=true;Result=AssetFileMutationService.Execute(Request);Assert.True(Result.Success,Result.Diagnostic);Assert.False(File.Exists(Request.Destination));
        }finally {
            File.SetAttributes(Source,FileAttributes.Normal);
            var Destination=Path.Combine(this.Root,"Assets","Editable.glb");if(File.Exists(Destination)){ File.SetAttributes(Destination,FileAttributes.Normal); }
        }
    }
    [Fact]
    public void ConflictAndPathEscapeFailBeforeChangingSource(){
        var Source=this.CreateSource();var Destination=Path.Combine(this.Root,"Assets","Moved.glb");var Request=this.Plan(AssetFileOperation.Move,Source,Destination);
        File.AppendAllText(Source," changed");Assert.False(AssetFileMutationService.Execute(Request).Success);Assert.False(File.Exists(Destination));
        Request.Destination=Path.Combine(this.Root,"..","escaped.glb");Assert.False(AssetFileMutationService.Execute(Request).Success);Assert.True(File.Exists(Source));
    }
    [Fact]
    public void DeletionIsRecoverableAndUndoRejectsChangedOutputs(){
        var Source=this.CreateSource();var Request=this.Plan(AssetFileOperation.Delete,Source,string.Empty);var Result=AssetFileMutationService.Execute(Request);
        Assert.True(Result.Success,Result.Diagnostic);Assert.False(File.Exists(Source));Request.Reverse=true;
        Assert.True(AssetFileMutationService.Execute(Request).Success);Assert.True(File.Exists(Source+".meta"));
        Request=this.Plan(AssetFileOperation.Move,Source,Path.Combine(this.Root,"Assets","Moved.glb"));Assert.True(AssetFileMutationService.Execute(Request).Success);
        File.AppendAllText(Request.Destination," changed");Request.Reverse=true;Assert.False(AssetFileMutationService.Execute(Request).Success);Assert.False(File.Exists(Source));
    }
    [Fact]
    public void UndoRejectsAnOperationKindThatDoesNotMatchItsJournal(){
        var Source=this.CreateSource();var Request=this.Plan(AssetFileOperation.Move,Source,Path.Combine(this.Root,"Assets","Moved.glb"));
        Assert.True(AssetFileMutationService.Execute(Request).Success);
        Request.Operation=AssetFileOperation.Copy;Request.Reverse=true;
        var Result=AssetFileMutationService.Execute(Request);Assert.False(Result.Success);Assert.False(File.Exists(Source));Assert.Equal("mesh",File.ReadAllText(Request.Destination));
    }
    [Fact]
    public void CreateAndUndoDoNotOverwriteExistingFiles(){
        Directory.CreateDirectory(this.Root);var Request=this.Plan(AssetFileOperation.Create,string.Empty,Path.Combine(this.Root,"Assets","New.cgeasset"));Request.Content="{}";
        Assert.True(AssetFileMutationService.Execute(Request).Success);Assert.False(AssetFileMutationService.Execute(Request).Success);
        Request.Reverse=true;Assert.True(AssetFileMutationService.Execute(Request).Success);Assert.False(File.Exists(Request.Destination));
    }
    [Fact]
    public void CaseOnlyRenameAndUndoKeepMetadataAndFilenameConsistent(){
        var Source=this.CreateSource();var Destination=Path.Combine(this.Root,"Assets","SHIP.glb");var Bytes=File.ReadAllBytes(Source+".meta");
        var Request=this.Plan(AssetFileOperation.Rename,Source,Destination);var Result=AssetFileMutationService.Execute(Request);
        Assert.True(Result.Success,Result.Diagnostic);Assert.Contains("SHIP.glb",Directory.GetFiles(Path.Combine(this.Root,"Assets")).Select(Path.GetFileName));
        Request.Reverse=true;Result=AssetFileMutationService.Execute(Request);Assert.True(Result.Success,Result.Diagnostic);Assert.Equal(Bytes,File.ReadAllBytes(Source+".meta"));
    }
    [Fact]
    public void DuplicateRedoKeepsItsAllocatedIdentity(){
        var Source=this.CreateSource();var Request=this.Plan(AssetFileOperation.Duplicate,Source,Path.Combine(this.Root,"Assets","Copy.glb"));
        Assert.True(AssetFileMutationService.Execute(Request).Success);var Bytes=File.ReadAllBytes(Request.Destination+".meta");
        Request.Reverse=true;Assert.True(AssetFileMutationService.Execute(Request).Success);
        Request.Reverse=false;Assert.True(AssetFileMutationService.Execute(Request).Success);Assert.Equal(Bytes,File.ReadAllBytes(Request.Destination+".meta"));
    }
    [Fact]
    public void PendingPhysicalOperationCanBeRecoveredButCommittedOperationIsNotOffered(){
        var Source=this.CreateSource();var Request=this.Plan(AssetFileOperation.Move,Source,Path.Combine(this.Root,"Assets","Moved.glb"));
        Assert.True(AssetFileMutationService.Execute(Request).Success);
        var Pending=AssetFileMutationService.Execute(new(){FileMutationCommand=4,RequestId=Guid.NewGuid(),Root=this.Root});
        Assert.Equal(Request.OperationId,Assert.Single(Pending.RecoveryOperations).OperationId);
        var Recovered=AssetFileMutationService.Execute(new(){FileMutationCommand=5,RequestId=Guid.NewGuid(),Root=this.Root,OperationId=Request.OperationId});
        Assert.True(Recovered.Success,Recovered.Diagnostic);Assert.True(File.Exists(Source));
        Assert.True(AssetFileMutationService.Execute(Request).Success);Request.FileMutationCommand=3;
        Assert.True(AssetFileMutationService.Execute(Request).Success);
        Pending=AssetFileMutationService.Execute(new(){FileMutationCommand=4,RequestId=Guid.NewGuid(),Root=this.Root});Assert.Empty(Pending.RecoveryOperations);
    }
    [Fact]
    public void ProjectMetadataKeepsItsWorkspaceRelativeSourceUriAfterMove(){
        var Source=this.CreateSource();var Workspace=Path.GetDirectoryName(this.Root)!;
        File.WriteAllBytes(Source+".meta",AssetMetadata.Create(AssetId.Parse(Guid.NewGuid().ToString("D")),Path.GetRelativePath(Workspace,Source).Replace('\\','/'),"SharpGLTF").ToCanonicalUtf8());
        var Request=this.Plan(AssetFileOperation.Move,Source,Path.Combine(this.Root,"Assets","Moved.glb"));Request.MetadataRoot=Workspace;
        var Result=AssetFileMutationService.Execute(Request);Assert.True(Result.Success,Result.Diagnostic);
        AssetMetadata.TryParse(File.ReadAllBytes(Request.Destination+".meta"),out var Metadata,out _);Assert.Equal(Path.GetRelativePath(Workspace,Request.Destination).Replace('\\','/'),Metadata!.SourceUri);
    }
    [Fact]
    public void ExternalGltfDependenciesCannotBeSilentlyBrokenByMovingTheModel(){
        var Source=Path.Combine(this.Root,"Assets","Ship.gltf");Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
        File.WriteAllText(Source,"""{"buffers":[{"uri":"Ship.bin","byteLength":4}]}""");File.WriteAllText(Path.Combine(this.Root,"Assets","Ship.bin"),"mesh");
        var Request=this.Plan(AssetFileOperation.Move,Source,Path.Combine(this.Root,"Assets","Other","Ship.gltf"));var Result=AssetFileMutationService.Execute(Request);
        Assert.False(Result.Success);Assert.Contains("ASSET_DEPENDENCY_RELOCATION_REQUIRED",Result.Diagnostic);Assert.True(File.Exists(Source));
    }
    private AssetFileMutationResult Recover(AssetFileMutationRequest Request)=>AssetFileMutationService.Execute(new(){FileMutationCommand=5,RequestId=Guid.NewGuid(),Root=this.Root,OperationId=Request.OperationId});
    [Fact]
    public void InterruptedCaseOnlyRenameRestoresOriginalCasingAndMetadata(){
        var Source=this.CreateSource();var Before=File.ReadAllBytes(Source+".meta");
        var Request=this.Plan(AssetFileOperation.Rename,Source,Path.Combine(this.Root,"Assets","SHIP.glb"));
        Assert.True(AssetFileMutationService.Execute(Request).Success);
        var Result=this.Recover(Request);Assert.True(Result.Success,Result.Diagnostic);
        Assert.Contains("Ship.glb",Directory.GetFiles(Path.GetDirectoryName(Source)!).Select(Path.GetFileName));
        Assert.Equal(Before,File.ReadAllBytes(Source+".meta"));
    }
    [Fact]
    public void InterruptedMoveBeforeMetadataUpdateRestoresOnlyItsOwnFiles(){
        var Source=this.CreateSource();var Before=File.ReadAllBytes(Source+".meta");
        var Request=this.Plan(AssetFileOperation.Move,Source,Path.Combine(this.Root,"Assets","Moved.glb"));
        Assert.True(AssetFileMutationService.Execute(Request).Success);
        File.Delete(Request.Destination+".meta");File.WriteAllBytes(Source+".meta",Before);
        var Result=this.Recover(Request);Assert.True(Result.Success,Result.Diagnostic);
        Assert.Equal("mesh",File.ReadAllText(Source));Assert.Equal(Before,File.ReadAllBytes(Source+".meta"));Assert.False(File.Exists(Request.Destination));
    }
    [Fact]
    public void RecoveryRejectsRecreatedOriginalEvenWhenItsBytesMatch(){
        var Source=this.CreateSource();var Before=File.ReadAllBytes(Source+".meta");
        var Request=this.Plan(AssetFileOperation.Move,Source,Path.Combine(this.Root,"Assets","Moved.glb"));
        Assert.True(AssetFileMutationService.Execute(Request).Success);
        File.WriteAllText(Source,"mesh");File.WriteAllBytes(Source+".meta",Before);
        var Result=this.Recover(Request);Assert.False(Result.Success);Assert.Equal("mesh",File.ReadAllText(Request.Destination));Assert.Equal(Before,File.ReadAllBytes(Source+".meta"));
    }
    [Fact]
    public void RecoveryRestoresSourceFromCaseRenameIntermediateFile(){
        var Source=this.CreateSource();var Before=File.ReadAllBytes(Source+".meta");
        var Request=this.Plan(AssetFileOperation.Rename,Source,Path.Combine(this.Root,"Assets","SHIP.glb"));
        Assert.True(AssetFileMutationService.Execute(Request).Success);
        var Recovery=Path.Combine(this.Root,".vs","CgeAssetOperations",Request.OperationId.ToString("N"));
        File.Move(Request.Destination,Path.Combine(Recovery,"case-rename"));
        File.Delete(Request.Destination+".meta");File.WriteAllBytes(Source+".meta",Before);
        var Result=this.Recover(Request);Assert.True(Result.Success,Result.Diagnostic);Assert.Equal("mesh",File.ReadAllText(Source));Assert.Equal(Before,File.ReadAllBytes(Source+".meta"));
    }
    public void Dispose(){ if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); } }
}
