namespace CSharpGameEngine.VSExtension;
public sealed class AssetWorkspaceFavorite {
    [System.Text.Json.Serialization.JsonInclude]
    internal string Scope{get;set;}=nameof(AssetWorkspaceScope.Folder);
    public string Project{get;set;}=string.Empty;
    public string Folder{get;set;}=string.Empty;
    public string Search{get;set;}=string.Empty;
    public string Kind{get;set;}=string.Empty;
    public string Provider{get;set;}=string.Empty;
    public string State{get;set;}=string.Empty;
    public bool WholeProject{get;set;}
    public string Name=>(this.Scope==nameof(AssetWorkspaceScope.Solution)?VsixText.Get("Assets_WholeSolution"):System.IO.Path.GetFileName(this.Folder))+(string.IsNullOrEmpty(this.Search)?string.Empty:" : "+this.Search);
    internal static AssetWorkspaceFavorite Capture(AssetWorkspaceQuery Query)=>new(){Scope=Query.Scope.ToString(),Project=Query.ProjectPath,Folder=Query.Folder,Search=Query.Search,Kind=Query.Kind,Provider=Query.Provider,State=Query.State,WholeProject=Query.WholeProject};
    internal void Restore(AssetWorkspaceQuery Query){
        Query.MoveTo(this.Project,this.Folder,EnumScope(this.Scope));
        Query.Search=this.Search;Query.Kind=this.Kind;Query.Provider=this.Provider;Query.State=this.State;Query.WholeProject=this.WholeProject;
    }
    private static AssetWorkspaceScope EnumScope(string Value)=>System.Enum.TryParse<AssetWorkspaceScope>(Value,out var Scope)&&System.Enum.IsDefined(typeof(AssetWorkspaceScope),Scope)?Scope:AssetWorkspaceScope.Folder;
}
