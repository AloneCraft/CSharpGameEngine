using System.Globalization;

namespace CSharpGameEngine.VSExtension;

/// <summary>2 つの派生 Scene 投影に割り当てられた、安定した EditorNodeId を比較する。</summary>
internal static class SceneProjectionIdentityComparer {
    internal static bool Matches(SceneProjection Left,SceneProjection Right){
        if(Left.Nodes.Count!=Right.Nodes.Count){ return false; }
        foreach(SceneProjectionNode Node in Left.Nodes){
            if(!Right.TryGetId(Node.Path,out EditorNodeId RightId)||!RightId.Equals(Node.Id)){
                return false;
            }
        }
        return true;
    }

    internal static string?FindFirstMismatch(SceneProjection Left,SceneProjection Right)=>
        FindFirstMismatch(VsixLocalization.UICulture,Left,Right);

    internal static string?FindFirstMismatch(
        CultureInfo Culture,SceneProjection Left,SceneProjection Right){
        if(Left.Nodes.Count!=Right.Nodes.Count){
            return VsixText.Format(
                Culture,"SceneProjectionNodeCountMismatch",Left.Nodes.Count,Right.Nodes.Count);
        }
        foreach(SceneProjectionNode Node in Left.Nodes){
            if(!Right.TryGetId(Node.Path,out EditorNodeId RightId)){
                return VsixText.Format(
                    Culture,"SceneProjectionPathMissingRight",Node.Path.ToDisplayString());
            }
            if(!RightId.Equals(Node.Id)){
                return VsixText.Format(
                    Culture,"SceneProjectionIdMismatch",Node.Path.ToDisplayString());
            }
        }
        return null;
    }
}
