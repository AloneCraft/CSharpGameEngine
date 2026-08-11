using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>[SceneDefinition]</c>が付いた静的クラスの解析: <c>partial SceneNodeSymbol</c>プロパティ一覧を集め、
/// <c>Define</c>メソッドの構文木を<c>Create</c>(親子関係)/<c>Set</c>(Component)呼び出しのネストとして
/// 静的に解析し、木構造の<see cref="SceneNodeModel"/>へ変換する。<c>Define</c>は実行せず構文だけを見る
/// (ランタイムでは<c>SceneRuntimeEmitter</c>が生成する直接呼び出しコードを使う)。ランタイム型へは
/// 完全修飾名の文字列としてのみ参照し、CSharpGameEngine.CodeAnalysisからCSharpGameEngine.Entitiesへの
/// アセンブリ参照は発生させない(ADR-0001、既存2ジェネレータと同じ方針)。
///
/// <c>Create</c>/<c>Set</c>呼び出しの「レシーバー名」(例: <c>Root.Create(...)</c>の<c>Root</c>)は、
/// 意味解析ではなく現在のスコープ名(1つ外側のCreate呼び出しのラムダ引数名、最上位では<c>Define</c>の
/// <c>SceneBuilder</c>パラメータ名)とのテキスト一致だけで検証する。これにより、あるノードのラムダ本体
/// から、クロージャ経由で外側のノード(祖先)のBuilderを誤って参照する(構文上はそのノードの子に見えて
/// 実際は別ノードの子になってしまう)ケースも正しく検出できる。
/// </summary>
internal static class SceneDefinitionAnalyzer
{
    private const string SceneNodeSymbolFullyQualifiedName = "global::CSharpGameEngine.Entities.SceneNodeSymbol";
    private const string SceneBuilderFullyQualifiedName = "global::CSharpGameEngine.Entities.SceneBuilder";

    internal static SceneDefinitionClassResult Analyze(GeneratorAttributeSyntaxContext Ctx, CancellationToken cancellationToken)
    {
        INamedTypeSymbol ClassSymbol = (INamedTypeSymbol)Ctx.TargetSymbol;
        ClassDeclarationSyntax ClassDeclaration = (ClassDeclarationSyntax)Ctx.TargetNode;
        SemanticModel semanticModel = Ctx.SemanticModel;
        ImmutableArray<Diagnostic>.Builder Diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (!ClassSymbol.IsStatic || ClassSymbol.TypeKind != TypeKind.Class || !ClassDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.MustBeStaticPartialClass, ClassSymbol.Locations.FirstOrDefault(), ClassSymbol.Name));
        }

        Dictionary<string, IPropertySymbol> DeclaredNodesByName = new(StringComparer.Ordinal);
        HashSet<string> KnownNodePropertyNames = new(StringComparer.Ordinal);

        foreach (ISymbol Member in ClassSymbol.GetMembers())
        {
            if (Member is not IPropertySymbol Property || Property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != SceneNodeSymbolFullyQualifiedName)
            {
                continue;
            }

            KnownNodePropertyNames.Add(Property.Name);

            bool IsWellFormed = Property.DeclaredAccessibility == Accessibility.Private
                && Property.IsStatic
                && Property.IsPartialDefinition
                && Property.GetMethod is not null
                && Property.SetMethod is null;

            if (!IsWellFormed)
            {
                Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.NodePropertyMustBeStaticPartialGetOnly, Property.Locations.FirstOrDefault(), Property.Name));
                continue;
            }

            DeclaredNodesByName[Property.Name] = Property;
        }

        IMethodSymbol? DefineMethod = ClassSymbol.GetMembers("Define").OfType<IMethodSymbol>().FirstOrDefault(IsDefineMethodSignature);

        HashSet<string> CreatedNodeNames = new(StringComparer.Ordinal);
        SceneNodeModel? Root = null;
        int TopLevelCreateCount = 0;

        if (DefineMethod is not null
            && DefineMethod.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is MethodDeclarationSyntax { Body: { } DefineBody })
        {
            Root = AnalyzeTopLevel(
                DefineBody,
                DefineMethod.Parameters[0].Name,
                semanticModel,
                DeclaredNodesByName,
                KnownNodePropertyNames,
                CreatedNodeNames,
                Diagnostics,
                out TopLevelCreateCount,
                cancellationToken);
        }

        if (TopLevelCreateCount != 1)
        {
            Location? location = DefineMethod?.Locations.FirstOrDefault() ?? ClassSymbol.Locations.FirstOrDefault();
            Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.TopLevelCreateMustBeSingle, location, TopLevelCreateCount));
        }

        foreach (KeyValuePair<string, IPropertySymbol> Declared in DeclaredNodesByName)
        {
            if (!CreatedNodeNames.Contains(Declared.Key))
            {
                Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.DeclaredNodeNotCreated, Declared.Value.Locations.FirstOrDefault(), Declared.Key));
            }
        }

        // 旧SCENE009(1ノードのComponent数上限8)の検証はADR-0033で削除した。閉集合Archetype方式では
        // 組み合わせごとに専用のstructを生成するため、ジェネリック型引数由来の上限が存在しない。

        return new SceneDefinitionClassResult(
            ClassSymbol.ContainingNamespace.IsGlobalNamespace ? null : ClassSymbol.ContainingNamespace.ToDisplayString(),
            ClassSymbol.Name,
            [.. CollectUsingDirectives(ClassDeclaration)],
            Root,
            Diagnostics.ToImmutable());
    }

    private static bool IsDefineMethodSignature(IMethodSymbol Method)
    {
        return Method.IsStatic
            && Method.ReturnsVoid
            && Method.Parameters.Length == 1
            && Method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == SceneBuilderFullyQualifiedName;
    }

    /// <summary><c>Define</c>直下のブロック: <c>Scene.Create(...)</c>だけを許可し、個数(SCENE006)を数える。</summary>
    private static SceneNodeModel? AnalyzeTopLevel(
        BlockSyntax DefineBody,
        string SceneParameterName,
        SemanticModel semanticModel,
        Dictionary<string, IPropertySymbol> DeclaredNodesByName,
        HashSet<string> KnownNodePropertyNames,
        HashSet<string> CreatedNodeNames,
        ImmutableArray<Diagnostic>.Builder Diagnostics,
        out int TopLevelCreateCount,
        CancellationToken cancellationToken)
    {
        List<SceneNodeModel> TopLevelModels = [];
        TopLevelCreateCount = 0;

        foreach (StatementSyntax Statement in DefineBody.Statements)
        {
            if (!TryGetMemberInvocation(Statement, out string ReceiverName, out string MethodName, out InvocationExpressionSyntax Invocation)
                || ReceiverName != SceneParameterName
                || MethodName != "Create")
            {
                Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.UnsupportedSyntaxInDefine, Statement.GetLocation(), Statement.ToString().Trim()));
                continue;
            }

            TopLevelCreateCount++;

            if (!TryParseCreateArguments(Invocation, Diagnostics, out string NodeArgName, out BlockSyntax LambdaBlock))
            {
                continue;
            }

            SceneNodeModel? Model = ResolveChild(
                NodeArgName,
                LambdaBlock,
                Invocation,
                semanticModel,
                DeclaredNodesByName,
                KnownNodePropertyNames,
                CreatedNodeNames,
                Diagnostics,
                cancellationToken);

            if (Model is { } M)
            {
                TopLevelModels.Add(M);
            }
        }

        return TopLevelCreateCount == 1 && TopLevelModels.Count == 1 ? TopLevelModels[0] : null;
    }

    /// <summary>あるノードのCreateラムダ本体: <c>Create</c>(子ノード)と<c>Set</c>(Component)の両方を許可する。</summary>
    private static (ImmutableArray<ComponentSetModel> Components, ImmutableArray<SceneNodeModel> Children) AnalyzeNodeBody(
        string ScopeName,
        BlockSyntax Block,
        SemanticModel semanticModel,
        Dictionary<string, IPropertySymbol> DeclaredNodesByName,
        HashSet<string> KnownNodePropertyNames,
        HashSet<string> CreatedNodeNames,
        ImmutableArray<Diagnostic>.Builder Diagnostics,
        CancellationToken cancellationToken)
    {
        List<ComponentSetModel> Components = [];
        List<SceneNodeModel> Children = [];

        foreach (StatementSyntax Statement in Block.Statements)
        {
            if (!TryGetMemberInvocation(Statement, out string ReceiverName, out string MethodName, out InvocationExpressionSyntax Invocation) || ReceiverName != ScopeName)
            {
                Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.UnsupportedSyntaxInDefine, Statement.GetLocation(), Statement.ToString().Trim()));
                continue;
            }

            if (MethodName == "Create")
            {
                if (!TryParseCreateArguments(Invocation, Diagnostics, out string NodeArgName, out BlockSyntax LambdaBlock))
                {
                    continue;
                }

                SceneNodeModel? Child = ResolveChild(
                    NodeArgName,
                    LambdaBlock,
                    Invocation,
                    semanticModel,
                    DeclaredNodesByName,
                    KnownNodePropertyNames,
                    CreatedNodeNames,
                    Diagnostics,
                    cancellationToken);

                if (Child is { } C)
                {
                    Children.Add(C);
                }

                continue;
            }

            if (MethodName == "Set")
            {
                ComponentSetModel? Component = AnalyzeSetCall(ScopeName, Invocation, semanticModel, Diagnostics, cancellationToken);

                if (Component is { } Value)
                {
                    Components.Add(Value);
                }

                continue;
            }

            Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.UnsupportedSyntaxInDefine, Invocation.GetLocation(), Invocation.ToString().Trim()));
        }

        return ([.. Components], [.. Children]);
    }

    private static ComponentSetModel? AnalyzeSetCall(
        string ScopeName,
        InvocationExpressionSyntax Invocation,
        SemanticModel semanticModel,
        ImmutableArray<Diagnostic>.Builder Diagnostics,
        CancellationToken cancellationToken)
    {
        if (Invocation.ArgumentList.Arguments.Count != 1)
        {
            Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.UnsupportedSyntaxInDefine, Invocation.GetLocation(), Invocation.ToString().Trim()));
            return null;
        }

        ExpressionSyntax SetArgument = Invocation.ArgumentList.Arguments[0].Expression;

        if (SetArgument is not ObjectCreationExpressionSyntax ObjectCreation)
        {
            Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.ComponentInitializerMustBeObjectCreationExpression, SetArgument.GetLocation(), ScopeName));
            return null;
        }

        ITypeSymbol? ComponentType = semanticModel.GetTypeInfo(ObjectCreation, cancellationToken).Type;

        if (ComponentType is null)
        {
            Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.ComponentInitializerMustBeObjectCreationExpression, SetArgument.GetLocation(), ScopeName));
            return null;
        }

        string TypeFullyQualifiedName = ComponentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string InitializerText = ObjectCreation.NormalizeWhitespace().ToFullString();
        return new ComponentSetModel(TypeFullyQualifiedName, InitializerText);
    }

    /// <summary>
    /// <c>Create</c>の第1引数(ノード参照)を declaredNodesByName から解決し、初出であることを確認したうえで
    /// 子ノードの解析へ再帰する。名前が既知のSceneNodeSymbolプロパティを指していない場合は
    /// (SCENE002で既に報告済みの不正な宣言を除き)SCENE007として報告する。
    /// </summary>
    private static SceneNodeModel? ResolveChild(
        string NodeArgName,
        BlockSyntax LambdaBlock,
        InvocationExpressionSyntax Invocation,
        SemanticModel semanticModel,
        Dictionary<string, IPropertySymbol> DeclaredNodesByName,
        HashSet<string> KnownNodePropertyNames,
        HashSet<string> CreatedNodeNames,
        ImmutableArray<Diagnostic>.Builder Diagnostics,
        CancellationToken cancellationToken)
    {
        if (!DeclaredNodesByName.TryGetValue(NodeArgName, out IPropertySymbol? Property))
        {
            if (!KnownNodePropertyNames.Contains(NodeArgName))
            {
                Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.UnsupportedSyntaxInDefine, Invocation.GetLocation(), Invocation.ToString().Trim()));
            }

            return null;
        }

        if (!CreatedNodeNames.Add(NodeArgName))
        {
            Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.DuplicateNodeCreation, Invocation.GetLocation(), NodeArgName));
            return null;
        }

        (ImmutableArray<ComponentSetModel> Components, ImmutableArray<SceneNodeModel> Children) = AnalyzeNodeBody(
            NodeArgName,
            LambdaBlock,
            semanticModel,
            DeclaredNodesByName,
            KnownNodePropertyNames,
            CreatedNodeNames,
            Diagnostics,
            cancellationToken);

        ulong NodeId = ComputeNodeId(Property.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Location location = Property.Locations.FirstOrDefault() ?? Location.None;
        return new SceneNodeModel(NodeArgName, NodeId, location, Components, Children);
    }

    /// <summary>
    /// <c>&lt;identifier&gt;.&lt;identifier&gt;(...)</c>という1文だけを認識する。それ以外の文の形
    /// (ローカル変数宣言、if/for、複合式など)はfalseを返し、呼び出し側がSCENE007を報告する。
    /// </summary>
    private static bool TryGetMemberInvocation(
        StatementSyntax Statement,
        out string ReceiverName,
        out string MethodName,
        out InvocationExpressionSyntax Invocation)
    {
        if (Statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax Inv }
            && Inv.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax ReceiverIdentifier } MemberAccess)
        {
            ReceiverName = ReceiverIdentifier.Identifier.Text;
            MethodName = MemberAccess.Name.Identifier.Text;
            Invocation = Inv;
            return true;
        }

        ReceiverName = string.Empty;
        MethodName = string.Empty;
        Invocation = null!;
        return false;
    }

    /// <summary>
    /// <c>Create(nodeArg, lambdaParam =&gt; { ... })</c>の引数形を検証する。引数の個数・型が違う場合は
    /// SCENE007、第1引数とラムダ引数名が一致しない場合はSCENE003を報告する。
    /// </summary>
    private static bool TryParseCreateArguments(
        InvocationExpressionSyntax Invocation,
        ImmutableArray<Diagnostic>.Builder Diagnostics,
        out string NodeArgName,
        out BlockSyntax LambdaBlock)
    {
        NodeArgName = string.Empty;
        LambdaBlock = null!;

        SeparatedSyntaxList<ArgumentSyntax> Arguments = Invocation.ArgumentList.Arguments;

        if (Arguments.Count != 2
            || Arguments[0].Expression is not IdentifierNameSyntax NodeArgIdentifier
            || Arguments[1].Expression is not SimpleLambdaExpressionSyntax Lambda
            || Lambda.Body is not BlockSyntax Block)
        {
            Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.UnsupportedSyntaxInDefine, Invocation.GetLocation(), Invocation.ToString().Trim()));
            return false;
        }

        string ArgName = NodeArgIdentifier.Identifier.Text;
        string LambdaParamName = Lambda.Parameter.Identifier.Text;

        if (ArgName != LambdaParamName)
        {
            Diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.CreateArgumentAndLambdaParameterNameMismatch, Invocation.GetLocation(), ArgName, LambdaParamName));
            return false;
        }

        NodeArgName = ArgName;
        LambdaBlock = Block;
        return true;
    }


    /// <summary>
    /// 元のファイルのusingディレクティブを、そのままの文字列として収集する。<see cref="ComponentSetModel.InitializerExpressionText"/>
    /// を生成ファイルへそのまま貼り付ける際、<c>Vector3.One</c>のような非完全修飾参照を解決できるようにするため
    /// (意味解析による完全修飾化は行わない、Phase Aの簡略化方針)。ブロックスコープ・ファイルスコープ両方の
    /// namespace宣言(<see cref="BaseNamespaceDeclarationSyntax"/>)とコンパイル単位直下の両方を辿る。
    /// </summary>
    private static List<string> CollectUsingDirectives(ClassDeclarationSyntax ClassDeclaration)
    {
        List<string> Usings = [];
        SyntaxNode? Node = ClassDeclaration.Parent;

        while (Node is not null)
        {
            if (Node is BaseNamespaceDeclarationSyntax NamespaceDeclaration)
            {
                foreach (UsingDirectiveSyntax UsingDirective in NamespaceDeclaration.Usings)
                {
                    Usings.Add(UsingDirective.NormalizeWhitespace().ToFullString().Trim());
                }
            }
            else if (Node is CompilationUnitSyntax CompilationUnit)
            {
                foreach (UsingDirectiveSyntax usingDirective in CompilationUnit.Usings)
                {
                    Usings.Add(usingDirective.NormalizeWhitespace().ToFullString().Trim());
                }
            }

            Node = Node.Parent;
        }

        return Usings;
    }

    /// <summary>
    /// ノードを表す<c>partial</c>プロパティの完全修飾シンボル名から決定論的に導出する、標準的なFNV-1a
    /// 64bitハッシュ(<see cref="global::CSharpGameEngine.Entities.SceneNodeId"/>参照)。
    /// </summary>
    private static ulong ComputeNodeId(string FullyQualifiedName)
    {
        const ulong OffsetBasis = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        ulong Hash = OffsetBasis;

        foreach (byte B in Encoding.UTF8.GetBytes(FullyQualifiedName))
        {
            Hash ^= B;
            Hash *= Prime;
        }

        return Hash;
    }
}
