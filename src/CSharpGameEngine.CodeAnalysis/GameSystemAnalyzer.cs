using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>ISystem</c>を実装するstructのシンボル解析。属性を一切使わない設計(ADR-0033)のため、
/// パラメータの役割はすべて構造的に判別する:
/// <list type="number">
/// <item>先頭・値渡し・型が<c>Entity</c> → Entityスロット(任意)</item>
/// <item>末尾・<c>ref State</c> → Stateスロット(<c>Update</c>では必須)</item>
/// <item>それ以外で<c>ref</c>の非nullable struct → Component</item>
/// <item>それ以外(<c>ref</c>でないもの。structは<c>in</c>、参照型は値渡し) → Context</item>
/// </list>
/// Componentは<c>ref</c>必須・Contextは<c>ref</c>禁止なので、ref-kindだけで曖昧性なく判別できる。
/// ランタイム型へは完全修飾名の文字列としてのみ参照し、CSharpGameEngine.CodeAnalysisから
/// CSharpGameEngine.Entitiesへのアセンブリ参照は発生させない(ADR-0001)。
/// </summary>
internal static class GameSystemAnalyzer {
    internal const string SystemInterfaceFullyQualifiedName="global::CSharpGameEngine.Entities.ISystem";

    /// <summary><c>Compilation.GetTypeByMetadataName</c>用(<c>global::</c>接頭辞を付けない)。</summary>
    internal const string SystemInterfaceMetadataName="CSharpGameEngine.Entities.ISystem";
    internal const string EntityFullyQualifiedName="global::CSharpGameEngine.Entities.Entity";
    internal const string StateFullyQualifiedName="global::CSharpGameEngine.Entities.State";

    private const string UpdateMethodName="Update";
    private const string SpawnMethodName="Spawn";

    internal static GameSystemResult Analyze(INamedTypeSymbol SystemSymbol){
        var Diagnostics=ImmutableArray.CreateBuilder<Diagnostic>();

        if(SystemSymbol.TypeKind!=TypeKind.Struct){
            Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SystemMustBeStruct,SystemSymbol.Locations.FirstOrDefault(),SystemSymbol.Name));
        }

        if(SystemSymbol.IsGenericType){
            Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SystemMustNotBeGeneric,SystemSymbol.Locations.FirstOrDefault(),SystemSymbol.Name));
        }

        if(SystemSymbol.ContainingType is not null){
            Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SystemMustNotBeNested,SystemSymbol.Locations.FirstOrDefault(),SystemSymbol.Name));
        }

        List<IMethodSymbol>Methods=[];

        foreach(var Member in SystemSymbol.GetMembers()){
            if(Member is IMethodSymbol { MethodKind:MethodKind.Ordinary } Method
                &&(Method.Name==UpdateMethodName||Method.Name==SpawnMethodName)){
                Methods.Add(Method);
            }
        }

        if(Methods.Count==0){
            Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SystemHasNoRecognizedMethod,SystemSymbol.Locations.FirstOrDefault(),SystemSymbol.Name));
        }

        // 同一struct内のオーバーロード順序は、実行順序(ADR-0033決定事項1: 記述順)に直結するため
        // ソース上の宣言位置で並べる。GetMembers()の順序はメタデータ順で保証が弱いので明示的に整列する。
        Methods.Sort(static(A,B)=>GetDeclarationPosition(A).CompareTo(GetDeclarationPosition(B)));

        List<GameSystemMethodModel>MethodModels=[];

        foreach(var Method in Methods){
            var Model=AnalyzeMethod(Method,Diagnostics);

            if(Model is { } Value){
                MethodModels.Add(Value);
            }
        }

        var TypeLocation=SystemSymbol.Locations.FirstOrDefault()??Location.None;

        return new GameSystemResult(
            SystemSymbol.ContainingNamespace.IsGlobalNamespace?null:SystemSymbol.ContainingNamespace.ToDisplayString(),
            SystemSymbol.Name,
            [.. MethodModels],
            Diagnostics.ToImmutable(),
            TypeLocation.SourceTree?.FilePath??string.Empty,
            TypeLocation.SourceSpan.Start);
    }

    /// <summary>
    /// <see cref="GameSystemResult.ClassFullyQualifiedName"/>と同じ規則(名前空間.型名、グローバル
    /// 名前空間ならglobal::無しの型名のみ)で完全修飾名を作る。<see cref="GameSystemOrderAnalyzer"/>が
    /// <c>[SystemOrder]</c>を直接付けられたSystemを、<see cref="GameSystemResult"/>側と同じキーで
    /// 引き当てるために必要(2箇所が別々の規則で名前を作るとキーがズレて対応付けが失敗する)。
    /// </summary>
    internal static string ComputeClassFullyQualifiedName(INamedTypeSymbol Symbol)=>
        Symbol.ContainingNamespace.IsGlobalNamespace
            ?Symbol.Name
            : $"{Symbol.ContainingNamespace.ToDisplayString()}.{Symbol.Name}";

    private static int GetDeclarationPosition(ISymbol Symbol){
        Location Location=Symbol.Locations.FirstOrDefault()??Location.None;
        return Location.SourceSpan.Start;
    }

    private static GameSystemMethodModel?AnalyzeMethod(IMethodSymbol Method,ImmutableArray<Diagnostic>.Builder Diagnostics){
        var IsSpawn=Method.Name==SpawnMethodName;
        var Role=IsSpawn?GameSystemMethodRole.SpawnOnly:GameSystemMethodRole.Dispatch;
        var IsValid=true;
        var MethodLocation=Method.Locations.FirstOrDefault()??Location.None;

        if(Method.IsStatic){
            Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.MethodMustBeInstance,MethodLocation,Method.Name));
            IsValid=false;
        }

        if(Method.DeclaredAccessibility==Accessibility.Private){
            Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.MethodMustNotBePrivate,MethodLocation,Method.Name));
            IsValid=false;
        }

        if(Method.IsGenericMethod){
            Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.MethodMustNotBeGeneric,MethodLocation,Method.Name));
            IsValid=false;
        }

        if(IsSpawn){
            if(!Method.ReturnsVoid){
                Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SpawnMethodMustReturnVoid,MethodLocation,Method.Name,Method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                IsValid=false;
            }
        } else if(Method.ReturnType.SpecialType!=SpecialType.System_Boolean){
            Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.UpdateMethodMustReturnBool,MethodLocation,Method.Name,Method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            IsValid=false;
        }

        var Parameters=Method.Parameters;
        var HasLeadingEntity=false;
        var ScanStart=0;
        var ScanEndExclusive=Parameters.Length;

        if(Parameters.Length>0){
            var First=Parameters[0];

            if(TypeFqn(First)==EntityFullyQualifiedName){
                if(IsSpawn){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SpawnMethodMustNotDeclareEntityOrState,MethodLocation,Method.Name));
                    IsValid=false;
                } else {
                    if(First.RefKind!=RefKind.None){
                        Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.LeadingEntityParameterMustBeByValueFirst,MethodLocation,Method.Name));
                        IsValid=false;
                    }

                    HasLeadingEntity=true;
                }

                ScanStart=1;
            }
        }

        var HasTrailingState=Parameters.Length>ScanStart
            &&Parameters[Parameters.Length-1].RefKind==RefKind.Ref
            &&TypeFqn(Parameters[Parameters.Length-1])==StateFullyQualifiedName;

        if(IsSpawn){
            if(HasTrailingState){
                Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SpawnMethodMustNotDeclareEntityOrState,MethodLocation,Method.Name));
                IsValid=false;
                ScanEndExclusive=Parameters.Length-1;
            }
        } else if(HasTrailingState){
            ScanEndExclusive=Parameters.Length-1;
        } else {
            Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.UpdateMethodMustEndWithRefState,MethodLocation,Method.Name));
            IsValid=false;
        }

        List<GameSystemComponentParameterModel>Components=[];
        List<GameSystemContextParameterModel>Contexts=[];
        List<GameSystemParameterSlot>CallOrder=[];
        HashSet<string>SeenComponentTypes=new(StringComparer.Ordinal);
        HashSet<string>SeenContextTypes=new(StringComparer.Ordinal);

        if(HasLeadingEntity){
            CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.Entity,0));
        }

        for(var I=ScanStart;I<ScanEndExclusive;I++){
            var Parameter=Parameters[I];
            var ParameterTypeFqn=TypeFqn(Parameter);

            if(ParameterTypeFqn==EntityFullyQualifiedName||ParameterTypeFqn==StateFullyQualifiedName){
                Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.EntityOrStateMustNotBeUsedAsComponent,Parameter.Locations.FirstOrDefault(),Parameter.Name,Method.Name,ParameterTypeFqn));
                IsValid=false;
                continue;
            }

            if(Parameter.RefKind==RefKind.Ref){
                if(!IsNonNullableStruct(Parameter.Type)){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ComponentTypeMustBeNonNullableStruct,Parameter.Locations.FirstOrDefault(),Parameter.Name,Method.Name,ParameterTypeFqn));
                    IsValid=false;
                    continue;
                }

                if(!SeenComponentTypes.Add(ParameterTypeFqn)){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateComponentParameter,Parameter.Locations.FirstOrDefault(),Method.Name,ParameterTypeFqn));
                    IsValid=false;
                    continue;
                }

                CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.Component,Components.Count));
                Components.Add(new GameSystemComponentParameterModel(Parameter.Name,ParameterTypeFqn));
                continue;
            }

            // ref以外はContext。ただし`out`/`ref readonly`のような中途半端な渡し方は弾く。
            var IsReferenceType=Parameter.Type.IsReferenceType;
            var RefKindOk=IsReferenceType?Parameter.RefKind==RefKind.None:Parameter.RefKind==RefKind.In;

            if(!RefKindOk){
                Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ContextParameterMustBeInOrByValue,Parameter.Locations.FirstOrDefault(),Parameter.Name,Method.Name,Parameter.RefKind.ToString()));
                IsValid=false;
                continue;
            }

            if(!SeenContextTypes.Add(ParameterTypeFqn)){
                Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateContextParameter,Parameter.Locations.FirstOrDefault(),Method.Name,ParameterTypeFqn));
                IsValid=false;
                continue;
            }

            CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.Context,Contexts.Count));
            Contexts.Add(new GameSystemContextParameterModel(Parameter.Name,ParameterTypeFqn,IsReferenceType));
        }

        if(HasTrailingState&&!IsSpawn){
            CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.State,0));
        }

        if(Components.Count==0){
            Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.MethodMustHaveAtLeastOneComponentParameter,MethodLocation,Method.Name));
            IsValid=false;
        }

        if(!IsValid){
            return null;
        }

        return new GameSystemMethodModel(Role,HasLeadingEntity,[.. Components],[.. Contexts],[.. CallOrder],GetDeclarationPosition(Method));
    }

    private static string TypeFqn(IParameterSymbol Parameter)=>
        Parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    /// Componentになれる型かどうかの唯一の述語。実体は共有ソースの
    /// <see cref="global::CSharpGameEngine.SceneAnalysis.SceneTypePredicates.IsNonNullableStruct"/>にあり、
    /// ここは委譲するだけ(ADR-0040)。Scene DSL側(<c>Components</c>要素の検証)とSystem側
    /// (<c>ref</c>パラメーターの検証)で2箇所に同じ規則を書くと「Systemでは通るがSceneでは通らない」
    /// というズレが生まれ、両者が同一のArchetype空間を共有するという前提が壊れる。
    /// </summary>
    internal static bool IsNonNullableStruct(ITypeSymbol Type)=>
        global::CSharpGameEngine.SceneAnalysis.SceneTypePredicates.IsNonNullableStruct(Type);
}
