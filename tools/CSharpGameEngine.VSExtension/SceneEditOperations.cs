using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>DesignerからSource Editorへ渡す編集操作の基底。UI層がRoslyn APIへ直接触れないための語彙。</summary>
internal abstract record SceneEditOperation(SceneKey SceneKey,EditorNodeId NodeId,long DocumentVersion);

internal sealed record SetComponentMemberOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    string ComponentTypeFullyQualifiedName,
    string MemberName,
    string ValueExpression,
    string?OriginalValueExpression=null,
    ImmutableArray<string>MemberPath=default) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

/// <summary>
/// <paramref name="Members"/>は<b>メモリ内モデル専用</b>で、C#への書き戻しには使わない
/// (書き戻しは<paramref name="ComponentExpression"/>をそのまま構文として挿す)。
/// これが空だと、Roslynの再解析が着地するまでInspectorが「解析待ち…」のまま何も編集できない
/// (<c>SceneModelEditApplier.TryApplyAddComponent</c>)。両者は
/// <c>SceneComponentInitializerPolicy.CreatePlan</c>が同じ列挙から作るので食い違わない。
/// </summary>
internal sealed record AddComponentOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    string ComponentTypeFullyQualifiedName,
    string ComponentExpression,
    ImmutableArray<SceneComponentMember>Members=default,
    SceneComponentOrderKey?OrderKey=null) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

internal sealed record ConvertLightOperation(
    SceneKey SceneKey,EditorNodeId NodeId,long DocumentVersion,
    string SourceType,string OriginalExpression,SceneComponent Replacement,string DefaultsFingerprint)
    :SceneEditOperation(SceneKey,NodeId,DocumentVersion);
internal sealed record RemoveComponentOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    string ComponentTypeFullyQualifiedName) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

/// <summary>重複を first-wins で除去し、カテゴリ・order・完全修飾型名順へ整列する明示操作。</summary>
internal sealed record NormalizeComponentsOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

internal sealed record RenameNodeOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    string NewName) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

/// <summary>新規Nodeを原子的に作るとき、同じNode初期化子へ入れるComponentの1件。</summary>
internal sealed record NewNodeComponent(
    string ComponentTypeFullyQualifiedName,
    string ComponentExpression,
    ImmutableArray<SceneComponentMember>Members=default,
    SceneComponentOrderKey?OrderKey=null);

internal sealed record AddNodeOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    string NodeName,
    ImmutableArray<NewNodeComponent>InitialComponents=default) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

internal sealed record RemoveNodeOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

internal sealed record ReparentNodeOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    EditorNodeId NewParentNodeId,
    int NewIndex) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

internal sealed record ReorderNodeOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    int NewIndex) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

[Flags]
internal enum SceneTransformMemberMask {
    None=0,
    Position=1,
    Rotation=2,
    Scale=4,
    All=Position|Rotation|Scale,
}

internal sealed record SetTransformOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    SceneTransformValue Transform,
    SceneTransformMemberMask Members=SceneTransformMemberMask.All) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

internal sealed record DuplicateNodeOperation(
    SceneKey SceneKey,
    EditorNodeId NodeId,
    long DocumentVersion,
    SceneTransformValue Transform,
    SceneTransformMemberMask Members) : SceneEditOperation(SceneKey,NodeId,DocumentVersion);

/// <summary>編集操作の全語彙。ViewModelとRoslyn書き込み層が同じ列挙を共有する(未実装の種類も先に確定させる)。</summary>
internal enum SceneEditOperationKind {
    SetComponentMember,
    SetTransform,
    RenameNode,
    AddNode,
    RemoveNode,
    DuplicateNode,
    ReparentNode,
    ReorderNode,
    AddComponent,
    RemoveComponent,
    NormalizeComponents,
    AssignAsset,
}

/// <summary>
/// シーン編集操作用の値ヘルパー。ADR-0070 で、以前ここにあった C# シーン writer を削除しました。操作は <see cref="SceneModel"/>（<see cref="SceneModelEditApplier"/>）を編集し、シーン JSON はモデルから <see cref="SceneJsonCommitPlanner"/> が書き出します。
/// </summary>
internal static class SceneEditValues {
    /// <summary>transform 操作で設定する public RUF Transform ベクトル。</summary>
    internal static IEnumerable<(string Name,Vector3 Value,Vector3 Default)>EnumerateTransformVectors(
        SceneTransformValue Transform,SceneTransformMemberMask Members){
        if((Members&SceneTransformMemberMask.Position)!=0){ yield return("Position",Transform.Position,Vector3.Zero); }
        if((Members&SceneTransformMemberMask.Rotation)!=0){ yield return("Rotation",Transform.EulerRotation,Vector3.Zero); }
        if((Members&SceneTransformMemberMask.Scale)!=0){ yield return("Scale",Transform.Scale,Vector3.One); }
    }
    /// <summary><see cref="SceneModelEditApplier"/> と <see cref="SceneEditConflictMerger"/> で共有します。</summary>
    internal static float ReadFloat(object?Value,float Default)=>Value switch {
        float Float=>Float,
        int Integer=>Integer,
        long Long=>Long,
        _=>Default,
    };

    /// <summary><see cref="SceneModelEditApplier"/> と <see cref="SceneEditConflictMerger"/> で共有します。</summary>
    internal static string FormatFloat(float Value){
        if(float.IsNaN(Value)||float.IsInfinity(Value)){
            throw new ArgumentOutOfRangeException(nameof(Value),VsixText.Get("SourceEditNonFiniteTransform"));
        }
        return SingleLiteral(Value);
    }

    /// <summary>
    /// 編集値の float を C# リテラルとして表します。.NET 上でもシーン JSON と同じ表記を使います。ドキュメントではリテラルの表記を維持し、.NET Framework の "R" は .NET と異なる桁で一部の float を出力するためです（.NET Framework は 312985.125、.NET は 312985.12）。JSON 数値として表せない NaN や infinity は、"R" の出力で書き込みます。
    /// </summary>
    internal static string SingleLiteral(float Value)=>
        (float.IsNaN(Value)||float.IsInfinity(Value)
            ?Value.ToString("R",CultureInfo.InvariantCulture)
            :CSharpGameEngine.SceneAnalysis.Json.SceneJsonNumbers.FormatSingle(Value))+"f";

    /// <summary><see cref="SceneModelEditApplier"/> と <see cref="SceneEditConflictMerger"/> で共有します。</summary>
    internal static bool IsGenerated(string?FilePath)=>
        FilePath is { Length:>0 }&&FilePath.EndsWith(".g.cs",StringComparison.OrdinalIgnoreCase);
}
