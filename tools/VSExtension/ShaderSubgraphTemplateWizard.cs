using System.Collections.Generic;
using System.IO;
using CSharpGameEngine.ShaderGraph;
using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
public sealed class ShaderSubgraphTemplateWizard:IWizard {
    public void RunStarted(object AutomationObject,Dictionary<string,string>ReplacementsDictionary,WizardRunKind RunKind,object[]CustomParams) { }
    public void ProjectItemFinishedGenerating(ProjectItem ProjectItem) {
        ThreadHelper.ThrowIfNotOnUIThread();
        var Destination=ProjectItem.FileNames[1];
        var Graph=ShaderGraphCanonicalization.FromJson(File.ReadAllText(Destination))??throw new InvalidDataException("Shader Subgraph template is invalid.");
        File.WriteAllText(Destination,ShaderGraphCanonicalization.ToCanonicalJson(ShaderGraphTemplateWizard.Remap(Graph)));
    }
    public void ProjectFinishedGenerating(Project Project) { }
    public void BeforeOpeningFile(ProjectItem ProjectItem) { }
    public void RunFinished() { }
    public bool ShouldAddProjectItem(string FilePath)=>true;
}