namespace CSharpGameEngine.Entities;
/// <summary>
/// The directory the runtime resolves relative content paths against: meshes, skeletal assets,
/// collision meshes and the shader artifact. It is the game's own directory. The Scene Designer's
/// runtime view runs a game inside the preview host and points it at the game build it shows
/// (ADR-0070 B1); each loaded game assembly has its own root.
/// </summary>
public static class ContentRoot {
    public static string Directory { get;private set; }=AppContext.BaseDirectory;

    public static string Resolve(string RelativePath)=>Path.GetFullPath(Path.Combine(Directory,RelativePath));

    public static void Use(string Directory){
        ArgumentException.ThrowIfNullOrWhiteSpace(Directory);
        ContentRoot.Directory=Path.GetFullPath(Directory);
    }
}
