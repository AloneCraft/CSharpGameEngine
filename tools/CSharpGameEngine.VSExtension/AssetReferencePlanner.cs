using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using CSharpGameEngine.ShaderGraph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
internal sealed record AssetReferenceEdit(string Path,string Before,string After);
internal sealed class AssetReferencePlan {
    internal AssetFileMutationRequest Mutation{get;set;}=new();
    internal string WorkspaceRoot{get;set;}=string.Empty;
    internal List<AssetReferenceEdit>Edits{get;}=[];
    internal Dictionary<string,string>ValidationReads{get;}=new(StringComparer.OrdinalIgnoreCase);
    internal VersionStamp?SolutionVersion{get;set;}
    internal string Description=>this.Mutation.Operation+": "+this.Mutation.Source+Environment.NewLine+this.Mutation.Destination+Environment.NewLine
        +string.Join(Environment.NewLine,this.Edits.Select(static Edit=>Edit.Path));
}
internal static class AssetReferencePlanner {
    internal static bool InCatalog(string PathValue){
        var Parts=PathValue.Replace('\\','/').Split('/');
        return (Parts.Length>1&&Parts[0]=="Assets"||Parts.Length>3&&Parts[0]=="Plugins"&&Parts[2]=="Assets")
            &&Path.GetExtension(PathValue).ToLowerInvariant() is ".wav" or ".glb" or ".gltf" or ".fbx" or ".png" or ".jpg" or ".jpeg" or ".cgefont" or ".cgeasset";
    }
    internal static async Task<AssetReferencePlan>PlanAsync(AssetWorkspaceItem Item,string Destination,AssetFileOperation Operation,CancellationToken CancellationToken,AssetWorkspaceItem?Replacement=null){
        if(Operation is not(AssetFileOperation.Inspect or AssetFileOperation.Duplicate)&&Item.Source.ReadOnly){ throw new InvalidOperationException(VsixText.Get("Assets_ReadOnly")); }
        var Context=CgeProjectContext.TryCreate(Item.Source.ProjectPath)??throw new InvalidOperationException("Game project unavailable.");
        var Result=new AssetReferencePlan{WorkspaceRoot=Context.WorkspaceRoot,Mutation=new(){RequestId=Guid.NewGuid(),Root=Context.ProjectDirectory,
            MetadataRoot=Context.WorkspaceRoot,Source=Item.Source.SourcePath,Destination=Destination,Operation=Operation,OperationId=Guid.NewGuid()}};
        if(Operation!=AssetFileOperation.Inspect){
            var Snapshot=await Task.Run(()=>AssetAuthoringHostClient.MutateFiles(Result.Mutation,Context.WorkspaceRoot),CancellationToken);
            Result.Mutation.ExpectedSourceHash=Snapshot.SourceHash;Result.Mutation.ExpectedMetadataHash=Snapshot.MetadataHash;
        }
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        var Items=AssetsToolWindowPane.Controller?.Items??[];
        if(Operation is AssetFileOperation.Move or AssetFileOperation.Rename or AssetFileOperation.Delete
            &&Items.Any(Other=>Other.Source.SourcePath==Item.Source.SourcePath&&Other.Source.ProjectPath!=Item.Source.ProjectPath)){
            throw new InvalidOperationException("ASSET_SHARED_SOURCE: organize this asset separately from the other loaded project.");
        }
        var OldRelative=Item.Source.RelativePath;
        if(Operation is AssetFileOperation.Move or AssetFileOperation.Rename or AssetFileOperation.Delete){
            var ProjectSource=AssetDocumentWorkspace.Read(Item.Source.ProjectPath);
            Result.ValidationReads[Item.Source.ProjectPath]=ProjectSource;
            var ProjectXml=System.Xml.Linq.XDocument.Parse(ProjectSource);
            if(ProjectXml.Descendants().SelectMany(static Element=>Element.Attributes()).Any(Attribute=>Attribute.Name.LocalName is "Include" or "Update" or "Remove"
                &&Attribute.Value.Split(';').Any(Value=>Value.Replace('\\','/')==OldRelative||string.Equals(Value,Item.Source.SourcePath,StringComparison.OrdinalIgnoreCase)))){
                throw new InvalidOperationException("ASSET_PROJECT_ITEM_REFERENCE: this explicitly included source requires a project-item refactoring: "+Item.Source.ProjectPath);
            }
        }
        var NewRelative=Replacement?.Source.RelativePath??AssetWorkspaceProjectItems.RelativePath(Context.ProjectDirectory,Destination);
        if(Operation is AssetFileOperation.Move or AssetFileOperation.Rename or AssetFileOperation.Duplicate&&InCatalog(OldRelative)&&!InCatalog(NewRelative)){
            throw new InvalidOperationException("ASSET_CATALOG_DESTINATION: keep runtime assets inside Assets or a project plugin's Assets folder.");
        }
        if(Replacement is not null&&(Replacement.Kind!=Item.Kind||Replacement.Source.ProjectPath!=Item.Source.ProjectPath||Replacement.Source.SourcePath==Item.Source.SourcePath)){
            throw new InvalidOperationException("ASSET_REPLACEMENT_TYPE_MISMATCH");
        }
        if(Replacement is not null&&Item.Kind==AssetWorkspaceKind.Data
            &&SceneJsonReader.Parse(AssetDocumentWorkspace.Read(Item.Source.SourcePath)).Get("type")?.Text!=SceneJsonReader.Parse(AssetDocumentWorkspace.Read(Replacement.Source.SourcePath)).Get("type")?.Text){
            throw new InvalidOperationException("ASSET_REPLACEMENT_TYPE_MISMATCH");
        }
        if(Operation==AssetFileOperation.Duplicate&&Item.Kind is AssetWorkspaceKind.Scene or AssetWorkspaceKind.ShaderGraph){
            var Original=AssetDocumentWorkspace.Read(Item.Source.SourcePath);
            if(Item.Kind==AssetWorkspaceKind.Scene){
                var Root=SceneJsonReader.Parse(Original);var Class=Root.Get("className")??throw new InvalidDataException("Missing scene className.");
                var Name=Path.GetFileNameWithoutExtension(Destination);
                if(!SyntaxFacts.IsValidIdentifier(Name)){ throw new InvalidOperationException("Invalid scene name."); }
                Result.Mutation.Content=Original.Substring(0,Class.Start)+System.Text.Json.JsonSerializer.Serialize(Name)+Original.Substring(Class.End);
            } else {
                var Graph=ShaderGraphCanonicalization.FromJson(Original)??throw new InvalidDataException("Invalid graph.");
                var Clone=ShaderGraphTemplateWizard.Remap(Graph);
                for(var Index=0;Index<Clone.Dependencies.Count;Index++){
                    Clone.Dependencies[Index].Id=Graph.Dependencies[Index].Id;
                    Clone.Dependencies[Index].Path=MakeRelative(Path.GetDirectoryName(Destination)!,Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Item.Source.SourcePath)!,Graph.Dependencies[Index].Path)));
                }
                Result.Mutation.Content=ShaderGraphCanonicalization.ToCanonicalJson(Clone);
            }
        }
        if(Operation is AssetFileOperation.Move or AssetFileOperation.Rename&&Item.Kind==AssetWorkspaceKind.ShaderGraph){
            var Original=AssetDocumentWorkspace.Read(Item.Source.SourcePath);
            Result.ValidationReads[Item.Source.SourcePath]=Original;
            Result.Mutation.Content=RelocateGraph(Original,Item.Source.SourcePath,Destination);
        }
        var Paths=Items.Where(Candidate=>Candidate.Source.ProjectPath==Item.Source.ProjectPath&&InCatalog(Candidate.Source.RelativePath)).Select(static Candidate=>Candidate.Source.RelativePath).ToArray();
        var OldMap=AssetCatalogLayout.Map(Paths);
        var NewPaths=Operation switch{
            AssetFileOperation.Rename or AssetFileOperation.Move=>Paths.Select(Value=>Value==OldRelative?NewRelative:Value),
            AssetFileOperation.Delete=>Paths.Where(Value=>Value!=OldRelative),
            AssetFileOperation.Duplicate=>Paths.Concat(new[]{NewRelative}),
            _=>Paths.AsEnumerable(),
        };
        var NewMap=AssetCatalogLayout.Map(NewPaths);
        var Renames=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var Pair in OldMap){
            var NewKey=Pair.Key==OldRelative&&Operation is AssetFileOperation.Rename or AssetFileOperation.Move?NewRelative:Pair.Key;
            if(NewMap.TryGetValue(NewKey,out var Name)&&Name!=Pair.Value){ Renames[Pair.Value]=Name; }
        }
        if(OldMap.TryGetValue(OldRelative,out var OldName)&&Operation is AssetFileOperation.Inspect or AssetFileOperation.Delete){
            Renames[OldName]=Replacement is not null&&NewMap.TryGetValue(Replacement.Source.RelativePath,out var ReplacementSymbol)?ReplacementSymbol:OldName;
        }
        var NewContainers=new HashSet<string>(NewMap.Values.SelectMany(ContainerNames),StringComparer.Ordinal);
        foreach(var Container in OldMap.Values.SelectMany(ContainerNames).Distinct(StringComparer.Ordinal).Where(Container=>!NewContainers.Contains(Container))){
            var Targets=OldMap.Values.Where(Name=>Name.StartsWith(Container+".",StringComparison.Ordinal)&&Renames.ContainsKey(Name)).Select(Name=>Renames[Name]).Select(static Name=>Name.Substring(0,Name.LastIndexOf('.'))).Distinct(StringComparer.Ordinal).ToArray();
            if(Targets.Length==1){ Renames[Container]=Targets[0]; }
            else { Renames[Container]=Container; }
        }
        var Solution=AssetDocumentWorkspace.Workspace.CurrentSolution;
        Result.SolutionVersion=Solution.Version;
        foreach(var Project in Solution.Projects){
            if(Project.FilePath!=Item.Source.ProjectPath){ continue; }
            foreach(var Document in Project.Documents){
                CancellationToken.ThrowIfCancellationRequested();
                if(Document.FilePath is not { }FilePath||AssetWorkspaceClassification.IsExcluded(FilePath)){ continue; }
                var Root=await Document.GetSyntaxRootAsync(CancellationToken);
                var Semantic=await Document.GetSemanticModelAsync(CancellationToken);
                if(Root is null||Semantic is null){ continue; }
                var Text=await Document.GetTextAsync(CancellationToken);
                var Changed=RewriteCSharp(Text,Root,Semantic,Renames,out var HasReferences,CancellationToken);
                var Changes=new List<TextChange>();
                foreach(var Literal in Root.DescendantNodes().OfType<LiteralExpressionSyntax>().Where(static Literal=>Literal.IsKind(SyntaxKind.StringLiteralExpression))){
                    if((Literal.Token.ValueText!=OldRelative&&!Literal.Token.ValueText.StartsWith(OldRelative+":",StringComparison.Ordinal)&&Literal.Token.ValueText!=Item.Source.SourcePath)||Operation==AssetFileOperation.Duplicate){ continue; }
                    if(Operation==AssetFileOperation.Inspect||Operation==AssetFileOperation.Delete&&Replacement is null){ Changes.Add(new(Literal.Span,Literal.ToString()));continue; }
                    throw new InvalidOperationException("ASSET_REFERENCE_UNRESOLVED: use a typed asset reference before reorganizing: "+FilePath);
                }
                if(HasReferences||Changes.Count>0){ Result.Edits.Add(new(FilePath,Text.ToString(),Changed)); }
            }
        }
        foreach(var Candidate in Items.Where(Candidate=>Candidate.Source.ProjectPath==Item.Source.ProjectPath&&Candidate.Kind is AssetWorkspaceKind.Scene or AssetWorkspaceKind.Data or AssetWorkspaceKind.ShaderGraph)){
            CancellationToken.ThrowIfCancellationRequested();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
            var Before=AssetDocumentWorkspace.Read(Candidate.Source.SourcePath);
            Result.ValidationReads[Candidate.Source.SourcePath]=Before;
            var RewriteOperation=Replacement is not null?AssetFileOperation.Move:Operation;
            var OwnRelocation=Candidate.Source.SourcePath==Item.Source.SourcePath&&Operation is AssetFileOperation.Move or AssetFileOperation.Rename&&Result.Mutation.Content.Length>0;
            var BaseSource=OwnRelocation?Result.Mutation.Content:Before;
            var After=RewriteJson(BaseSource,OldRelative,NewRelative,Renames,Candidate.Kind,OwnRelocation?Destination:Candidate.Source.SourcePath,Item.Source.SourcePath,Replacement?.Source.SourcePath??Destination,RewriteOperation,out var Found);
            if(Found||BaseSource!=Before){ Result.Edits.Add(new(Candidate.Source.SourcePath,Before,After)); }
        }
        if(Operation==AssetFileOperation.Delete&&Replacement is null&&Result.Edits.Count>0){ throw new InvalidOperationException("ASSET_REFERENCED: "+string.Join(Environment.NewLine,Result.Edits.Select(static Edit=>Edit.Path))); }
        Result.Mutation.FileMutationCommand=2;
        return Result;
    }
    internal static string RelocateGraph(string Original,string Source,string Destination){
        if(string.Equals(Path.GetDirectoryName(Source),Path.GetDirectoryName(Destination),StringComparison.OrdinalIgnoreCase)){ return Original; }
        var Graph=ShaderGraphCanonicalization.FromJson(Original)??throw new InvalidDataException("Invalid graph.");
        foreach(var Dependency in Graph.Dependencies){
            Dependency.Path=MakeRelative(Path.GetDirectoryName(Destination)!,Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source)!,Dependency.Path)));
        }
        return ShaderGraphCanonicalization.ToCanonicalJson(Graph);
    }
    private static IEnumerable<string>ContainerNames(string Name){
        for(var Index=Name.IndexOf('.');Index>=0;Index=Name.IndexOf('.',Index+1)){ yield return Name.Substring(0,Index); }
    }
    internal static string RewriteCSharp(SourceText Text,SyntaxNode Root,SemanticModel Semantic,IReadOnlyDictionary<string,string>Renames,out bool Found,CancellationToken CancellationToken=default){
        var Changes=new List<TextChange>();
        foreach(var Node in Root.DescendantNodes().OfType<IdentifierNameSyntax>()){
            var Symbol=Semantic.GetSymbolInfo(Node,CancellationToken).Symbol;
            var Name=Symbol?.ToDisplayString().Replace("global::",string.Empty);
            if(Symbol is not(IFieldSymbol or IPropertySymbol or INamedTypeSymbol)||Name is null||!Renames.TryGetValue(Name,out var NewName)){ continue; }
            SyntaxNode Target=Node.Parent switch{
                MemberAccessExpressionSyntax Access when Access.Name==Node=>Access,
                QualifiedNameSyntax Qualified when Qualified.Right==Node=>Qualified,
                _=>Node,
            };
            Changes.Add(new(Target.Span,"global::"+NewName));
        }
        foreach(var Access in Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()){
            if(Renames.ContainsKey(Access.ToString().Replace("global::",string.Empty))&&Semantic.GetSymbolInfo(Access,CancellationToken).Symbol is null){ throw new InvalidOperationException("ASSET_REFERENCE_UNRESOLVED: "+Root.SyntaxTree.FilePath); }
        }
        Found=Changes.Count>0;
        var NonOverlapping=new List<TextChange>();
        foreach(var Change in Changes.OrderByDescending(static Change=>Change.Span.Length)){
            if(!NonOverlapping.Any(Existing=>Existing.Span.OverlapsWith(Change.Span))){ NonOverlapping.Add(Change); }
        }
        return Text.WithChanges(NonOverlapping).ToString();
    }
    internal static string RewriteJson(string Before,string OldPath,string NewPath,IReadOnlyDictionary<string,string>Symbols,
        AssetWorkspaceKind Kind,string DocumentPath,string OldAbsolute,string NewAbsolute,AssetFileOperation Operation,out bool Found){
        var Root=SceneJsonReader.Parse(Before);
        if(Kind==AssetWorkspaceKind.ShaderGraph){ _=ShaderGraphCanonicalization.FromJson(Before)??throw new InvalidDataException("Invalid graph."); }
        var Changes=new List<TextChange>();
        void Visit(SceneJsonValue Value,string Key){
            if(Value.Kind==SceneJsonKind.String&&Value.Text is { }Text){
                string?Replacement=null;
                if((Text==OldPath||Text.StartsWith(OldPath+":",StringComparison.Ordinal))&&Operation!=AssetFileOperation.Duplicate){ Replacement=NewPath+Text.Substring(OldPath.Length); }
                else if(Symbols.TryGetValue(Text.Replace("global::",string.Empty),out var NewSymbol)){ Replacement=NewSymbol; }
                else if(Kind==AssetWorkspaceKind.ShaderGraph&&Key=="path"&&Operation!=AssetFileOperation.Duplicate){
                    var Absolute=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(DocumentPath)!,Text));
                    if(string.Equals(Absolute,OldAbsolute,StringComparison.OrdinalIgnoreCase)){
                        Replacement=MakeRelative(Path.GetDirectoryName(DocumentPath)!,NewAbsolute);
                    }
                }
                if(Replacement is not null){
                    if(Kind!=AssetWorkspaceKind.ShaderGraph&&Key is not("AssetPath" or "MeshAssetPath" or "TexturePath" or "Font" or "FontPath" or "SoundPath" or "Path" or "asset" or "ref")){
                        throw new InvalidOperationException("ASSET_REFERENCE_UNRESOLVED: "+DocumentPath+" : "+Key);
                    }
                    Changes.Add(new(new TextSpan(Value.Start,Value.End-Value.Start),Operation is AssetFileOperation.Inspect or AssetFileOperation.Delete?Before.Substring(Value.Start,Value.End-Value.Start):System.Text.Json.JsonSerializer.Serialize(Replacement)));
                }
            }
            foreach(var Property in Value.Properties){ Visit(Property.Value,Property.Name); }
            foreach(var Child in Value.Items){ Visit(Child,Key); }
        }
        Visit(Root,string.Empty);Found=Changes.Count>0;
        return SourceText.From(Before).WithChanges(Changes).ToString();
    }
    private static string MakeRelative(string Directory,string PathValue)=>Uri.UnescapeDataString(new Uri(Directory.TrimEnd('\\','/')+Path.DirectorySeparatorChar).MakeRelativeUri(new Uri(PathValue)).ToString());
}
