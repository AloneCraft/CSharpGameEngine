using System;
using System.Linq;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

internal sealed class AssetAuthoringPresentation {
    private AssetAuthoringPresentation(){ }

    public string State{get;private set;}=string.Empty;
    public string SourceUri{get;private set;}=string.Empty;
    public string AssetId{get;private set;}=string.Empty;
    public string AssetUri{get;private set;}=string.Empty;
    public string Importer{get;private set;}=string.Empty;
    public string MetadataSha256{get;private set;}=string.Empty;
    public string Diagnostics{get;private set;}=string.Empty;
    public bool CanCreateMetadata{get;private set;}

    public static AssetAuthoringPresentation FromResult(AssetAuthoringResult Result){
        if(Result is null){ throw new ArgumentNullException(nameof(Result)); }
        return new AssetAuthoringPresentation {
            State=Result.State.ToString(),SourceUri=Result.SourceUri,AssetId=Result.AssetId,
            AssetUri=Result.AssetUri,Importer=Result.Importer,
            MetadataSha256=Result.MetadataSha256,
            Diagnostics=string.Join(Environment.NewLine,(Result.Diagnostics??[]).Select(
                static Diagnostic=>Diagnostic is null
                    ?"ASSET_RESULT_INVALID: Missing diagnostic."
                    :$"{Diagnostic.Code}: {Diagnostic.Message}")),
            CanCreateMetadata=Result.Success&&Result.State==AssetAuthoringState.MissingMetadata,
        };
    }

    public static AssetAuthoringPresentation Failure(Exception Exception)=>new(){
        State=AssetAuthoringState.Rejected.ToString(),
        Diagnostics=$"ASSET_AUTHORING_HOST_FAILED: {Exception.Message}",
        CanCreateMetadata=false,
    };
}
