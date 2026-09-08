using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
namespace CSharpGameEngine.CodeAnalysis;
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CanonicalNumericAnalyzer:DiagnosticAnalyzer {
    private const string AdapterAttribute="CSharpGameEngine.Contracts.ExternalNumericAdapterAttribute";
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics=>CanonicalNumericDiagnostics.All;
    public override void Initialize(AnalysisContext Context){
        Context.EnableConcurrentExecution();
        Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        Context.RegisterCompilationStartAction(static StartContext=>{
            if(!IsProductCompilation(StartContext.Compilation.AssemblyName)){ return; }
            StartContext.RegisterSyntaxNodeAction(AnalyzePredefinedType,SyntaxKind.PredefinedType);
            StartContext.RegisterSyntaxNodeAction(AnalyzeIdentifierName,SyntaxKind.IdentifierName);
            StartContext.RegisterSyntaxNodeAction(AnalyzeNumericLiteral,SyntaxKind.NumericLiteralExpression);
            StartContext.RegisterSyntaxNodeAction(AnalyzeInferredExpression,SyntaxKind.SimpleMemberAccessExpression,SyntaxKind.InvocationExpression,SyntaxKind.ElementAccessExpression);
        });
    }
    private static bool IsProductCompilation(string? AssemblyName)=>AssemblyName=="UserEcsProject"
        ||AssemblyName is not null&&AssemblyName.StartsWith("CSharpGameEngine.",StringComparison.Ordinal)&&!AssemblyName.EndsWith(".Tests",StringComparison.Ordinal)&&AssemblyName!="CSharpGameEngine.CodeAnalysis";
    private static void AnalyzePredefinedType(SyntaxNodeAnalysisContext Context){
        var Syntax=(PredefinedTypeSyntax)Context.Node;
        if(IsAdapter(Context,Syntax)){ return; }
        var Type=Context.SemanticModel.GetTypeInfo(Syntax,Context.CancellationToken).Type;
        if(!ContainsForbiddenType(Type,out var Forbidden)){ return; }
        Context.ReportDiagnostic(Diagnostic.Create(CanonicalNumericDiagnostics.ForbiddenNumericType,Syntax.GetLocation(),Forbidden));
    }
    private static void AnalyzeIdentifierName(SyntaxNodeAnalysisContext Context){
        var Syntax=(IdentifierNameSyntax)Context.Node;
        if(Syntax.Identifier.ValueText=="var"||IsAdapter(Context,Syntax)){ return; }
        var Symbol=Context.SemanticModel.GetSymbolInfo(Syntax,Context.CancellationToken).Symbol;
        var Type=Symbol switch {
            IAliasSymbol Alias=>Alias.Target as ITypeSymbol,
            ITypeSymbol TypeSymbol=>TypeSymbol,
            _=>null,
        };
        if(!ContainsForbiddenType(Type,out var Forbidden)){ return; }
        Context.ReportDiagnostic(Diagnostic.Create(CanonicalNumericDiagnostics.ForbiddenNumericType,Syntax.GetLocation(),Forbidden));
    }
    private static void AnalyzeInferredExpression(SyntaxNodeAnalysisContext Context){
        var Syntax=(ExpressionSyntax)Context.Node;
        if(IsAdapter(Context,Syntax)||IsProjectedExternalMember(Context,Syntax)||HasExplicitForbiddenSource(Context,Syntax)){ return; }
        var Type=Context.SemanticModel.GetTypeInfo(Syntax,Context.CancellationToken).Type;
        if(!ContainsForbiddenType(Type,out var Forbidden)){ return; }
        Context.ReportDiagnostic(Diagnostic.Create(CanonicalNumericDiagnostics.ForbiddenNumericType,Syntax.GetLocation(),Forbidden));
    }
    private static bool IsProjectedExternalMember(SyntaxNodeAnalysisContext Context,ExpressionSyntax Expression){
        var Symbol=Context.SemanticModel.GetSymbolInfo(Expression,Context.CancellationToken).Symbol;
        if(Symbol?.ContainingType is null||!IsExternalAdapterType(Symbol.ContainingType)){ return false; }
        if(Expression.Parent is AssignmentExpressionSyntax Assignment&&Assignment.Left==Expression){ return true; }
        if(Expression.Parent is ArgumentSyntax Argument&&Argument.Parent?.Parent is InvocationExpressionSyntax Invocation){
            var Target=Context.SemanticModel.GetSymbolInfo(Invocation,Context.CancellationToken).Symbol;
            if(Target?.GetAttributes().Any(static Attribute=>Attribute.AttributeClass?.ToDisplayString()==AdapterAttribute)==true){ return true; }
        }
        if(Expression.Parent is not CastExpressionSyntax Cast||Cast.Expression!=Expression){ return false; }
        var ProjectedType=Context.SemanticModel.GetTypeInfo(Cast.Type,Context.CancellationToken).Type;
        return ProjectedType?.SpecialType==SpecialType.System_Single;
    }
    private static bool HasExplicitForbiddenSource(SyntaxNodeAnalysisContext Context,ExpressionSyntax Expression){
        foreach(var Syntax in Expression.DescendantNodesAndSelf()){
            ITypeSymbol? Type=null;
            if(Syntax is PredefinedTypeSyntax Predefined){ Type=Context.SemanticModel.GetTypeInfo(Predefined,Context.CancellationToken).Type; }
            else if(Syntax is IdentifierNameSyntax Identifier){
                var Symbol=Context.SemanticModel.GetSymbolInfo(Identifier,Context.CancellationToken).Symbol;
                Type=Symbol switch {
                    IAliasSymbol Alias=>Alias.Target as ITypeSymbol,
                    ITypeSymbol TypeSymbol=>TypeSymbol,
                    _=>null,
                };
            }
            else if(Syntax is LiteralExpressionSyntax Literal){ Type=Context.SemanticModel.GetTypeInfo(Literal,Context.CancellationToken).Type; }
            if(ContainsForbiddenType(Type,out _)){ return true; }
        }
        return false;
    }
    private static void AnalyzeNumericLiteral(SyntaxNodeAnalysisContext Context){
        var Syntax=(LiteralExpressionSyntax)Context.Node;
        if(IsAdapter(Context,Syntax)){ return; }
        var Type=Context.SemanticModel.GetTypeInfo(Syntax,Context.CancellationToken).Type;
        if(!ContainsForbiddenType(Type,out var Forbidden)){ return; }
        Context.ReportDiagnostic(Diagnostic.Create(CanonicalNumericDiagnostics.ForbiddenNumericType,Syntax.GetLocation(),Forbidden));
    }
    private static bool IsAdapter(ISymbol? Symbol){
        for(var Current=Symbol;Current is not null;Current=Current.ContainingSymbol){
            if((Current is IMethodSymbol||Current is IPropertySymbol)&&Current.GetAttributes().Any(static Attribute=>Attribute.AttributeClass?.ToDisplayString()==AdapterAttribute)){ return true; }
            if(Current is IMethodSymbol Method&&(Method.ExplicitInterfaceImplementations.Any(static Implementation=>IsExternalAdapterType(Implementation.ContainingType))||Method.OverriddenMethod is not null&&IsExternalAdapterType(Method.OverriddenMethod.ContainingType)||Method.Parameters.Any(static Parameter=>IsExternalAdapterType(Parameter.Type))||ImplementsExternalInterfaceMethod(Method))){ return true; }
        }
        return false;
    }
    private static bool IsAdapter(SyntaxNodeAnalysisContext Context,SyntaxNode Syntax){
        if(IsAdapter(Context.ContainingSymbol)){ return true; }
        foreach(var Declaration in Syntax.AncestorsAndSelf().OfType<MemberDeclarationSyntax>()){
            var Symbol=Context.SemanticModel.GetDeclaredSymbol(Declaration,Context.CancellationToken);
            if(IsAdapter(Symbol)){ return true; }
        }
        return false;
    }
    private static bool ImplementsExternalInterfaceMethod(IMethodSymbol Method){
        foreach(var Interface in Method.ContainingType.AllInterfaces){
            if(!IsExternalAdapterType(Interface)){ continue; }
            foreach(var Member in Interface.GetMembers(Method.Name)){
                if(SymbolEqualityComparer.Default.Equals(Method.ContainingType.FindImplementationForInterfaceMember(Member),Method)){ return true; }
            }
        }
        return false;
    }
    private static bool IsExternalAdapterType(ITypeSymbol Type){
        var Namespace=Type.ContainingNamespace?.ToDisplayString()??string.Empty;
        return Namespace.StartsWith("System.Windows",StringComparison.Ordinal)||Namespace.StartsWith("System.Drawing",StringComparison.Ordinal);
    }
    private static bool ContainsForbiddenType(ITypeSymbol? Type,out string Forbidden){
        Forbidden=string.Empty;
        if(Type is null){ return false; }
        if(Type.SpecialType==SpecialType.System_Double){ Forbidden="double";return true; }
        if(Type.SpecialType==SpecialType.System_Decimal){ Forbidden="decimal";return true; }
        if(Type.ToDisplayString()=="System.Numerics.BigInteger"){ Forbidden="BigInteger";return true; }
        if(Type is IArrayTypeSymbol Array){ return ContainsForbiddenType(Array.ElementType,out Forbidden); }
        if(Type is IPointerTypeSymbol Pointer){ return ContainsForbiddenType(Pointer.PointedAtType,out Forbidden); }
        if(Type is INamedTypeSymbol Named){
            foreach(var Argument in Named.TypeArguments){ if(ContainsForbiddenType(Argument,out Forbidden)){ return true; } }
        }
        return false;
    }
}
