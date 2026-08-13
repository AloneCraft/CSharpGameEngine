using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// ????? Scene ?????CurrentModel ??????? C# ????????blocking error ?
/// ????Designer ??? Projection ? LastKnownGoodModel ?????????????????
/// </summary>
internal sealed class SceneDocumentSnapshot {
    internal SceneDocumentSnapshot(
        SceneKey Key,
        string DocumentId,
        long DocumentVersion,
        long AnalysisGeneration,
        SceneModel?CurrentModel,
        SceneModel?LastKnownGoodModel,
        SceneProjection?Projection,
        SceneDiff Diff,
        bool HasBlockingErrors){
        this.Key=Key;
        this.DocumentId=DocumentId;
        this.DocumentVersion=DocumentVersion;
        this.AnalysisGeneration=AnalysisGeneration;
        this.CurrentModel=CurrentModel;
        this.LastKnownGoodModel=LastKnownGoodModel;
        this.Projection=Projection;
        this.Diff=Diff;
        this.HasBlockingErrors=HasBlockingErrors;
    }

    internal SceneKey Key { get; }

    internal string DocumentId { get; }

    internal long DocumentVersion { get; }

    internal long AnalysisGeneration { get; }

    internal SceneModel?CurrentModel { get; }

    internal SceneModel?LastKnownGoodModel { get; }

    internal SceneProjection?Projection { get; }

    internal SceneDiff Diff { get; }

    internal bool HasBlockingErrors { get; }

    internal static SceneDocumentSnapshot Create(
        SceneKey Key,
        string DocumentId,
        long DocumentVersion,
        long AnalysisGeneration,
        SceneModel?Model,
        bool HasBlockingErrors,
        SceneDocumentSnapshot?Previous,
        SceneProjectionReconciler Reconciler){
        if(!HasBlockingErrors&&Model?.Root is not null){
            SceneProjection Projection=Reconciler.Reconcile(Key,Model,Previous?.Projection);
            return new SceneDocumentSnapshot(
                Key,DocumentId,DocumentVersion,AnalysisGeneration,Model,Model,Projection,
                SceneDiffEngine.Compare(Previous?.Projection,Projection),HasBlockingErrors:false);
        }

        // Syntax/semantic/DSL shape error during typing must not turn a useful preview into a blank
        // window. Asset errors are handled lower in the Renderer as per-node fallbacks and therefore
        // do not set HasBlockingErrors here.
        return new SceneDocumentSnapshot(
            Key,DocumentId,DocumentVersion,AnalysisGeneration,Model,
            Previous?.LastKnownGoodModel,Previous?.Projection,
            new SceneDiff([]),HasBlockingErrors:true);
    }
}

internal enum SceneSelectionOrigin {
    Hierarchy,
    Viewport,
    Inspector,
    Source,
    ProgrammaticSourceSync,
}

/// <summary>??????????????????????????????????UX????????</summary>
internal sealed class SceneSelection {
    internal SceneSelection(
        SceneKey Key,
        IReadOnlyList<EditorNodeId>NodeIds,
        EditorNodeId?PrimaryNodeId,
        string?ComponentType,
        string?MemberName,
        SceneSelectionOrigin Origin,
        long Revision,
        Guid CorrelationId){
        this.Key=Key;
        this.NodeIds=NodeIds.ToArray();
        this.PrimaryNodeId=PrimaryNodeId;
        this.ComponentType=ComponentType;
        this.MemberName=MemberName;
        this.Origin=Origin;
        this.Revision=Revision;
        this.CorrelationId=CorrelationId;
    }

    internal SceneKey Key { get; }

    internal IReadOnlyList<EditorNodeId>NodeIds { get; }

    internal EditorNodeId?PrimaryNodeId { get; }

    internal string?ComponentType { get; }

    internal string?MemberName { get; }

    internal SceneSelectionOrigin Origin { get; }

    internal long Revision { get; }

    internal Guid CorrelationId { get; }
}

/// <summary>
/// Hierarchy?Viewport?Inspector?Source ??????????????????????? bool
/// ???????????? CorrelationId ????????????? Source ???????????
/// </summary>
internal sealed class SceneSelectionService {
    private SceneSelection?Current;
    private long Revision;

    internal event Action<SceneSelection>?SelectionChanged;

    internal SceneSelection?CurrentSelection=>this.Current;

    internal bool Select(
        SceneKey Key,
        IReadOnlyList<EditorNodeId>NodeIds,
        EditorNodeId?PrimaryNodeId,
        string?ComponentType,
        string?MemberName,
        SceneSelectionOrigin Origin,
        Guid CorrelationId){
        if(this.Current is { } Existing&&SameSemanticSelection(Existing,Key,NodeIds,PrimaryNodeId,ComponentType,MemberName)){
            return false;
        }

        var Next=new SceneSelection(
            Key,NodeIds,PrimaryNodeId,ComponentType,MemberName,Origin,++this.Revision,CorrelationId);
        this.Current=Next;
        this.SelectionChanged?.Invoke(Next);
        return true;
    }

    internal bool Clear(SceneKey Key,SceneSelectionOrigin Origin,Guid CorrelationId)=>
        this.Select(Key,[],PrimaryNodeId:null,ComponentType:null,MemberName:null,Origin,CorrelationId);

    private static bool SameSemanticSelection(
        SceneSelection Existing,
        SceneKey Key,
        IReadOnlyList<EditorNodeId>NodeIds,
        EditorNodeId?PrimaryNodeId,
        string?ComponentType,
        string?MemberName)=>
        Existing.Key.Equals(Key)
        &&Nullable.Equals(Existing.PrimaryNodeId,PrimaryNodeId)
        &&string.Equals(Existing.ComponentType,ComponentType,StringComparison.Ordinal)
        &&string.Equals(Existing.MemberName,MemberName,StringComparison.Ordinal)
        &&Existing.NodeIds.SequenceEqual(NodeIds);
}
