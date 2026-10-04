using System.Xml.Linq;
namespace CSharpGameEngine.Core.Tests;
public sealed class SolutionProjectRegistrationSpec {
    private static readonly string RepositoryRoot=FindRepositoryRoot();
    private static readonly string SolutionPath=Path.Combine(RepositoryRoot,"CSharpGameEngine.slnx");
    [Fact]
    public void AllProjectsAndSharedProjectFilesAreRegistered(){
        var Document=XDocument.Load(SolutionPath);
        var ExpectedProjects=EnumeratePaths("*.csproj").Concat(EnumeratePaths("*.shproj")).OrderBy(Path=>Path).ToArray();
        var ExpectedSharedFiles=EnumeratePaths("*.projitems").OrderBy(Path=>Path).ToArray();
        var RegisteredProjects=Document.Descendants("Project").Select(Element=>Normalize(Element.Attribute("Path")?.Value)).Where(Path=>Path is not null).Select(Path=>Path!).OrderBy(Path=>Path).ToArray();
        var RegisteredSharedFiles=Document.Descendants("File").Select(Element=>Normalize(Element.Attribute("Path")?.Value)).Where(Path=>Path is not null&&Path.EndsWith(".projitems",StringComparison.OrdinalIgnoreCase)).Select(Path=>Path!).OrderBy(Path=>Path).ToArray();
        Assert.Equal(RegisteredProjects.Distinct(StringComparer.OrdinalIgnoreCase).Count(),RegisteredProjects.Length);
        Assert.Equal(RegisteredSharedFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count(),RegisteredSharedFiles.Length);
        Assert.Equal(ExpectedProjects,RegisteredProjects);
        Assert.Equal(ExpectedSharedFiles,RegisteredSharedFiles);
    }
    [Fact]
    public void SharedProjectsImportExplicitProjitemsWithoutWildcards(){
        foreach(var SharedProjectPath in EnumeratePaths("*.shproj")){
            var SharedProject=XDocument.Load(Path.Combine(RepositoryRoot,SharedProjectPath.Replace('/',Path.DirectorySeparatorChar)));
            var Imports=SharedProject.Descendants().Where(Element=>Element.Name.LocalName=="Import").Where(Element=>string.Equals(Element.Attribute("Label")?.Value,"Shared",StringComparison.Ordinal));
            Assert.NotEmpty(Imports);
            foreach(var Import in Imports){
                var ImportPath=Import.Attribute("Project")?.Value;
                Assert.False(string.IsNullOrWhiteSpace(ImportPath));
                var ResolvedPath=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.Combine(RepositoryRoot,SharedProjectPath.Replace('/',Path.DirectorySeparatorChar)))!,ImportPath!));
                Assert.True(File.Exists(ResolvedPath),$"共有プロジェクトのprojitemsがありません: {SharedProjectPath} -> {ImportPath}");
            }
        }
        foreach(var SharedFilePath in EnumeratePaths("*.projitems")){
            var SharedFile=XDocument.Load(Path.Combine(RepositoryRoot,SharedFilePath.Replace('/',Path.DirectorySeparatorChar)));
            var WildcardIncludes=SharedFile.Descendants().Attributes("Include").Select(Attribute=>Attribute.Value).Where(Value=>Value.Contains('*')||Value.Contains('?')).ToArray();
            Assert.Empty(WildcardIncludes);
        }
    }
    [Fact]
    public void SharedProjectsCarryNoSolutionConfigurationMapping(){
        // A shproj has no build configurations; mapping one makes Visual Studio report
        // "specifies a project configuration ... that does not exist" when the solution opens.
        var Document=XDocument.Load(SolutionPath);
        var Mapped=Document.Descendants("Project").Where(Element=>Element.Attribute("Path")?.Value.EndsWith(".shproj",StringComparison.OrdinalIgnoreCase)==true).Where(Element=>Element.Elements().Any(Child=>Child.Name.LocalName is "Platform" or "BuildType" or "Build" or "Deploy")).Select(Element=>Element.Attribute("Path")!.Value).ToArray();
        Assert.Empty(Mapped);
    }
    [Fact]
    public void TransientWpfBuildProjectsAreNotRegistrationCandidates(){
        var TransientPath=Path.Combine(RepositoryRoot,"tools","CSharpGameEngine.VSExtension","CSharpGameEngine.VSExtension_build_wpftmp.csproj");
        Assert.False(IsTrackedArea(TransientPath));
    }
    private static string[] EnumeratePaths(string Pattern){
        var Extension=Pattern.TrimStart('*');
        return Directory.EnumerateFiles(RepositoryRoot,Pattern,SearchOption.AllDirectories).Where(IsTrackedArea).Where(Path=>Path.EndsWith(Extension,StringComparison.OrdinalIgnoreCase)).Select(Path=>Normalize(Path)).Where(Path=>Path is not null).Select(Path=>Path!).ToArray();
    }
    private static bool IsTrackedArea(string FilePath){
        var Relative=Path.GetRelativePath(RepositoryRoot,FilePath);
        return !Relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Any(Segment=>Segment is "bin" or "obj" or ".git" or ".vs" or "VSIXProject1")&&!Path.GetFileName(FilePath).EndsWith("_wpftmp.csproj",StringComparison.OrdinalIgnoreCase);
    }
    private static string? Normalize(string? PathValue){
        if(string.IsNullOrWhiteSpace(PathValue)){
            return null;
        }
        var Relative=Path.IsPathRooted(PathValue)?Path.GetRelativePath(RepositoryRoot,PathValue):PathValue;
        return Relative.Replace(Path.DirectorySeparatorChar,'/').Replace(Path.AltDirectorySeparatorChar,'/');
    }
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
