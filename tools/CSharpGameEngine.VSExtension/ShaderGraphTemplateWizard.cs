using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSharpGameEngine.ShaderGraph;
using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
public sealed class ShaderGraphTemplateWizard:IWizard {
    public void RunStarted(object AutomationObject,Dictionary<string,string>ReplacementsDictionary,WizardRunKind RunKind,object[]CustomParams) { }
    public void ProjectItemFinishedGenerating(ProjectItem ProjectItem) {
        ThreadHelper.ThrowIfNotOnUIThread();
        var Destination=ProjectItem.FileNames[1];
        var TemplateRoot=Path.Combine(Path.GetDirectoryName(typeof(ShaderGraphTemplateWizard).Assembly.Location)!,"ShaderGraphTemplates");
        var Source=Path.Combine(TemplateRoot,"Stage.cgeshadergraph");
        var Graph=ShaderGraphCanonicalization.FromJson(File.ReadAllText(Source))??throw new InvalidDataException("Shader Graph template is invalid.");
        var CreatedDependencies=new List<string>();
        var Remapped=RemapBundle(Graph,TemplateRoot,Destination,CreatedDependencies.Add);
        foreach(var DependencyPath in CreatedDependencies) { ProjectItem.Collection.AddFromFile(DependencyPath); }
        File.WriteAllText(Destination,ShaderGraphCanonicalization.ToCanonicalJson(Remapped));
    }
    public void ProjectFinishedGenerating(Project Project) { }
    public void BeforeOpeningFile(ProjectItem ProjectItem) { }
    public void RunFinished() { }
    public bool ShouldAddProjectItem(string FilePath)=>true;
    internal static ShaderGraphDocument Remap(ShaderGraphDocument Source)=>Remap(Source,new Dictionary<StableId,StableId>());
    internal static ShaderGraphDocument RemapBundle(ShaderGraphDocument Source,string SourceDirectory,string Destination,Action<string>?DependencyCreated=null) {
        var IdMap=new Dictionary<StableId,StableId>();
        var Result=Remap(Source,IdMap);
        var DestinationDirectory=Path.GetDirectoryName(Path.GetFullPath(Destination))!;
        var DestinationStem=Path.GetFileNameWithoutExtension(Destination);
        var CopiedPaths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var OutputNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var Stack=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string CopyDependency(string DependencySourcePath,StableId ExpectedId) {
            var FullSourcePath=Path.GetFullPath(DependencySourcePath);
            if(CopiedPaths.TryGetValue(FullSourcePath,out var ExistingName)) { return ExistingName; }
            if(!Stack.Add(FullSourcePath)) { throw new InvalidDataException("Shader Graph template dependency recursion: "+FullSourcePath); }
            try {
                if(!File.Exists(FullSourcePath)) { throw new FileNotFoundException("Shader Graph template dependency is missing.",FullSourcePath); }
                var Dependency=ShaderGraphCanonicalization.FromJson(File.ReadAllText(FullSourcePath))??throw new InvalidDataException("Shader Graph template dependency is invalid: "+FullSourcePath);
                if(Dependency.Id!=ExpectedId) { throw new InvalidDataException($"Shader Graph template dependency identity mismatch for '{FullSourcePath}'."); }
                var RemappedDependency=Remap(Dependency,IdMap);
                var OutputName=DestinationStem+"."+Path.GetFileName(FullSourcePath);
                if(!OutputNames.Add(OutputName)) { OutputName=DestinationStem+"."+Dependency.Id.ToString().Substring(0,8)+"."+Path.GetFileName(FullSourcePath);OutputNames.Add(OutputName); }
                CopiedPaths.Add(FullSourcePath,OutputName);
                for(var Index=0;Index<Dependency.Dependencies.Count;Index++) {
                    var SourceReference=Dependency.Dependencies[Index];
                    RemappedDependency.Dependencies[Index].Path=CopyDependency(Path.Combine(Path.GetDirectoryName(FullSourcePath)!,SourceReference.Path),SourceReference.Id);
                }
                var OutputPath=Path.Combine(DestinationDirectory,OutputName);
                File.WriteAllText(OutputPath,ShaderGraphCanonicalization.ToCanonicalJson(RemappedDependency));
                DependencyCreated?.Invoke(OutputPath);
                return OutputName;
            } finally { Stack.Remove(FullSourcePath); }
        }
        for(var Index=0;Index<Source.Dependencies.Count;Index++) {
            var Reference=Source.Dependencies[Index];
            Result.Dependencies[Index].Path=CopyDependency(Path.Combine(SourceDirectory,Reference.Path),Reference.Id);
        }
        return Result;
    }
    private static ShaderGraphDocument Remap(ShaderGraphDocument Source,Dictionary<StableId,StableId>IdMap) {
        StableId Map(StableId Id) { if(!IdMap.TryGetValue(Id,out var Mapped)){Mapped=StableId.New();IdMap.Add(Id,Mapped);}return Mapped; }
        StableId MapOptional(StableId Id)=>Id.IsEmpty?default:Map(Id);
        var Result=new ShaderGraphDocument { Version=Source.Version,Id=Map(Source.Id),Target=new ShaderGraphTarget { Kind=Source.Target.Kind,Name=Source.Target.Name },EntryPoint=Source.EntryPoint is null?null:new ShaderGraphEntryPointContract { TargetKind=Source.EntryPoint.TargetKind,Name=Source.EntryPoint.Name,ReturnType=Source.EntryPoint.ReturnType,ReturnSemantic=Source.EntryPoint.ReturnSemantic },RootSubgraphId=Map(Source.RootSubgraphId),Dependencies=Source.Dependencies.Select(Dependency=>new ShaderGraphDependency { Id=Map(Dependency.Id),Path=Dependency.Path }).ToList(),Layout=Source.Layout is null?null:new ShaderGraphEditorLayout() };
        foreach(var Subgraph in Source.Subgraphs) {
            var Clone=new ShaderGraphSubgraph { Id=Map(Subgraph.Id),Name=Subgraph.Name,Inputs=Subgraph.Inputs.Select(Port=>new ShaderGraphPort { Name=Port.Name,Type=Port.Type }).ToList(),Outputs=Subgraph.Outputs.Select(Port=>new ShaderGraphPort { Name=Port.Name,Type=Port.Type }).ToList() };
            foreach(var Node in Subgraph.Nodes) { Clone.Nodes.Add(new ShaderGraphNode { Id=Map(Node.Id),Type=Node.Type,Name=Node.Name,Arguments=Node.Arguments.Select(Argument=>new ShaderGraphArgument { Name=Argument.Name,Value=Argument.Value }).ToList() }); }
            foreach(var Edge in Subgraph.Edges) { Clone.Edges.Add(new ShaderGraphEdge { Id=Map(Edge.Id),FromNodeId=Map(Edge.FromNodeId),FromPort=Edge.FromPort,ToNodeId=Map(Edge.ToNodeId),ToPort=Edge.ToPort }); }
            foreach(var Region in Subgraph.Regions) { Clone.Regions.Add(new ShaderGraphRegion { Id=Map(Region.Id),Kind=Region.Kind,Name=Region.Name,ControlNodeId=MapOptional(Region.ControlNodeId),ParentRegionId=MapOptional(Region.ParentRegionId),ParentBranch=Region.ParentBranch,NodeIds=Region.NodeIds.Select(Map).ToList(),ElseNodeIds=Region.ElseNodeIds.Select(Map).ToList() }); }
            Result.Subgraphs.Add(Clone);
        }
        if(Source.Layout is not null&&Result.Layout is not null) {
            foreach(var Node in Source.Layout.Nodes) { Result.Layout.Nodes.Add(new ShaderGraphNodeLayout { NodeId=Map(Node.NodeId),X=Node.X,Y=Node.Y }); }
            foreach(var Region in Source.Layout.Regions) { Result.Layout.Regions.Add(new ShaderGraphRegionLayout { RegionId=Map(Region.RegionId),X=Region.X,Y=Region.Y,Width=Region.Width,Height=Region.Height }); }
        }
        return Result;
    }
}
