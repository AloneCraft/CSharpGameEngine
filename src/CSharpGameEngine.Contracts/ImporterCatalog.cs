namespace CSharpGameEngine.Contracts;
/// <summary>Provider-free importer discovery shared with L6 authoring and the net472 frontend.</summary>
public interface IImporterCatalog {
    IReadOnlyList<ImporterCandidateDto> Candidates(string Extension);
    bool IsValid(string Identity,string Extension);
}
public sealed class ImporterCandidateDto {
    public string Identity{get;set;}=string.Empty;
    public int Version{get;set;}
    public string DisplayName{get;set;}=string.Empty;
    public string[]Extensions{get;set;}=[];
    public int Priority{get;set;}
    public string Provider{get;set;}=string.Empty;
}
