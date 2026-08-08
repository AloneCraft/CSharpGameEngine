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
internal static class SceneDefinitionSourceParser
{
    internal static IReadOnlyList<string> ParseNodeNamesInDeclarationOrder(string sourceText)
    {
        CompilationUnitSyntax root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(sourceText).GetRoot();

        ClassDeclarationSyntax? sceneClass = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(HasSceneDefinitionAttribute);

        MethodDeclarationSyntax? defineMethod = sceneClass?.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "Define");

        if (defineMethod?.Body is not { } defineBody)
        {
            return [];
        }

        List<string> names = [];

        foreach (StatementSyntax statement in defineBody.Statements)
        {
            if (TryGetCreateInvocation(statement, out InvocationExpressionSyntax? invocation))
            {
                CollectNodeNames(invocation!, names);
            }
        }

        return names;
    }

    /// <summary>Recurses depth-first, matching the exact order <c>SceneRuntimeEmitter</c> creates entities in - this is what makes zipping this list against <c>Entity.Id</c>-sorted runtime nodes sound.</summary>
    private static void CollectNodeNames(InvocationExpressionSyntax createInvocation, List<string> names)
    {
        SeparatedSyntaxList<ArgumentSyntax> arguments = createInvocation.ArgumentList.Arguments;

        if (arguments.Count != 2
            || arguments[0].Expression is not IdentifierNameSyntax nodeArgIdentifier
            || arguments[1].Expression is not SimpleLambdaExpressionSyntax { Body: BlockSyntax lambdaBlock })
        {
            return;
        }

        names.Add(nodeArgIdentifier.Identifier.Text);

        foreach (StatementSyntax statement in lambdaBlock.Statements)
        {
            if (TryGetCreateInvocation(statement, out InvocationExpressionSyntax? childInvocation))
            {
                CollectNodeNames(childInvocation!, names);
            }
        }
    }

    private static bool TryGetCreateInvocation(StatementSyntax statement, out InvocationExpressionSyntax? invocation)
    {
        if (statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv }
            && inv.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Create" })
        {
            invocation = inv;
            return true;
        }

        invocation = null;
        return false;
    }

    private static bool HasSceneDefinitionAttribute(ClassDeclarationSyntax classDeclaration)
    {
        foreach (AttributeListSyntax attributeList in classDeclaration.AttributeLists)
        {
            foreach (AttributeSyntax attribute in attributeList.Attributes)
            {
                string name = attribute.Name.ToString();

                if (name is "SceneDefinition" or "SceneDefinitionAttribute"
                    or "CSharpGameEngine.Entities.SceneDefinition" or "CSharpGameEngine.Entities.SceneDefinitionAttribute")
                {
                    return true;
                }
            }
        }

        return false;
    }
}
