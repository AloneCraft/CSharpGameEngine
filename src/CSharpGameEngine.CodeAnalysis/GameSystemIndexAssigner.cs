using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// 診断のないSystemだけを対象に、Component型へグローバルインデックスを割り当て、Archetype集合を
/// 導出する純粋関数(ADR-0033)。形状の要求元は3つ: <c>Update</c>オーバーロード、<c>Spawn</c>
/// オーバーロード、そして<c>IScene</c>のノード形状(ADR-0040)。いずれも同一のArchetype空間へ入る
/// (単一の<c>partial struct World</c>/<c>enum Archetype</c>を作るため、生成は1回しか行えない)。
/// Scene由来の集合は<c>AdditionalComponentSets</c>として<b>実Systemの後に</b>登録されるため、
/// Sceneを足してもSystem由来のArchetype番号は動かない。
///
/// <para>
/// 実行順序は<see cref="GameSystemExecutionOrder"/>に委譲する(ADR-0073: Phase順、Phaseの中は
/// <c>[RunsAfter]</c>/<c>[RunsBefore]</c>のトポロジカル順、同着は完全修飾名)。同一struct内のオーバーロードは
/// 宣言順(<see cref="GameSystemAnalyzer"/>で整列済み)。
/// </para>
///
/// <para>
/// Archetypeの名前と出力順序はいずれも<b>出現順の連番</b>(<c>Archetype0</c>, <c>Archetype1</c>, …)。
/// Component集合を連結した名前(<c>Archetype0001020304050613</c>のような形)は読めない上に、どこかの
/// Systemへ引数を1つ足すだけで全Archetypeの名前が変わってしまうため採用しない。Archetype名は
/// ユーザーコードから参照されない内部識別子であり(生成される<c>World.Create</c>/<c>MoveArchetype</c>
/// オーバーロードがComponent型の組み合わせで解決されるため)、短く安定した連番で十分である。
/// </para>
/// </summary>
internal static class GameSystemIndexAssigner {
    /// <summary>
    /// Component型数の上限(SYSGEN026)。マスクは64型ごとに1語を足す(<see cref="ComponentMask"/>)。プラグインがゲームと同じ番号の
    /// 空間を使うため、二語(128)から八語に広げた(ADR-0079)。
    /// </summary>
    internal const int MaxComponentTypes=512;

    internal static GameSystemRegistry Assign(
        ImmutableArray<GameSystemResult>AllSystems,
        IReadOnlyList<IReadOnlyList<string>>AdditionalComponentSets){
        List<GameSystemResult>ValidSystems=GameSystemExecutionOrder.Order(AllSystems.Where(static S=>S.Diagnostics.Length==0));

        Dictionary<string,int>ComponentIndexByType=new(StringComparer.Ordinal);
        List<string>ComponentTypesByIndex=[];
        HashSet<string>SeenComboKeys=new(StringComparer.Ordinal);
        List<List<int>>CombosInDiscoveryOrder=[];

        void RegisterSet(IReadOnlyList<string>ComponentTypeFqns){
            List<int>Indices=new(ComponentTypeFqns.Count);

            foreach(var TypeFqn in ComponentTypeFqns){
                if(!ComponentIndexByType.TryGetValue(TypeFqn,out var Index)){
                    Index=ComponentTypesByIndex.Count;
                    ComponentIndexByType.Add(TypeFqn,Index);
                    ComponentTypesByIndex.Add(TypeFqn);
                }

                Indices.Add(Index);
            }

            // 同一性の判定は集合(ソート済みキー)で行うが、保持するのは宣言順のまま。生成される
            // World.Create/MoveArchetypeのパラメータ順が、最初にこの構成を宣言したメソッドの
            // 引数順と一致するようにするため。
            List<int>Sorted=[.. Indices];
            Sorted.Sort();

            if(SeenComboKeys.Add(string.Join(",",Sorted))){
                CombosInDiscoveryOrder.Add(Indices);
            }
        }

        foreach(var System in ValidSystems){
            foreach(var Method in System.Methods){
                if(Method.Components.Length>0){
                    RegisterSet([.. Method.Components.Select(static C=>C.TypeFullyQualifiedName)]);
                }

                // ADR-0077: a Parent<T> relation reads T from another entity through World.TryGetComponent, so T needs a
                // component index (and its TryGetComponent overload) even if no method of this child archetype owns it.
                foreach(var Relation in Method.Relations){
                    if(!ComponentIndexByType.ContainsKey(Relation.ComponentTypeFullyQualifiedName)){
                        ComponentIndexByType.Add(Relation.ComponentTypeFullyQualifiedName,ComponentTypesByIndex.Count);
                        ComponentTypesByIndex.Add(Relation.ComponentTypeFullyQualifiedName);
                    }
                }
            }
        }

        foreach(var ComponentSet in AdditionalComponentSets){
            RegisterSet(ComponentSet);
        }

        var Diagnostics=ImmutableArray.CreateBuilder<Diagnostic>();

        // 上限を超えたら、生成を明示的に拒否する。
        if(ComponentTypesByIndex.Count>MaxComponentTypes){
            Diagnostics.Add(Diagnostic.Create(
                GameSystemDiagnostics.TooManyComponentTypes,
                Location.None,
                ComponentTypesByIndex.Count,
                MaxComponentTypes));
        }

        var ComponentsByIndex=BuildComponents(ComponentTypesByIndex,Diagnostics);

        // comboByKeyは挿入順を保つDictionaryではないため、出現順は別途記録したリストから取る。
        List<GameSystemArchetypeInfo>Archetypes=[];
        var WordCount=ComponentMask.WordCount(ComponentTypesByIndex.Count);

        for(var ArchetypeIndex=0;ArchetypeIndex<CombosInDiscoveryOrder.Count;ArchetypeIndex++){
            var Combo=CombosInDiscoveryOrder[ArchetypeIndex];
            Archetypes.Add(new GameSystemArchetypeInfo($"Archetype{ArchetypeIndex}",[.. Combo],ComponentMask.Build(Combo,WordCount)));
        }

        return new GameSystemRegistry(ComponentsByIndex,[.. Archetypes],[.. ValidSystems],Diagnostics.ToImmutable());
    }

    /// <summary>
    /// 生成される<c>ArchetypeNN</c>のフィールド名と<c>EcsComponentType</c>の名前には、Component型の単純名を使う(サンプルに忠実)。
    /// 異なる名前空間の同名型(例: 2つのプラグインの<c>Wheel</c>)は、名前空間を含めた名前(<c>Vehicles_Wheel</c>)にする(ADR-0079)。
    /// それでも重なるとき(<c>Vehicles_Wheel</c>という単純名の型がほかにある)だけ、SYSGEN020で明示的に弾く。
    /// </summary>
    private static ImmutableArray<GameSystemComponentInfo>BuildComponents(List<string>ComponentTypesByIndex,ImmutableArray<Diagnostic>.Builder Diagnostics){
        Dictionary<string,int>SimpleNameCounts=new(StringComparer.Ordinal);
        foreach(var TypeFqn in ComponentTypesByIndex){
            var SimpleName=ExtractSimpleTypeName(TypeFqn);
            SimpleNameCounts[SimpleName]=SimpleNameCounts.TryGetValue(SimpleName,out var Count)?Count+1:1;
        }
        Dictionary<string,string>TypeByFieldName=new(StringComparer.Ordinal);
        var Builder=ImmutableArray.CreateBuilder<GameSystemComponentInfo>(ComponentTypesByIndex.Count);

        for(var Index=0;Index<ComponentTypesByIndex.Count;Index++){
            var TypeFqn=ComponentTypesByIndex[Index];
            var SimpleName=ExtractSimpleTypeName(TypeFqn);
            var FieldName=SimpleNameCounts[SimpleName]>1?QualifiedFieldName(TypeFqn):SimpleName;

            if(TypeByFieldName.TryGetValue(FieldName,out var Existing)){
                Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ComponentSimpleNameCollision,Location.None,Existing,TypeFqn,FieldName));
            } else {
                TypeByFieldName.Add(FieldName,TypeFqn);
            }

            Builder.Add(new GameSystemComponentInfo(Index,TypeFqn,FieldName));
        }

        return Builder.ToImmutable();
    }

    /// <summary><c>global::Vehicles.Wheel</c> → <c>Vehicles_Wheel</c>.</summary>
    private static string QualifiedFieldName(string TypeFullyQualifiedName){
        const string GlobalPrefix="global::";
        var Value=TypeFullyQualifiedName.StartsWith(GlobalPrefix,StringComparison.Ordinal)
            ?TypeFullyQualifiedName.Substring(GlobalPrefix.Length)
            :TypeFullyQualifiedName;
        return Value.Replace('.','_');
    }

    internal static string ExtractSimpleTypeName(string TypeFullyQualifiedName){
        const string GlobalPrefix="global::";
        var Value=TypeFullyQualifiedName.StartsWith(GlobalPrefix,StringComparison.Ordinal)
            ?TypeFullyQualifiedName.Substring(GlobalPrefix.Length)
            :TypeFullyQualifiedName;

        var LastDot=Value.LastIndexOf('.');
        return LastDot>=0?Value.Substring(LastDot+1):Value;
    }
}
