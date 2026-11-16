using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace CSharpGameEngine.Core.Tests;
/// <summary>
/// M0096 G：レイヤー境界。物理とキャラクターのランタイムは移植可能で、<c>IPhysicsHost</c> を通してホストへ接続し、Windows ホストやプラットフォームレイヤーの名前に依存し
/// ない。物理ツールのホストはゲームを参照せず、渡されたゲームビルドを読み込む。
/// </summary>
public sealed class EngineLayerSpec {
    private static readonly string RepositoryRoot=FindRepositoryRoot();
    private static readonly Regex WindowsHostReference=new(
        @"\bEngineHost\b|CSharpGameEngine\.Platform\.Windows|CSharpGameEngine\.Graphics\.D3D12|CSharpGameEngine\.Audio\.XAudio2|\bWin32\b",
        RegexOptions.CultureInvariant);
    [Theory]
    [InlineData("src/Runtime/Physics")]
    [InlineData("src/Runtime/Character")]
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
        var ProjectPath=Path.Combine(RepositoryRoot,"tools","Physics.Cli","Physics.Cli.csproj");
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
        var Violations=Directory.EnumerateFiles(Path.Combine(RepositoryRoot,"src","Rendering"),"*.cs",SearchOption.AllDirectories)
            .Where(File=>!File.Split(Path.DirectorySeparatorChar).Any(Part=>Part is "bin" or "obj"))
            .SelectMany(File=>System.IO.File.ReadLines(File).Select((Line,Index)=>(File,Line,Number:Index+1)))
            .Where(Entry=>Forbidden.IsMatch(Entry.Line))
            .Select(Entry=>$"{Entry.File}:{Entry.Number}: {Entry.Line.Trim()}").ToArray();
        Assert.True(Violations.Length==0,string.Join(Environment.NewLine,Violations));
    }
    [Fact]
    public void PortableRenderingReferencesOnlyPortableProjects(){
        var Project=XDocument.Load(Path.Combine(RepositoryRoot,"src","Rendering","Rendering.csproj"));
        Assert.Equal("net10.0",Assert.Single(Project.Descendants("TargetFramework")).Value);
        Assert.Empty(Project.Descendants("PackageReference"));
        Assert.DoesNotContain(Project.Descendants("AllowUnsafeBlocks"),Element=>string.Equals(Element.Value,"true",StringComparison.OrdinalIgnoreCase));
        foreach(var Reference in Project.Descendants("ProjectReference")){
            var Name=Path.GetFileNameWithoutExtension(Reference.Attribute("Include")!.Value);
            Assert.Contains(Name,new[]{"Contracts","Content","Core"});
        }
        Assert.DoesNotContain(Project.Descendants("AssemblyAttribute"),Element=>Element.Elements().Any(Child=>Child.Value=="Windows"));
    }
    [Fact]
    public void RenderingL4DoesNotNameL5View(){
        var Root=Path.Combine(RepositoryRoot,"src","Rendering");
        var Forbidden=new Regex(@"CSharpGameEngine\.Rendering\.View|\bRenderCamera\b",RegexOptions.CultureInvariant);
        var Violations=Directory.EnumerateFiles(Root,"*.cs",SearchOption.AllDirectories)
            .Where(File=>!Path.GetRelativePath(Root,File).Split(Path.DirectorySeparatorChar).Any(Part=>Part is "View" or "bin" or "obj"))
            .SelectMany(File=>System.IO.File.ReadLines(File).Select((Line,Index)=>(File,Line,Number:Index+1)))
            .Where(Entry=>Forbidden.IsMatch(Entry.Line))
            .Select(Entry=>$"{Entry.File}:{Entry.Number}: {Entry.Line.Trim()}").ToArray();
        Assert.True(Violations.Length==0,string.Join(Environment.NewLine,Violations));
    }
    /// <summary>
    /// ADR-0087 §1-3・§3、M0120 単位 6：L0（D3D12 の実行器）は計画の関数を呼ばない。ビューの方針、フレームの計画、光源と影の計画、
    /// 対数深度の投影、カスケード影、光源の予算とアトラスの割り当ては L4・L5 が決め、L0 は計画の結果を受け取って書くだけ。
    /// </summary>
    [Fact]
    public void NativeBackendDoesNotPlanFrames(){
        var Forbidden=new Regex(
            @"RenderViewPolicy\.|StagePipelinePlanner\.|StageLightPlanner\.|StageShadowPlanner\.|RadialDepthProjection\.TryCreate|DirectionalShadowCascades\.Build"
            +@"|PhysicalLightBudget\.|PhysicalShadowAtlasPlan\.Select|\.PlanDirectional\(|\.PlanLocal\(|\.PlanLocalShadows\(|\bLights\.Plan\(",
            RegexOptions.CultureInvariant);
        var Violations=NativeBackendViolations(Forbidden);
        Assert.True(Violations.Length==0,string.Join(Environment.NewLine,Violations));
    }
    private static string[]NativeBackendViolations(Regex Forbidden){
        var Root=Path.Combine(RepositoryRoot,"src","Windows");
        return Directory.EnumerateFiles(Root,"*.cs",SearchOption.AllDirectories)
            .Where(File=>!Path.GetRelativePath(Root,File).Split(Path.DirectorySeparatorChar).Any(Part=>Part is "bin" or "obj"))
            .SelectMany(File=>System.IO.File.ReadLines(File).Select((Line,Index)=>(File,Line,Number:Index+1)))
            .Where(Entry=>!Entry.Line.TrimStart().StartsWith("//",StringComparison.Ordinal)&&Forbidden.IsMatch(Entry.Line))
            .Select(Entry=>$"{Path.GetRelativePath(Root,Entry.File)}:{Entry.Number}: {Entry.Line.Trim()}").ToArray();
    }
    [Fact]
    public void NoPortableEngineLibraryReferencesTheNativeBackend(){
        var Root=Path.Combine(RepositoryRoot,"src");
        var Violations=Directory.EnumerateFiles(Root,"*.csproj",SearchOption.AllDirectories)
            .Where(File=>!Path.GetRelativePath(Root,File).Split(Path.DirectorySeparatorChar).Any(Part=>Part is "bin" or "obj"))
            .Where(File=>Path.GetFileName(File)!="Windows.csproj")
            .SelectMany(File=>XDocument.Load(File).Descendants("ProjectReference").Select(Reference=>(File,Path:Reference.Attribute("Include")?.Value)))
            .Where(Entry=>Entry.Path is not null&&Path.GetFileName(Entry.Path.Replace('\\','/'))=="Windows.csproj")
            .Select(Entry=>$"{Entry.File}: {Entry.Path}").ToArray();
        Assert.Empty(Violations);
    }
    [Fact]
    public void NativeStageDoesNotDeclareFrameOrObjectOffsets(){
        var Root=Path.Combine(RepositoryRoot,"src","Windows","Graphics");
        var Forbidden=new Regex(@"private\s+const\s+uint\s+(?:Frame\w+Offset|Object\w+Offset|FrameConstantsSize)\s*=",RegexOptions.CultureInvariant);
        var Violations=Directory.EnumerateFiles(Root,"StageRenderer*.cs",SearchOption.TopDirectoryOnly)
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
