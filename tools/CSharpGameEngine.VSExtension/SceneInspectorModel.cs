using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>Inspectorに表示する1つのComponent memberの行。編集不能なC#式はread-onlyとして表示する。</summary>
internal sealed class SceneInspectorMember {
    internal SceneInspectorMember(EditorNodeId NodeId,string ComponentType,string MemberName,string SourceExpression,bool IsEditable){
        this.NodeId=NodeId;
        this.ComponentType=ComponentType;
        this.MemberName=MemberName;
        this.SourceExpression=SourceExpression;
        this.IsEditable=IsEditable;
    }

    internal EditorNodeId NodeId { get; }

    internal string ComponentType { get; }

    internal string MemberName { get; }

    internal string SourceExpression { get; }

    internal bool IsEditable { get; }
}

/// <summary>選択中のNode/ComponentをSceneModelから読み出し、UI非依存のInspector行モデルを組み立てる。</summary>
internal static class SceneInspectorModelBuilder {
    internal static IReadOnlyList<SceneInspectorMember>Build(SceneDocumentSnapshot Snapshot,SceneSelection?Selection){
        if(Selection is null
            ||!Snapshot.Key.Equals(Selection.Key)
            ||Selection.PrimaryNodeId is not { } NodeId
            ||Snapshot.Projection?.TryGetNode(NodeId,out SceneProjectionNode Node)!=true){
            return [];
        }

        IEnumerable<SceneComponent>Components=Selection.ComponentType is { } Type
            ?Node.Node.Components.Where(Component=>string.Equals(Component.TypeFullyQualifiedName,Type,StringComparison.Ordinal))
            :Node.Node.Components;
        return Components.SelectMany(Component=>Component.Members.Select(Member=>new SceneInspectorMember(
            NodeId,
            Component.TypeFullyQualifiedName,
            Member.MemberName,
            Member.Value.SourceExpressionText,
            Member.Value.IsEditable))).ToArray();
    }
}
