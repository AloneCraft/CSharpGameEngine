namespace CSharpGameEngine.Contracts;

public static class AssetAuthoringProtocol {
    public const int CurrentVersion=1;
    public const int MaximumOptions=1024;
    public const int MaximumDiagnostics=256;
    public const int MaximumPathLength=32_768;
    public const int MaximumTextLength=4_096;

    public static string?Validate(AssetAuthoringRequest Request){
        if(Request is null){ return "Asset authoring request is required."; }
        if(Request.ProtocolVersion!=CurrentVersion){ return $"Unsupported asset authoring protocol {Request.ProtocolVersion}; expected {CurrentVersion}."; }
        if(Request.RequestId==Guid.Empty){ return "Asset authoring RequestId is required."; }
        if(!Enum.IsDefined(typeof(AssetAuthoringCommand),Request.Command)){ return "Asset authoring command is unknown."; }
        if(!IsPath(Request.RepositoryRoot)){ return "Asset authoring repository root is invalid."; }
        if(!IsPath(Request.SourcePath)){ return "Asset authoring source path is invalid."; }
        AssetAuthoringOption[]Options=Request.Options??[];
        string?OptionsError=ValidateOptions(Options);
        if(OptionsError is not null){ return OptionsError; }
        if(Request.Command==AssetAuthoringCommand.MigrateLegacyMetadata){
            if(!IsSha256(Request.ExpectedMetadataSha256)||Request.AssetId.Length!=0||Request.Importer.Length!=0||Options.Length!=0){ return "Legacy migration requires only the inspected metadata SHA-256."; }
            return null;
        }
        if(Request.Command==AssetAuthoringCommand.Inspect){
            if(Request.AssetId.Length!=0||Request.Importer.Length!=0||Request.ExpectedMetadataSha256.Length!=0||Options.Length!=0){ return "Inspect cannot carry mutable metadata fields."; }
            return null;
        }
        if(!AssetId.TryParse(Request.AssetId,out var Asset)||!Asset.IsValid){ return "SaveMetadata requires a valid AssetId."; }
        if(string.IsNullOrWhiteSpace(Request.Importer)||Request.Importer.Length>MaximumTextLength){ return "SaveMetadata requires a bounded importer identity."; }
        if(Request.ExpectedMetadataSha256.Length!=0&&!IsSha256(Request.ExpectedMetadataSha256)){ return "Expected metadata SHA-256 is invalid."; }
        return null;
    }

    public static string?Validate(AssetAuthoringRequest Request,AssetAuthoringResult Result){
        string?RequestError=Validate(Request);
        if(RequestError is not null){ return RequestError; }
        if(Result is null){ return "Asset authoring result is required."; }
        if(Result.ProtocolVersion!=CurrentVersion){ return $"Unsupported asset authoring result protocol {Result.ProtocolVersion}."; }
        if(Result.RequestId!=Request.RequestId){ return "Asset authoring result RequestId does not match the request."; }
        if(!Enum.IsDefined(typeof(AssetAuthoringState),Result.State)){ return "Asset authoring result state is unknown."; }
        if(Result.Success!=(Result.State is AssetAuthoringState.Ready or AssetAuthoringState.MissingMetadata)){ return "Asset authoring result success disagrees with its state."; }
        if(Result.Diagnostics is null||Result.Options is null){ return "Asset authoring result collections are required."; }
        string?ResultOptionsError=ValidateOptions(Result.Options);
        if(ResultOptionsError is not null){ return ResultOptionsError; }
        if(Result.Diagnostics.Length>MaximumDiagnostics){ return "Asset authoring result has too many diagnostics."; }
        foreach(AuthoringHostDiagnostic?Diagnostic in Result.Diagnostics){
            if(Diagnostic is null||string.IsNullOrWhiteSpace(Diagnostic.Code)
                ||Diagnostic.Code.Length>MaximumTextLength||Diagnostic.Message is null
                ||Diagnostic.Message.Length>MaximumTextLength){
                return "Asset authoring result has an invalid diagnostic.";
            }
        }
        if(Result.State is AssetAuthoringState.Ready or AssetAuthoringState.MissingMetadata){
            if(!AssetId.TryParse(Result.AssetId,out var Asset)||!Asset.IsValid){ return "Successful asset authoring result has an invalid AssetId."; }
            if(Result.AssetUri!="asset://"+Result.AssetId){ return "Successful asset authoring result has a non-canonical Asset URI."; }
            if(!IsBoundedText(Result.SourceUri)||!IsPath(Result.MetadataPath)
                ||!IsBoundedText(Result.Importer)){
                return "Successful asset authoring result is missing or has invalid identity fields.";
            }
            if(Result.State==AssetAuthoringState.Ready&&!IsSha256(Result.MetadataSha256)){ return "Ready asset authoring result has an invalid metadata SHA-256."; }
            if(Result.State==AssetAuthoringState.MissingMetadata&&Result.MetadataSha256.Length!=0){ return "Missing metadata result cannot claim a metadata SHA-256."; }
        }
        if(Result.State==AssetAuthoringState.Rejected&&!Result.Diagnostics.Any(
            static Diagnostic=>Diagnostic.Severity==AuthoringHostDiagnosticSeverity.Error)){
            return "Rejected asset authoring result requires an error diagnostic.";
        }
        return null;
    }

    private static string?ValidateOptions(AssetAuthoringOption[]Options){
        if(Options.Length>MaximumOptions){ return $"Asset authoring options exceed {MaximumOptions}."; }
        var Names=new HashSet<string>(StringComparer.Ordinal);
        foreach(AssetAuthoringOption?Option in Options){
            if(Option is null||!IsBoundedText(Option.Name)){
                return "Asset authoring option names must be non-empty and bounded.";
            }
            if(Option.Value is null||Option.Value.Length>MaximumTextLength){
                return $"Asset authoring option {Option.Name} value is invalid.";
            }
            if(!Names.Add(Option.Name)){ return $"Asset authoring option {Option.Name} is duplicated."; }
        }
        return null;
    }

    private static bool IsBoundedText(string?Value)=>
        Value is not null&&!string.IsNullOrWhiteSpace(Value)&&Value.Length<=MaximumTextLength;
    private static bool IsPath(string?Value)=>Value is not null&&!string.IsNullOrWhiteSpace(Value)&&Value.Length<=MaximumPathLength&&Value.IndexOf('\0')<0;
    private static bool IsSha256(string?Value)=>Value?.Length==64&&Value.All(static Character=>(Character>='0'&&Character<='9')||(Character>='a'&&Character<='f'));
}

public enum AssetAuthoringCommand:byte { Inspect,SaveMetadata,MigrateLegacyMetadata }
public enum AssetAuthoringState:byte { MissingMetadata,Ready,Rejected }

public sealed class AssetAuthoringOption {
    public string Name{get;set;}=string.Empty;
    public string Value{get;set;}=string.Empty;
}

public sealed class AssetAuthoringRequest {
    public int ProtocolVersion{get;set;}=AssetAuthoringProtocol.CurrentVersion;
    public Guid RequestId{get;set;}
    public AssetAuthoringCommand Command{get;set;}
    public string RepositoryRoot{get;set;}=string.Empty;
    public string SourcePath{get;set;}=string.Empty;
    public string ExpectedMetadataSha256{get;set;}=string.Empty;
    public string AssetId{get;set;}=string.Empty;
    public string Importer{get;set;}=string.Empty;
    public AssetAuthoringOption[]Options{get;set;}=[];
}

public sealed class AssetAuthoringResult {
    public int ProtocolVersion{get;set;}=AssetAuthoringProtocol.CurrentVersion;
    public Guid RequestId{get;set;}
    public AssetAuthoringState State{get;set;}
    public bool Success{get;set;}
    public string SourceUri{get;set;}=string.Empty;
    public string MetadataPath{get;set;}=string.Empty;
    public string AssetId{get;set;}=string.Empty;
    public string AssetUri{get;set;}=string.Empty;
    public string Importer{get;set;}=string.Empty;
    public AssetAuthoringOption[]Options{get;set;}=[];
    public string MetadataSha256{get;set;}=string.Empty;
    public AuthoringHostDiagnostic[]Diagnostics{get;set;}=[];
}
