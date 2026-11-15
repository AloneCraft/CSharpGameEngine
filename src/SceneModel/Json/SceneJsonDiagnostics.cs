using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// Scene JSON ドキュメントの診断情報（ADR-0070）。いずれもエラーとする。生成できない Scene で値を黙って破棄したままビルドしてはならない（2026-10-01のユーザー判断）。
/// 診断を出すのはアナライザーだけだが、ジェネレーターも同じ解析を行い、出力可能か判断する。
/// </summary>
internal static class SceneJsonDiagnostics {
    private const string Category="CSharpGameEngine.Entities.CodeGen";
    internal static readonly DiagnosticDescriptor UnknownComponentType=new(
        id:"SCENE201",
        title:"Scene JSON names a component type that does not exist or cannot be a component",
        messageFormat:"'{0}' is not a scene component type: {1}",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);
    internal static readonly DiagnosticDescriptor UnknownMember=new(
        id:"SCENE202",
        title:"Scene JSON sets a member that the type does not have",
        messageFormat:"'{0}' has no settable member or constructor parameter named '{1}'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);
    internal static readonly DiagnosticDescriptor ValueMismatch=new(
        id:"SCENE203",
        title:"Scene JSON value does not fit the member's type",
        messageFormat:"The value for '{0}' does not fit '{1}': {2}",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);
    internal static readonly DiagnosticDescriptor InvalidDocument=new(
        id:"SCENE204",
        title:"Scene JSON document is malformed",
        messageFormat:"{0}",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);
    internal static readonly DiagnosticDescriptor DuplicateComponent=new(
        id:"SCENE205",
        title:"A scene node declares the same component type twice",
        messageFormat:"'{0}' is declared more than once on this node",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);
    /// <summary>ADR-0070 の切り替え後は C# Scene クラスをビルドしないため、これを黙って通してはならない。</summary>
    internal static readonly DiagnosticDescriptor CSharpSceneNotBuilt=new(
        id:"SCENE206",
        title:"C# scene classes are not built; scenes are JSON documents under Scenes",
        messageFormat:"'{0}' is a C# scene class, which is no longer built; convert it to a scene JSON (Scenes/*.json) with 'CSharpGameEngine.SceneJson.Cli convert <project> --write --delete-source'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);
    /// <summary>複数の Scene ファイルが同じ Scene クラス名を宣言している（通常は className を変えずにファイルをコピーした場合）。パス順で最初のファイルだけをビルドし、後続ファイルの className それぞれに診断を出す。</summary>
    internal static readonly DiagnosticDescriptor DuplicateScene=new(
        id:"SCENE207",
        title:"Two scene files declare the same scene class",
        messageFormat:"'{0}' is already declared by {1}; give this scene its own className or namespace",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true,
        description:null,
        helpLinkUri:null,
        WellKnownDiagnosticTags.CompilationEnd);
    internal static readonly DiagnosticDescriptor[]All=[UnknownComponentType,UnknownMember,ValueMismatch,InvalidDocument,DuplicateComponent,CSharpSceneNotBuilt,DuplicateScene];
}
