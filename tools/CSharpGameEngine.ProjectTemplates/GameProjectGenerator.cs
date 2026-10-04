using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CSharpGameEngine.ProjectTemplates;

/// <summary>
/// Turns the template source project (templates/CSharpGameEngine.Templates.ThirdPerson) into a new
/// game project that builds from the CSharpGameEngine.Sdk package (ADR-0065, ADR-0068). Shared by the
/// Visual Studio project-template wizard and the package end-to-end Stage, so both create the same
/// project; only the Visual Studio template adds a ReadMe.md, the item that makes the VSSDK archive the
/// template's project file (ADR-0068). The template source stays an ordinary compilable project; its namespace
/// <see cref="TemplateNamespace"/> is replaced here rather than through template tokens.
/// </summary>
internal static class GameProjectGenerator {
    internal const string TemplateNamespace="CgeThirdPerson";
    internal const string SdkPackageId="CSharpGameEngine.Sdk";
    internal const string EngineVersionToken="$cgeengineversion$";
    internal const string StartupScenePath="Scenes/Lvl_ThirdPerson.json";
    // .cge holds the scene schema that each build writes for its own project (ADR-0070).
    private static readonly string[]SkippedDirectories=["bin","obj",".vs",".cge"];

    /// <summary>
    /// C# reserved keywords: a namespace cannot be one (<c>namespace event.Gameplay;</c> does not parse). The list is
    /// written out because the Feature Runner, which shares this file, does not reference Roslyn.
    /// </summary>
    private static readonly HashSet<string>ReservedKeywords=new(StringComparer.Ordinal){
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue","decimal",
        "default","delegate","do","double","else","enum","event","explicit","extern","false","finally","fixed","float","for",
        "foreach","goto","if","implicit","in","int","interface","internal","is","lock","long","namespace","new","null",
        "object","operator","out","override","params","private","protected","public","readonly","ref","return","sbyte",
        "sealed","short","sizeof","stackalloc","static","string","struct","switch","this","throw","true","try","typeof",
        "uint","ulong","unchecked","unsafe","ushort","using","virtual","void","volatile","while",
    };

    /// <summary>
    /// A C# identifier for <paramref name="ProjectName"/>, as Visual Studio forms $safeprojectname$; a reserved keyword
    /// also gets a leading underscore so the template's namespaces parse.
    /// </summary>
    internal static string SafeIdentifier(string ProjectName){
        StringBuilder Builder=new();
        foreach(var Character in ProjectName??string.Empty){
            Builder.Append(char.IsLetterOrDigit(Character)||Character=='_'?Character:'_');
        }
        if(Builder.Length==0){
            return "Game";
        }
        if(char.IsDigit(Builder[0])||ReservedKeywords.Contains(Builder.ToString())){
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
        // A scene file is a *.json under Scenes (ADR-0070); it declares the template's namespace too.
        static bool IsSceneFile(string[]Segments)=>
            Segments.Length>1&&string.Equals(Segments[0],"Scenes",StringComparison.OrdinalIgnoreCase)
            &&Segments[Segments.Length-1].EndsWith(".json",StringComparison.OrdinalIgnoreCase);
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
                ||IsSceneFile(Segments)){
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
        if(File.Exists(Destination)&&SameContent(Destination,PackagePath)){
            return;
        }
        File.Copy(PackagePath,Destination,true);
    }

    /// <summary>
    /// Byte equality: the package keeps its version while it is rebuilt (ADR-0065), and a rebuilt package can have the
    /// old one's length.
    /// </summary>
    private static bool SameContent(string Left,string Right){
        if(new FileInfo(Left).Length!=new FileInfo(Right).Length){
            return false;
        }
        using var LeftStream=File.OpenRead(Left);
        using var RightStream=File.OpenRead(Right);
        var LeftBuffer=new byte[81920];
        var RightBuffer=new byte[LeftBuffer.Length];
        while(true){
            var Read=LeftStream.Read(LeftBuffer,0,LeftBuffer.Length);
            if(Read==0){
                return true;
            }
            var Offset=0;
            while(Offset<Read){
                var RightRead=RightStream.Read(RightBuffer,Offset,Read-Offset);
                if(RightRead==0){
                    return false;
                }
                Offset+=RightRead;
            }
            for(var Index=0;Index<Read;Index++){
                if(LeftBuffer[Index]!=RightBuffer[Index]){
                    return false;
                }
            }
        }
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
