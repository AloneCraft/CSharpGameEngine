using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CSharpGameEngine.ProjectTemplates;

/// <summary>
/// Turns the template source project (templates/CSharpGameEngine.Templates.ThirdPerson) into a new
/// game project that builds from the CSharpGameEngine.Sdk package (ADR-0065, ADR-0068). Shared by the
/// Visual Studio project-template wizard and the package end-to-end Stage, so both create exactly the
/// same project. The template source stays an ordinary compilable project; its namespace
/// <see cref="TemplateNamespace"/> is replaced here rather than through template tokens.
/// </summary>
internal static class GameProjectGenerator {
    internal const string TemplateNamespace="CgeThirdPerson";
    internal const string SdkPackageId="CSharpGameEngine.Sdk";
    internal const string EngineVersionToken="$cgeengineversion$";
    internal const string StartupScenePath="Scenes/Lvl_ThirdPerson.scene.json";
    // .cge holds the scene schema that each build writes for its own project (ADR-0070).
    private static readonly string[]SkippedDirectories=["bin","obj",".vs",".cge"];

    /// <summary>A C# identifier for <paramref name="ProjectName"/>, as Visual Studio forms $safeprojectname$.</summary>
    internal static string SafeIdentifier(string ProjectName){
        StringBuilder Builder=new();
        foreach(var Character in ProjectName??string.Empty){
            Builder.Append(char.IsLetterOrDigit(Character)||Character=='_'?Character:'_');
        }
        if(Builder.Length==0){
            return "Game";
        }
        if(char.IsDigit(Builder[0])){
            Builder.Insert(0,'_');
        }
        return Builder.ToString();
    }

    /// <summary>
    /// Copies the template source into <paramref name="ProjectDirectory"/>, skipping build outputs and
    /// the template's own project file, and renames the template namespace in C# sources and scene
    /// JSON documents (their <c>namespace</c> and game type names).
    /// Returns the created files.
    /// </summary>
    internal static IReadOnlyList<string>CopySources(string TemplateDirectory,string ProjectDirectory,string RootNamespace){
        var Source=Path.GetFullPath(TemplateDirectory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(!Directory.Exists(Source)){
            throw new DirectoryNotFoundException($"The template source was not found: {Source}");
        }
        List<string>Created=[];
        foreach(var File in Directory.EnumerateFiles(Source,"*",SearchOption.AllDirectories).OrderBy(static Item=>Item,StringComparer.OrdinalIgnoreCase)){
            var Relative=File.Substring(Source.Length+1);
            var Segments=Relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(Segments.Take(Segments.Length-1).Any(Segment=>SkippedDirectories.Contains(Segment,StringComparer.OrdinalIgnoreCase))
                ||string.Equals(Path.GetExtension(File),".csproj",StringComparison.OrdinalIgnoreCase)){
                continue;
            }
            var Destination=Path.Combine(ProjectDirectory,Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            if(string.Equals(Path.GetExtension(File),".cs",StringComparison.OrdinalIgnoreCase)
                ||File.EndsWith(".scene.json",StringComparison.OrdinalIgnoreCase)){
                System.IO.File.WriteAllText(Destination,System.IO.File.ReadAllText(File).Replace(TemplateNamespace,RootNamespace),new UTF8Encoding(false));
            }
            else {
                System.IO.File.Copy(File,Destination,true);
            }
            Created.Add(Destination);
        }
        return Created;
    }

    /// <summary>The game project file: Game.props conventions arrive through the SDK package; only what restore needs is local.</summary>
    internal static string ProjectFile(string RootNamespace,string EngineVersion)=>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n"
        +"  <!-- C# Game Engine game project. The CSharpGameEngine.Sdk package supplies the runtime, the ECS generator, the default shaders and the build conventions. -->\r\n"
        +"  <PropertyGroup>\r\n"
        +"    <OutputType>Exe</OutputType>\r\n"
        +"    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>\r\n"
        +"    <RuntimeIdentifier>win-x64</RuntimeIdentifier>\r\n"
        +"    <RootNamespace>"+RootNamespace+"</RootNamespace>\r\n"
        +"  </PropertyGroup>\r\n"
        +"  <ItemGroup>\r\n"
        +"    <PackageReference Include=\""+SdkPackageId+"\" Version=\""+EngineVersion+"\" />\r\n"
        +"  </ItemGroup>\r\n"
        +"</Project>\r\n";

    /// <summary>Adds the local engine package feed to the user's configured sources (the engine's dependencies still come from them).</summary>
    internal static string NuGetConfig(string FeedDirectory)=>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
        +"<configuration>\r\n"
        +"  <packageSources>\r\n"
        +"    <add key=\"CSharpGameEngine\" value=\""+SecurityElementEscape(FeedDirectory)+"\" />\r\n"
        +"  </packageSources>\r\n"
        +"</configuration>\r\n";

    /// <summary>Where the extension unpacks the engine package of <paramref name="EngineVersion"/> for restore.</summary>
    internal static string DefaultFeedDirectory(string EngineVersion)=>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CSharpGameEngine","packages",EngineVersion);

    /// <summary>The engine version of a <c>CSharpGameEngine.Sdk.&lt;version&gt;.nupkg</c> file name.</summary>
    internal static string VersionOfPackage(string PackagePath){
        var Name=Path.GetFileNameWithoutExtension(PackagePath);
        var Prefix=SdkPackageId+".";
        if(!Name.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)||Name.Length==Prefix.Length){
            throw new ArgumentException($"Not a {SdkPackageId} package: {PackagePath}",nameof(PackagePath));
        }
        return Name.Substring(Prefix.Length);
    }

    /// <summary>Copies <paramref name="PackagePath"/> into <paramref name="FeedDirectory"/> unless an identical file is there.</summary>
    internal static void PublishToFeed(string PackagePath,string FeedDirectory){
        Directory.CreateDirectory(FeedDirectory);
        var Destination=Path.Combine(FeedDirectory,Path.GetFileName(PackagePath));
        if(File.Exists(Destination)&&new FileInfo(Destination).Length==new FileInfo(PackagePath).Length){
            return;
        }
        File.Copy(PackagePath,Destination,true);
    }

    /// <summary>Creates a complete game project: sources, <c>&lt;ProjectName&gt;.csproj</c> and <c>nuget.config</c>.</summary>
    internal static string Generate(string TemplateDirectory,string ProjectDirectory,string ProjectName,string EngineVersion,string FeedDirectory){
        var RootNamespace=SafeIdentifier(ProjectName);
        Directory.CreateDirectory(ProjectDirectory);
        CopySources(TemplateDirectory,ProjectDirectory,RootNamespace);
        var ProjectPath=Path.Combine(ProjectDirectory,ProjectName+".csproj");
        File.WriteAllText(ProjectPath,ProjectFile(RootNamespace,EngineVersion),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(ProjectDirectory,"nuget.config"),NuGetConfig(FeedDirectory),new UTF8Encoding(false));
        return ProjectPath;
    }

    private static string SecurityElementEscape(string Value)=>Value.Replace("&","&amp;").Replace("\"","&quot;").Replace("<","&lt;").Replace(">","&gt;");
}
