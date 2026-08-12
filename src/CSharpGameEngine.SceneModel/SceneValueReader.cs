using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// Scene DSLで許可される値式の<b>唯一の実装</b>。判定は<b>whitelist(default-deny)</b>であり、
/// <see cref="Read"/>が認識する形以外はすべてSCENE110/111/112になる(ADR-0040)。
///
/// <para>
/// 禁止構文(<c>if</c>/<c>for</c>/LINQ/lambda/<c>await</c>/reflection/…)を列挙して弾く方式は採らない。
/// C#の構文は増え続けるため、列挙方式では<c>with</c>式・パターン・非定数の補間文字列といった
/// 「後から増えた合法な構文」が素通りしてしまう。許可する形だけを数え上げれば、未知の構文は自動的に落ちる。
/// </para>
///
/// <para>
/// <b>不完全な構文で例外を投げてはならない</b>(VSIXがユーザーの入力途中にこれを呼ぶため)。
/// 型が未解決の場合は診断を出さずに<see cref="SceneValueKind.Unsupported"/>を返す —
/// 別のコンパイルエラーが既に出ているので、その上に赤線を重ねない。
/// </para>
/// </summary>
internal sealed class SceneValueReader {
    private readonly SemanticModel SemanticModel;
    private readonly ImmutableArray<Diagnostic>.Builder Diagnostics;
    private readonly CancellationToken CancellationToken;

    internal SceneValueReader(SemanticModel SemanticModel,ImmutableArray<Diagnostic>.Builder Diagnostics,CancellationToken CancellationToken){
        this.SemanticModel=SemanticModel;
        this.Diagnostics=Diagnostics;
        this.CancellationToken=CancellationToken;
    }

    internal SceneValue Read(ExpressionSyntax Expression,int Depth){
        this.CancellationToken.ThrowIfCancellationRequested();

        if(Depth>SceneDslWellKnownNames.MaxRecursionDepth){
            this.ReportUnsupported(Expression,"nesting depth limit");
            return SceneValue.Unsupported(Expression.ToString(),SceneSourceLocation.FromNode(Expression));
        }

        // 1) 入力途中。診断は出さない(R2)。
        if(Expression.IsMissing){
            return SceneValue.Unsupported(Expression.ToString(),SceneSourceLocation.FromNode(Expression));
        }

        TypeInfo Info=this.SemanticModel.GetTypeInfo(Expression,this.CancellationToken);
        ITypeSymbol?ResolvedType=Info.Type??Info.ConvertedType;

        switch(Expression){
            // 2) default / default(T)
            case LiteralExpressionSyntax DefaultLiteral when DefaultLiteral.IsKind(SyntaxKind.DefaultLiteralExpression):
                return this.Create(SceneValueKind.Default,Expression,ResolvedType,ConstantValue:null,ReferencedSymbol:null,"default");
            case DefaultExpressionSyntax:
                return this.Create(SceneValueKind.Default,Expression,ResolvedType,ConstantValue:null,ReferencedSymbol:null,"default");

            // 3) リテラル。トークンテキストをそのまま出して接尾辞(1.5f)・基数(0x1F)・区切り(1_000)を保存する。
            case LiteralExpressionSyntax Literal:
                return this.Create(
                    SceneValueKind.Literal,
                    Expression,
                    ResolvedType,
                    this.SemanticModel.GetConstantValue(Expression,this.CancellationToken).Value,
                    ReferencedSymbol:null,
                    Literal.Token.Text);

            // 4) 符号付きリテラル(-1.5f)。GetConstantValueでも取れるが、接尾辞を保存するためこちらで扱う。
            case PrefixUnaryExpressionSyntax { Operand:LiteralExpressionSyntax SignedLiteral } Prefix
                when Prefix.IsKind(SyntaxKind.UnaryMinusExpression)||Prefix.IsKind(SyntaxKind.UnaryPlusExpression):
                return this.Create(
                    SceneValueKind.Literal,
                    Expression,
                    ResolvedType,
                    this.SemanticModel.GetConstantValue(Expression,this.CancellationToken).Value,
                    ReferencedSymbol:null,
                    Prefix.OperatorToken.Text+SignedLiteral.Token.Text);

            case ObjectCreationExpressionSyntax:
            case ImplicitObjectCreationExpressionSyntax:
                return this.ReadObjectCreation(Expression,ResolvedType,Depth);

            case CollectionExpressionSyntax:
            case ArrayCreationExpressionSyntax:
            case ImplicitArrayCreationExpressionSyntax:
                return this.ReadCollection(Expression,ResolvedType,Depth);

            case InvocationExpressionSyntax Invocation:
                return this.ReadInvocation(Invocation,ResolvedType,Depth);

            case MemberAccessExpressionSyntax:
            case IdentifierNameSyntax:
                return this.ReadMemberReference(Expression,ResolvedType,Depth);
        }

        // 5) 上記の形に当てはまらないが、コンパイル時定数として畳み込める式
        //    (12f*MathF.PI/180f、$"{Const} - Stage"、nameof(X)、const参照)。
        Optional<object?>Constant=this.SemanticModel.GetConstantValue(Expression,this.CancellationToken);

        if(Constant.HasValue){
            return this.Create(
                SceneValueKind.ConstantExpression,
                Expression,
                ResolvedType,
                Constant.Value,
                ReferencedSymbol:null,
                SceneEmitExpressionWriter.WriteConstant(Constant.Value));
        }

        // 6) whitelist外。型が未解決なら入力途中なので黙る(R2)。
        if(SceneTypePredicates.IsUnresolved(ResolvedType)){
            return SceneValue.Unsupported(Expression.ToString(),SceneSourceLocation.FromNode(Expression));
        }

        this.ReportUnsupported(Expression,Expression.Kind().ToString());
        return SceneValue.Unsupported(Expression.ToString(),SceneSourceLocation.FromNode(Expression));
    }

    /// <summary>
    /// <c>new Vector3(0, 0, 0)</c> / <c>new Transform { Position = … }</c> / <c>new(…)</c>。
    /// 値型に限定する — 参照型の生成を許すとVSIXが値を静的に評価できず、Componentから参照型を排除する
    /// 方針とも衝突するため。参照型が必要な場合は<c>[SceneFactory]</c>で明示的にオプトインする。
    /// </summary>
    private SceneValue ReadObjectCreation(ExpressionSyntax Expression,ITypeSymbol?ResolvedType,int Depth){
        if(SceneTypePredicates.IsUnresolved(ResolvedType)){
            return SceneValue.Unsupported(Expression.ToString(),SceneSourceLocation.FromNode(Expression));
        }

        if(ResolvedType!.TypeKind!=TypeKind.Struct&&ResolvedType.TypeKind!=TypeKind.Enum){
            this.ReportUnsupported(Expression,"reference type construction");
            return SceneValue.Unsupported(Expression.ToString(),SceneSourceLocation.FromNode(Expression));
        }

        BaseObjectCreationExpressionSyntax Creation=(BaseObjectCreationExpressionSyntax)Expression;
        (ImmutableArray<SceneValue>Arguments,List<string>ArgumentTexts)=this.ReadArguments(Creation.ArgumentList,Depth);
        (ImmutableArray<SceneComponentMember>Members,List<string>MemberTexts)=this.ReadInitializer(Creation.Initializer,Depth);

        var TypeText=SceneEmitExpressionWriter.WriteTypeReference(ResolvedType);
        var EmitText=SceneEmitExpressionWriter.WriteObjectCreation(TypeText,ArgumentTexts,MemberTexts);

        return new SceneValue(
            SceneValueKind.ObjectCreation,
            ResolvedType,
            ConstantValue:null,
            ReferencedSymbol:null,
            Members,
            Arguments,
            ImmutableArray<SceneValue>.Empty,
            EmitText,
            Expression.ToString(),
            SceneSourceLocation.FromNode(Expression));
    }

    private SceneValue ReadCollection(ExpressionSyntax Expression,ITypeSymbol?ResolvedType,int Depth){
        // `new bool[3]` のような「サイズ指定だけで初期化子が無い配列生成」は要素を静的に確定できない。
        // 黙って空配列として扱うと、書いた内容と生成される内容が食い違うため明示的に弾く。
        if(Expression is ArrayCreationExpressionSyntax { Initializer:null }){
            this.ReportUnsupported(Expression,"array creation without an initializer");
            return SceneValue.Unsupported(Expression.ToString(),SceneSourceLocation.FromNode(Expression));
        }

        // コレクション式 [ … ] はターゲット型付けなので Type が null になる。ConvertedType が唯一の手がかり。
        if(ResolvedType is not IArrayTypeSymbol ArrayType){
            if(SceneTypePredicates.IsUnresolved(ResolvedType)){
                return SceneValue.Unsupported(Expression.ToString(),SceneSourceLocation.FromNode(Expression));
            }

            this.ReportUnsupported(Expression,"non-array collection");
            return SceneValue.Unsupported(Expression.ToString(),SceneSourceLocation.FromNode(Expression));
        }

        ImmutableArray<SceneValue>.Builder Elements=ImmutableArray.CreateBuilder<SceneValue>();
        List<string>ElementTexts=[];

        foreach(ExpressionSyntax Element in EnumerateCollectionElements(Expression)){
            SceneValue Value=this.Read(Element,Depth+1);
            Elements.Add(Value);
            ElementTexts.Add(Value.EmitExpressionText);
        }

        var EmitText=SceneEmitExpressionWriter.WriteArrayCreation(
            SceneEmitExpressionWriter.WriteTypeReference(ArrayType.ElementType),
            ElementTexts);

        return new SceneValue(
            SceneValueKind.Collection,
            ArrayType,
            ConstantValue:null,
            ReferencedSymbol:null,
            ImmutableArray<SceneComponentMember>.Empty,
            ImmutableArray<SceneValue>.Empty,
            Elements.ToImmutable(),
            EmitText,
            Expression.ToString(),
            SceneSourceLocation.FromNode(Expression));
    }

    /// <summary><c>[SceneFactory]</c>を付けた静的メソッドの呼び出しだけを許可する。</summary>
    private SceneValue ReadInvocation(InvocationExpressionSyntax Invocation,ITypeSymbol?ResolvedType,int Depth){
        // 呼び出しの形をしていてもコンパイル時定数になるもの(nameof(...)、const引数の畳み込み)が先。
        Optional<object?>Constant=this.SemanticModel.GetConstantValue(Invocation,this.CancellationToken);

        if(Constant.HasValue){
            return this.Create(
                SceneValueKind.ConstantExpression,
                Invocation,
                ResolvedType,
                Constant.Value,
                ReferencedSymbol:null,
                SceneEmitExpressionWriter.WriteConstant(Constant.Value));
        }

        if(this.SemanticModel.GetSymbolInfo(Invocation,this.CancellationToken).Symbol is not IMethodSymbol Method
            ||!SceneTypePredicates.HasAttribute(Method,SceneDslWellKnownNames.SceneFactoryAttributeMetadataName)){
            if(SceneTypePredicates.IsUnresolved(ResolvedType)){
                return SceneValue.Unsupported(Invocation.ToString(),SceneSourceLocation.FromNode(Invocation));
            }

            this.Report(SceneDslDiagnostics.MethodInvocationRequiresSceneFactory,Invocation,Invocation.ToString());
            return SceneValue.Unsupported(Invocation.ToString(),SceneSourceLocation.FromNode(Invocation));
        }

        if(!Method.IsStatic){
            this.Report(SceneDslDiagnostics.InvalidSceneFactoryTarget,Invocation,Method.Name);
            return SceneValue.Unsupported(Invocation.ToString(),SceneSourceLocation.FromNode(Invocation));
        }

        (ImmutableArray<SceneValue>Arguments,List<string>ArgumentTexts)=this.ReadArguments(Invocation.ArgumentList,Depth);
        var EmitText=SceneEmitExpressionWriter.WriteInvocation(SceneEmitExpressionWriter.WriteSymbolReference(Method),ArgumentTexts);

        return new SceneValue(
            SceneValueKind.FactoryCall,
            ResolvedType,
            ConstantValue:null,
            ReferencedSymbol:Method,
            ImmutableArray<SceneComponentMember>.Empty,
            Arguments,
            ImmutableArray<SceneValue>.Empty,
            EmitText,
            Invocation.ToString(),
            SceneSourceLocation.FromNode(Invocation));
    }

    /// <summary>
    /// enumメンバー参照(<c>PrimitiveType.Cube</c>)と、<c>[SceneAssets]</c>クラスの静的メンバー参照
    /// (<c>Assets.Floor</c>)だけを許可する。
    ///
    /// <para>
    /// enumとAsset参照は<b>定数畳み込みより先に</b>判定する。<c>PrimitiveType.Cube</c>は
    /// <c>GetConstantValue</c>で<c>1</c>になってしまい、生成コードの可読性とAssetの参照関係が失われるため。
    /// </para>
    /// </summary>
    private SceneValue ReadMemberReference(ExpressionSyntax Expression,ITypeSymbol?ResolvedType,int Depth){
        SymbolInfo Symbol=this.SemanticModel.GetSymbolInfo(Expression,this.CancellationToken);

        if(Symbol.Symbol is IFieldSymbol { IsConst:true } EnumField
            &&EnumField.ContainingType is { TypeKind:TypeKind.Enum }){
            return this.Create(
                SceneValueKind.EnumMember,
                Expression,
                ResolvedType,
                EnumField.ConstantValue,
                EnumField,
                SceneEmitExpressionWriter.WriteSymbolReference(EnumField));
        }

        if(Symbol.Symbol is { IsStatic:true } StaticMember
            &&StaticMember.ContainingType is { } Container
            &&SceneTypePredicates.HasAttribute(Container,SceneDslWellKnownNames.SceneAssetsAttributeMetadataName)
            &&IsReadOnlyMember(StaticMember)){
            // [SceneAssets] の const string は、Source Generator ではシンボル参照のまま
            // 出力しつつ、VSIX では実ファイルを解決する必要がある。参照種別を失わずに
            // ConstantValue も保持し、両者が同じ SceneModel を共有できるようにする。
            Optional<object?>StaticMemberConstant=this.SemanticModel.GetConstantValue(Expression,this.CancellationToken);
            return this.Create(
                SceneValueKind.StaticMemberRef,
                Expression,
                ResolvedType,
                StaticMemberConstant.HasValue?StaticMemberConstant.Value:null,
                StaticMember,
                SceneEmitExpressionWriter.WriteSymbolReference(StaticMember));
        }

        // const参照や static readonly の定数畳み込みはここで拾う。
        Optional<object?>Constant=this.SemanticModel.GetConstantValue(Expression,this.CancellationToken);

        if(Constant.HasValue){
            return this.Create(
                SceneValueKind.ConstantExpression,
                Expression,
                ResolvedType,
                Constant.Value,
                ReferencedSymbol:null,
                SceneEmitExpressionWriter.WriteConstant(Constant.Value));
        }

        if(SceneTypePredicates.IsUnresolved(ResolvedType)||Symbol.Symbol is null){
            return SceneValue.Unsupported(Expression.ToString(),SceneSourceLocation.FromNode(Expression));
        }

        this.Report(SceneDslDiagnostics.MemberAccessRequiresSceneAssets,Expression,Expression.ToString());
        return SceneValue.Unsupported(Expression.ToString(),SceneSourceLocation.FromNode(Expression));
    }

    private (ImmutableArray<SceneValue>Values,List<string>Texts)ReadArguments(ArgumentListSyntax?ArgumentList,int Depth){
        ImmutableArray<SceneValue>.Builder Values=ImmutableArray.CreateBuilder<SceneValue>();
        List<string>Texts=[];

        if(ArgumentList is null){
            return (Values.ToImmutable(),Texts);
        }

        foreach(ArgumentSyntax Argument in ArgumentList.Arguments){
            SceneValue Value=this.Read(Argument.Expression,Depth+1);
            Values.Add(Value);
            Texts.Add(SceneEmitExpressionWriter.WriteArgument(Argument.NameColon?.Name.Identifier.Text,Value.EmitExpressionText));
        }

        return (Values.ToImmutable(),Texts);
    }

    /// <summary>オブジェクト初期化子の<c>Member = value</c>だけを受理する。コレクション初期化子はSCENE110。</summary>
    private (ImmutableArray<SceneComponentMember>Members,List<string>Texts)ReadInitializer(InitializerExpressionSyntax?Initializer,int Depth){
        ImmutableArray<SceneComponentMember>.Builder Members=ImmutableArray.CreateBuilder<SceneComponentMember>();
        List<string>Texts=[];

        if(Initializer is null){
            return (Members.ToImmutable(),Texts);
        }

        foreach(ExpressionSyntax Element in Initializer.Expressions){
            if(Element is not AssignmentExpressionSyntax { Left:IdentifierNameSyntax MemberName } Assignment){
                this.ReportUnsupported(Element,Element.Kind().ToString());
                continue;
            }

            SceneValue Value=this.Read(Assignment.Right,Depth+1);
            Members.Add(new SceneComponentMember(
                MemberName.Identifier.Text,
                this.SemanticModel.GetSymbolInfo(MemberName,this.CancellationToken).Symbol,
                Value,
                SceneSourceLocation.FromNode(Assignment)));
            Texts.Add(SceneEmitExpressionWriter.WriteMemberAssignment(MemberName.Identifier.Text,Value.EmitExpressionText));
        }

        return (Members.ToImmutable(),Texts);
    }

    /// <summary><c>[ … ]</c> / <c>new[] { … }</c> / <c>new T[] { … }</c>の3形から要素式を取り出す。</summary>
    internal static IEnumerable<ExpressionSyntax>EnumerateCollectionElements(ExpressionSyntax Expression){
        switch(Expression){
            case CollectionExpressionSyntax Collection:
                foreach(CollectionElementSyntax Element in Collection.Elements){
                    // スプレッド要素(..source)は静的に確定できないため、式として渡してSCENE110に落とす。
                    if(Element is ExpressionElementSyntax ExpressionElement){
                        yield return ExpressionElement.Expression;
                    } else if(Element is SpreadElementSyntax Spread){
                        yield return Spread.Expression;
                    }
                }

                break;
            case ArrayCreationExpressionSyntax { Initializer:{ } ArrayInitializer }:
                foreach(ExpressionSyntax Element in ArrayInitializer.Expressions){
                    yield return Element;
                }

                break;
            case ImplicitArrayCreationExpressionSyntax ImplicitArray:
                foreach(ExpressionSyntax Element in ImplicitArray.Initializer.Expressions){
                    yield return Element;
                }

                break;
        }
    }

    /// <summary>この式が<see cref="EnumerateCollectionElements"/>で要素を取り出せる形かどうか。</summary>
    internal static bool IsCollectionLike(ExpressionSyntax Expression){
        return Expression is CollectionExpressionSyntax
            or ArrayCreationExpressionSyntax
            or ImplicitArrayCreationExpressionSyntax;
    }

    private static bool IsReadOnlyMember(ISymbol Symbol){
        return Symbol switch {
            IFieldSymbol Field=>Field.IsReadOnly||Field.IsConst,
            IPropertySymbol Property=>Property.SetMethod is null&&Property.GetMethod is not null,
            _=>false,
        };
    }

    private SceneValue Create(
        SceneValueKind Kind,
        ExpressionSyntax Expression,
        ITypeSymbol?Type,
        object?ConstantValue,
        ISymbol?ReferencedSymbol,
        string EmitExpressionText){
        return new SceneValue(
            Kind,
            Type,
            ConstantValue,
            ReferencedSymbol,
            ImmutableArray<SceneComponentMember>.Empty,
            ImmutableArray<SceneValue>.Empty,
            ImmutableArray<SceneValue>.Empty,
            EmitExpressionText,
            Expression.ToString(),
            SceneSourceLocation.FromNode(Expression));
    }

    private void ReportUnsupported(SyntaxNode Node,string KindDescription){
        this.Report(SceneDslDiagnostics.UnsupportedExpression,Node,Node.ToString(),KindDescription);
    }

    private void Report(DiagnosticDescriptor Descriptor,SyntaxNode Node,params object?[]Arguments){
        this.Diagnostics.Add(Diagnostic.Create(Descriptor,Node.GetLocation(),Arguments));
    }
}
