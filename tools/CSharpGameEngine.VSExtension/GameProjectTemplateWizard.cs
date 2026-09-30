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
/// Creates a C# Game Engine game from the ThirdPerson template (ADR-0068). The VSIX carries the
/// template source (ProjectTemplateSources/ThirdPerson) and the engine SDK package (Packages/);
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
        var ExtensionDirectory=Path.GetDirectoryName(typeof(GameProjectTemplateWizard).Assembly.Location)!;
        var TemplateName=CustomParams?.OfType<string>().Where(static Parameter=>Parameter.EndsWith(".vstemplate",StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileNameWithoutExtension).FirstOrDefault()??"ThirdPerson";
        var ProjectDirectory=ReplacementsDictionary["$destinationdirectory$"];
        var RootNamespace=ReplacementsDictionary["$safeprojectname$"];
        var (EngineVersion,FeedDirectory)=PrepareFeed(ExtensionDirectory);
        ReplacementsDictionary[GameProjectGenerator.EngineVersionToken]=EngineVersion;
        GameProjectGenerator.CopySources(Path.Combine(ExtensionDirectory,TemplateSourceFolder,TemplateName),ProjectDirectory,RootNamespace);
        File.WriteAllText(Path.Combine(ProjectDirectory,"nuget.config"),GameProjectGenerator.NuGetConfig(FeedDirectory));
        this.StartupScene=Path.Combine(ProjectDirectory,GameProjectGenerator.StartupScenePath);
    }
    /// <summary>Copies the bundled engine package into the per-version local feed and returns its version and feed.</summary>
    internal static (string Version,string Feed)PrepareFeed(string ExtensionDirectory){
        var Packages=Directory.GetFiles(Path.Combine(ExtensionDirectory,PackagesFolder),GameProjectGenerator.SdkPackageId+".*.nupkg");
        if(Packages.Length!=1){
            throw new InvalidOperationException($"The extension must carry exactly one {GameProjectGenerator.SdkPackageId} package, found {Packages.Length}.");
        }
        var Version=GameProjectGenerator.VersionOfPackage(Packages[0]);
        var Feed=GameProjectGenerator.DefaultFeedDirectory(Version);
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
