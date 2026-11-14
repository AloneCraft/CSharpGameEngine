using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.CodeAnalysis;
internal static class CanonicalNumericDiagnostics {
    private const string Category="CSharpGameEngine.Numerics";
    internal static readonly DiagnosticDescriptor ForbiddenNumericType=new(
        id:"NUM001",
        title:"Engine-owned numeric state must use the configured numeric type",
        messageFormat:"'{0}' is not allowed in this numeric configuration; use the configured logical numeric type or a validated boundary type",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true,
        description:"Double is available when the project selects Double. Decimal and BigInteger remain restricted to external adapter signatures and independent test oracles.");
    internal static ImmutableArray<DiagnosticDescriptor> All=>[ForbiddenNumericType];
}
