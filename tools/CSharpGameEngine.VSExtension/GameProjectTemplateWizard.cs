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
/// Creates a C# Game Engine game from a project template (ADR-0068: ThirdPerson, FirstPerson, TopDown, Vehicle). The VSIX carries the
/// template sources (ProjectTemplateSources/&lt;name&gt;) and the engine SDK package (Packages/);
/// the wizard places the package in the local feed, writes the sources with the new project's
/// namespace and a nuget.config, and lets the template write the PackageMode project file. After
/// creation it opens the startup level.
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
    /// Publishes the bundled package to the local feed (under <paramref name="FeedRoot"/>, by default
    /// %LOCALAPPDATA%\CSharpGameEngine\packages), writes the template sources and nuget.config into
    /// <paramref name="ProjectDirectory"/>, and returns the engine version for the project file.
    /// </summary>
    internal static string CreateProject(string ExtensionDirectory,string TemplateName,string ProjectDirectory,string RootNamespace,string?FeedRoot){
        var (EngineVersion,FeedDirectory)=PrepareFeed(ExtensionDirectory,FeedRoot);
        GameProjectGenerator.CopySources(Path.Combine(ExtensionDirectory,TemplateSourceFolder,TemplateName),ProjectDirectory,RootNamespace,TemplateName);
        File.WriteAllText(Path.Combine(ProjectDirectory,"nuget.config"),GameProjectGenerator.NuGetConfig(FeedDirectory));
        return EngineVersion;
    }
    /// <summary>Copies the bundled engine package into the per-version local feed and returns its version and feed.</summary>
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
