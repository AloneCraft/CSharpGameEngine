using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TemplateWizard;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Completes a new <c>*.scene.json</c> item (ADR-0070): its <c>"$schema"</c> must point at the project's
/// generated <c>.cge/scene.schema.json</c> from wherever the item was added, which the template cannot know.
/// </summary>
public sealed class SceneFileTemplateWizard:IWizard {
    internal const string SchemaToken="$cgesceneschema$";
    public void RunStarted(object AutomationObject,Dictionary<string,string>ReplacementsDictionary,WizardRunKind RunKind,object[]CustomParams) { }
    public void ProjectItemFinishedGenerating(ProjectItem ProjectItem){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Destination=ProjectItem.FileNames[1];
        var ProjectPath=ProjectItem.ContainingProject?.FullName;
        if(string.IsNullOrEmpty(ProjectPath)){ return; }
        File.WriteAllText(Destination,Complete(File.ReadAllText(Destination),Destination,ProjectPath!),new UTF8Encoding(false));
    }
    /// <summary>Replaces <see cref="SchemaToken"/> with the schema path relative to <paramref name="ItemPath"/>.</summary>
    internal static string Complete(string Text,string ItemPath,string ProjectPath)=>
        Text.Replace(SchemaToken,SchemaReference(ItemPath,ProjectPath));
    internal static string SchemaReference(string ItemPath,string ProjectPath){
        var ItemDirectory=Path.GetDirectoryName(Path.GetFullPath(ItemPath))!+Path.DirectorySeparatorChar;
        var Schema=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ProjectPath))!,".cge",CSharpGameEngine.SceneAnalysis.Json.SceneJsonSchemaBuilder.FileName);
        return Uri.UnescapeDataString(new Uri(ItemDirectory).MakeRelativeUri(new Uri(Schema)).ToString());
    }
    public void ProjectFinishedGenerating(Project Project) { }
    public void BeforeOpeningFile(ProjectItem ProjectItem) { }
    public void RunFinished() { }
    public bool ShouldAddProjectItem(string FilePath)=>true;
}
