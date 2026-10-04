using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>
/// Diagnostics for scene JSON documents (ADR-0070). They are errors: a scene that cannot be
/// generated must not build with values silently dropped (user choice, 2026-10-01). Only the
/// analyzer reports them; the generator runs the same analysis to decide whether it can emit.
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
    /// <summary>ADR-0070 cutover: a C# scene class is no longer built, so it must not pass silently.</summary>
    internal static readonly DiagnosticDescriptor CSharpSceneNotBuilt=new(
        id:"SCENE206",
        title:"C# scene classes are not built; scenes are JSON documents under Scenes",
        messageFormat:"'{0}' is a C# scene class, which is no longer built; convert it to a scene JSON (Scenes/*.json) with 'CSharpGameEngine.SceneJson.Cli convert <project> --write --delete-source'",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);
    /// <summary>
    /// Two scene files declare the same scene class (typically a copied file whose className was kept). Only the first by
    /// path is built; reported at each later file's className.
    /// </summary>
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
