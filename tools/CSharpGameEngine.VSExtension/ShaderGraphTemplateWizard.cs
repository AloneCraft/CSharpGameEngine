using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CSharpGameEngine.ShaderGraph;
using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
public sealed class ShaderGraphTemplateWizard:IWizard {
    private ShaderGraphTargetKind Target=ShaderGraphTargetKind.Stage;
    public void RunStarted(object AutomationObject,Dictionary<string,string>ReplacementsDictionary,WizardRunKind RunKind,object[]CustomParams) {
        System.Windows.Window Dialog=new() { Title=VsixText.Get("ShaderGraphWizardTitle"),Width=360,Height=190,WindowStartupLocation=WindowStartupLocation.CenterScreen,ResizeMode=ResizeMode.NoResize };
        StackPanel Panel=new() { Margin=new Thickness(16) };
        Panel.Children.Add(new TextBlock { Text=VsixText.Get("ShaderGraphWizardPrompt"),Margin=new Thickness(0,0,0,8) });
        ComboBox TargetPicker=new() { ItemsSource=new[]{ShaderGraphTargetKind.Stage,ShaderGraphTargetKind.Model,ShaderGraphTargetKind.Triangle},SelectedItem=ShaderGraphTargetKind.Stage,Margin=new Thickness(0,0,0,16) };
        Panel.Children.Add(TargetPicker);
        Button Create=new() { Content=VsixText.Get("ShaderGraphWizardCreate"),Width=90,HorizontalAlignment=HorizontalAlignment.Right,IsDefault=true };
        Create.Click+=(Sender,Arguments)=>{ this.Target=(ShaderGraphTargetKind)TargetPicker.SelectedItem;Dialog.DialogResult=true; };
        Panel.Children.Add(Create);
        Dialog.Content=Panel;
        if(Dialog.ShowDialog()!=true) { throw new WizardCancelledException(); }
    }
    public void ProjectItemFinishedGenerating(ProjectItem ProjectItem) {
        ThreadHelper.ThrowIfNotOnUIThread();
        var Destination=ProjectItem.FileNames[1];
        var TemplateRoot=Path.Combine(Path.GetDirectoryName(typeof(ShaderGraphTemplateWizard).Assembly.Location)!,"ShaderGraphTemplates");
        var Source=Path.Combine(TemplateRoot,this.Target+".cgeshadergraph");
        var Graph=ShaderGraphCanonicalization.FromJson(File.ReadAllText(Source))??throw new InvalidDataException("Shader Graph template is invalid.");
        var Remapped=Remap(Graph);
        File.WriteAllText(Destination,ShaderGraphCanonicalization.ToCanonicalJson(Remapped));
    }
    public void ProjectFinishedGenerating(Project Project) { }
    public void BeforeOpeningFile(ProjectItem ProjectItem) { }
    public void RunFinished() { }
    public bool ShouldAddProjectItem(string FilePath)=>true;
    internal static ShaderGraphDocument Remap(ShaderGraphDocument Source) {
        var IdMap=new Dictionary<StableId,StableId>();
        StableId Map(StableId Id) { if(!IdMap.TryGetValue(Id,out var Mapped)){Mapped=StableId.New();IdMap.Add(Id,Mapped);}return Mapped; }
        var Result=new ShaderGraphDocument { Version=Source.Version,Id=StableId.New(),Target=new ShaderGraphTarget { Kind=Source.Target.Kind,Name=Source.Target.Name },EntryPoint=Source.EntryPoint is null?null:new ShaderGraphEntryPointContract { TargetKind=Source.EntryPoint.TargetKind,Name=Source.EntryPoint.Name,ReturnType=Source.EntryPoint.ReturnType,ReturnSemantic=Source.EntryPoint.ReturnSemantic },RootSubgraphId=Map(Source.RootSubgraphId),Layout=Source.Layout is null?null:new ShaderGraphEditorLayout() };
        foreach(var Subgraph in Source.Subgraphs) {
            var Clone=new ShaderGraphSubgraph { Id=Map(Subgraph.Id),Name=Subgraph.Name,Inputs=Subgraph.Inputs.Select(Port=>new ShaderGraphPort { Name=Port.Name,Type=Port.Type }).ToList(),Outputs=Subgraph.Outputs.Select(Port=>new ShaderGraphPort { Name=Port.Name,Type=Port.Type }).ToList() };
            foreach(var Node in Subgraph.Nodes) { Clone.Nodes.Add(new ShaderGraphNode { Id=Map(Node.Id),Type=Node.Type,Name=Node.Name,Arguments=Node.Arguments.Select(Argument=>new ShaderGraphArgument { Name=Argument.Name,Value=Argument.Value }).ToList() }); }
            foreach(var Edge in Subgraph.Edges) { Clone.Edges.Add(new ShaderGraphEdge { Id=Map(Edge.Id),FromNodeId=Map(Edge.FromNodeId),FromPort=Edge.FromPort,ToNodeId=Map(Edge.ToNodeId),ToPort=Edge.ToPort }); }
            foreach(var Region in Subgraph.Regions) { Clone.Regions.Add(new ShaderGraphRegion { Id=Map(Region.Id),Name=Region.Name,NodeIds=Region.NodeIds.Select(Map).ToList() }); }
            Result.Subgraphs.Add(Clone);
        }
        if(Source.Layout is not null&&Result.Layout is not null) {
            foreach(var Node in Source.Layout.Nodes) { Result.Layout.Nodes.Add(new ShaderGraphNodeLayout { NodeId=Map(Node.NodeId),X=Node.X,Y=Node.Y }); }
            foreach(var Region in Source.Layout.Regions) { Result.Layout.Regions.Add(new ShaderGraphRegionLayout { RegionId=Map(Region.RegionId),X=Region.X,Y=Region.Y,Width=Region.Width,Height=Region.Height }); }
        }
        return Result;
    }
}