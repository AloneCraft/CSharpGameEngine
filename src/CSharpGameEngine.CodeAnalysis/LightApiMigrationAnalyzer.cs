using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// Reports the pre-physical-light member vocabulary only in an authored Scene compilation that
/// opted into <c>TransformPrecision</c>. The syntax pass intentionally also sees members that no
/// longer bind after an API upgrade, so users receive a migration diagnostic instead of only a
/// generic missing-member compiler error.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LightApiMigrationAnalyzer:DiagnosticAnalyzer {
    private const string TransformPrecisionAttributeName=
        "global::CSharpGameEngine.Entities.TransformPrecisionAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics=>LightApiMigrationDiagnostics.All;

    public override void Initialize(AnalysisContext Context){
        Context.EnableConcurrentExecution();
        Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        Context.RegisterCompilationStartAction(static StartContext=>{
            if(!HasTransformPrecisionContract(StartContext.Compilation)){
                return;
            }

            StartContext.RegisterSyntaxNodeAction(
                AnalyzeAssignment,SyntaxKind.SimpleAssignmentExpression);
        });
    }

    private static bool HasTransformPrecisionContract(Compilation Compilation){
        foreach(var Attribute in Compilation.Assembly.GetAttributes()){
            if(Attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                ==TransformPrecisionAttributeName){
                return true;
            }
        }

        return false;
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext Context){
        var Assignment=(AssignmentExpressionSyntax)Context.Node;

        // Resolve the receiver type from syntax, rather than the target member symbol. A removed
        // member is represented by an invalid operation and never reaches an operation action,
        // but its object-creation type or member-access receiver is still semantically known.
        if(!TryGetLegacyMemberAssignment(
                Assignment,Context.SemanticModel,Context.CancellationToken,
                out var Type,out var MemberName)
            ||Type is null
            ||MemberName is null
            ||!TryGetLightName(Type,out var LightName)
            ||!TryGetGuidance(LightName,MemberName,out var Guidance)){
            return;
        }

        Context.ReportDiagnostic(Diagnostic.Create(
            LightApiMigrationDiagnostics.LegacyLightMember,
            Assignment.Left.GetLocation(),Type.Name,MemberName,Guidance));
    }

    private static bool TryGetLegacyMemberAssignment(
        AssignmentExpressionSyntax Assignment,SemanticModel SemanticModel,CancellationToken CancellationToken,
        out INamedTypeSymbol? Type,out string? MemberName){
        Type=null;
        MemberName=null;

        switch(Assignment.Left){
        case IdentifierNameSyntax Identifier:
            // `new PointLight { Intensity = ... }`: even after Intensity is removed, the type
            // syntax itself binds to PointLight. Use the nearest creation so nested initializers
            // do not inherit the outer object's type.
            var Creation=FindContainingObjectCreation(Assignment);
            if(Creation is null){
                return false;
            }

            Type=SemanticModel.GetTypeInfo(Creation,CancellationToken).Type as INamedTypeSymbol;
            MemberName=Identifier.Identifier.ValueText;
            return Type is not null;

        case MemberAccessExpressionSyntax Access:
            // `light.Intensity = ...`: Access itself is invalid once Intensity is removed, but
            // its receiver (`light`) remains a correctly bound PointLight expression.
            Type=SemanticModel.GetTypeInfo(Access.Expression,CancellationToken).Type as INamedTypeSymbol;
            MemberName=Access.Name.Identifier.ValueText;
            return Type is not null;

        default:
            return false;
        }
    }

    private static ObjectCreationExpressionSyntax? FindContainingObjectCreation(AssignmentExpressionSyntax Assignment){
        foreach(var Ancestor in Assignment.Ancestors()){
            if(Ancestor is ObjectCreationExpressionSyntax { Initializer:not null } Creation
                &&Creation.Initializer.Span.Contains(Assignment.Span)){
                return Creation;
            }
        }

        return null;
    }

    private static bool TryGetLightName(INamedTypeSymbol Type,out string LightName){
        LightName=Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) switch {
            "global::UserEcsProject.DirectionalLight"=>"DirectionalLight",
            "global::UserEcsProject.PointLight"=>"PointLight",
            "global::UserEcsProject.SpotLight"=>"SpotLight",
            "global::UserEcsProject.AreaLight"=>"AreaLight",
            _=>string.Empty,
        };
        return LightName.Length!=0;
    }

    private static bool TryGetGuidance(string LightName,string MemberName,out string Guidance){
        Guidance=string.Empty;

        if(IsPosition(MemberName)){
            Guidance=$"Move the value to Transform.{MemberName} (meters).";
            return true;
        }

        if(IsDirection(MemberName)){
            Guidance=LightName=="DirectionalLight"&&MemberName.StartsWith("DirectionTowardLight",StringComparison.Ordinal)
                ?"Invert the old toward-light vector and set Transform.Rotation so local +Z is the light travel direction."
                :"Set Transform.Rotation so local +Z is the light travel direction.";
            return true;
        }

        switch(LightName){
        case "DirectionalLight" when MemberName=="Intensity":
            Guidance="Use IlluminanceLux in lux; choose the authored lux value explicitly because unitless Intensity has no safe automatic conversion.";
            return true;
        case "PointLight" when MemberName=="Intensity":
        case "SpotLight" when MemberName=="Intensity":
            Guidance="Use LuminousFluxLumens in lumens; choose the authored flux explicitly because unitless Intensity has no safe automatic conversion.";
            return true;
        case "AreaLight" when MemberName=="Intensity":
            Guidance="Use LuminanceNits in cd/m^2 (nits); choose the authored luminance explicitly because unitless Intensity has no safe automatic conversion.";
            return true;
        case "PointLight" when MemberName=="Range":
        case "SpotLight" when MemberName=="Range":
        case "AreaLight" when MemberName=="Range":
            Guidance="Use RangeMeters in meters.";
            return true;
        case "SpotLight" when MemberName=="InnerConeAngleRadians":
            Guidance="Use InnerConeFullAngleDegrees; convert the old half-angle with fullDegrees = radians * 360 / Math.PI.";
            return true;
        case "SpotLight" when MemberName=="OuterConeAngleRadians":
            Guidance="Use OuterConeFullAngleDegrees; convert the old half-angle with fullDegrees = radians * 360 / Math.PI.";
            return true;
        case "AreaLight" when MemberName=="Width":
            Guidance="Use WidthMeters in meters; Transform.Scale does not change physical area size.";
            return true;
        case "AreaLight" when MemberName=="Height":
            Guidance="Use HeightMeters in meters; Transform.Scale does not change physical area size.";
            return true;
        default:
            return false;
        }
    }

    private static bool IsPosition(string MemberName)=>
        MemberName is "PositionX" or "PositionY" or "PositionZ";

    private static bool IsDirection(string MemberName)=>
        MemberName is "DirectionX" or "DirectionY" or "DirectionZ"
            or "DirectionTowardLightX" or "DirectionTowardLightY" or "DirectionTowardLightZ";
}
