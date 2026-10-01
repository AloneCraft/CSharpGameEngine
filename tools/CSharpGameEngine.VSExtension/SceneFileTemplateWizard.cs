using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TemplateWizard;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Completes a new scene JSON item (ADR-0070): its <c>"$schema"</c> must point at the project's
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
        if(IsOutsideScenes(Destination,ProjectPath!)
            &&Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar))is Microsoft.VisualStudio.Shell.Interop.IVsStatusbar StatusBar){
            StatusBar.SetText(VsixText.Format("SceneFileOutsideScenes",Path.GetFileName(Destination)));
        }
    }
    /// <summary>Whether the new item lies outside the project's Scenes folder, where it is not a scene (ADR-0070).</summary>
    internal static bool IsOutsideScenes(string ItemPath,string ProjectPath)=>
        !CSharpGameEngine.SceneAnalysis.Json.SceneFiles.IsScene(ItemPath,Path.GetDirectoryName(Path.GetFullPath(ProjectPath)));
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
