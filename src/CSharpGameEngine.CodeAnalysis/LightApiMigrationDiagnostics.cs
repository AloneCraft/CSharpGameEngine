using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>物理ライティング導入前のSceneを移行可能にする診断。</summary>
internal static class LightApiMigrationDiagnostics {
    private const string Category="CSharpGameEngine.Lighting";

    internal static readonly DiagnosticDescriptor LegacyLightMember=new(
        id:"LGT001",
        title:"Legacy light member must be migrated to the physical-light contract",
        messageFormat:"Migrate legacy light member '{0}' on {1}: {2}",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true,
        description:"Physical lights store pose on Transform and use explicit units.");

    internal static ImmutableArray<DiagnosticDescriptor> All=>[LegacyLightMember];
}
