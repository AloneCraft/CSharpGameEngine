using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>[StartupScene]</c>クラスの解析。<c>[SceneEntity]</c>を付けたプロパティ1つが1つの論理Entityを
/// 表し、戻り値の型構造(単一structかValueTupleか)からComponent集合を導出する
/// (<see cref="GameSystemAnalyzer"/>と同じ「構造で判別する」方針、ただし起点の属性1つだけは
/// <see cref="GameSystemOrderAnalyzer"/>と同様に「構造から導出できない情報を宣言的に受け取るため」の
/// 意図的な例外)。
/// </summary>
internal static class StartupSceneAnalyzer {
    internal const string StartupSceneAttributeFullyQualifiedName="CSharpGameEngine.Entities.StartupSceneAttribute";

    private const string SceneEntityAttributeFullyQualifiedName="global::CSharpGameEngine.Entities.SceneEntityAttribute";

    internal static StartupSceneClassResult Analyze(GeneratorAttributeSyntaxContext Ctx){
        var ClassSymbol=(INamedTypeSymbol)Ctx.TargetSymbol;
        var ClassDeclaration=(ClassDeclarationSyntax)Ctx.TargetNode;
        ImmutableArray<Diagnostic>.Builder Diagnostics=ImmutableArray.CreateBuilder<Diagnostic>();

        if(!ClassSymbol.IsStatic||ClassSymbol.TypeKind!=TypeKind.Class||!ClassDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword)){
            Diagnostics.Add(Diagnostic.Create(StartupSceneDiagnostics.MustBeStaticPartialClass,ClassSymbol.Locations.FirstOrDefault(),ClassSymbol.Name));
        }

        if(ClassSymbol.IsGenericType){
            Diagnostics.Add(Diagnostic.Create(StartupSceneDiagnostics.MustNotBeGeneric,ClassSymbol.Locations.FirstOrDefault(),ClassSymbol.Name));
        }

        var Order=0;
        AttributeData Attribute=Ctx.Attributes[0];

        foreach(KeyValuePair<string,TypedConstant>NamedArgument in Attribute.NamedArguments){
            if(NamedArgument.Key=="Order"&&NamedArgument.Value.Value is int Value){
                Order=Value;
            }
        }

        List<IPropertySymbol>SceneEntityProperties=[];

        foreach(ISymbol Member in ClassSymbol.GetMembers()){
            if(Member is not IPropertySymbol Property){
                continue;
            }

            var HasSceneEntityAttribute=Property.GetAttributes().Any(static A=>
                A.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)==SceneEntityAttributeFullyQualifiedName);

            if(HasSceneEntityAttribute){
                SceneEntityProperties.Add(Property);
            }
        }

        // 複数Scene Entityの生成順は決定論的なソース宣言順(要件)。GetMembers()の順序保証が弱いため
        // 宣言位置で明示的に整列する(GameSystemAnalyzerの同一struct内メソッド整列と同じ理由)。
        SceneEntityProperties.Sort(static(A,B)=>GetDeclarationPosition(A).CompareTo(GetDeclarationPosition(B)));

        List<StartupSceneEntityModel>Entities=[];

        foreach(IPropertySymbol Property in SceneEntityProperties){
            StartupSceneEntityModel?Model=AnalyzeSceneEntityProperty(Property,Diagnostics);

            if(Model is { } EntityValue){
                Entities.Add(EntityValue);
            }
        }

        Location TypeLocation=ClassSymbol.Locations.FirstOrDefault()??Location.None;

        return new StartupSceneClassResult(
            ClassSymbol.ContainingNamespace.IsGlobalNamespace?null:ClassSymbol.ContainingNamespace.ToDisplayString(),
            ClassSymbol.Name,
            Order,
            [.. Entities],
            Diagnostics.ToImmutable(),
            TypeLocation.SourceTree?.FilePath??string.Empty,
            TypeLocation.SourceSpan.Start);
    }

    private static int GetDeclarationPosition(ISymbol Symbol){
        Location location=Symbol.Locations.FirstOrDefault()??Location.None;
        return location.SourceSpan.Start;
    }

    private static StartupSceneEntityModel?AnalyzeSceneEntityProperty(IPropertySymbol Property,ImmutableArray<Diagnostic>.Builder Diagnostics){
        Location PropertyLocation=Property.Locations.FirstOrDefault()??Location.None;
        var IsValid=true;

        if(!Property.IsStatic){
            Diagnostics.Add(Diagnostic.Create(StartupSceneDiagnostics.SceneEntityPropertyMustBeStatic,PropertyLocation,Property.Name));
            IsValid=false;
        }

        if(Property.IsIndexer){
            Diagnostics.Add(Diagnostic.Create(StartupSceneDiagnostics.SceneEntityMustNotBeIndexer,PropertyLocation,Property.Name));
            IsValid=false;
        }

        if(Property.GetMethod is null){
            Diagnostics.Add(Diagnostic.Create(StartupSceneDiagnostics.SceneEntityMustHaveGetter,PropertyLocation,Property.Name));
            IsValid=false;
        }

        if(Property.ReturnsByRef||Property.ReturnsByRefReadonly){
            Diagnostics.Add(Diagnostic.Create(StartupSceneDiagnostics.SceneEntityMustNotReturnByRef,PropertyLocation,Property.Name));
            IsValid=false;
        }

        // 型自体が壊れている(indexer/非static等)場合、戻り値の解析まで進めても二次的な誤診断を
        // 積み増すだけなのでここで打ち切る(GameSystemAnalyzerの同種チェックと同じ方針)。
        if(!IsValid){
            return null;
        }

        List<StartupSceneComponentModel>Components=[];

        if(Property.Type is INamedTypeSymbol { IsTupleType:true } TupleType){
            HashSet<string>SeenComponentTypes=new(StringComparer.Ordinal);
            var ElementNumber=0;

            foreach(IFieldSymbol Element in TupleType.TupleElements){
                ElementNumber++;

                if(!Element.IsExplicitlyNamedTupleElement){
                    Diagnostics.Add(Diagnostic.Create(StartupSceneDiagnostics.TupleElementMustBeNamed,PropertyLocation,Property.Name,ElementNumber));
                    IsValid=false;
                    continue;
                }

                var ElementTypeFqn=Element.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                if(!GameSystemAnalyzer.IsNonNullableStruct(Element.Type)){
                    Diagnostics.Add(Diagnostic.Create(StartupSceneDiagnostics.TupleElementMustBeNonNullableStruct,PropertyLocation,Property.Name,Element.Name,ElementTypeFqn));
                    IsValid=false;
                    continue;
                }

                if(!SeenComponentTypes.Add(ElementTypeFqn)){
                    Diagnostics.Add(Diagnostic.Create(StartupSceneDiagnostics.DuplicateComponentTypeInTuple,PropertyLocation,Property.Name,ElementTypeFqn));
                    IsValid=false;
                    continue;
                }

                Components.Add(new StartupSceneComponentModel(ElementTypeFqn,Element.Name));
            }
        } else if(GameSystemAnalyzer.IsNonNullableStruct(Property.Type)){
            Components.Add(new StartupSceneComponentModel(Property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),null));
        } else {
            Diagnostics.Add(Diagnostic.Create(StartupSceneDiagnostics.SceneEntityReturnTypeInvalid,PropertyLocation,Property.Name,Property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            IsValid=false;
        }

        if(!IsValid||Components.Count==0){
            return null;
        }

        return new StartupSceneEntityModel(Property.Name,[.. Components],PropertyLocation.SourceSpan.Start);
    }
}
