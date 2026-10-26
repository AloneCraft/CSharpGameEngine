using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CSharpGameEngine.ProjectTemplates;

/// <summary>
/// テンプレートのソースプロジェクト (Templates/CSharpGameEngine.Templates.ThirdPerson) を、CSharpGameEngine.Sdk パッケージからビルドするゲームプロジェクトに変換する (ADR-0065、ADR-0068)。Visual Studio のプロジェクトテンプレートウィザードとパッケージのエンドツーエンド Stage が共有するため、どちらも同じプロジェクトを作成する。Visual Studio テンプレートだけは ReadMe.md を追加する。このファイルがあることで VSSDK がテンプレートのプロジェクトファイルをアーカイブする (ADR-0068)。テンプレートソースは通常のコンパイル可能なプロジェクトとして維持し、その名前空間 (<see cref="NamespaceOf"/>、ThirdPerson では <c>CgeThirdPerson</c>) はテンプレートトークンではなく、ここで置換する。
/// </summary>
internal static class GameProjectGenerator {
    internal const string DefaultTemplate="ThirdPerson";
    internal const string TemplateNamespace="Cge"+DefaultTemplate;
    internal const string SdkPackageId="CSharpGameEngine.Sdk";
    internal const string EngineVersionToken="$cgeengineversion$";
    internal const string StartupScenePath="Scenes/Lvl_"+DefaultTemplate+".json";
    /// <summary>拡張機能が提供するテンプレート。フォルダーは Templates/CSharpGameEngine.Templates.&lt;name&gt;。</summary>
    internal static readonly string[]TemplateNames=["ThirdPerson","FirstPerson","TopDown","Vehicle"];
    /// <summary>テンプレートソースで宣言され、<see cref="CopySources"/> が置換する名前空間：<c>Cge&lt;name&gt;</c>。</summary>
    internal static string NamespaceOf(string TemplateName)=>"Cge"+TemplateName;
    /// <summary><paramref name="TemplateName"/> からプロジェクトを作成した後、ウィザードが開くレベル。</summary>
    internal static string StartupSceneOf(string TemplateName)=>TemplateName switch {
        "Vehicle"=>"Scenes/Lvl_VehicleBasic.json",
        _=>"Scenes/Lvl_"+TemplateName+".json",
    };
    // .cge には、各ビルドがそのプロジェクト用に書き出すシーンスキーマが格納される (ADR-0070)。
    private static readonly string[]SkippedDirectories=["bin","obj",".vs",".cge"];

    /// <summary>
    /// C# の予約語：名前空間には使用できない（<c>namespace event.Gameplay;</c> は解析できない）。このファイルを共有する Feature Runner は Roslyn を参照しないため、一覧をここに記述する。
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
    /// <paramref name="ProjectName"/> に対する C# 識別子。Visual Studio が $safeprojectname$ を生成する方法に従う。予約語の場合は先頭にアンダースコアを付け、テンプレートの名前空間を正しく解析できるようにする。
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
    /// テンプレートソースを <paramref name="ProjectDirectory"/> にコピーする。ビルド出力とテンプレート自身のプロジェクトファイルは除外し、C# ソースとシーン JSON ドキュメント内のテンプレート名前空間（<c>namespace</c> とゲーム型名）を置換する。
    /// 作成したファイルを返す。
    /// </summary>
    internal static IReadOnlyList<string>CopySources(string TemplateDirectory,string ProjectDirectory,string RootNamespace,string TemplateName=DefaultTemplate){
        var SourceNamespace=NamespaceOf(TemplateName);
        var Source=Path.GetFullPath(TemplateDirectory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(!Directory.Exists(Source)){
            throw new DirectoryNotFoundException($"The template source was not found: {Source}");
        }
        // シーンファイルは Scenes 以下の *.json であり (ADR-0070)、テンプレートの名前空間も宣言する。
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
                System.IO.File.WriteAllText(Destination,System.IO.File.ReadAllText(File).Replace(SourceNamespace,RootNamespace),new UTF8Encoding(false));
            }
            else {
                System.IO.File.Copy(File,Destination,true);
            }
            Created.Add(Destination);
        }
        return Created;
    }

    /// <summary>
    /// ゲームプロジェクトファイル：Game.props の規約は SDK パッケージから提供される。ローカルに置くのは復元に必要なものと、テンプレートが使うエンジンプラグイン（<paramref name="Plugins"/>、テンプレートの <c>CgePlugins</c>、ADR-0079）だけ。
    /// </summary>
    internal static string ProjectFile(string RootNamespace,string EngineVersion,string Plugins="")=>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n"
        +"  <!-- C# Game Engine game project. The CSharpGameEngine.Sdk package supplies the runtime, the ECS generator, the default shaders and the build conventions. -->\r\n"
        +"  <PropertyGroup>\r\n"
        +"    <OutputType>Exe</OutputType>\r\n"
        +"    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>\r\n"
        +"    <RuntimeIdentifier>win-x64</RuntimeIdentifier>\r\n"
        +"    <RootNamespace>"+RootNamespace+"</RootNamespace>\r\n"
        +(Plugins.Length==0?string.Empty:"    <CgePlugins>"+Plugins+"</CgePlugins>\r\n")
        +"  </PropertyGroup>\r\n"
        +"  <ItemGroup>\r\n"
        +"    <PackageReference Include=\""+SdkPackageId+"\" Version=\""+EngineVersion+"\" />\r\n"
        +"  </ItemGroup>\r\n"
        +"</Project>\r\n";

    /// <summary>
    /// テンプレートのソースプロジェクトで有効にするエンジンプラグイン：そのプロジェクト自身の <c>&lt;CgePlugins&gt;</c> の値、または空 (ADR-0079)。生成プロジェクトとテンプレートが同じゲームをビルドするため、これを記述する場所はテンプレート自身のプロジェクトファイルだけである。
    /// </summary>
    internal static string PluginsOf(string TemplateDirectory){
        foreach(var ProjectPath in Directory.EnumerateFiles(TemplateDirectory,"*.csproj",SearchOption.TopDirectoryOnly)){
            var Text=File.ReadAllText(ProjectPath);
            const string Open="<CgePlugins>";
            var Start=Text.IndexOf(Open,StringComparison.Ordinal);
            if(Start<0){ continue; }
            var End=Text.IndexOf("</CgePlugins>",Start,StringComparison.Ordinal);
            if(End>Start){ return Text.Substring(Start+Open.Length,End-Start-Open.Length).Trim(); }
        }
        return string.Empty;
    }

    /// <summary>ローカルのエンジンパッケージフィードをユーザーの設定済みフィードに追加する（エンジンの依存関係は引き続きそちらから取得する）。</summary>
    internal static string NuGetConfig(string FeedDirectory)=>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
        +"<configuration>\r\n"
        +"  <packageSources>\r\n"
        +"    <add key=\"CSharpGameEngine\" value=\""+SecurityElementEscape(FeedDirectory)+"\" />\r\n"
        +"  </packageSources>\r\n"
        +"</configuration>\r\n";

    /// <summary>復元時に拡張機能が <paramref name="EngineVersion"/> のエンジンパッケージを展開する場所。</summary>
    internal static string DefaultFeedDirectory(string EngineVersion)=>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CSharpGameEngine","packages",EngineVersion);

    /// <summary><c>CSharpGameEngine.Sdk.&lt;version&gt;.nupkg</c> というファイル名に含まれるエンジンバージョン。</summary>
    internal static string VersionOfPackage(string PackagePath){
        var Name=Path.GetFileNameWithoutExtension(PackagePath);
        var Prefix=SdkPackageId+".";
        if(!Name.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)||Name.Length==Prefix.Length){
            throw new ArgumentException($"Not a {SdkPackageId} package: {PackagePath}",nameof(PackagePath));
        }
        return Name.Substring(Prefix.Length);
    }

    /// <summary>同一のファイルが存在しない場合、<paramref name="PackagePath"/> を <paramref name="FeedDirectory"/> にコピーする。</summary>
    internal static void PublishToFeed(string PackagePath,string FeedDirectory){
        Directory.CreateDirectory(FeedDirectory);
        var Destination=Path.Combine(FeedDirectory,Path.GetFileName(PackagePath));
        if(File.Exists(Destination)&&SameContent(Destination,PackagePath)){
            return;
        }
        File.Copy(PackagePath,Destination,true);
    }

    /// <summary>
    /// バイト単位で一致することを確認する。パッケージの再ビルド後もバージョンは維持され (ADR-0065)、新しくビルドしたパッケージが以前のものと同じ長さになる場合もある。
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

    /// <summary>ソース、<c>&lt;ProjectName&gt;.csproj</c>、<c>nuget.config</c> を含む完全なゲームプロジェクトを作成する。</summary>
    internal static string Generate(string TemplateDirectory,string ProjectDirectory,string ProjectName,string EngineVersion,string FeedDirectory,string TemplateName=DefaultTemplate){
        var RootNamespace=SafeIdentifier(ProjectName);
        Directory.CreateDirectory(ProjectDirectory);
        CopySources(TemplateDirectory,ProjectDirectory,RootNamespace,TemplateName);
        var ProjectPath=Path.Combine(ProjectDirectory,ProjectName+".csproj");
        File.WriteAllText(ProjectPath,ProjectFile(RootNamespace,EngineVersion,PluginsOf(TemplateDirectory)),new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(ProjectDirectory,"nuget.config"),NuGetConfig(FeedDirectory),new UTF8Encoding(false));
        return ProjectPath;
    }

    private static string SecurityElementEscape(string Value)=>Value.Replace("&","&amp;").Replace("\"","&quot;").Replace("<","&lt;").Replace(">","&gt;");
}
