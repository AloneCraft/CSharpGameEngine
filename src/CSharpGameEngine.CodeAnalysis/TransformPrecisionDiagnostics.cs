using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>Diagnostics emitted by <see cref="TransformPrecisionGenerator"/> for its assembly-level input contract.</summary>
internal static class TransformPrecisionDiagnostics {
    private const string Category="CSharpGameEngine.Entities.TransformPrecision";

    internal static readonly DiagnosticDescriptor DuplicateConfiguration=new(
        id:"TRFPREC001",
        title:"Only one TransformPrecision configuration is allowed",
        messageFormat:"This compilation declares TransformPrecision {0} times; declare it once at assembly scope",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor UnsupportedTotalBits=new(
        id:"TRFPREC002",
        title:"TransformPrecision total bits are unsupported",
        messageFormat:"TransformPrecision TotalBits '{0}' is unsupported; use one of 64, 128, 192, 256, 320, 384, 448, or 512",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor InvalidFractionalBits=new(
        id:"TRFPREC003",
        title:"TransformPrecision fractional bits are out of range",
        messageFormat:"TransformPrecision FractionalBits '{0}' is invalid for TotalBits '{1}'; it must be between 1 and {2}",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);

    internal static readonly DiagnosticDescriptor InvalidArguments=new(
        id:"TRFPREC004",
        title:"TransformPrecision arguments must be compile-time integer constants",
        messageFormat:"TransformPrecision must receive exactly two compile-time int constants: TotalBits and FractionalBits",
        category:Category,
        defaultSeverity:DiagnosticSeverity.Error,
        isEnabledByDefault:true);
}
