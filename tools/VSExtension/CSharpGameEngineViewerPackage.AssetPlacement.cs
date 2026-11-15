using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
public sealed partial class CSharpGameEngineViewerPackage {
    private void OnAssetPlacement(AssetPlacementRequest Request)=>_=this.JoinableTaskFactory.RunAsync(async()=>{
        try { await this.PlaceAssetAsync(Request); }
        catch(Exception Error){ ActivityLog.LogError(nameof(CSharpGameEngineViewerPackage),Error.ToString()); }
    });
    private async Task PlaceAssetAsync(AssetPlacementRequest Request){
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
        if(this.SceneSession is not { }Session||SceneEditorHub.ActiveSceneModel is not { }Model
            ||!AssetPlacement.CanPlace(Request.Item,Model.FilePath)
            ||this.EnsurePinned(Session,Model) is not { Projection:{ }Projection }Snapshot
            ||!Projection.TryGetId(Request.Parent??NodePath.Root,out var ParentId)){ return; }
        const string MeshType="global::CSharpGameEngine.Runtime.Windows.StaticMesh";
        var Mesh=await Session.ResolveComponentTypeAsync(MeshType,this.DisposalToken);
        var Transform=await Session.ResolveComponentTypeAsync(SceneComponentBindings.TransformTypeFullyQualifiedName,this.DisposalToken);
        await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
        if(Mesh is not { }MeshValue||Transform is not { }TransformValue){ return; }
        var TransformPlan=SceneComponentInitializerPolicy.CreatePlan(TransformValue.Symbol,SceneComponentBindings.TransformTypeFullyQualifiedName,TransformValue.Compilation);
        var MeshPlan=SceneComponentInitializerPolicy.CreatePlan(MeshValue.Symbol,MeshType,MeshValue.Compilation);
        var Literal=Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(Request.Item.Source.RelativePath,true);
        var Field=MeshValue.Symbol.GetMembers("AssetPath").FirstOrDefault();
        var StringValue=new SceneValue(SceneValueKind.Literal,MeshValue.Compilation.GetSpecialType(Microsoft.CodeAnalysis.SpecialType.System_String),Request.Item.Source.RelativePath,null,[],[],[],Literal,Literal,SceneSourceLocation.None);
        var Members=MeshPlan.Members.Where(static Member=>Member.MemberName!="AssetPath").Concat(new[]{new SceneComponentMember("AssetPath",Field,StringValue,SceneSourceLocation.None)}).ToImmutableArray();
        var Initial=ImmutableArray.Create(
            new NewNodeComponent(SceneComponentBindings.TransformTypeFullyQualifiedName,TransformPlan.Expression,TransformPlan.Members,SceneComponentOrdering.GetSortKey(TransformValue.Symbol,SceneComponentBindings.TransformTypeFullyQualifiedName)),
            new NewNodeComponent(MeshType,"new "+MeshType+" { AssetPath = "+Literal+" }",Members,SceneComponentOrdering.GetSortKey(MeshValue.Symbol,MeshType)));
        var Operation=new AddNodeOperation(Snapshot.Key,ParentId,Snapshot.DocumentVersion,System.IO.Path.GetFileNameWithoutExtension(Request.Item.Source.SourcePath),Initial);
        var Result=await Session.CommitAsync(new(Snapshot.Key,Snapshot.DocumentVersion,VsixText.Get("Assets_Place"),Array.Empty<SceneTransformChange>(),SceneTransformMemberMask.None,DirectOperations:[Operation]),this.DisposalToken);
        if(!Result.Succeeded){ ActivityLog.LogError(nameof(CSharpGameEngineViewerPackage),Result.ErrorMessage??"Asset placement failed."); }
    }
}
