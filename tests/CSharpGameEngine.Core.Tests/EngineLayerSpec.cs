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
        Assert.True(Violations.Length==0,string.Join(Environment.NewLine,Violations));
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
    [Fact]
    public void PortableRenderingDoesNotNameNativeBinding(){
        var Forbidden=new Regex(@"TerraFX|CSharpGameEngine\.Graphics\.D3D12|CSharpGameEngine\.Platform\.Windows|\bWin32\b|\bDXGI_|\bD3D12_|\bID3D12|\bIDXGI|\bHWND\b",RegexOptions.CultureInvariant);
        var Violations=Directory.EnumerateFiles(Path.Combine(RepositoryRoot,"src","CSharpGameEngine.Rendering"),"*.cs",SearchOption.AllDirectories)
            .Where(File=>!File.Split(Path.DirectorySeparatorChar).Any(Part=>Part is "bin" or "obj"))
            .SelectMany(File=>System.IO.File.ReadLines(File).Select((Line,Index)=>(File,Line,Number:Index+1)))
            .Where(Entry=>Forbidden.IsMatch(Entry.Line))
            .Select(Entry=>$"{Entry.File}:{Entry.Number}: {Entry.Line.Trim()}").ToArray();
        Assert.True(Violations.Length==0,string.Join(Environment.NewLine,Violations));
    }
    [Fact]
    public void PortableRenderingReferencesOnlyPortableProjects(){
        var Project=XDocument.Load(Path.Combine(RepositoryRoot,"src","CSharpGameEngine.Rendering","CSharpGameEngine.Rendering.csproj"));
        Assert.Equal("net10.0",Assert.Single(Project.Descendants("TargetFramework")).Value);
        Assert.Empty(Project.Descendants("PackageReference"));
        Assert.DoesNotContain(Project.Descendants("AllowUnsafeBlocks"),Element=>string.Equals(Element.Value,"true",StringComparison.OrdinalIgnoreCase));
        foreach(var Reference in Project.Descendants("ProjectReference")){
            var Name=Path.GetFileNameWithoutExtension(Reference.Attribute("Include")!.Value);
            Assert.Contains(Name,new[]{"CSharpGameEngine.Contracts","CSharpGameEngine.Content","CSharpGameEngine.Core"});
        }
        Assert.DoesNotContain(Project.Descendants("AssemblyAttribute"),Element=>Element.Elements().Any(Child=>Child.Value=="CSharpGameEngine.Windows"));
    }
    [Fact]
    public void RenderingL4DoesNotNameL5View(){
        var Root=Path.Combine(RepositoryRoot,"src","CSharpGameEngine.Rendering");
        var Forbidden=new Regex(@"CSharpGameEngine\.Rendering\.View|\bRenderCamera\b",RegexOptions.CultureInvariant);
        var Violations=Directory.EnumerateFiles(Root,"*.cs",SearchOption.AllDirectories)
            .Where(File=>!Path.GetRelativePath(Root,File).Split(Path.DirectorySeparatorChar).Any(Part=>Part is "View" or "bin" or "obj"))
            .SelectMany(File=>System.IO.File.ReadLines(File).Select((Line,Index)=>(File,Line,Number:Index+1)))
            .Where(Entry=>Forbidden.IsMatch(Entry.Line))
            .Select(Entry=>$"{Entry.File}:{Entry.Number}: {Entry.Line.Trim()}").ToArray();
        Assert.True(Violations.Length==0,string.Join(Environment.NewLine,Violations));
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
