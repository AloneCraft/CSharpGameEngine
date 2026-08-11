using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.SceneAuthoring.Cli;

/// <summary>
/// Syntax-only (no <c>Compilation</c>/<c>SemanticModel</c>) parse of a <c>[SceneDefinition]</c>
/// source file, used purely to recover node names in depth-first declaration order - the one piece
/// of information that does not survive into the executed <c>Entity</c>/<c>World</c> output
/// (<c>SceneNodeId</c>/<c>SceneNodeSymbol</c> are compile-time-only concepts per ADR-0030). This is
/// deliberately not a re-validation of the DSL rules (that is <c>SceneDefinitionAnalyzer</c>'s job at
/// build time, in <c>CSharpGameEngine.CodeAnalysis</c>, and by the time this bridge runs the target
/// code has already compiled successfully, so those rules are already satisfied). If the shape does
/// not match what is expected, this returns an empty list rather than throwing - name recovery is a
/// display nicety, not load-bearing for the rest of the preview pipeline
/// (<see cref="ScenePreviewBridgeWorker"/> falls back to unnamed nodes when the count does not line
/// up with <see cref="WorldInspector"/>'s output).
/// </summary>
internal static class SceneDefinitionSourceParser {
    internal static IReadOnlyList<string>ParseNodeNamesInDeclarationOrder(string SourceText){
        CompilationUnitSyntax Root=(CompilationUnitSyntax)CSharpSyntaxTree.ParseText(SourceText).GetRoot();

        ClassDeclarationSyntax?SceneClass=Root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(HasSceneDefinitionAttribute);

        MethodDeclarationSyntax?DefineMethod=SceneClass?.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(M=>M.Identifier.Text=="Define");

        if(DefineMethod?.Body is not { } DefineBody){
            return[];
        }

        List<string>Names=[];

        foreach(StatementSyntax Statement in DefineBody.Statements){
            if(TryGetCreateInvocation(Statement,out InvocationExpressionSyntax?Invocation)){
                CollectNodeNames(Invocation!,Names);
            }
        }

        return Names;
    }

    /// <summary>Recurses depth-first, matching the exact order <c>SceneRuntimeEmitter</c> creates entities in - this is what makes zipping this list against <c>Entity.Id</c>-sorted runtime nodes sound.</summary>
    private static void CollectNodeNames(InvocationExpressionSyntax CreateInvocation,List<string>Names){
        SeparatedSyntaxList<ArgumentSyntax>Arguments=CreateInvocation.ArgumentList.Arguments;

        if(Arguments.Count!=2
            ||Arguments[0].Expression is not IdentifierNameSyntax NodeArgIdentifier
            ||Arguments[1].Expression is not SimpleLambdaExpressionSyntax { Body:BlockSyntax LambdaBlock }){
            return;
        }

        Names.Add(NodeArgIdentifier.Identifier.Text);

        foreach(StatementSyntax Statement in LambdaBlock.Statements){
            if(TryGetCreateInvocation(Statement,out InvocationExpressionSyntax?ChildInvocation)){
                CollectNodeNames(ChildInvocation!,Names);
            }
        }
    }

    private static bool TryGetCreateInvocation(StatementSyntax Statement,out InvocationExpressionSyntax?Invocation){
        if(Statement is ExpressionStatementSyntax { Expression:InvocationExpressionSyntax Inv }
            &&Inv.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text:"Create" }){
            Invocation=Inv;
            return true;
        }

        Invocation=null;
        return false;
    }

    private static bool HasSceneDefinitionAttribute(ClassDeclarationSyntax ClassDeclaration){
        foreach(AttributeListSyntax AttributeList in ClassDeclaration.AttributeLists){
            foreach(AttributeSyntax attribute in AttributeList.Attributes){
                var Name=attribute.Name.ToString();

                if(Name is "SceneDefinition" or "SceneDefinitionAttribute"
                    or "CSharpGameEngine.Entities.SceneDefinition" or "CSharpGameEngine.Entities.SceneDefinitionAttribute"){
                    return true;
                }
            }
        }

        return false;
    }
}
