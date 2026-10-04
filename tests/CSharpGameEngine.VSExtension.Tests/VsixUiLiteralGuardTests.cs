using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class VsixUiLiteralGuardTests {
    private static readonly HashSet<string>UiMethods=new(StringComparer.Ordinal){
        "Show",
        "ShowMessage",
        "ShowMessageBox",
        "SetStatus",
        "SetStatusBarText",
        "SetText",
        "Failure",
        "Success",
    };
    private static readonly HashSet<string>UiProperties=new(StringComparer.Ordinal){
        "Caption",
        "Content",
        "Header",
        "StatusText",
        "Text",
        "Title",
        "ToolTip",
    };
    private static readonly HashSet<string>UiArgumentNames=new(StringComparer.Ordinal){
        "Caption",
        "Content",
        "Description",
        "DisplayName",
        "ErrorMessage",
        "Header",
        "Label",
        "Message",
        "Status",
        "Text",
        "Title",
        "ToolTip",
    };
    private static readonly HashSet<string>UiObjectTypes=new(StringComparer.Ordinal){
        "Button",
        "CheckBox",
        "Label",
        "MenuItem",
        "SceneCameraPreview",
        "SceneEditCommitResult",
        "TextBlock",
        "ToolTip",
    };
    private static readonly HashSet<string>InvariantReturnMembers=new(StringComparer.Ordinal){
        "VsixBuildIdentity.ActivityLogText",
        "VsixBuildIdentity.DisplayText",
    };
    [Fact]
    public void KnownUiSinks_DoNotReceiveNewDisplayStringLiterals(){
        string DirectoryPath=Path.Combine(VsixBuildIdentity.RepoRoot,"tools","CSharpGameEngine.VSExtension");
        HashSet<string>Violations=new(StringComparer.Ordinal);
        foreach(string FilePath in Directory.EnumerateFiles(DirectoryPath,"*.cs",SearchOption.TopDirectoryOnly)){
            SyntaxTree Tree=CSharpSyntaxTree.ParseText(File.ReadAllText(FilePath));
            SyntaxNode Root=Tree.GetRoot();
            foreach(InvocationExpressionSyntax Invocation in Root.DescendantNodes().OfType<InvocationExpressionSyntax>()){
                string Name=Invocation.Expression switch {
                    IdentifierNameSyntax Identifier=>Identifier.Identifier.ValueText,
                    MemberAccessExpressionSyntax Member=>Member.Name.Identifier.ValueText,
                    _=>string.Empty,
                };
                if(!UiMethods.Contains(Name)){
                    continue;
                }
                foreach(ArgumentSyntax Argument in Invocation.ArgumentList.Arguments){
                    if(IsDisplayLiteral(Argument.Expression)){
                        AddViolation(Violations,FilePath,Argument.Expression);
                    }
                }
            }
            foreach(AssignmentExpressionSyntax Assignment in Root.DescendantNodes().OfType<AssignmentExpressionSyntax>()){
                InspectUiPropertyAssignment(Violations,FilePath,Assignment);
            }
            foreach(BaseObjectCreationExpressionSyntax Creation in Root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>()){
                if(!IsUiObjectCreation(Creation)){
                    continue;
                }
                foreach(ArgumentSyntax Argument in Creation.ArgumentList?.Arguments??default){
                    if(IsDisplayLiteral(Argument.Expression)){
                        AddViolation(Violations,FilePath,Argument.Expression);
                    }
                }
            }
            foreach(InitializerExpressionSyntax Initializer in Root.DescendantNodes().OfType<InitializerExpressionSyntax>()){
                foreach(AssignmentExpressionSyntax Assignment in Initializer.Expressions.OfType<AssignmentExpressionSyntax>()){
                    InspectUiPropertyAssignment(Violations,FilePath,Assignment);
                }
            }
            foreach(ReturnStatementSyntax Return in Root.DescendantNodes().OfType<ReturnStatementSyntax>()){
                if(Return.Expression is { }Expression&&IsUiReturningMember(Return)&&IsDisplayLiteral(Expression)){
                    AddViolation(Violations,FilePath,Expression);
                }
            }
            foreach(ArrowExpressionClauseSyntax Arrow in Root.DescendantNodes().OfType<ArrowExpressionClauseSyntax>()){
                if(IsUiReturningMember(Arrow)&&IsDisplayLiteral(Arrow.Expression)){
                    AddViolation(Violations,FilePath,Arrow.Expression);
                }
            }
        }
        Assert.True(Violations.Count==0,
            "VSIX UI literals must use VsixText/LocExtension:"+Environment.NewLine+string.Join(Environment.NewLine,Violations.OrderBy(Value=>Value,StringComparer.Ordinal)));
    }
    private static void InspectUiPropertyAssignment(ISet<string>Violations,string FilePath,AssignmentExpressionSyntax Assignment){
        string Name=Assignment.Left switch {
            IdentifierNameSyntax Identifier=>Identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax Member=>Member.Name.Identifier.ValueText,
            _=>string.Empty,
        };
        if(UiProperties.Contains(Name)&&IsDisplayLiteral(Assignment.Right)){
            AddViolation(Violations,FilePath,Assignment.Right);
        }
    }
    private static bool IsUiObjectCreation(BaseObjectCreationExpressionSyntax Creation){
        if(Creation is ObjectCreationExpressionSyntax Explicit&&UiObjectTypes.Contains(GetTypeName(Explicit.Type))){
            return true;
        }
        if(Creation.ArgumentList?.Arguments.Any(Argument=>
            Argument.NameColon?.Name is IdentifierNameSyntax Name&&UiArgumentNames.Contains(Name.Identifier.ValueText))==true){
            return true;
        }
        if(Creation is not ImplicitObjectCreationExpressionSyntax){
            return false;
        }
        TypeSyntax?TargetType=Creation.Ancestors().Select(Ancestor=>Ancestor switch {
            VariableDeclarationSyntax Variable=>Variable.Type,
            PropertyDeclarationSyntax Property=>Property.Type,
            MethodDeclarationSyntax Method=>Method.ReturnType,
            _=>null,
        }).FirstOrDefault(Type=>Type is not null);
        return TargetType is not null&&UiObjectTypes.Contains(GetTypeName(TargetType));
    }
    private static bool IsDisplayLiteral(ExpressionSyntax Expression)=>Expression switch {
        LiteralExpressionSyntax Literal=>Literal.IsKind(SyntaxKind.StringLiteralExpression),
        InterpolatedStringExpressionSyntax=>true,
        BinaryExpressionSyntax Binary=>IsDisplayLiteral(Binary.Left)||IsDisplayLiteral(Binary.Right),
        ConditionalExpressionSyntax Conditional=>IsDisplayLiteral(Conditional.WhenTrue)||IsDisplayLiteral(Conditional.WhenFalse),
        ParenthesizedExpressionSyntax Parenthesized=>IsDisplayLiteral(Parenthesized.Expression),
        CastExpressionSyntax Cast=>IsDisplayLiteral(Cast.Expression),
        SwitchExpressionSyntax Switch=>Switch.Arms.Any(Arm=>IsDisplayLiteral(Arm.Expression)),
        _=>false,
    };
    private static string GetTypeName(TypeSyntax Type)=>Type switch {
        IdentifierNameSyntax Identifier=>Identifier.Identifier.ValueText,
        QualifiedNameSyntax Qualified=>Qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax Alias=>Alias.Name.Identifier.ValueText,
        GenericNameSyntax Generic=>Generic.Identifier.ValueText,
        _=>Type.ToString(),
    };
    private static bool IsUiReturningMember(SyntaxNode Node){
        MemberDeclarationSyntax?Member=Node.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        string MemberName=Member switch {
            MethodDeclarationSyntax Method=>Method.Identifier.ValueText,
            PropertyDeclarationSyntax Property=>Property.Identifier.ValueText,
            IndexerDeclarationSyntax=>"Item",
            _=>string.Empty,
        };
        string TypeName=Member?.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText??string.Empty;
        if(InvariantReturnMembers.Contains(TypeName+"."+MemberName)){
            return false;
        }
        if(Member is MethodDeclarationSyntax MethodDeclaration
            &&UiObjectTypes.Contains(GetTypeName(MethodDeclaration.ReturnType))){
            return true;
        }
        return UiMethods.Contains(MemberName)
            ||UiProperties.Contains(MemberName)
            ||MemberName.StartsWith("Describe",StringComparison.Ordinal)
            ||MemberName.EndsWith("Text",StringComparison.Ordinal)
            ||MemberName.EndsWith("Message",StringComparison.Ordinal)
            ||MemberName.EndsWith("Status",StringComparison.Ordinal)
            ||string.Equals(TypeName,"DebugStatusText",StringComparison.Ordinal);
    }
    private static void AddViolation(ISet<string>Violations,string FilePath,SyntaxNode Node){
        FileLinePositionSpan Span=Node.GetLocation().GetLineSpan();
        Violations.Add($"{Path.GetFileName(FilePath)}:{Span.StartLinePosition.Line+1}: {Node}");
    }
}