using System;
using System.Collections.Generic;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension;

/// <summary>エディタが Component に与える意味。</summary>
internal enum SceneComponentRole {
    /// <summary>ローカルTRS。親子合成 <c>world = local * parentWorld</c> の入力。</summary>
    LocalTransform,
    /// <summary>手続き生成プリミティブの描画。</summary>
    PrimitiveMesh,
    /// <summary>アセット(.glb等)参照の描画。</summary>
    StaticMesh,
}

/// <summary>Component型完全修飾名1件と、それがエディタ上で持つ意味の対応。</summary>
internal sealed class SceneComponentBinding {
    internal SceneComponentBinding(string TypeFullyQualifiedName,SceneComponentRole Role){
        this.TypeFullyQualifiedName=TypeFullyQualifiedName
            ??throw new ArgumentNullException(nameof(TypeFullyQualifiedName));
        this.Role=Role;
    }

    /// <summary><c>global::</c>接頭辞付き(<see cref="SceneComponent.TypeFullyQualifiedName"/>と同じ表記)。</summary>
    internal string TypeFullyQualifiedName { get; }

    internal SceneComponentRole Role { get; }
}

/// <summary>
/// Scene デザイナーが認識する Component 型の表。完全修飾名から <see cref="SceneComponentRole"/> への
/// 多対一で、ライトのように将来1つの意味へ複数型が対応しても行を足すだけで済む。
///
/// <para>
/// この表を <c>SceneModel</c> 側(<c>SceneDslWellKnownNames</c>)に置かないのは意図的である。あちらは
/// Source Generator へコンパイルされて出荷される DSL 構文検証の語彙(<c>IScene</c>/<c>Node</c>/
/// <c>Parent</c> 等)専用で、アナライザーは Component の中身を特別扱いしない(ADR-0040)。
/// <c>Runtime.Windows</c> のレンダリング語彙を混ぜると、アナライザーが Component 型を特別扱いし
/// 始める誘惑を作ってしまう。唯一の消費者はこの VSIX である。
/// </para>
///
/// <para>
/// 拡張点: <c>global::CSharpGameEngine.Runtime.Windows.Camera</c>(FrustumGizmo、M0020 Phase 6)と
/// ライト各種はここへ行を足す。ライトの所在(エンジン標準化するか L9 の型を登録するか)は
/// ADR-0041 決定事項9 の未決事項であり、Phase 6 着手時の小 ADR で決める。
/// </para>
/// </summary>
internal static class SceneComponentBindings {
    internal const string TransformTypeFullyQualifiedName="global::CSharpGameEngine.Runtime.Windows.Transform";
    internal const string PrimitiveMeshTypeFullyQualifiedName="global::CSharpGameEngine.Runtime.Windows.PrimitiveMesh";
    internal const string StaticMeshTypeFullyQualifiedName="global::CSharpGameEngine.Runtime.Windows.StaticMesh";

    // 退役した global::UserEcsProject.Transform は登録しない。互換のため両方登録すると、実シーンが
    // 原点に固まって描画されていたバグ(M0020 Phase 0 で修正)を静かに塗り潰し、直ったかどうかが
    // 判定不能になる。
    private static readonly SceneComponentBinding[]Table=[
        new SceneComponentBinding(TransformTypeFullyQualifiedName,SceneComponentRole.LocalTransform),
        new SceneComponentBinding(PrimitiveMeshTypeFullyQualifiedName,SceneComponentRole.PrimitiveMesh),
        new SceneComponentBinding(StaticMeshTypeFullyQualifiedName,SceneComponentRole.StaticMesh),
    ];

    private static readonly IReadOnlyList<SceneComponentBinding>AllValue=Array.AsReadOnly(Table);

    private static readonly Dictionary<string,SceneComponentRole>RoleByTypeFullyQualifiedName=BuildIndex(Table);

    internal static IReadOnlyList<SceneComponentBinding>All=>AllValue;

    internal static bool TryGetRole(string TypeFullyQualifiedName,out SceneComponentRole Role)=>
        RoleByTypeFullyQualifiedName.TryGetValue(TypeFullyQualifiedName,out Role);

    /// <summary>ノードが持つComponentのうち、指定Roleに束縛された最初の1件(宣言順で先着)。</summary>
    internal static SceneComponent?FindComponent(SceneNode Node,SceneComponentRole Role){
        foreach(SceneComponent Component in Node.Components){
            if(TryGetRole(Component.TypeFullyQualifiedName,out SceneComponentRole Found)&&Found==Role){
                return Component;
            }
        }

        return null;
    }

    private static Dictionary<string,SceneComponentRole>BuildIndex(SceneComponentBinding[]Bindings){
        // SceneComponent.TypeFullyQualifiedName は SymbolDisplayFormat.FullyQualifiedFormat の出力なので
        // Ordinal 一致が正しい(SceneNode.FindComponent も StringComparison.Ordinal)。
        Dictionary<string,SceneComponentRole>Index=new(StringComparer.Ordinal);
        foreach(SceneComponentBinding Binding in Bindings){
            Index[Binding.TypeFullyQualifiedName]=Binding.Role;
        }

        return Index;
    }
}
