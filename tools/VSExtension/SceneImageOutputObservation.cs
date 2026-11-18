using System;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
namespace CSharpGameEngine.VSExtension;
/// <summary>Allows only the prepared metadata edit to advance an otherwise unchanged scene assignment.</summary>
internal sealed record SceneImageOutputObservation(SceneKey Key,string ScenePath,string SceneText,SceneInspectorMember Member) {
    internal bool MatchesSource(string Path,string Text)=>string.Equals(this.ScenePath,Path,StringComparison.OrdinalIgnoreCase)
        &&string.Equals(this.SceneText,Text,StringComparison.Ordinal);
    internal bool TryRefresh(SceneSourceCommitContext Context,SceneDocumentSnapshot Latest,SceneEditCommitRequest Request,out SceneEditCommitRequest Updated){
        Updated=Request;
        if(!this.Key.Equals(Request.SceneKey)||!this.Key.Equals(Latest.Key)||Latest.HasBlockingErrors
            ||!this.MatchesSource(Context.Model.FilePath,Context.Text.ToString())
            ||Request.ImageOutputPrecondition is not { }Precondition||!ReferenceEquals(Precondition.SceneObservation,this)
            ||Request.Changes.Count!=0||Request.Members!=SceneTransformMemberMask.None||Request.IsDuplicate
            ||Request.DirectOperations.Count!=1||Request.DirectOperations[0] is not SetComponentMemberOperation Operation||!Operation.SceneKey.Equals(this.Key)
            ||!Operation.NodeId.Equals(this.Member.NodeId)||Operation.ComponentTypeFullyQualifiedName!=this.Member.ComponentTypeFullyQualifiedName
            ||Operation.MemberName!=this.Member.MemberName||Operation.MemberPath.IsDefaultOrEmpty||!Operation.MemberPath.SequenceEqual(this.Member.DefinitionMemberPath)
            ||Operation.ValueExpression!=Precondition.Expression||Precondition.AssetTypeName!=this.Member.AssetTypeName){ return false; }
        var Selection=new SceneSelection(this.Key,[this.Member.NodeId],this.Member.NodeId,this.Member.ComponentTypeFullyQualifiedName,this.Member.MemberName,SceneSelectionOrigin.ProgrammaticSourceSync,0,Guid.Empty);
        var Candidates=SceneInspectorModelBuilder.Build(Latest,Selection).Where(Current=>Current.NodeId.Equals(this.Member.NodeId)
            &&Current.ComponentTypeFullyQualifiedName==this.Member.ComponentTypeFullyQualifiedName&&Current.MemberName==this.Member.MemberName
            &&Current.DefinitionMemberPath.SequenceEqual(this.Member.DefinitionMemberPath)).Take(2).ToArray();
        if(Candidates.Length!=1){ return false; }
        var Current=Candidates[0];
        if(!this.Member.IsEditable||!Current.IsEditable||Current.EditorKind!=this.Member.EditorKind
            ||Current.AssetTypeName!=this.Member.AssetTypeName||Current.TextureUsage!=this.Member.TextureUsage
            ||Current.SourceExpression!=this.Member.SourceExpression||Current.AssetPath!=this.Member.AssetPath){ return false; }
        Updated=SceneInspectorCommitPlanner.CreateCommit(this.Key,Latest.DocumentVersion,Current,Operation.ValueExpression,Precondition);
        return true;
    }
}
