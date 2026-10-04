using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// The Scene Designer's write-back over the ThirdPerson template's real scenes, compiled exactly as the build compiles
/// the template (its compile items, defines and references from MSBuild), so the engine's own component types are used.
/// Tests with stub types had missed that a startup scene lost <c>startupOrder</c>, that the VSIX rewrote unrelated
/// floats, that enum and text edits failed, and that half of the palette could not be added.
/// </summary>
public sealed class TemplateSceneEditingTests {
    private sealed record TemplateProject(string Directory,CSharpCompilation Compilation);
    private static readonly Lazy<TemplateProject>Template=new(CompileTemplate);

    private static TemplateProject CompileTemplate(){
        var Root=new DirectoryInfo(AppContext.BaseDirectory);
        while(Root is not null&&!File.Exists(Path.Combine(Root.FullName,"CSharpGameEngine.slnx"))){ Root=Root.Parent; }
        Assert.NotNull(Root);
        var Project=Path.Combine(Root!.FullName,"templates","CSharpGameEngine.Templates.ThirdPerson");
        var StartInfo=new ProcessStartInfo("dotnet",
            "msbuild \""+Path.Combine(Project,"CSharpGameEngine.Templates.ThirdPerson.csproj")+"\" -nologo -restore -target:ResolveReferences"
            +" -getItem:Compile -getItem:ReferencePath -getProperty:DefineConstants -p:Configuration=Debug"){
            UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,WorkingDirectory=Project,
        };
        string Text;
        var Error=new System.Text.StringBuilder();
        using(var Process=new Process{ StartInfo=StartInfo }){
            Process.ErrorDataReceived+=(Sender,Line)=>{ lock(Error){ Error.AppendLine(Line.Data); } };
            Process.Start();
            Process.BeginErrorReadLine();
            Text=Process.StandardOutput.ReadToEnd();
            Process.WaitForExit();
            Assert.True(Process.ExitCode==0,Text+Error);
        }
        var Split=Text.IndexOf("\"ReferencePath\"",StringComparison.Ordinal);
        var Defines=Regex.Match(Text,"\"DefineConstants\":\\s*\"([^\"]*)\"").Groups[1].Value.Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries);
        var Options=new CSharpParseOptions(LanguageVersion.Preview,preprocessorSymbols:Defines);
        var Sources=Paths(Text.Substring(0,Split)).Select(Path=>CSharpSyntaxTree.ParseText(File.ReadAllText(Path),Options,Path)).ToList();
        var References=Paths(Text.Substring(Split)).Select(static Path=>(MetadataReference)MetadataReference.CreateFromFile(Path)).ToList();
        return new TemplateProject(Project,CSharpCompilation.Create("TemplateScenes",Sources,References,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,allowUnsafe:true)));
    }

    private static IEnumerable<string>Paths(string Section)=>
        Regex.Matches(Section,"\"FullPath\":\\s*\"((?:[^\"\\\\]|\\\\.)*)\"").Cast<Match>().Select(static Match=>Regex.Unescape(Match.Groups[1].Value));

    private sealed record Scene(string Path,string Json,SceneKey Key,SceneDocumentSnapshot Snapshot,SceneSourceCommitContext Context,SceneProjectionReconciler Reconciler);

    private static IEnumerable<Scene>Scenes(){
        foreach(var JsonPath in Directory.EnumerateFiles(Path.Combine(Template.Value.Directory,"Scenes"),"*.json",SearchOption.AllDirectories)){
            var Json=File.ReadAllText(JsonPath);
            var Namespace=Regex.Match(Json,"\"namespace\":\\s*\"([^\"]*)\"").Groups[1].Value;
            var ClassName=Regex.Match(Json,"\"className\":\\s*\"([^\"]*)\"").Groups[1].Value;
            var Fqn=(Namespace.Length>0?Namespace+".":string.Empty)+ClassName;
            Assert.True(SceneJsonCommitPlanner.TryAnalyze(Json,JsonPath,Template.Value.Compilation,Fqn,CancellationToken.None,
                out var Model,out var Root,out var Compilation,out var Error),JsonPath+": "+Error);
            var Key=new SceneKey("template",Fqn);
            var Reconciler=new SceneProjectionReconciler();
            yield return new Scene(JsonPath,Json,Key,SceneDocumentSnapshot.Create(Key,"document",1,1,Model!,HasBlockingErrors:false,null,Reconciler),
                new SceneSourceCommitContext(Model!,Root!,Microsoft.CodeAnalysis.Text.SourceText.From(Json),Compilation!),Reconciler);
        }
    }

    private static string Commit(Scene Scene,SceneEditCommitRequest Request){
        var Plan=SceneSourceCommitPlanner.Plan(Scene.Context,Scene.Snapshot,Request,Scene.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,System.IO.Path.GetFileName(Scene.Path)+" "+Request.UndoDescription+": "+Plan.ErrorMessage);
        return Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString();
    }

    private static string[]ChangedLines(string Before,string After){
        var Old=Before.Replace("\r",string.Empty).Split('\n');
        var New=After.Replace("\r",string.Empty).Split('\n');
        Assert.Equal(Old.Length,New.Length);
        return New.Where((Line,Index)=>Line!=Old[Index]).ToArray();
    }

    /// <summary>Committing a member's current value changes nothing; choosing another enum member writes that one line.</summary>
    [Fact]
    public void InspectorCommits_WriteOnlyWhatChanged(){
        var Seen=new HashSet<string>(StringComparer.Ordinal);
        var Checked=0;
        foreach(var Scene in Scenes()){
            foreach(var Node in Scene.Snapshot.Projection!.Nodes){
                var Selection=new SceneSelection(Scene.Key,new[]{Node.Id},Node.Id,null,null,SceneSelectionOrigin.Inspector,1,Guid.NewGuid());
                foreach(var Member in SceneInspectorModelBuilder.Build(Scene.Snapshot,Selection).Where(static Member=>Member.IsEditable)){
                    if(!Seen.Add(Member.ComponentTypeFullyQualifiedName+"/"+string.Join(".",Member.DefinitionMemberPath))){ continue; }
                    Checked++;
                    var Same=Commit(Scene,SceneInspectorCommitPlanner.CreateCommit(Scene.Key,1,Member,Member.SourceExpression));
                    Assert.True(Same==Scene.Json,$"{System.IO.Path.GetFileName(Scene.Path)} {Node.Node.Name} {Member.MemberName}: committing the current value changed the document");
                    if(Member.EditorKind==InspectorEditorKind.Enum&&Member.Candidates.Count>1){
                        var Other=Member.Candidates.First(Choice=>!SceneInspectorCommitPlanner.IsCurrentChoice(Member,Choice.Name));
                        Assert.True(SceneInspectorCommitPlanner.TryCreateChoiceCommit(Scene.Key,1,Member,Other.Name,out var Request));
                        var Changed=ChangedLines(Scene.Json,Commit(Scene,Request));
                        Assert.True(Changed.Length==1&&Changed[0].Contains("\""+Other.Name+"\""),$"{Node.Node.Name} {Member.MemberName}: {string.Join(" | ",Changed)}");
                    }
                }
            }
        }
        Assert.True(Checked>50,$"only {Checked} members were checked");
    }

    /// <summary>Every palette component can be added to a template scene (a LightShadow needs its light, so it is added with one).</summary>
    [Fact]
    public void EveryPaletteComponent_CanBeAddedWithItsDefaults(){
        var Compilation=Template.Value.Compilation;
        var Scene=Scenes().First(static Scene=>Scene.Path.EndsWith("ThirdPersonGameMode.json",StringComparison.Ordinal));
        var Target=Scene.Snapshot.Projection!.Nodes.First(static Node=>Node.ParentId is null);
        var Catalog=SceneComponentCatalog.Build(Compilation);
        Assert.NotEmpty(Catalog);
        foreach(var Entry in Catalog.Where(Entry=>Target.Node.FindComponent(Entry.FullyQualifiedName) is null)){
            var Operations=new List<SceneEditOperation>();
            void Add(string Type){
                if(Target.Node.FindComponent(Type) is not null||Operations.OfType<AddComponentOperation>().Any(Operation=>Operation.ComponentTypeFullyQualifiedName==Type)){ return; }
                var Symbol=Compilation.GetTypeByMetadataName(Type.Replace("global::",string.Empty))!;
                var Plan=SceneComponentInitializerPolicy.CreatePlan(Symbol,Type,Compilation);
                Operations.Add(new AddComponentOperation(Scene.Key,Target.Id,1,Type,Plan.Expression,Plan.Members,SceneComponentOrdering.GetSortKey(Symbol,Type)));
            }
            if(Entry.FullyQualifiedName==LightComponentContract.ShadowType){ Add(SceneComponentBindings.TransformTypeFullyQualifiedName);Add("global::CSharpGameEngine.Runtime.Lighting.PointLight"); }
            if(LightComponentContract.IsLight(Entry.FullyQualifiedName)){ Add(SceneComponentBindings.TransformTypeFullyQualifiedName); }
            Add(Entry.FullyQualifiedName);
            var After=Commit(Scene,new SceneEditCommitRequest(Scene.Key,1,"Add "+Entry.DisplayName,Array.Empty<SceneTransformChange>(),SceneTransformMemberMask.None,DirectOperations:Operations));
            Assert.Contains("\"type\": \""+Entry.FullyQualifiedName.Replace("global::",string.Empty)+"\"",After);
        }
    }

    /// <summary>A startup scene keeps <c>startupOrder</c> through an edit, and an unedited write-back is byte-identical.</summary>
    [Fact]
    public void MovesKeepStartupOrderAndWriteOneLine(){
        foreach(var Scene in Scenes()){
            var Node=Scene.Snapshot.Projection!.Nodes.FirstOrDefault(static Node=>SceneTransformBinding.TryRead(Node.Node,out var Local)&&Local.Position!=System.Numerics.Vector3.Zero);
            if(Node is null){ continue; }
            Assert.True(SceneTransformBinding.TryRead(Node.Node,out var Original));
            var Final=new SceneTransformValue(Original.Position+new System.Numerics.Vector3(1f,0f,0f),Original.EulerRotation,Original.Scale);
            var After=Commit(Scene,new SceneEditCommitRequest(Scene.Key,1,"Move",new[]{new SceneTransformChange(Node.Id,Original,Final)},SceneTransformMemberMask.Position));
            var Changed=ChangedLines(Scene.Json,After);
            Assert.True(Changed.Length==1&&Changed[0].Contains("\"Position\""),System.IO.Path.GetFileName(Scene.Path)+": "+string.Join(" | ",Changed));
            Assert.Equal(Scene.Json.Contains("\"startupOrder\""),After.Contains("\"startupOrder\""));
        }
    }
}
