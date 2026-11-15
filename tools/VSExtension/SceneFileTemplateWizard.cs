using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TemplateWizard;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// 新しいシーン JSON 項目を完成させます（ADR-0070）。<c>"$schema"</c> は、その項目がどこに追加されたかをテンプレートでは判断できないため、プロジェクトの生成済み <c>.cge/scene.schema.json</c> を指す必要があります。
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
    /// <summary>新しい項目がプロジェクトの Scenes フォルダー外にあり、シーンではないかどうかを判定します（ADR-0070）。</summary>
    internal static bool IsOutsideScenes(string ItemPath,string ProjectPath)=>
        !CSharpGameEngine.SceneAnalysis.Json.SceneFiles.IsScene(ItemPath,Path.GetDirectoryName(Path.GetFullPath(ProjectPath)));
    /// <summary><paramref name="ItemPath"/> からの相対スキーマパスで <see cref="SchemaToken"/> を置き換えます。</summary>
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
