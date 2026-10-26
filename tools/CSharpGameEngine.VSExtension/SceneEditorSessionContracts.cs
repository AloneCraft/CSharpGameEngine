using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// pin 中の Scene の不変スナップショット。CurrentModel は最新の C# 解析結果で、blocking error の
/// 間は、Designer が直前の Projection と LastKnownGoodModel を保持して表示を空白にしない。
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
        SceneProjectionReconciler Reconciler,
        SceneProjection?IdentityBasis=null){
        if(!HasBlockingErrors&&Model?.Root is not null){
            // 通常はひとつ前のcanonical投影からIDを引き継ぐ。自分のsource commitの着地だけは
            // one-shot operation projectionをIdentityBasisに渡す。直前のgestureで追加したノードは
            // 旧Roslyn投影に存在しないため、このbasisが無いとIDが払い直され、Roslyn landing前に
            // 開始した次gestureや現在の選択が別ノードを指すためである。
            var Projection=Reconciler.Reconcile(Key,Model,IdentityBasis??Previous?.Projection);
            return new SceneDocumentSnapshot(
                Key,DocumentId,DocumentVersion,AnalysisGeneration,Model,Model,Projection,
                SceneDiffEngine.Compare(Previous?.Projection,Projection),HasBlockingErrors:false);
        }

        // 入力中の構文、意味解析、DSL 形状のエラーによって、有用なプレビューを空の
        // ウィンドウに変えてはいけません。アセットエラーは Renderer の下位層でノード単位の
        // fallback として処理するため、ここでは HasBlockingErrors を設定しません。
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
    ComponentPalette,
    Source,
    ProgrammaticSourceSync,
}

/// <summary>現在の選択(複数選択・Component/Member単位)。発生源と相関IDを持ち回り、選択UXのエコーを抑える。</summary>
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
/// Hierarchy・Viewport・Inspector・Source の選択を1本化するサービス。再入(エコー)抑制は発生源 bool
/// の持ち回りではなく CorrelationId で行い、意味的に同一の選択は Source を問わず再発行しない。
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
    internal bool TryClearCurrent(SceneSelectionOrigin Origin,Guid CorrelationId){
        if(this.Current is not { }Current
            ||(Current.NodeIds.Count==0&&Current.ComponentType is null&&Current.MemberName is null)){
            return false;
        }
        return this.Clear(Current.Key,Origin,CorrelationId);
    }

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
