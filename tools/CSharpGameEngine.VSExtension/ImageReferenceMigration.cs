using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>旧形式の画像フィールドが型付き出力グループになるときの意味的編集をプレビューします。</summary>
internal static class ImageReferenceMigration {
    internal static void ValidateOwnership(AssetWorkspaceItem Item,IEnumerable<AssetWorkspaceItem>Items){
        if(Item.Source.ReadOnly||Items.Any(Other=>string.Equals(Other.Source.SourcePath,Item.Source.SourcePath,StringComparison.OrdinalIgnoreCase)&&Other.Source.ProjectPath!=Item.Source.ProjectPath)){
            throw new InvalidOperationException("ASSET_SHARED_SOURCE: image output refactoring requires a single editable project owner.");
        }
    }
    internal static async Task<AssetReferencePlan>PlanAsync(AssetWorkspaceItem Item,string Metadata,string TextureMember,CancellationToken CancellationToken){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        ValidateOwnership(Item,AssetsToolWindowPane.Controller?.Items??[]);
        var Context=CgeProjectContext.TryCreate(Item.Source.ProjectPath)??throw new InvalidOperationException("Game project unavailable.");
        var Solution=AssetDocumentWorkspace.Workspace.CurrentSolution;
        var Plan=new AssetReferencePlan{WorkspaceRoot=Context.WorkspaceRoot,SolutionVersion=Solution.Version,
            Mutation=new(){FileMutationCommand=2,RequestId=Guid.NewGuid(),OperationId=Guid.NewGuid(),Root=Context.ProjectDirectory,MetadataRoot=Context.WorkspaceRoot,
                Operation=AssetFileOperation.Create,Destination=Item.Source.SourcePath+".meta",Content=Metadata}};
        var Paths=(AssetsToolWindowPane.Controller?.Items??[]).Where(I=>I.Source.ProjectPath==Item.Source.ProjectPath&&AssetReferencePlanner.InCatalog(I.Source.RelativePath)).Select(I=>I.Source.RelativePath).Append(Item.Source.RelativePath);
        var Map=AssetCatalogLayout.Map(Paths);
        if(!Map.TryGetValue(Item.Source.RelativePath,out var Old)){ return Plan; }
        var Renames=new Dictionary<string,string>(StringComparer.Ordinal){{Old,Old+"."+TextureMember}};
        foreach(var Project in Solution.Projects.Where(P=>P.FilePath==Item.Source.ProjectPath)){
            foreach(var Document in Project.Documents){
                if(Document.FilePath is not { }PathValue||AssetWorkspaceClassification.IsExcluded(PathValue)){ continue; }
                var Syntax=await Document.GetSyntaxRootAsync(CancellationToken);var Semantic=await Document.GetSemanticModelAsync(CancellationToken);
                if(Syntax is null||Semantic is null){ continue; }var Text=await Document.GetTextAsync(CancellationToken);
                var After=AssetReferencePlanner.RewriteCSharp(Text,Syntax,Semantic,Renames,out var Found,CancellationToken);
                if(Found&&After!=Text.ToString()){ Plan.Edits.Add(new(PathValue,Text.ToString(),After)); }
            }
        }
        return Plan;
    }
    internal static async Task<AssetReferencePlan>ReviewOutputsAsync(AssetWorkspaceItem Item,string Metadata,string MetadataHash,ImageImportSettings BeforeSettings,ImageImportSettings AfterSettings,IReadOnlyDictionary<string,string>Replacements,CancellationToken CancellationToken){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        ValidateOwnership(Item,AssetsToolWindowPane.Controller?.Items??[]);
        var Context=CgeProjectContext.TryCreate(Item.Source.ProjectPath)??throw new InvalidOperationException("Game project unavailable.");
        var Solution=AssetDocumentWorkspace.Workspace.CurrentSolution;
        var PathValue=Item.Source.SourcePath+".meta";var Before=AssetDocumentWorkspace.Read(PathValue);
        if(Before!=File.ReadAllText(PathValue)){ throw new InvalidOperationException(VsixText.Get("Assets_ImageSaveFirst")); }
        var Plan=new AssetReferencePlan{WorkspaceRoot=Context.WorkspaceRoot,SolutionVersion=Solution.Version,Mutation=new(){FileMutationCommand=2,RequestId=Guid.NewGuid(),OperationId=Guid.NewGuid(),Root=Context.ProjectDirectory,MetadataRoot=Context.WorkspaceRoot,Operation=AssetFileOperation.UpdateMetadata,Source=PathValue,Destination=PathValue,ExpectedSourceHash=MetadataHash,Content=Metadata}};
        Plan.Edits.Add(new(PathValue,Before,Metadata));
        var Items=AssetsToolWindowPane.Controller?.Items??[];
        var Names=AssetCatalogLayout.Map(Items.Where(I=>I.Source.ProjectPath==Item.Source.ProjectPath&&AssetReferencePlanner.InCatalog(I.Source.RelativePath)).Select(I=>I.Source.RelativePath));
        if(!Names.TryGetValue(Item.Source.RelativePath,out var Name)){ throw new InvalidOperationException("Asset catalog changed."); }
        var Symbols=new Dictionary<string,string>(StringComparer.Ordinal);
        var Strings=new Dictionary<string,string>(StringComparer.Ordinal);
        var Unresolved=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        foreach(var Pair in Replacements){
            var Old=BeforeSettings.Outputs.Single(O=>O.Key==Pair.Key);var New=Pair.Value.Length==0?Old:AfterSettings.Outputs.Single(O=>O.Key==Pair.Value);
            if(Pair.Value.Length==0){ Unresolved.Add(Name+"."+ImageOutputDrop.Member(Old.Key));Unresolved.Add(Item.Source.RelativePath+":"+Old.Key);Unresolved.Add("asset://"+Item.AssetId+"/"+Old.GetSubAssetId()); }
            if(Old.Kind!=New.Kind||Old.Kind==ImageOutputKind.Texture&&Old.Usage!=New.Usage){ throw new InvalidOperationException("IMAGE_REPLACEMENT_TYPE_MISMATCH"); }
            Symbols[Name+"."+ImageOutputDrop.Member(Old.Key)]=Name+"."+ImageOutputDrop.Member(New.Key);
            Strings[Item.Source.RelativePath+":"+Old.Key]=Item.Source.RelativePath+":"+New.Key;
            var Id=AssetId.Parse(Item.AssetId);
            Strings["asset://"+Id+"/"+Old.GetSubAssetId()]="asset://"+Id+"/"+New.GetSubAssetId();
        }
        if(BeforeSettings.DefaultOutput!=AfterSettings.DefaultOutput){
            var Old=BeforeSettings.Outputs.Single(O=>O.Key==BeforeSettings.DefaultOutput);
            var Target=Replacements.TryGetValue(Old.Key,out var Replacement)&&Replacement.Length>0?Replacement:Old.Key;
            if(Replacements.TryGetValue(Old.Key,out var Empty)&&Empty.Length==0){ Unresolved.Add(Name+".Default"); }
            Symbols[Name+".Default"]=Name+"."+ImageOutputDrop.Member(Target);
        }
        foreach(var Project in Solution.Projects.Where(P=>P.FilePath==Item.Source.ProjectPath)){
            foreach(var Document in Project.Documents){
                if(Document.FilePath is not { }FilePath||AssetWorkspaceClassification.IsExcluded(FilePath)){ continue; }
                var Syntax=await Document.GetSyntaxRootAsync(CancellationToken);var Semantic=await Document.GetSemanticModelAsync(CancellationToken);
                if(Syntax is null||Semantic is null){ continue; }var Text=await Document.GetTextAsync(CancellationToken);
                var Blockers=Unresolved.ToDictionary(K=>K,K=>K,StringComparer.Ordinal);
                _=AssetReferencePlanner.RewriteCSharp(Text,Syntax,Semantic,Blockers,out var Referenced,CancellationToken);
                if(Referenced){ throw new InvalidOperationException("IMAGE_OUTPUT_REFERENCED: "+FilePath+VsixText.Get("Assets_ImageReplacementRequired")); }
                var Changed=AssetReferencePlanner.RewriteCSharp(Text,Syntax,Semantic,Symbols,out _,CancellationToken);
                var Updated=await Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(Changed).GetRootAsync(CancellationToken);
                var Edits=new List<Microsoft.CodeAnalysis.Text.TextChange>();
                foreach(var Literal in Updated.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax>()){
                    if(Unresolved.Contains(Literal.Token.ValueText)){ throw new InvalidOperationException("IMAGE_OUTPUT_REFERENCED: "+FilePath); }
                    if(Strings.TryGetValue(Literal.Token.ValueText,out var Value)){ Edits.Add(new(Literal.Span,Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Literal(Value).ToString())); }
                }
                Changed=Microsoft.CodeAnalysis.Text.SourceText.From(Changed).WithChanges(Edits).ToString();
                Plan.ValidationReads[FilePath]=Text.ToString();
                if(Changed!=Text.ToString()){ Plan.Edits.Add(new(FilePath,Text.ToString(),Changed)); }
            }
        }
        var JsonSymbols=Symbols.Concat(Strings).ToDictionary(P=>P.Key,P=>P.Value,StringComparer.Ordinal);
        foreach(var Candidate in Items.Where(I=>I.Source.ProjectPath==Item.Source.ProjectPath&&I.Kind is AssetWorkspaceKind.Scene or AssetWorkspaceKind.Data or AssetWorkspaceKind.ShaderGraph)){
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
            var Text=AssetDocumentWorkspace.Read(Candidate.Source.SourcePath);Plan.ValidationReads[Candidate.Source.SourcePath]=Text;
            var Blockers=Unresolved.ToDictionary(K=>K,K=>K,StringComparer.Ordinal);
            _=AssetReferencePlanner.RewriteJson(Text,"\0","\0",Blockers,Candidate.Kind,Candidate.Source.SourcePath,"\0","\0",AssetFileOperation.Inspect,out var Referenced);
            if(Referenced){ throw new InvalidOperationException("IMAGE_OUTPUT_REFERENCED: "+Candidate.Source.SourcePath); }
            var Changed=AssetReferencePlanner.RewriteJson(Text,"\0","\0",JsonSymbols,Candidate.Kind,Candidate.Source.SourcePath,"\0","\0",AssetFileOperation.UpdateMetadata,out _);
            if(Changed!=Text){ Plan.Edits.Add(new(Candidate.Source.SourcePath,Text,Changed)); }
        }
        return Plan;
    }
}
