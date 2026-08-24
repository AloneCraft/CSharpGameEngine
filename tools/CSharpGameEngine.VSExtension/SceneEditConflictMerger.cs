using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

internal sealed class SceneEditConflictMergeResult {
    private SceneEditConflictMergeResult(
        bool Succeeded,string?ErrorMessage,IReadOnlyList<SceneEditOperation>Operations){
        this.Succeeded=Succeeded;
        this.ErrorMessage=ErrorMessage;
        this.Operations=Operations;
    }

    internal bool Succeeded { get; }

    internal string?ErrorMessage { get; }

    internal IReadOnlyList<SceneEditOperation>Operations { get; }

    internal static SceneEditConflictMergeResult Success(IReadOnlyList<SceneEditOperation>Operations)=>
        new(true,ErrorMessage:null,Operations);

    internal static SceneEditConflictMergeResult Failure(string ErrorMessage)=>
        new(false,ErrorMessage,Array.Empty<SceneEditOperation>());
}

/// <summary>ドラッグ開始値から得た差分を、commit 時点の最新 Scene 値へ合成する。</summary>
internal static class SceneEditConflictMerger {
    internal static SceneEditConflictMergeResult Merge(
        SceneEditCommitRequest Request,SceneDocumentSnapshot Latest){
        if(Request is null){ throw new ArgumentNullException(nameof(Request)); }
        if(Latest is null){ throw new ArgumentNullException(nameof(Latest)); }
        if(!Request.SceneKey.Equals(Latest.Key)||Latest.Projection is not { }Projection){
            return SceneEditConflictMergeResult.Failure(VsixText.Get("ConflictTargetUnavailable"));
        }
        if(Request.DocumentVersion==Latest.DocumentVersion){
            return SceneEditConflictMergeResult.Success(SceneEditorSessionService.CreateOperations(Request));
        }

        if(Request.DirectOperations.Count>0){
            List<SceneEditOperation>Operations=[];
            foreach(var Operation in Request.DirectOperations){
                if(Operation is SetComponentMemberOperation Member
                    &&Member.OriginalValueExpression is not null){
                    var Merged=MergeMember(Member,Projection);
                    if(!Merged.Succeeded){ return Merged; }
                    Operations.Add(Merged.Operations[0]);
                } else {
                    Operations.Add(Operation);
                }
            }
            return SceneEditConflictMergeResult.Success(Operations);
        }

        List<SceneEditOperation>Transforms=[];
        foreach(var Change in Request.Changes){
            if(!Projection.TryGetNode(Change.NodeId,out var Node)
                ||!SceneTransformBinding.TryRead(Node.Node,out var Current)){
                return SceneEditConflictMergeResult.Failure(
                    VsixText.Format("ConflictNodeTransformUnavailable",Change.NodeId.ToString()));
            }
            if(!CanEditTransformMembers(Node.Node,Request.Members,out var ErrorMessage)){
                return SceneEditConflictMergeResult.Failure(ErrorMessage!);
            }

            var Merged=MergeTransform(
                                      Change.OriginalLocal,Change.Local,Current,Request.Members);
            SceneEditOperation Operation=Request.IsDuplicate
                ?new DuplicateNodeOperation(
                    Request.SceneKey,Change.NodeId,Request.DocumentVersion,Merged,Request.Members)
                :new SetTransformOperation(
                    Request.SceneKey,Change.NodeId,Request.DocumentVersion,Merged,Request.Members);
            Transforms.Add(Operation);
        }
        return SceneEditConflictMergeResult.Success(Transforms);
    }

    private static SceneEditConflictMergeResult MergeMember(
        SetComponentMemberOperation Operation,SceneProjection Projection){
        if(!Projection.TryGetNode(Operation.NodeId,out var Node)){
            return SceneEditConflictMergeResult.Failure(VsixText.Format("ConflictNodeValueUnavailable",Operation.NodeId.ToString()));
        }
        var Component=Node.Node.FindComponent(Operation.ComponentTypeFullyQualifiedName);
        if(Component is null){
            return SceneEditConflictMergeResult.Failure(
                VsixText.Format("ConflictComponentMissing",Operation.ComponentTypeFullyQualifiedName));
        }
        var Member=Component.FindMember(Operation.MemberName);
        if(Member is not null&&!Member.Value.IsEditable){
            return SceneEditConflictMergeResult.Failure(
                VsixText.Format("ConflictMemberExternallyChanged",Operation.MemberName));
        }
        if(!TryParseFloat(Operation.OriginalValueExpression!,out var Original)
            ||!TryParseFloat(Operation.ValueExpression,out var Final)){
            return SceneEditConflictMergeResult.Failure(
                VsixText.Format("ConflictMemberDeltaNotNumeric",Operation.MemberName));
        }

        var Current=Member is null?Original:ReadFloat(Member.Value.ConstantValue,Original);
        var Merged=Current+(Final-Original);
        if(float.IsNaN(Merged)||float.IsInfinity(Merged)){
            return SceneEditConflictMergeResult.Failure(
                VsixText.Format("ConflictMemberMergeNonFinite",Operation.MemberName));
        }
        var Result=Operation with { ValueExpression=FormatFloat(Merged) };
        return SceneEditConflictMergeResult.Success([Result]);
    }

    private static SceneTransformValue MergeTransform(
        in SceneTransformValue Original,in SceneTransformValue Final,in SceneTransformValue Current,
        SceneTransformMemberMask Members){
        var Position=Members.HasFlag(SceneTransformMemberMask.Position)
                         ?Current.Position+(Final.Position-Original.Position)
                         :Current.Position;
        var Rotation=Current.Rotation;
        if(Members.HasFlag(SceneTransformMemberMask.Rotation)){
            var Delta=Quaternion.Concatenate(
                                             Quaternion.Inverse(Original.Rotation),Final.Rotation);
            Rotation=SceneTransformMath.NormalizeRotation(
                Quaternion.Concatenate(Current.Rotation,Delta));
        }
        var Scale=Members.HasFlag(SceneTransformMemberMask.Scale)
                      ?new Vector3(
                                   MergeScale(Original.Scale.X,Final.Scale.X,Current.Scale.X),
                                   MergeScale(Original.Scale.Y,Final.Scale.Y,Current.Scale.Y),
                                   MergeScale(Original.Scale.Z,Final.Scale.Z,Current.Scale.Z))
                      :Current.Scale;
        return new SceneTransformValue(Position,Rotation,Scale);
    }

    private static float MergeScale(float Original,float Final,float Current)=>
        Math.Abs(Original)>1e-6f?Current*(Final/Original):Current+(Final-Original);

    private static bool CanEditTransformMembers(
        SceneNode Node,SceneTransformMemberMask Members,out string?ErrorMessage){
        var Transform=SceneComponentBindings.FindComponent(Node,SceneComponentRole.LocalTransform);
        if(Transform is null){
            ErrorMessage=VsixText.Get("ConflictTransformMissing");
            return false;
        }
        foreach((string Name,Vector3 _,Vector3 _) in
            SceneSourceEditor.EnumerateTransformVectors(SceneTransformValue.Identity,Members)){
            for(int Index=0;Index<3;Index++){
                if(SceneTransformBinding.CanEditVector(Transform,Name,Index)){ continue; }
                ErrorMessage=VsixText.Format("ConflictTransformMemberNotEditable",Name);
                return false;
            }
        }
        ErrorMessage=null;
        return true;
    }
    private static float ReadFloat(object? Value,float Default)=>Value switch {
        float Float=>Float,
        double Double=>(float)Double,
        int Integer=>Integer,
        long Long=>Long,
        _=>Default,
    };

    private static bool TryParseFloat(string Expression,out float Value){
        var Text=Expression.Trim();
        if(Text.EndsWith("f",StringComparison.OrdinalIgnoreCase)){
            Text=Text.Substring(0,Text.Length-1);
        }
        return float.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out Value);
    }

    private static string FormatFloat(float Value)=>Value.ToString("R",CultureInfo.InvariantCulture)+"f";
}
