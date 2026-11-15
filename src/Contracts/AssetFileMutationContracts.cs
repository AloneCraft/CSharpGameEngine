namespace CSharpGameEngine.Contracts;
public enum AssetFileOperation { Inspect,Rename,Move,Duplicate,Delete,Create,Copy,UpdateMetadata }
public sealed class AssetFileMutationRequest {
    public int FileMutationCommand{get;set;}=1;
    public int ProtocolVersion{get;set;}=1;
    public Guid RequestId{get;set;}
    public string Root{get;set;}=string.Empty;
    public string MetadataRoot{get;set;}=string.Empty;
    public TextureUsage?TargetTextureUsage{get;set;}
    public string Source{get;set;}=string.Empty;
    public string Destination{get;set;}=string.Empty;
    public AssetFileOperation Operation{get;set;}
    public string ExpectedSourceHash{get;set;}=string.Empty;
    public string ExpectedMetadataHash{get;set;}=string.Empty;
    public string Content{get;set;}=string.Empty;
    public bool Directory{get;set;}
    public Guid OperationId{get;set;}
    public bool Reverse{get;set;}
}
public sealed class AssetFileMutationResult {
    public int ProtocolVersion{get;set;}=1;
    public Guid RequestId{get;set;}
    public bool Success{get;set;}
    public string Diagnostic{get;set;}=string.Empty;
    public string SourceHash{get;set;}=string.Empty;
    public string MetadataHash{get;set;}=string.Empty;
    public Guid OperationId{get;set;}
    public AssetFileRecoveryInfo[]RecoveryOperations{get;set;}=[];
}
public sealed class AssetFileRecoveryInfo {
    public Guid OperationId{get;set;}
    public string Description{get;set;}=string.Empty;
}
