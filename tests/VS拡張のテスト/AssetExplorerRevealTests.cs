namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetExplorerRevealTests {
    private static bool Always(string _)=>true;
    private static bool Never(string _)=>false;
    [Fact]
    public void AnExistingFileIsSelectedWithItsFullQuotedPath(){
        var Result=AssetExplorerReveal.Resolve(@"C:\Game\Assets\Hero [1], 日本語.glb",@"C:\Other",Always,Always);
        Assert.Equal("/select,\"C:\\Game\\Assets\\Hero [1], 日本語.glb\"",Result);
    }
    [Fact]
    public void ForwardSlashesAreNormalizedToBackslashes(){
        Assert.Equal("/select,\"C:\\Game\\Assets\\Hero.glb\"",AssetExplorerReveal.Resolve("C:/Game/Assets/Hero.glb",null,Always,Always));
        Assert.Equal("\"C:\\Game\\Assets\"",AssetExplorerReveal.Resolve(null,"C:/Game/Assets",Always,Always));
    }
    [Fact]
    public void AMissingFileOpensItsParentFolderWithoutSelecting(){
        var Result=AssetExplorerReveal.Resolve(@"C:\Game\Assets\Gone.glb",null,Never,static Path=>Path==@"C:\Game\Assets");
        Assert.Equal("\"C:\\Game\\Assets\"",Result);
    }
    [Fact]
    public void AMissingFileWithoutItsParentFolderCannotBeRevealed(){
        Assert.Null(AssetExplorerReveal.Resolve(@"C:\Nowhere\Gone.glb",@"C:\Game",Never,Never));
    }
    [Fact]
    public void NoSelectionOpensTheCurrentFolderWithoutSelectingAnything(){
        var Result=AssetExplorerReveal.Resolve(null,@"C:\Game\Assets\Models",Never,Always);
        Assert.Equal("\"C:\\Game\\Assets\\Models\"",Result);
        Assert.DoesNotContain("/select",Result);
        Assert.Equal("\"C:\\Game\\Assets\\Models\"",AssetExplorerReveal.Resolve(string.Empty,@"C:\Game\Assets\Models",Never,Always));
    }
    [Fact]
    public void NoSelectionWithAMissingOrEmptyFolderHasNothingToOpen(){
        Assert.Null(AssetExplorerReveal.Resolve(null,@"C:\Gone",Never,Never));
        Assert.Null(AssetExplorerReveal.Resolve(null,null,Always,Always));
        Assert.Null(AssetExplorerReveal.Resolve(null,string.Empty,Always,Always));
    }
    [Fact]
    public void ARealFileAndFolderResolveAgainstTheFileSystem(){
        var Folder=Path.Combine(Path.GetTempPath(),"CgeReveal-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        try {
            var FilePath=Path.Combine(Folder,"A B.txt");
            File.WriteAllText(FilePath,"x");
            Assert.Equal("/select,\""+FilePath+"\"",AssetExplorerReveal.Resolve(FilePath,null));
            Assert.Equal("\""+Folder+"\"",AssetExplorerReveal.Resolve(null,Folder));
            Assert.Null(AssetExplorerReveal.Resolve(Path.Combine(Folder,"Missing","x.txt"),null));
        } finally { Directory.Delete(Folder,true); }
    }
}
