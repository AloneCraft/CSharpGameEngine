using System;
using System.Collections.Generic;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>現在のC#文書を右クリックメニューへ投影するための、UIスレッド専用スナップショット。</summary>
internal sealed class SceneDocumentMenuSnapshot {
    private readonly IReadOnlyList<SceneDocumentTypeRange> TypeRanges;

    internal SceneDocumentMenuSnapshot(
        SceneDesignerTargetState State,
        IReadOnlyList<SceneDocumentTypeRange> TypeRanges,
        IReadOnlyList<SceneDesignerMenuItem> Items){
        this.State=State;
        this.TypeRanges=TypeRanges;
        this.Items=Items;
    }

    internal SceneDesignerTargetState State { get; }

    internal IReadOnlyList<SceneDesignerMenuItem> Items { get; }

    internal SceneDesignerTargetState GetTargetState(int CaretPosition){
        if(this.State is not SceneDesignerTargetState.SceneType){
            return this.State;
        }

        var Innermost=this.FindInnermostTypeRange(CaretPosition);
        return Innermost is not { } Type
            ?SceneDesignerTargetState.OutsideType
            :Type.IsScene?SceneDesignerTargetState.SceneType:SceneDesignerTargetState.NonSceneType;
    }

    /// <summary>
    /// 複数 Scene の動的サブメニューで、右クリック位置のクラスだけを選択状態にするための識別子。
    /// 型宣言外では <see langword="null"/> を返すので、いずれの項目も強調しない。
    /// </summary>
    internal string?GetTargetSceneFullyQualifiedName(int CaretPosition){
        if(this.State is not SceneDesignerTargetState.SceneType){
            return null;
        }

        var Innermost=this.FindInnermostTypeRange(CaretPosition);
        return Innermost is { IsScene:true } Type?Type.FullyQualifiedName:null;
    }

    private SceneDocumentTypeRange?FindInnermostTypeRange(int CaretPosition){
        SceneDocumentTypeRange?Innermost=null;
        foreach(var Range in this.TypeRanges){
            if(!Range.Contains(CaretPosition)
                ||Innermost is { } Current&&Current.Length<=Range.Length){
                continue;
            }

            Innermost=Range;
        }

        return Innermost;
    }
}

/// <summary>現在文書内の型宣言範囲。partialでは宣言ごとに個別の範囲を持つ。</summary>
internal readonly record struct SceneDocumentTypeRange(
    int Start,
    int End,
    bool IsScene,
    string?FullyQualifiedName=null) {
    internal int Length=> this.End- this.Start;

    internal bool Contains(int Position)=>Position>= this.Start&&Position< this.End;
}

/// <summary>Roslyn解析1回分のSceneModelと、右クリック表示に必要な文書内範囲の組。</summary>
internal sealed class SceneDocumentAnalysisResult {
    internal SceneDocumentAnalysisResult(
        IReadOnlyList<SceneModel> Models,
        SceneDocumentMenuSnapshot MenuSnapshot){
        this.Models=Models;
        this.MenuSnapshot=MenuSnapshot;
    }

    internal IReadOnlyList<SceneModel> Models { get; }

    internal SceneDocumentMenuSnapshot MenuSnapshot { get; }
}
