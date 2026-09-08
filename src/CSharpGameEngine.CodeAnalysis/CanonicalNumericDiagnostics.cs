using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.CodeAnalysis;
internal static class CanonicalNumericDiagnostics {
    private const string Category="CSharpGameEngine.Numerics";
    internal static readonly DiagnosticDescriptor ForbiddenNumericType=new(
        id:"NUM001",
        title:"Engine-owned numeric state must use LogicalScalar or float",
        messageFormat:"'{0}' is not allowed in engine-owned product code; use LogicalScalar for authoritative values or float at a validated boundary",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true,
        description:"double, decimal, and BigInteger are restricted to external adapter signatures and independent test oracles.");
    internal static ImmutableArray<DiagnosticDescriptor> All=>[ForbiddenNumericType];
}
