using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace CSharpGameEngine.Core.Tests;
/// <summary>
/// M0096 G: the layer boundaries. The physics and character runtimes are portable: they reach the host through
/// <c>IPhysicsHost</c> and never name the Windows host or its platform layers. The physics tool host references no
/// game; it loads the game build it is given.
/// </summary>
public sealed class EngineLayerSpec {
    private static readonly string RepositoryRoot=FindRepositoryRoot();
    private static readonly Regex WindowsHostReference=new(
        @"\bEngineHost\b|CSharpGameEngine\.Platform\.Windows|CSharpGameEngine\.Graphics\.D3D12|CSharpGameEngine\.Audio\.XAudio2|\bWin32\b",
        RegexOptions.CultureInvariant);
    [Theory]
    [InlineData("src/CSharpGameEngine.Runtime.Physics")]
    [InlineData("src/CSharpGameEngine.Runtime.Character")]
    public void PortableRuntimeDoesNotNameTheWindowsHost(string Directory){
        var Violations=System.IO.Directory.EnumerateFiles(Path.Combine(RepositoryRoot,Directory),"*.cs",SearchOption.TopDirectoryOnly)
            .SelectMany(File=>System.IO.File.ReadLines(File).Select((Line,Index)=>(File,Line,Number:Index+1)))
            .Where(Entry=>!Entry.Line.TrimStart().StartsWith("//",StringComparison.Ordinal)&&WindowsHostReference.IsMatch(Entry.Line))
            .Select(Entry=>$"{Path.GetFileName(Entry.File)}:{Entry.Number}: {Entry.Line.Trim()}")
            .ToArray();
        Assert.Empty(Violations);
    }
    [Fact]
    public void PhysicsToolHostReferencesNoGame(){
        var ProjectPath=Path.Combine(RepositoryRoot,"tools","CSharpGameEngine.Physics.Cli","CSharpGameEngine.Physics.Cli.csproj");
        var Games=XDocument.Load(ProjectPath).Descendants().Where(Element=>Element.Name.LocalName=="ProjectReference")
            .Select(Element=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ProjectPath)!,Element.Attribute("Include")!.Value)))
            .Where(IsGameProject)
            .Select(Path.GetFileName)
            .ToArray();
        Assert.Empty(Games);
    }
    private static bool IsGameProject(string ProjectPath)=>
        File.ReadAllText(ProjectPath).Contains("CSharpGameEngine.Game.props",StringComparison.OrdinalIgnoreCase);
    private static string FindRepositoryRoot(){
        var Current=new DirectoryInfo(AppContext.BaseDirectory);
        while(Current is not null){
            if(File.Exists(Path.Combine(Current.FullName,"CSharpGameEngine.slnx"))){
                return Current.FullName;
            }
            Current=Current.Parent;
        }
        throw new DirectoryNotFoundException("CSharpGameEngine.slnxのルートが見つかりません。");
    }
}
