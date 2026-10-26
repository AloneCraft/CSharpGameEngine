namespace CSharpGameEngine.VSExtension;
public sealed class AssetWorkspaceFavorite {
    public string Project{get;set;}=string.Empty;
    public string Folder{get;set;}=string.Empty;
    public string Search{get;set;}=string.Empty;
    public string Kind{get;set;}=string.Empty;
    public string Provider{get;set;}=string.Empty;
    public string State{get;set;}=string.Empty;
    public bool WholeProject{get;set;}
    public string Name=>System.IO.Path.GetFileName(this.Folder)+(string.IsNullOrEmpty(this.Search)?string.Empty:" : "+this.Search);
}
