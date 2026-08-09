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

    internal static SceneDefinitionClassResult Analyze(GeneratorAttributeSyntaxContext ctx, CancellationToken cancellationToken)
    {
        INamedTypeSymbol classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
        ClassDeclarationSyntax classDeclaration = (ClassDeclarationSyntax)ctx.TargetNode;
        SemanticModel semanticModel = ctx.SemanticModel;
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (!classSymbol.IsStatic || classSymbol.TypeKind != TypeKind.Class || !classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.MustBeStaticPartialClass, classSymbol.Locations.FirstOrDefault(), classSymbol.Name));
        }

        Dictionary<string, IPropertySymbol> declaredNodesByName = new(StringComparer.Ordinal);
        HashSet<string> knownNodePropertyNames = new(StringComparer.Ordinal);

        foreach (ISymbol member in classSymbol.GetMembers())
        {
            if (member is not IPropertySymbol property || property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != SceneNodeSymbolFullyQualifiedName)
            {
                continue;
            }

            knownNodePropertyNames.Add(property.Name);

            bool isWellFormed = property.DeclaredAccessibility == Accessibility.Private
                && property.IsStatic
                && property.IsPartialDefinition
                && property.GetMethod is not null
                && property.SetMethod is null;

            if (!isWellFormed)
            {
                diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.NodePropertyMustBeStaticPartialGetOnly, property.Locations.FirstOrDefault(), property.Name));
                continue;
            }

            declaredNodesByName[property.Name] = property;
        }

        IMethodSymbol? defineMethod = classSymbol.GetMembers("Define").OfType<IMethodSymbol>().FirstOrDefault(IsDefineMethodSignature);

        HashSet<string> createdNodeNames = new(StringComparer.Ordinal);
        SceneNodeModel? root = null;
        int topLevelCreateCount = 0;

        if (defineMethod is not null
            && defineMethod.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is MethodDeclarationSyntax { Body: { } defineBody })
        {
            root = AnalyzeTopLevel(
                defineBody,
                defineMethod.Parameters[0].Name,
                semanticModel,
                declaredNodesByName,
                knownNodePropertyNames,
                createdNodeNames,
                diagnostics,
                out topLevelCreateCount,
                cancellationToken);
        }

        if (topLevelCreateCount != 1)
        {
            Location? location = defineMethod?.Locations.FirstOrDefault() ?? classSymbol.Locations.FirstOrDefault();
            diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.TopLevelCreateMustBeSingle, location, topLevelCreateCount));
        }

        foreach (KeyValuePair<string, IPropertySymbol> declared in declaredNodesByName)
        {
            if (!createdNodeNames.Contains(declared.Key))
            {
                diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.DeclaredNodeNotCreated, declared.Value.Locations.FirstOrDefault(), declared.Key));
            }
        }

        // 旧SCENE009(1ノードのComponent数上限8)の検証はADR-0033で削除した。閉集合Archetype方式では
        // 組み合わせごとに専用のstructを生成するため、ジェネリック型引数由来の上限が存在しない。

        return new SceneDefinitionClassResult(
            classSymbol.ContainingNamespace.IsGlobalNamespace ? null : classSymbol.ContainingNamespace.ToDisplayString(),
            classSymbol.Name,
            [.. CollectUsingDirectives(classDeclaration)],
            root,
            diagnostics.ToImmutable());
    }

    private static bool IsDefineMethodSignature(IMethodSymbol method)
    {
        return method.IsStatic
            && method.ReturnsVoid
            && method.Parameters.Length == 1
            && method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == SceneBuilderFullyQualifiedName;
    }

    /// <summary><c>Define</c>直下のブロック: <c>Scene.Create(...)</c>だけを許可し、個数(SCENE006)を数える。</summary>
    private static SceneNodeModel? AnalyzeTopLevel(
        BlockSyntax defineBody,
        string sceneParameterName,
        SemanticModel semanticModel,
        Dictionary<string, IPropertySymbol> declaredNodesByName,
        HashSet<string> knownNodePropertyNames,
        HashSet<string> createdNodeNames,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        out int topLevelCreateCount,
        CancellationToken cancellationToken)
    {
        List<SceneNodeModel> topLevelModels = [];
        topLevelCreateCount = 0;

        foreach (StatementSyntax statement in defineBody.Statements)
        {
            if (!TryGetMemberInvocation(statement, out string receiverName, out string methodName, out InvocationExpressionSyntax invocation)
                || receiverName != sceneParameterName
                || methodName != "Create")
            {
                diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.UnsupportedSyntaxInDefine, statement.GetLocation(), statement.ToString().Trim()));
                continue;
            }

            topLevelCreateCount++;

            if (!TryParseCreateArguments(invocation, diagnostics, out string nodeArgName, out BlockSyntax lambdaBlock))
            {
                continue;
            }

            SceneNodeModel? model = ResolveChild(
                nodeArgName,
                lambdaBlock,
                invocation,
                semanticModel,
                declaredNodesByName,
                knownNodePropertyNames,
                createdNodeNames,
                diagnostics,
                cancellationToken);

            if (model is { } m)
            {
                topLevelModels.Add(m);
            }
        }

        return topLevelCreateCount == 1 && topLevelModels.Count == 1 ? topLevelModels[0] : null;
    }

    /// <summary>あるノードのCreateラムダ本体: <c>Create</c>(子ノード)と<c>Set</c>(Component)の両方を許可する。</summary>
    private static (ImmutableArray<ComponentSetModel> Components, ImmutableArray<SceneNodeModel> Children) AnalyzeNodeBody(
        string scopeName,
        BlockSyntax block,
        SemanticModel semanticModel,
        Dictionary<string, IPropertySymbol> declaredNodesByName,
        HashSet<string> knownNodePropertyNames,
        HashSet<string> createdNodeNames,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        CancellationToken cancellationToken)
    {
        List<ComponentSetModel> components = [];
        List<SceneNodeModel> children = [];

        foreach (StatementSyntax statement in block.Statements)
        {
            if (!TryGetMemberInvocation(statement, out string receiverName, out string methodName, out InvocationExpressionSyntax invocation) || receiverName != scopeName)
            {
                diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.UnsupportedSyntaxInDefine, statement.GetLocation(), statement.ToString().Trim()));
                continue;
            }

            if (methodName == "Create")
            {
                if (!TryParseCreateArguments(invocation, diagnostics, out string nodeArgName, out BlockSyntax lambdaBlock))
                {
                    continue;
                }

                SceneNodeModel? child = ResolveChild(
                    nodeArgName,
                    lambdaBlock,
                    invocation,
                    semanticModel,
                    declaredNodesByName,
                    knownNodePropertyNames,
                    createdNodeNames,
                    diagnostics,
                    cancellationToken);

                if (child is { } c)
                {
                    children.Add(c);
                }

                continue;
            }

            if (methodName == "Set")
            {
                ComponentSetModel? component = AnalyzeSetCall(scopeName, invocation, semanticModel, diagnostics, cancellationToken);

                if (component is { } value)
                {
                    components.Add(value);
                }

                continue;
            }

            diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.UnsupportedSyntaxInDefine, invocation.GetLocation(), invocation.ToString().Trim()));
        }

        return ([.. components], [.. children]);
    }

    private static ComponentSetModel? AnalyzeSetCall(
        string scopeName,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        CancellationToken cancellationToken)
    {
        if (invocation.ArgumentList.Arguments.Count != 1)
        {
            diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.UnsupportedSyntaxInDefine, invocation.GetLocation(), invocation.ToString().Trim()));
            return null;
        }

        ExpressionSyntax setArgument = invocation.ArgumentList.Arguments[0].Expression;

        if (setArgument is not ObjectCreationExpressionSyntax objectCreation)
        {
            diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.ComponentInitializerMustBeObjectCreationExpression, setArgument.GetLocation(), scopeName));
            return null;
        }

        ITypeSymbol? componentType = semanticModel.GetTypeInfo(objectCreation, cancellationToken).Type;

        if (componentType is null)
        {
            diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.ComponentInitializerMustBeObjectCreationExpression, setArgument.GetLocation(), scopeName));
            return null;
        }

        string typeFullyQualifiedName = componentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string initializerText = objectCreation.NormalizeWhitespace().ToFullString();
        return new ComponentSetModel(typeFullyQualifiedName, initializerText);
    }

    /// <summary>
    /// <c>Create</c>の第1引数(ノード参照)を declaredNodesByName から解決し、初出であることを確認したうえで
    /// 子ノードの解析へ再帰する。名前が既知のSceneNodeSymbolプロパティを指していない場合は
    /// (SCENE002で既に報告済みの不正な宣言を除き)SCENE007として報告する。
    /// </summary>
    private static SceneNodeModel? ResolveChild(
        string nodeArgName,
        BlockSyntax lambdaBlock,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        Dictionary<string, IPropertySymbol> declaredNodesByName,
        HashSet<string> knownNodePropertyNames,
        HashSet<string> createdNodeNames,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        CancellationToken cancellationToken)
    {
        if (!declaredNodesByName.TryGetValue(nodeArgName, out IPropertySymbol? property))
        {
            if (!knownNodePropertyNames.Contains(nodeArgName))
            {
                diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.UnsupportedSyntaxInDefine, invocation.GetLocation(), invocation.ToString().Trim()));
            }

            return null;
        }

        if (!createdNodeNames.Add(nodeArgName))
        {
            diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.DuplicateNodeCreation, invocation.GetLocation(), nodeArgName));
            return null;
        }

        (ImmutableArray<ComponentSetModel> components, ImmutableArray<SceneNodeModel> children) = AnalyzeNodeBody(
            nodeArgName,
            lambdaBlock,
            semanticModel,
            declaredNodesByName,
            knownNodePropertyNames,
            createdNodeNames,
            diagnostics,
            cancellationToken);

        ulong nodeId = ComputeNodeId(property.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Location location = property.Locations.FirstOrDefault() ?? Location.None;
        return new SceneNodeModel(nodeArgName, nodeId, location, components, children);
    }

    /// <summary>
    /// <c>&lt;identifier&gt;.&lt;identifier&gt;(...)</c>という1文だけを認識する。それ以外の文の形
    /// (ローカル変数宣言、if/for、複合式など)はfalseを返し、呼び出し側がSCENE007を報告する。
    /// </summary>
    private static bool TryGetMemberInvocation(
        StatementSyntax statement,
        out string receiverName,
        out string methodName,
        out InvocationExpressionSyntax invocation)
    {
        if (statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv }
            && inv.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiverIdentifier } memberAccess)
        {
            receiverName = receiverIdentifier.Identifier.Text;
            methodName = memberAccess.Name.Identifier.Text;
            invocation = inv;
            return true;
        }

        receiverName = string.Empty;
        methodName = string.Empty;
        invocation = null!;
        return false;
    }

    /// <summary>
    /// <c>Create(nodeArg, lambdaParam =&gt; { ... })</c>の引数形を検証する。引数の個数・型が違う場合は
    /// SCENE007、第1引数とラムダ引数名が一致しない場合はSCENE003を報告する。
    /// </summary>
    private static bool TryParseCreateArguments(
        InvocationExpressionSyntax invocation,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        out string nodeArgName,
        out BlockSyntax lambdaBlock)
    {
        nodeArgName = string.Empty;
        lambdaBlock = null!;

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        if (arguments.Count != 2
            || arguments[0].Expression is not IdentifierNameSyntax nodeArgIdentifier
            || arguments[1].Expression is not SimpleLambdaExpressionSyntax lambda
            || lambda.Body is not BlockSyntax block)
        {
            diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.UnsupportedSyntaxInDefine, invocation.GetLocation(), invocation.ToString().Trim()));
            return false;
        }

        string argName = nodeArgIdentifier.Identifier.Text;
        string lambdaParamName = lambda.Parameter.Identifier.Text;

        if (argName != lambdaParamName)
        {
            diagnostics.Add(Diagnostic.Create(SceneDefinitionDiagnostics.CreateArgumentAndLambdaParameterNameMismatch, invocation.GetLocation(), argName, lambdaParamName));
            return false;
        }

        nodeArgName = argName;
        lambdaBlock = block;
        return true;
    }


    /// <summary>
    /// 元のファイルのusingディレクティブを、そのままの文字列として収集する。<see cref="ComponentSetModel.InitializerExpressionText"/>
    /// を生成ファイルへそのまま貼り付ける際、<c>Vector3.One</c>のような非完全修飾参照を解決できるようにするため
    /// (意味解析による完全修飾化は行わない、Phase Aの簡略化方針)。ブロックスコープ・ファイルスコープ両方の
    /// namespace宣言(<see cref="BaseNamespaceDeclarationSyntax"/>)とコンパイル単位直下の両方を辿る。
    /// </summary>
    private static List<string> CollectUsingDirectives(ClassDeclarationSyntax classDeclaration)
    {
        List<string> usings = [];
        SyntaxNode? node = classDeclaration.Parent;

        while (node is not null)
        {
            if (node is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                foreach (UsingDirectiveSyntax usingDirective in namespaceDeclaration.Usings)
                {
                    usings.Add(usingDirective.NormalizeWhitespace().ToFullString().Trim());
                }
            }
            else if (node is CompilationUnitSyntax compilationUnit)
            {
                foreach (UsingDirectiveSyntax usingDirective in compilationUnit.Usings)
                {
                    usings.Add(usingDirective.NormalizeWhitespace().ToFullString().Trim());
                }
            }

            node = node.Parent;
        }

        return usings;
    }

    /// <summary>
    /// ノードを表す<c>partial</c>プロパティの完全修飾シンボル名から決定論的に導出する、標準的なFNV-1a
    /// 64bitハッシュ(<see cref="global::CSharpGameEngine.Entities.SceneNodeId"/>参照)。
    /// </summary>
    private static ulong ComputeNodeId(string fullyQualifiedName)
    {
        const ulong OffsetBasis = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        ulong hash = OffsetBasis;

        foreach (byte b in Encoding.UTF8.GetBytes(fullyQualifiedName))
        {
            hash ^= b;
            hash *= Prime;
        }

        return hash;
    }
}
