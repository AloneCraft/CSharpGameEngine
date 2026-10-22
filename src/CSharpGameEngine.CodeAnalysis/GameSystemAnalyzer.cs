using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>ISystem</c>を実装するstructのシンボル解析。属性を一切使わない設計(ADR-0033)のため、
/// パラメータの役割はすべて構造的に判別する:
/// <list type="number">
/// <item>先頭・値渡し・型が<c>Entity</c> → Entityスロット(任意)</item>
/// <item>末尾・<c>ref State</c> → Stateスロット(任意。ADR-0077)</item>
/// <item><c>ref</c>の非nullable struct → 書き込むComponent</item>
/// <item><c>in</c>の非nullable struct → 読み取るComponent(ADR-0077。<c>[ReadOnlyComponent] in</c>と同じ)</item>
/// <item>それ以外(参照型は値渡し) → Context</item>
/// </list>
/// 書き込みは<c>ref</c>・読み取りは<c>in</c>・Contextは参照型の値渡しなので、ref-kindと型の種類だけで曖昧性なく判別できる。
/// ランタイム型へは完全修飾名の文字列としてのみ参照し、CSharpGameEngine.CodeAnalysisから
/// CSharpGameEngine.Entitiesへのアセンブリ参照は発生させない(ADR-0001)。
/// </summary>
internal static class GameSystemAnalyzer {
    internal const string SystemInterfaceFullyQualifiedName="global::CSharpGameEngine.Entities.ISystem";

    /// <summary><c>Compilation.GetTypeByMetadataName</c>用(<c>global::</c>接頭辞を付けない)。</summary>
    internal const string SystemInterfaceMetadataName="CSharpGameEngine.Entities.ISystem";
    internal const string WorldSystemInterfaceMetadataName="CSharpGameEngine.Entities.IWorldSystem";
    internal const string SystemPhaseAttributeMetadataName="CSharpGameEngine.Entities.SystemPhaseAttribute";
    internal const string ReadOnlyComponentAttributeMetadataName="CSharpGameEngine.Entities.ReadOnlyComponentAttribute";
    internal const string ScheduledSystemAttributeMetadataName="CSharpGameEngine.Entities.ScheduledSystemAttribute";
    internal const string RunsAfterAttributeMetadataName="CSharpGameEngine.Entities.RunsAfterAttribute";
    internal const string RunsBeforeAttributeMetadataName="CSharpGameEngine.Entities.RunsBeforeAttribute";
    private const string EngineHostMetadataName="CSharpGameEngine.Runtime.Windows.EngineHost";
    internal const string EntityFullyQualifiedName="global::CSharpGameEngine.Entities.Entity";
    internal const string StateFullyQualifiedName="global::CSharpGameEngine.Entities.State";
    internal const string WindowSystemMetadataName="CSharpGameEngine.Runtime.Windows.Window";
    internal const string EventInterfaceMetadataName="CSharpGameEngine.Entities.IEvent";
    internal const string EventWriterMetadataName="CSharpGameEngine.Entities.EventWriter<T>";
    internal const string ParentRelationMetadataName="CSharpGameEngine.Entities.Parent<T>";
    internal const string ParentComponentFullyQualifiedName="global::CSharpGameEngine.Entities.Parent";

    private const string UpdateMethodName="Update";
    private const string SpawnMethodName="Spawn";
    private const string ShutdownMethodName="Shutdown";

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

        var IsWorldSystem=SystemSymbol.AllInterfaces.Any(static Interface=>Interface.ToDisplayString()==WorldSystemInterfaceMetadataName);

        List<IMethodSymbol>Methods=[];

        foreach(var Member in SystemSymbol.GetMembers()){
            if(Member is IMethodSymbol { MethodKind:MethodKind.Ordinary } Method
                &&(Method.Name==UpdateMethodName||Method.Name==SpawnMethodName||Method.Name==ShutdownMethodName)){
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
            var Model=AnalyzeMethod(Method,Diagnostics,IsWorldSystem);

            if(Model is { } Value){
                MethodModels.Add(Value);
            }
        }

        var TypeLocation=SystemSymbol.Locations.FirstOrDefault()??Location.None;
        var ExecuteInEditor=false;
        var RealtimeInEditor=false;
        var IsScheduled=false;
        var RunsAfter=ImmutableArray.CreateBuilder<string>();
        var RunsBefore=ImmutableArray.CreateBuilder<string>();
        foreach(var Attribute in SystemSymbol.GetAttributes()){
            if(Attribute.AttributeClass?.ToDisplayString()=="CSharpGameEngine.Entities.ExecuteInEditorAttribute"){
                ExecuteInEditor=true;
                RealtimeInEditor=Attribute.NamedArguments.Any(static A=>A.Key=="Realtime"&&A.Value.Value is true);
            } else if(Attribute.AttributeClass?.ToDisplayString()==ScheduledSystemAttributeMetadataName){
                IsScheduled=true;
            } else if(Attribute.AttributeClass?.ToDisplayString()==RunsAfterAttributeMetadataName
                &&Attribute.ConstructorArguments.Length==1
                &&Attribute.ConstructorArguments[0].Value is INamedTypeSymbol Earlier){
                RunsAfter.Add(Earlier.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            } else if(Attribute.AttributeClass?.ToDisplayString()==RunsBeforeAttributeMetadataName
                &&Attribute.ConstructorArguments.Length==1
                &&Attribute.ConstructorArguments[0].Value is INamedTypeSymbol Later){
                RunsBefore.Add(Later.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
        }

        var ReadsInput=MethodModels.Any(static Method=>Method.Resources.Length>0);
        // ADR-0077: a system that reads [InputActions] runs in FrameInput, after the engine's Window system has pumped the events,
        // so a game never writes [SystemPhase] or [RunsAfter(typeof(Window))] for input. An explicit [SystemPhase] still wins.
        if(ReadsInput&&SystemSymbol.ContainingAssembly.GetTypeByMetadataName(WindowSystemMetadataName) is { }WindowSystem){
            var WindowFqn=WindowSystem.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if(!RunsAfter.Contains(WindowFqn)){ RunsAfter.Add(WindowFqn); }
        }

        var PhaseIsExplicit=false;
        var Phase=MethodModels.Any(static Method=>Method.Components.Any(static Component=>Component.TypeFullyQualifiedName=="global::CSharpGameEngine.Entities.Bootstrap")||Method.Resources.Length>0)?0:1;
        foreach(var Attribute in SystemSymbol.GetAttributes()){
            if(Attribute.AttributeClass?.ToDisplayString()==SystemPhaseAttributeMetadataName
                &&Attribute.ConstructorArguments.Length==1){
                PhaseIsExplicit=true;
                object? Value=Attribute.ConstructorArguments[0].Value;
                Phase=Value switch {
                    byte ByteValue=>ByteValue,
                    sbyte SignedByteValue=>SignedByteValue,
                    short ShortValue=>ShortValue,
                    ushort UnsignedShortValue=>UnsignedShortValue,
                    int IntValue=>IntValue,
                    uint UnsignedIntValue=>checked((int)UnsignedIntValue),
                    long LongValue=>checked((int)LongValue),
                    ulong UnsignedLongValue=>checked((int)UnsignedLongValue),
                    _=>Phase,
                };
                break;
            }
        }

        return new GameSystemResult(
            SystemSymbol.ContainingNamespace.IsGlobalNamespace?null:SystemSymbol.ContainingNamespace.ToDisplayString(),
            SystemSymbol.Name,
            [.. MethodModels],
            Diagnostics.ToImmutable(),
            ExecuteInEditor,
            RealtimeInEditor,
            Phase,
            IsWorldSystem,
            IsScheduled,
            RunsAfter.ToImmutable(),
            RunsBefore.ToImmutable(),
            TypeLocation,
            TypeLocation.SourceTree?.FilePath??string.Empty,
            TypeLocation.SourceSpan.Start,
            PhaseIsExplicit:PhaseIsExplicit);
    }

    internal static bool IsEventStruct(ITypeSymbol Type)=>
        Type.TypeKind==TypeKind.Struct&&Type.AllInterfaces.Any(static Interface=>Interface.ToDisplayString()==EventInterfaceMetadataName);

    private static int GetDeclarationPosition(ISymbol Symbol){
        Location Location=Symbol.Locations.FirstOrDefault()??Location.None;
        return Location.SourceSpan.Start;
    }

    private static GameSystemMethodModel?AnalyzeMethod(IMethodSymbol Method,ImmutableArray<Diagnostic>.Builder Diagnostics,bool IsWorldSystem){
        var IsSpawn=Method.Name==SpawnMethodName;
        var IsShutdown=Method.Name==ShutdownMethodName;
        var Role=IsSpawn?GameSystemMethodRole.SpawnOnly:IsShutdown?GameSystemMethodRole.Shutdown:GameSystemMethodRole.Dispatch;
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
        } else if(IsShutdown){
            if(!Method.ReturnsVoid){
                Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ShutdownMethodMustReturnVoid,MethodLocation,Method.Name,Method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                IsValid=false;
            }
        } else if(!Method.ReturnsVoid&&Method.ReturnType.SpecialType!=SpecialType.System_Boolean){
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
            // ADR-0077: the trailing `ref State` is optional. A system that does not need the world or the frame
            // time simply leaves it out, and the loop passes nothing.
            ScanEndExclusive=Parameters.Length-1;
        }

        List<GameSystemComponentParameterModel>Components=[];
        List<GameSystemContextParameterModel>Contexts=[];
        List<GameSystemRelationParameterModel>Relations=[];
        List<GameSystemResourceParameterModel>Resources=[];
        List<GameSystemServiceParameterModel>Services=[];
        List<GameSystemEventParameterModel>EventReads=[];
        List<GameSystemEventParameterModel>EventWrites=[];
        List<GameSystemParameterSlot>CallOrder=[];
        HashSet<string>SeenComponentTypes=new(StringComparer.Ordinal);
        HashSet<string>SeenContextTypes=new(StringComparer.Ordinal);

        if(HasLeadingEntity){
            CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.Entity,0));
        }

        for(var I=ScanStart;I<ScanEndExclusive;I++){
            var Parameter=Parameters[I];
            var ParameterTypeFqn=TypeFqn(Parameter);
            var IsReadOnlyComponent=Parameter.GetAttributes().Any(static Attribute=>
                Attribute.AttributeClass?.ToDisplayString()==ReadOnlyComponentAttributeMetadataName);

            if(ParameterTypeFqn==EntityFullyQualifiedName||ParameterTypeFqn==StateFullyQualifiedName){
                Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.EntityOrStateMustNotBeUsedAsComponent,Parameter.Locations.FirstOrDefault(),Parameter.Name,Method.Name,ParameterTypeFqn));
                IsValid=false;
                continue;
            }

            // ADR-0077 stage 6: events. `in EventWriter<T>` sends them, `in T` (T : IEvent) receives one at a time.
            if(Parameter.Type is INamedTypeSymbol { IsGenericType:true } WriterType
                &&WriterType.ConstructedFrom.ToDisplayString()==EventWriterMetadataName){
                if(IsSpawn||Parameter.RefKind!=RefKind.In||WriterType.TypeArguments.Length!=1||!IsEventStruct(WriterType.TypeArguments[0])){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.EventParameterInvalid,Parameter.Locations.FirstOrDefault(),Parameter.Name,Method.Name));
                    IsValid=false;
                    continue;
                }

                var WrittenFqn=WriterType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if(EventWrites.Any(Existing=>Existing.EventTypeFullyQualifiedName==WrittenFqn)){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.EventParameterInvalid,Parameter.Locations.FirstOrDefault(),Parameter.Name,Method.Name));
                    IsValid=false;
                    continue;
                }

                CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.EventWrite,EventWrites.Count));
                EventWrites.Add(new GameSystemEventParameterModel(Parameter.Name,WrittenFqn));
                continue;
            }

            if(Parameter.Type.TypeKind==TypeKind.Struct&&IsEventStruct(Parameter.Type)){
                if(IsSpawn||IsShutdown||Parameter.RefKind!=RefKind.In||EventReads.Count>0){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.EventParameterInvalid,Parameter.Locations.FirstOrDefault(),Parameter.Name,Method.Name));
                    IsValid=false;
                    continue;
                }

                CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.EventRead,EventReads.Count));
                EventReads.Add(new GameSystemEventParameterModel(Parameter.Name,ParameterTypeFqn));
                continue;
            }

            // ADR-0077 stage 4: an [InputActions] struct is a per-frame resource, not a component. Checked before the generic
            // `in` struct rule, which would make it a component and never match any entity.
            if(Parameter.Type.TypeKind==TypeKind.Struct
                &&Parameter.Type.GetAttributes().Any(static Attribute=>Attribute.AttributeClass?.ToDisplayString()==InputActionsEmitter.AttributeMetadataName)){
                if(IsSpawn||Parameter.RefKind!=RefKind.In){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.InputActionsMustBePassedByIn,Parameter.Locations.FirstOrDefault(),Parameter.Name,Method.Name));
                    IsValid=false;
                    continue;
                }

                if(Resources.Any(Existing=>Existing.TypeFullyQualifiedName==ParameterTypeFqn)){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateContextParameter,Parameter.Locations.FirstOrDefault(),Method.Name,ParameterTypeFqn));
                    IsValid=false;
                    continue;
                }

                CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.Resource,Resources.Count));
                Resources.Add(new GameSystemResourceParameterModel(Parameter.Name,ParameterTypeFqn));
                continue;
            }

            // ADR-0077: `in Parent<T>` reads component T from the parent entity. It is checked before the generic
            // `in` struct rule because Parent<T> is itself a non-nullable struct.
            if(Parameter.Type is INamedTypeSymbol { IsGenericType:true } RelationType
                &&RelationType.ConstructedFrom.ToDisplayString()==ParentRelationMetadataName){
                if(IsSpawn||Parameter.RefKind!=RefKind.In||RelationType.TypeArguments.Length!=1||!IsNonNullableStruct(RelationType.TypeArguments[0])){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ParentRelationParameterInvalid,Parameter.Locations.FirstOrDefault(),Parameter.Name,Method.Name));
                    IsValid=false;
                    continue;
                }

                var RelatedFqn=RelationType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if(Relations.Any(Existing=>Existing.ComponentTypeFullyQualifiedName==RelatedFqn)){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ParentRelationConflictsWithComponent,MethodLocation,Method.Name,RelatedFqn));
                    IsValid=false;
                    continue;
                }

                CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.ParentRelation,Relations.Count));
                Relations.Add(new GameSystemRelationParameterModel(Parameter.Name,RelatedFqn));
                continue;
            }

            if(IsReadOnlyComponent){
                if(Parameter.RefKind!=RefKind.In||!IsNonNullableStruct(Parameter.Type)){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ReadOnlyComponentMustBeInStruct,
                        Parameter.Locations.FirstOrDefault(),Parameter.Name,Method.Name));
                    IsValid=false;
                    continue;
                }

                if(!SeenComponentTypes.Add(ParameterTypeFqn)){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateComponentParameter,Parameter.Locations.FirstOrDefault(),Method.Name,ParameterTypeFqn));
                    IsValid=false;
                    continue;
                }

                CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.Component,Components.Count));
                Components.Add(new GameSystemComponentParameterModel(Parameter.Name,ParameterTypeFqn,IsReadOnly:true));
                continue;
            }

            // ADR-0077: `in T` on a struct reads the component. It replaces `[ReadOnlyComponent] in T`, which is still
            // accepted above. Primitives and enums stay what they were, a by-`in` value context.
            if(Parameter.RefKind==RefKind.In&&Parameter.Type.SpecialType==SpecialType.None&&Parameter.Type.TypeKind==TypeKind.Struct&&IsNonNullableStruct(Parameter.Type)){
                if(!SeenComponentTypes.Add(ParameterTypeFqn)){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateComponentParameter,Parameter.Locations.FirstOrDefault(),Method.Name,ParameterTypeFqn));
                    IsValid=false;
                    continue;
                }

                CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.Component,Components.Count));
                Components.Add(new GameSystemComponentParameterModel(Parameter.Name,ParameterTypeFqn,IsReadOnly:true));
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
                Components.Add(new GameSystemComponentParameterModel(Parameter.Name,ParameterTypeFqn,IsReadOnly:false));
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

            // ADR-0077 stage 5: a reference-type parameter whose type is a property of EngineHost (Audio, ...) is a service:
            // the loop passes Host.<Property>. This generalizes the IPhysicsHost rule below; a system needs no [Context] ceremony.
            if(Parameter.Type.TypeKind==TypeKind.Class
                &&Method.ContainingAssembly.GetTypeByMetadataName(EngineHostMetadataName) is { }ServiceHost
                &&ServiceHost.GetMembers().OfType<IPropertySymbol>().FirstOrDefault(Property=>!Property.IsStatic&&Property.GetMethod is not null&&SymbolEqualityComparer.Default.Equals(Property.Type,Parameter.Type)) is { }ServiceProperty){
                if(Services.Any(Existing=>Existing.TypeFullyQualifiedName==ParameterTypeFqn)){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateContextParameter,Parameter.Locations.FirstOrDefault(),Method.Name,ParameterTypeFqn));
                    IsValid=false;
                    continue;
                }

                CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.Service,Services.Count));
                Services.Add(new GameSystemServiceParameterModel(Parameter.Name,ParameterTypeFqn,ServiceProperty.Name));
                continue;
            }

            // M0096 G: an interface the engine host implements (IPhysicsHost) is the host itself, so a portable
            // system can take it and the loop passes the one host it already has.
            var ContextTypeFqn=Parameter.Type.TypeKind==TypeKind.Interface
                &&Method.ContainingAssembly.GetTypeByMetadataName(EngineHostMetadataName) is { }EngineHost
                &&EngineHost.AllInterfaces.Contains(Parameter.Type,SymbolEqualityComparer.Default)
                    ?EngineHost.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    :ParameterTypeFqn;
            if(!SeenContextTypes.Add(ContextTypeFqn)){
                Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.DuplicateContextParameter,Parameter.Locations.FirstOrDefault(),Method.Name,ContextTypeFqn));
                IsValid=false;
                continue;
            }

            CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.Context,Contexts.Count));
            Contexts.Add(new GameSystemContextParameterModel(Parameter.Name,ContextTypeFqn,IsReferenceType));
        }

        if(HasTrailingState&&!IsSpawn){
            CallOrder.Add(new GameSystemParameterSlot(GameSystemParameterRole.State,0));
        }

        if(EventReads.Count>0&&(Components.Count>0||Relations.Count>0||HasLeadingEntity)){
            Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.EventConsumerCannotTakeEntities,MethodLocation,Method.Name));
            IsValid=false;
        } else if(Components.Count==0&&!IsWorldSystem&&EventReads.Count==0){
            Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.MethodMustHaveAtLeastOneComponentParameter,MethodLocation,Method.Name));
            IsValid=false;
        }

        if(Relations.Count>0){
            foreach(var Relation in Relations){
                if(SeenComponentTypes.Contains(Relation.ComponentTypeFullyQualifiedName)){
                    Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ParentRelationConflictsWithComponent,MethodLocation,Method.Name,Relation.ComponentTypeFullyQualifiedName));
                    IsValid=false;
                }
            }

            // The child's archetype carries Parent (the scene gives every child one); the loop reads it to find the parent.
            if(IsValid&&!SeenComponentTypes.Contains(ParentComponentFullyQualifiedName)){
                Components.Add(new GameSystemComponentParameterModel(string.Empty,ParentComponentFullyQualifiedName,IsReadOnly:true));
            }
        }


        if(!IsValid){
            return null;
        }

        return new GameSystemMethodModel(Role,HasLeadingEntity,[.. Components],[.. Contexts],[.. CallOrder],GetDeclarationPosition(Method),
            ReturnsBool:Role==GameSystemMethodRole.Dispatch&&!Method.ReturnsVoid,[.. Relations],[.. Resources],[.. Services],[.. EventReads],[.. EventWrites]);
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
