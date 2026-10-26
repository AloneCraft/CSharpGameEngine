using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>TransformPrecision</c>を指定したSceneのコンパイル時に限り、物理ライティング導入前のメンバー名を報告する。
/// 構文解析ではAPI更新後にバインドされなくなったメンバーも検出するため、一般的なメンバー不在のコンパイラーエラーだけでなく、
/// 移行用の診断をユーザーに提示できる。
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

        // 対象メンバーのシンボルではなく、構文からレシーバーの型を解決する。削除済み
        // メンバーは無効な操作として表現され、操作アクションには到達しないが、
        // オブジェクト生成の型やメンバーアクセスのレシーバーは意味的に特定できる。
        if(!TryGetLegacyMemberAssignment(
                Assignment,Context.SemanticModel,Context.CancellationToken,
                out var Type,out var MemberName)
            ||Type is null
            ||MemberName is null
            ||!TryGetLightName(Type,out var LightName)
            ||(Type.ContainingNamespace.ToDisplayString()=="CSharpGameEngine.Runtime.Lighting"&&MemberName=="Range")
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
            // `new PointLight { Intensity = ... }`：Intensityが削除された後も、型の
            // 構文自体はPointLightにバインドされる。入れ子の初期化子が外側のオブジェクトの型を
            // 引き継がないよう、最も近い生成式を使う。
            var Creation=FindContainingObjectCreation(Assignment);
            if(Creation is null){
                return false;
            }

            Type=SemanticModel.GetTypeInfo(Creation,CancellationToken).Type as INamedTypeSymbol;
            MemberName=Identifier.Identifier.ValueText;
            return Type is not null;

        case MemberAccessExpressionSyntax Access:
            // `light.Intensity = ...`：Intensityの削除後、アクセス自体は無効になるが、
            // レシーバー（`light`）は引き続き正しくバインドされたPointLight式である。
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
            "global::CSharpGameEngine.Runtime.Lighting.DirectionalLight"=>"DirectionalLight",
            "global::CSharpGameEngine.Runtime.Lighting.PointLight"=>"PointLight",
            "global::CSharpGameEngine.Runtime.Lighting.SpotLight"=>"SpotLight",
            "global::CSharpGameEngine.Runtime.Lighting.RectAreaLight"=>"AreaLight",
            "global::CSharpGameEngine.Runtime.Windows.DirectionalLight"=>"DirectionalLight",
            "global::UserEcsProject.DirectionalLight"=>"DirectionalLight",
            "global::CSharpGameEngine.Runtime.Windows.PointLight"=>"PointLight",
            "global::UserEcsProject.PointLight"=>"PointLight",
            "global::CSharpGameEngine.Runtime.Windows.SpotLight"=>"SpotLight",
            "global::UserEcsProject.SpotLight"=>"SpotLight",
            "global::CSharpGameEngine.Runtime.Windows.AreaLight"=>"AreaLight",
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

        if(MemberName is "CastsShadows" or "ShadowPriority" or "ShadowBiasMeters"){
            Guidance="Use the optional Runtime.Lighting.LightShadow component with Priority and Bias=new ShadowBias(meters); omit it when shadows are not requested.";
            return true;
        }
        if(MemberName is "IlluminanceLux" or "LuminousFluxLumens" or "LuminanceNits" or "RangeMeters" or "WidthMeters" or "HeightMeters" or "InnerConeFullAngleDegrees" or "OuterConeFullAngleDegrees"){
            Guidance="Use Runtime.Lighting typed lights and Lighting value constructors: Illuminance, LuminousFlux, Luminance, LightRange, RectangleSize or SpotConeAngles.";
            return true;
        }
        switch(LightName){
        case "DirectionalLight" when MemberName=="Intensity":
            Guidance="Use Illuminance=new Illuminance(lux); choose the authored lux value explicitly because unitless Intensity has no safe automatic conversion.";
            return true;
        case "PointLight" when MemberName=="Intensity":
        case "SpotLight" when MemberName=="Intensity":
            Guidance="Use Flux=new LuminousFlux(lumens); choose the authored flux explicitly because unitless Intensity has no safe automatic conversion.";
            return true;
        case "AreaLight" when MemberName=="Intensity":
            Guidance="Use RectAreaLight.Luminance=new Luminance(nits); choose the authored luminance explicitly because unitless Intensity has no safe automatic conversion.";
            return true;
        case "PointLight" when MemberName=="Range":
        case "SpotLight" when MemberName=="Range":
        case "AreaLight" when MemberName=="Range":
            Guidance="Use Range=new LightRange(meters).";
            return true;
        case "SpotLight" when MemberName=="InnerConeAngleRadians":
            Guidance="Use Cone=new SpotConeAngles(innerFullDegrees, outerFullDegrees); convert the old half-angle with fullDegrees = radians * 360 / Math.PI.";
            return true;
        case "SpotLight" when MemberName=="OuterConeAngleRadians":
            Guidance="Use Cone=new SpotConeAngles(innerFullDegrees, outerFullDegrees); convert the old half-angle with fullDegrees = radians * 360 / Math.PI.";
            return true;
        case "AreaLight" when MemberName=="Width":
            Guidance="Use RectAreaLight.Size=new RectangleSize(widthMeters, heightMeters); Transform.Scale does not change physical area size.";
            return true;
        case "AreaLight" when MemberName=="Height":
            Guidance="Use RectAreaLight.Size=new RectangleSize(widthMeters, heightMeters); Transform.Scale does not change physical area size.";
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
