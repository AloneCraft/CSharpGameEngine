using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis.Json;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension;
internal static class AssetWorkspaceProjectItems {
    internal static AssetWorkspaceSource[]Collect(DTE Dte,out CgeProjectContext[]Contexts){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Sources=new List<AssetWorkspaceSource>();
        var Projects=new List<CgeProjectContext>();
        foreach(Project Project in Dte.Solution.Projects){ VisitProject(Project,Sources,Projects); }
        Contexts=Projects.ToArray();
        return Sources.GroupBy(static Source=>Source.ProjectPath+"\n"+Source.SourcePath,StringComparer.OrdinalIgnoreCase).Select(static Group=>Group.First()).ToArray();
    }
    private static void VisitProject(Project Project,List<AssetWorkspaceSource>Sources,List<CgeProjectContext>Contexts){
        ThreadHelper.ThrowIfNotOnUIThread();
        string?ProjectFile=null;
        try { ProjectFile=Project.FullName; }catch(COMException){ }
        if(!string.IsNullOrEmpty(ProjectFile)&&CgeProjectContext.TryCreate(ProjectFile!) is { }Context){
            Contexts.Add(Context);
            IReadOnlyList<PluginRoot>Plugins=Array.Empty<PluginRoot>();
            if(Package.GetGlobalService(typeof(SVsSolution)) is IVsSolution Solution
                &&ErrorHandler.Succeeded(Solution.GetProjectOfUniqueName(Project.UniqueName,out var Hierarchy))
                &&Hierarchy is IVsBuildPropertyStorage Properties
                &&ErrorHandler.Succeeded(Properties.GetPropertyValue("CgePluginRoots",null,(uint)_PersistStorageType.PST_PROJECT_FILE,out var Roots))){ Plugins=PluginRoot.Parse(Roots); }
            VisitItems(Project.ProjectItems,Context,Sources,Plugins);
        } else if(Project.ProjectItems is { }Items){
            foreach(ProjectItem Item in Items){ if(Item.SubProject is { }Child){ VisitProject(Child,Sources,Contexts); } }
        }
    }
    private static void VisitItems(ProjectItems?Items,CgeProjectContext Context,List<AssetWorkspaceSource>Sources,IReadOnlyList<PluginRoot>Plugins){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Items is null){ return; }
        foreach(ProjectItem Item in Items){
            for(short Index=1;Index<=Item.FileCount;Index++){
                var SourcePath=Item.FileNames[Index];
                if(string.IsNullOrEmpty(SourcePath)||Directory.Exists(SourcePath)){ continue; }
                var Relative=RelativePath(Context.ProjectDirectory,SourcePath);
                try {
                    if(File.Exists(SourcePath)&&Item.Properties?.Item("Link")?.Value is string Link&&!string.IsNullOrEmpty(Link)){ Relative=Link.Replace('\\','/'); }
                }catch(ArgumentException){ }catch(COMException){ }
                foreach(var Plugin in Plugins){
                    if(AssetWorkspaceQuery.IsWithin(SourcePath,Plugin.Directory)){
                        Relative="Plugins/"+Plugin.Name+"/"+RelativePath(Plugin.Directory,SourcePath);break;
                    }
                }
                if(AssetWorkspaceClassification.Classify(Relative) is null||AssetWorkspaceClassification.IsExcluded(Relative)){ continue; }
                var Inside=AssetWorkspaceQuery.IsWithin(SourcePath,Context.ProjectDirectory);
                var Provider=!Inside?Context.EngineRoot is not null&&AssetWorkspaceQuery.IsWithin(SourcePath,Context.EngineRoot)?"Engine":"Package":Relative.StartsWith("Plugins/",StringComparison.OrdinalIgnoreCase)?"ProjectPlugin":"Project";
                Sources.Add(new(){ProjectPath=Context.ProjectFilePath,ProjectRoot=Context.ProjectDirectory,SourcePath=SourcePath,
                    MetadataRoot=Context.WorkspaceRoot,RelativePath=Relative,Provider=Provider,ReadOnly=!Inside||((File.Exists(SourcePath)?File.GetAttributes(SourcePath):0)&FileAttributes.ReadOnly)!=0});
            }
            VisitItems(Item.ProjectItems,Context,Sources,Plugins);
        }
    }
    internal static string RelativePath(string Root,string PathValue){
        var Prefix=Root.TrimEnd('\\','/')+System.IO.Path.DirectorySeparatorChar;
        return PathValue.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)?PathValue.Substring(Prefix.Length).Replace('\\','/'):PathValue.Replace('\\','/');
    }
}
