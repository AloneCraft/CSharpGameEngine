using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.SceneAnalysis;

/// <summary>
/// <c>IScene</c>を実装するクラスを<see cref="SceneModel"/>へ変換する<b>唯一の解析実装</b>(ADR-0040)。
/// Source GeneratorとVSIXはこのソースを共有し、それぞれのRoslynホスト上で同じコードを走らせる。
/// <c>Root</c>プロパティは<b>実行せず構文と意味解析だけを見る</b>。
///
/// <para>
/// <b>例外を投げてはならない。</b> VSIXがユーザーの入力途中にこれを呼ぶため、不完全な構文・未解決の型・
/// 欠落したトークンはすべて「診断なしで解析を打ち切る」形で扱う(ADR-0040 F-6)。
/// </para>
/// </summary>
internal static class SceneDeclarationAnalyzer {
    /// <summary>
    /// Scene宣言かもしれない構文ノードの安価な事前判定。<c>IScene</c>には属性が無いため
    /// <c>ForAttributeWithMetadataName</c>が使えず、<c>ISystem</c>と同じくbase listを持つクラス宣言を
    /// 拾ってから意味解析で確認するしかない。
    ///
    /// <para>
    /// SGとVSIXの両方がこの述語を使う。片方だけが別の絞り込みをすると「SGは見つけるがエディターは
    /// 見つけない」Sceneが生まれるため、共有ソース側に置いている。
    /// </para>
    /// </summary>
    internal static bool IsSceneCandidate(SyntaxNode Node){
        return Node is ClassDeclarationSyntax { BaseList.Types.Count:>0 };
    }

    internal static SceneModel Analyze(INamedTypeSymbol ClassSymbol,Compilation Compilation,CancellationToken CancellationToken){
        var Diagnostics=ImmutableArray.CreateBuilder<Diagnostic>();
        var ClassFullyQualifiedName=ComputeClassFullyQualifiedName(ClassSymbol);
        var Namespace=ClassSymbol.ContainingNamespace.IsGlobalNamespace?null:ClassSymbol.ContainingNamespace.ToDisplayString();
        var ClassDeclaration=GetFirstDeclaration(ClassSymbol,CancellationToken);
        var FilePath=ClassDeclaration?.SyntaxTree.FilePath??string.Empty;
        var DeclarationPosition=ClassDeclaration?.SpanStart??0;

        SceneModel Failed()=>SceneModel.Failed(
            Namespace,
            ClassSymbol.Name,
            ClassFullyQualifiedName,
            ClassSymbol,
            Diagnostics.ToImmutable(),
            FilePath,
            DeclarationPosition);

        if(ClassSymbol.ContainingType is not null){
            Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.SceneMustNotBeNested,ClassSymbol.Locations.FirstOrDefault(),ClassSymbol.Name));
            return Failed();
        }

        var IsPartial=ClassDeclaration is not null&&ClassDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword);

        if(ClassSymbol.TypeKind!=TypeKind.Class||ClassSymbol.IsGenericType||!IsPartial){
            Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.SceneMustBeNonGenericPartialClass,ClassSymbol.Locations.FirstOrDefault(),ClassSymbol.Name));
            return Failed();
        }

        // SGが生成partialへ `private X() { }` を出して外部からの `new X()` を封じる(Scene DSLは実行されない)。
        // ユーザーが自分でコンストラクターを書いていると衝突するため、ここで弾く。
        foreach(var Constructor in ClassSymbol.InstanceConstructors){
            // VisualStudioWorkspace の Compilation には、すでにSGが出力した .Scene.g.cs も含まれる。
            // その生成コンストラクターまでSCENE102扱いすると、ライブ解析だけが失敗してRoot=nullになる。
            // Roslyn標準の generated-code 印が付いた宣言はSG自身の出力なので除外し、ユーザー宣言は従来どおり弾く。
            if(!Constructor.IsImplicitlyDeclared
                &&Constructor.DeclaringSyntaxReferences.Length>0
                &&!Constructor.DeclaringSyntaxReferences.All(Reference=>IsGeneratedSyntaxTree(Reference.SyntaxTree,CancellationToken))){
                Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.SceneMustNotDeclareConstructor,Constructor.Locations.FirstOrDefault(),ClassSymbol.Name));
                return Failed();
            }
        }

        var RootProperty=FindRootProperty(ClassSymbol,Compilation);

        if(RootProperty is null){
            Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.RootPropertyMustBeExpressionBodied,ClassSymbol.Locations.FirstOrDefault(),ClassSymbol.Name));
            return Failed();
        }

        var RootExpression=GetRootExpression(RootProperty,CancellationToken);

        if(RootExpression is null){
            Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.RootPropertyMustBeExpressionBodied,RootProperty.Locations.FirstOrDefault(),ClassSymbol.Name));
            return Failed();
        }

        var SemanticModel=Compilation.GetSemanticModel(RootExpression.SyntaxTree);
        SceneValueReader Reader=new(SemanticModel,Diagnostics,CancellationToken);
        var Root=ReadNode(RootExpression,NodePath.Root,Depth:0,SemanticModel,Reader,Diagnostics,ClassSymbol.Name,CancellationToken);

        if(Root is null){
            return Failed();
        }

        (var IsStartupScene,var StartupOrder)=ReadStartupScene(ClassSymbol);

        return new SceneModel(
            Namespace,
            ClassSymbol.Name,
            ClassFullyQualifiedName,
            ClassSymbol,
            Root,
            IsStartupScene,
            StartupOrder,
            Diagnostics.ToImmutable(),
            FilePath,
            DeclarationPosition);
    }

    /// <summary><c>[StartupScene]</c>の有無とOrderを読む。<c>IScene</c>実装かどうかの検査(SCENE117)は呼び出し側の責務。</summary>
    internal static (bool IsStartupScene,int Order)ReadStartupScene(INamedTypeSymbol ClassSymbol){
        foreach(var Attribute in ClassSymbol.GetAttributes()){
            if(Attribute.AttributeClass is not { } AttributeClass
                ||AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)!= $"global::{SceneDslWellKnownNames.StartupSceneAttributeMetadataName}"){
                continue;
            }

            foreach(var Named in Attribute.NamedArguments){
                if(Named.Key=="Order"&&Named.Value.Value is int Order){
                    return (true,Order);
                }
            }

            return (true,0);
        }

        return (false,0);
    }

    internal static string ComputeClassFullyQualifiedName(INamedTypeSymbol ClassSymbol){
        return ClassSymbol.ContainingNamespace.IsGlobalNamespace
            ? ClassSymbol.Name
            : $"{ClassSymbol.ContainingNamespace.ToDisplayString()}.{ClassSymbol.Name}";
    }

    private static TypeDeclarationSyntax?GetFirstDeclaration(INamedTypeSymbol ClassSymbol,CancellationToken CancellationToken){
        TypeDeclarationSyntax?FirstDeclaration=null;

        foreach(var Reference in ClassSymbol.DeclaringSyntaxReferences){
            if(Reference.GetSyntax(CancellationToken) is TypeDeclarationSyntax Declaration){
                FirstDeclaration??=Declaration;

                // 生成partialが先頭に並ぶこともある。コマンドが右クリック元ファイルと照合できるよう、
                // SceneModel.FilePath/DeclarationPositionはユーザー宣言を優先する。
                if(!IsGeneratedSyntaxTree(Reference.SyntaxTree,CancellationToken)){
                    return Declaration;
                }
            }
        }

        return FirstDeclaration;
    }

    private static bool IsGeneratedSyntaxTree(SyntaxTree SyntaxTree,CancellationToken CancellationToken){
        var FilePath=SyntaxTree.FilePath??string.Empty;
        if(FilePath.EndsWith(".g.cs",StringComparison.OrdinalIgnoreCase)
            ||FilePath.EndsWith(".generated.cs",StringComparison.OrdinalIgnoreCase)){
            return true;
        }

        var Text=SyntaxTree.GetText(CancellationToken);
        var PrefixLength=Math.Min(Text.Length,256);
        var Prefix=Text.ToString(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(0,PrefixLength));
        return Prefix.IndexOf("<auto-generated",StringComparison.OrdinalIgnoreCase)>=0;
    }

    /// <summary>明示的インターフェース実装(<c>Node IScene.Root =&gt; …</c>)も拾えるよう、まず実装解決を試す。</summary>
    private static IPropertySymbol?FindRootProperty(INamedTypeSymbol ClassSymbol,Compilation Compilation){
        var SceneInterface=Compilation.GetTypeByMetadataName(SceneDslWellKnownNames.SceneInterfaceMetadataName);

        if(SceneInterface is not null){
            foreach(var InterfaceMember in SceneInterface.GetMembers(SceneDslWellKnownNames.RootPropertyName)){
                if(ClassSymbol.FindImplementationForInterfaceMember(InterfaceMember) is IPropertySymbol Implementation){
                    return Implementation;
                }
            }
        }

        foreach(var Member in ClassSymbol.GetMembers(SceneDslWellKnownNames.RootPropertyName)){
            if(Member is IPropertySymbol { IsStatic:false,GetMethod:not null } Property
                &&SceneTypePredicates.ToFullyQualifiedName(Property.Type)==SceneDslWellKnownNames.NodeFullyQualifiedName){
                return Property;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>=&gt; expr</c> / <c>{ get =&gt; expr; }</c> / <c>{ get { return expr; } }</c>の3形だけを受理する。
    /// 複数文のgetter・setterの存在・<c>ref</c>返しはSCENE101。
    /// </summary>
    private static ExpressionSyntax?GetRootExpression(IPropertySymbol RootProperty,CancellationToken CancellationToken){
        foreach(var Reference in RootProperty.DeclaringSyntaxReferences){
            if(Reference.GetSyntax(CancellationToken) is not PropertyDeclarationSyntax Declaration){
                continue;
            }

            if(Declaration.ExpressionBody is { Expression:{ } ArrowExpression }){
                return ArrowExpression;
            }

            if(Declaration.AccessorList is not { } AccessorList){
                continue;
            }

            foreach(var Accessor in AccessorList.Accessors){
                if(!Accessor.IsKind(SyntaxKind.GetAccessorDeclaration)){
                    continue;
                }

                if(Accessor.ExpressionBody is { Expression:{ } AccessorExpression }){
                    return AccessorExpression;
                }

                // リストパターン([… ])はSystem.Indexを要求するがnetstandard2.0には無いため使わない。
                if(Accessor.Body is { } Body
                    &&Body.Statements.Count==1
                    &&Body.Statements[0] is ReturnStatementSyntax { Expression:{ } ReturnExpression }){
                    return ReturnExpression;
                }
            }
        }

        return null;
    }

    private static SceneNode?ReadNode(
        ExpressionSyntax Expression,
        NodePath Path,
        int Depth,
        SemanticModel SemanticModel,
        SceneValueReader Reader,
        ImmutableArray<Diagnostic>.Builder Diagnostics,
        string SceneClassName,
        CancellationToken CancellationToken){
        CancellationToken.ThrowIfCancellationRequested();

        if(Depth>SceneDslWellKnownNames.MaxRecursionDepth){
            Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.UnsupportedExpression,Expression.GetLocation(),Expression.ToString(),"nesting depth limit"));
            return null;
        }

        if(Expression is not BaseObjectCreationExpressionSyntax Creation){
            ReportNodeShape(Expression,Path,Diagnostics,SceneClassName);
            return null;
        }

        var Type=SemanticModel.GetTypeInfo(Expression,CancellationToken).Type;

        if(SceneTypePredicates.IsUnresolved(Type)){
            return null;
        }

        if(SceneTypePredicates.ToFullyQualifiedName(Type!)!=SceneDslWellKnownNames.NodeFullyQualifiedName){
            ReportNodeShape(Expression,Path,Diagnostics,SceneClassName);
            return null;
        }

        string?Name=null;
        SceneSourceLocation?NameValueLocation=null;
        SceneSourceLocation?ComponentsListLocation=null;
        SceneSourceLocation?ChildrenListLocation=null;
        var Components=ImmutableArray<SceneComponent>.Empty;
        var Children=ImmutableArray<SceneNode>.Empty;

        foreach(var Element in Creation.Initializer?.Expressions??default){
            if(Element is not AssignmentExpressionSyntax { Left:IdentifierNameSyntax MemberName } Assignment){
                Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.UnknownNodeMember,Element.GetLocation(),Element.ToString()));
                continue;
            }

            switch(MemberName.Identifier.Text){
                case SceneDslWellKnownNames.NameMemberName:
                    Name=ReadName(Assignment.Right,SemanticModel,Diagnostics,CancellationToken);
                    NameValueLocation=SceneSourceLocation.FromNode(Assignment.Right);
                    break;
                case SceneDslWellKnownNames.ComponentsMemberName:
                    if(!SceneValueReader.IsCollectionLike(Assignment.Right)){
                        Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.MemberMustBeCollectionExpression,Assignment.Right.GetLocation(),SceneDslWellKnownNames.ComponentsMemberName));
                        break;
                    }

                    ComponentsListLocation=SceneSourceLocation.FromNode(Assignment.Right);
                    Components=ReadComponents(Assignment.Right,Path,Depth,SemanticModel,Reader,Diagnostics,CancellationToken);
                    break;
                case SceneDslWellKnownNames.ChildrenMemberName:
                    if(!SceneValueReader.IsCollectionLike(Assignment.Right)){
                        Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.MemberMustBeCollectionExpression,Assignment.Right.GetLocation(),SceneDslWellKnownNames.ChildrenMemberName));
                        break;
                    }

                    ChildrenListLocation=SceneSourceLocation.FromNode(Assignment.Right);
                    Children=ReadChildren(Assignment.Right,Path,Depth,SemanticModel,Reader,Diagnostics,SceneClassName,CancellationToken);
                    break;
                default:
                    Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.UnknownNodeMember,MemberName.GetLocation(),MemberName.Identifier.Text));
                    break;
            }
        }

        return new SceneNode(
            Path,
            Name,
            Components,
            Children,
            SceneSourceLocation.FromNode(Expression),
            Creation.Initializer is { } Initializer?SceneSourceLocation.FromNode(Initializer):null,
            NameValueLocation,
            ComponentsListLocation,
            ChildrenListLocation);
    }

    private static void ReportNodeShape(ExpressionSyntax Expression,NodePath Path,ImmutableArray<Diagnostic>.Builder Diagnostics,string SceneClassName){
        Diagnostics.Add(Path.IsRoot
            ? Diagnostic.Create(SceneDslDiagnostics.RootMustBeNodeCreation,Expression.GetLocation(),SceneClassName)
            : Diagnostic.Create(SceneDslDiagnostics.ChildMustBeNodeCreation,Expression.GetLocation(),Expression.ToString()));
    }

    private static string?ReadName(
        ExpressionSyntax Expression,
        SemanticModel SemanticModel,
        ImmutableArray<Diagnostic>.Builder Diagnostics,
        CancellationToken CancellationToken){
        var Constant=SemanticModel.GetConstantValue(Expression,CancellationToken);

        if(Constant.HasValue){
            return Constant.Value as string;
        }

        // 型が未解決なら入力途中なので黙る(R2)。
        if(SceneTypePredicates.IsUnresolved(SemanticModel.GetTypeInfo(Expression,CancellationToken).Type)){
            return null;
        }

        Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.NodeNameMustBeConstantString,Expression.GetLocation(),Expression.ToString()));
        return null;
    }

    /// <summary>
    /// <c>Components = [ new Transform { … }, new PrimitiveMesh { … } ]</c>の各要素を読む。
    ///
    /// <para>
    /// <b><c>GetTypeInfo(...).Type</c>を使う。</b><c>Node.Components</c>が<c>object[]</c>であるため
    /// <c>ConvertedType</c>は必ず<c>object</c>になり、全Componentが<c>object</c>に見えて静かに壊れる。
    /// また<c>object[]</c>は<c>null</c>・文字列・<c>Node</c>の混入を型システムが止められないので、
    /// ここで明示的に弾く必要がある。
    /// </para>
    /// </summary>
    private static ImmutableArray<SceneComponent>ReadComponents(
        ExpressionSyntax CollectionExpression,
        NodePath Path,
        int Depth,
        SemanticModel SemanticModel,
        SceneValueReader Reader,
        ImmutableArray<Diagnostic>.Builder Diagnostics,
        CancellationToken CancellationToken){
        var Components=ImmutableArray.CreateBuilder<SceneComponent>();
        HashSet<string>SeenTypes=new(StringComparer.Ordinal);

        foreach(var Element in SceneValueReader.EnumerateCollectionElements(CollectionExpression)){
            CancellationToken.ThrowIfCancellationRequested();

            if(Element is not BaseObjectCreationExpressionSyntax){
                Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.ComponentMustBeStructCreation,Element.GetLocation(),Element.ToString()));
                continue;
            }

            var Type=SemanticModel.GetTypeInfo(Element,CancellationToken).Type;

            if(SceneTypePredicates.IsUnresolved(Type)){
                continue;
            }

            if(!SceneTypePredicates.IsNonNullableStruct(Type!)){
                Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.ComponentMustBeStructCreation,Element.GetLocation(),Element.ToString()));
                continue;
            }

            var TypeFullyQualifiedName=SceneTypePredicates.ToFullyQualifiedName(Type!);

            if(SceneTypePredicates.IsReservedComponent(TypeFullyQualifiedName)){
                Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.ReservedComponentType,Element.GetLocation(),TypeFullyQualifiedName));
                continue;
            }

            // SCENE119: IRuntimeComponentを実装する型はエンジンが実行時に埋めるためのもので、
            // 随伴先の作者Componentを宣言すればSGが自動付与する。明示すると重複するので弾く。
            if(Type is INamedTypeSymbol NamedType
                &&SceneTypePredicates.ImplementsRuntimeComponentInterface(NamedType,SemanticModel.Compilation)){
                var AuthoredName=SceneTypePredicates.TryGetAuthoredComponentType(
                                                                                 NamedType,SemanticModel.Compilation,out var Authored)
                                     ?SceneTypePredicates.ToFullyQualifiedName(Authored)
                                     :"(none)";
                Diagnostics.Add(Diagnostic.Create(
                    SceneDslDiagnostics.RuntimeComponentType,Element.GetLocation(),TypeFullyQualifiedName,AuthoredName));
                continue;
            }

            if(!SeenTypes.Add(TypeFullyQualifiedName)){
                Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.DuplicateComponentType,Element.GetLocation(),Path.ToDisplayString(),TypeFullyQualifiedName));
                continue;
            }

            var Value=Reader.Read(Element,Depth+1);

            if(Value.Kind!=SceneValueKind.ObjectCreation){
                continue;
            }

            foreach(var Member in Value.Members){
                var Contract=LightValueContract.Find(Member.Value.Type);
                if(Contract is not null&&Contract.TryRead(Member.Value,out var Values,out _)&&!Contract.Validate(Values)){
                    Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.UnsupportedExpression,Element.GetLocation(),Member.MemberName,"invalid physical light value "+Contract.Name));
                }
            }
            Components.Add(new SceneComponent(
                Type as INamedTypeSymbol,
                TypeFullyQualifiedName,
                Value.Arguments,
                Value.Members,
                Value.EmitExpressionText,
                Value.Location));
        }

        var LightCount=Components.Count(Component=>LightComponentContract.IsLight(Component.TypeFullyQualifiedName));
        var HasShadow=Components.Any(Component=>Component.TypeFullyQualifiedName==LightComponentContract.ShadowType);
        if(LightCount>1||HasShadow&&LightCount!=1||LightCount>0&&!Components.Any(Component=>Component.TypeFullyQualifiedName=="global::CSharpGameEngine.Entities.Transform")){
            Diagnostics.Add(Diagnostic.Create(SceneDslDiagnostics.UnsupportedExpression,CollectionExpression.GetLocation(),Path.ToDisplayString(),"a light requires its Transform; exactly one light must own each LightShadow"));
        }
        return Components.ToImmutable();
    }

    private static ImmutableArray<SceneNode>ReadChildren(
        ExpressionSyntax CollectionExpression,
        NodePath Path,
        int Depth,
        SemanticModel SemanticModel,
        SceneValueReader Reader,
        ImmutableArray<Diagnostic>.Builder Diagnostics,
        string SceneClassName,
        CancellationToken CancellationToken){
        var Children=ImmutableArray.CreateBuilder<SceneNode>();
        var ChildIndex=0;

        foreach(var Element in SceneValueReader.EnumerateCollectionElements(CollectionExpression)){
            var Child=ReadNode(Element,Path.Append(ChildIndex),Depth+1,SemanticModel,Reader,Diagnostics,SceneClassName,CancellationToken);

            if(Child is not null){
                Children.Add(Child);
                ChildIndex++;
            }
        }

        return Children.ToImmutable();
    }
}
