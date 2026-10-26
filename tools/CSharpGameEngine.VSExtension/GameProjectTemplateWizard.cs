using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSharpGameEngine.ProjectTemplates;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TemplateWizard;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// プロジェクトテンプレートから C# Game Engine のゲームを作成します（ADR-0068：ThirdPerson、FirstPerson、TopDown、Vehicle）。VSIX にはテンプレートソース（ProjectTemplateSources/&lt;name&gt;）とエンジン SDK パッケージ（Packages/）が含まれます。wizard はパッケージをローカル feed に配置し、新しいプロジェクトの namespace と nuget.config を反映したソースを書き込み、テンプレートに PackageMode のプロジェクトファイルを作成させます。作成後、起動レベルを開きます。
/// </summary>
public sealed class GameProjectTemplateWizard:IWizard {
    internal const string TemplateSourceFolder="ProjectTemplateSources";
    internal const string PackagesFolder="Packages";
    private DTE?Dte;
    private string?StartupScene;
    public void RunStarted(object AutomationObject,Dictionary<string,string>ReplacementsDictionary,WizardRunKind RunKind,object[]CustomParams){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Dte=AutomationObject as DTE;
        var TemplateName=CustomParams?.OfType<string>().Where(static Parameter=>Parameter.EndsWith(".vstemplate",StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileNameWithoutExtension).FirstOrDefault()??"ThirdPerson";
        var ProjectDirectory=ReplacementsDictionary["$destinationdirectory$"];
        var ExtensionDirectory=Path.GetDirectoryName(typeof(GameProjectTemplateWizard).Assembly.Location)!;
        ReplacementsDictionary[GameProjectGenerator.EngineVersionToken]=CreateProject(ExtensionDirectory,TemplateName,ProjectDirectory,ReplacementsDictionary["$safeprojectname$"],null);
        this.StartupScene=Path.Combine(ProjectDirectory,GameProjectGenerator.StartupSceneOf(TemplateName));
    }
    /// <summary>
    /// 同梱パッケージをローカル feed（<paramref name="FeedRoot"/>、既定では %LOCALAPPDATA%\CSharpGameEngine\packages）の下に publish し、テンプレートソースと nuget.config を <paramref name="ProjectDirectory"/> に書き込み、プロジェクトファイル用のエンジンバージョンを返します。
    /// </summary>
    internal static string CreateProject(string ExtensionDirectory,string TemplateName,string ProjectDirectory,string RootNamespace,string?FeedRoot){
        var (EngineVersion,FeedDirectory)=PrepareFeed(ExtensionDirectory,FeedRoot);
        GameProjectGenerator.CopySources(Path.Combine(ExtensionDirectory,TemplateSourceFolder,TemplateName),ProjectDirectory,RootNamespace,TemplateName);
        File.WriteAllText(Path.Combine(ProjectDirectory,"nuget.config"),GameProjectGenerator.NuGetConfig(FeedDirectory));
        return EngineVersion;
    }
    /// <summary>同梱されたエンジンパッケージをバージョン別のローカル feed にコピーし、そのバージョンと feed を返します。</summary>
    internal static (string Version,string Feed)PrepareFeed(string ExtensionDirectory,string?FeedRoot){
        var Packages=Directory.GetFiles(Path.Combine(ExtensionDirectory,PackagesFolder),GameProjectGenerator.SdkPackageId+".*.nupkg");
        if(Packages.Length!=1){
            throw new InvalidOperationException($"The extension must carry exactly one {GameProjectGenerator.SdkPackageId} package, found {Packages.Length}.");
        }
        var Version=GameProjectGenerator.VersionOfPackage(Packages[0]);
        var Feed=FeedRoot is null?GameProjectGenerator.DefaultFeedDirectory(Version):Path.Combine(FeedRoot,Version);
        GameProjectGenerator.PublishToFeed(Packages[0],Feed);
        return (Version,Feed);
    }
    public void ProjectFinishedGenerating(Project Project){ }
    public void ProjectItemFinishedGenerating(ProjectItem ProjectItem){ }
    public void BeforeOpeningFile(ProjectItem ProjectItem){ }
    public void RunFinished(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Dte is not null&&this.StartupScene is not null&&File.Exists(this.StartupScene)){
            _=this.Dte.ItemOperations.OpenFile(this.StartupScene);
        }
    }
    public bool ShouldAddProjectItem(string FilePath)=>true;
}
