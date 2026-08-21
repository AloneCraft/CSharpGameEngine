using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Builds a new <c>Node</c> declaration and all of its initial components before any parent
/// collection is changed. This keeps a blank-Hierarchy component drop atomic in source as well
/// as in the predicted scene model.
/// </summary>
internal static class SceneInitialComponentFactory {
    internal static bool TryCreateNodeCreation(
        string Name,ImmutableArray<NewNodeComponent> InitialComponents,
        out BaseObjectCreationExpressionSyntax Node,out string?ErrorMessage){
        List<ExpressionSyntax>Components=[];
        foreach(NewNodeComponent Component in Normalize(InitialComponents)){
            ExpressionSyntax Expression=SyntaxFactory.ParseExpression(Component.ComponentExpression);
            if(Expression.ContainsDiagnostics||Expression is not BaseObjectCreationExpressionSyntax){
                Node=null!;
                ErrorMessage=$"Initial component '{Component.ComponentTypeFullyQualifiedName}' is not a valid object creation expression.";
                return false;
            }
            Components.Add(Expression);
        }

        Node=CreateNodeCreation(Name,Components);
        ErrorMessage=null;
        return true;
    }

    private static IEnumerable<NewNodeComponent> Normalize(
        ImmutableArray<NewNodeComponent> InitialComponents){
        if(InitialComponents.IsDefaultOrEmpty){
            return Array.Empty<NewNodeComponent>();
        }

        HashSet<string>SeenTypes=new(StringComparer.Ordinal);
        List<(NewNodeComponent Component,SceneComponentOrderKey Key,int Sequence)>FirstDeclarations=[];
        var Sequence=0;
        foreach(NewNodeComponent Component in InitialComponents){
            if(string.IsNullOrWhiteSpace(Component.ComponentTypeFullyQualifiedName)
                ||!SeenTypes.Add(Component.ComponentTypeFullyQualifiedName)){
                Sequence++;
                continue;
            }

            SceneComponentOrderKey Key=Component.OrderKey??SceneComponentOrdering.GetSortKey(
                TypeSymbol:null,Component.ComponentTypeFullyQualifiedName);
            FirstDeclarations.Add((Component,Key,Sequence++));
        }

        return FirstDeclarations
            .OrderBy(static Entry=>Entry.Key.Category)
            .ThenBy(static Entry=>Entry.Key.Order)
            .ThenBy(static Entry=>Entry.Key.FullyQualifiedName,StringComparer.Ordinal)
            .ThenBy(static Entry=>Entry.Sequence)
            .Select(static Entry=>Entry.Component);
    }

    private static BaseObjectCreationExpressionSyntax CreateNodeCreation(
        string Name,IReadOnlyList<ExpressionSyntax> Components){
        List<ExpressionSyntax>Assignments=[
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName("Name"),
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,SyntaxFactory.Literal(Name))),
        ];
        if(Components.Count>0){
            CollectionExpressionSyntax ComponentCollection=SyntaxFactory.CollectionExpression(
                SyntaxFactory.SeparatedList<CollectionElementSyntax>(
                    Components.Select(static Component=>SyntaxFactory.ExpressionElement(Component))));
            Assignments.Add(SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName("Components"),ComponentCollection));
        }

        InitializerExpressionSyntax Initializer=SyntaxFactory.InitializerExpression(
            SyntaxKind.ObjectInitializerExpression,SyntaxFactory.SeparatedList(Assignments));
        return SyntaxFactory.ObjectCreationExpression(SyntaxFactory.IdentifierName("Node"))
            .WithInitializer(Initializer)
            .NormalizeWhitespace();
    }
}
