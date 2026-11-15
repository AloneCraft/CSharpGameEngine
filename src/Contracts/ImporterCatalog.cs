namespace CSharpGameEngine.Contracts;
/// <summary>L6アセット作成とnet472フロントエンドで共有する、プロバイダー非依存のインポーター探索。</summary>
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
