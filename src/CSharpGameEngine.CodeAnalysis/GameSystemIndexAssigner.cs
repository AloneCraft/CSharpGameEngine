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
/// 実行順序(ADR-0033決定事項1)は「ソース上の記述順」。整列は<see cref="GameSystemExecutionOrder"/>に
/// 委譲しており、キーは①<c>[SystemOrder]</c>をSystemへ直接付けた明示値 ②<c>[SystemOrder]</c>を
/// ファイルマーカーへ付けたファイル既定値(いずれもADR-0036、宣言が無ければ0) ③ファイルパスの
/// Ordinal順(後方互換のタイブレーク) ④宣言位置(同一struct内のオーバーロード順、
/// <see cref="GameSystemAnalyzer"/>で整列済み)。
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
    /// <summary>二語のArchetypeマスクで表現できるComponent型数の上限(SYSGEN026)。</summary>
    internal const int MaxComponentTypes=128;

    internal static GameSystemRegistry Assign(
        ImmutableArray<GameSystemResult>AllSystems,
        IReadOnlyList<IReadOnlyList<string>>AdditionalComponentSets,
        GameSystemOrderContext OrderContext){
        List<GameSystemResult>ValidSystems=[.. AllSystems.Where(static S=>S.Diagnostics.Length==0)];
        ValidSystems.Sort((A,B)=>GameSystemExecutionOrder.Compare(A,B,OrderContext));

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
            }
        }

        foreach(var ComponentSet in AdditionalComponentSets){
            RegisterSet(ComponentSet);
        }

        var Diagnostics=ImmutableArray.CreateBuilder<Diagnostic>();

        // 129個目以降は二語のマスクで表現できないため、生成を明示的に拒否する。
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

        for(var ArchetypeIndex=0;ArchetypeIndex<CombosInDiscoveryOrder.Count;ArchetypeIndex++){
            var Combo=CombosInDiscoveryOrder[ArchetypeIndex];
            ulong MaskLow=0;
            ulong MaskHigh=0;

            foreach(var Index in Combo){
                if(Index<64){ MaskLow|=1UL<<Index; }
                else { MaskHigh|=1UL<<(Index-64); }
            }

            Archetypes.Add(new GameSystemArchetypeInfo($"Archetype{ArchetypeIndex}",[.. Combo],MaskLow,MaskHigh));
        }

        return new GameSystemRegistry(ComponentsByIndex,[.. Archetypes],[.. ValidSystems],Diagnostics.ToImmutable());
    }

    /// <summary>
    /// 生成される<c>ArchetypeNN</c>のフィールド名にはComponent型の単純名を使う(サンプルに忠実)。
    /// 異なる名前空間の同名型が2つあるとフィールド名が衝突するため、SYSGEN020で明示的に弾く。
    /// </summary>
    private static ImmutableArray<GameSystemComponentInfo>BuildComponents(List<string>ComponentTypesByIndex,ImmutableArray<Diagnostic>.Builder Diagnostics){
        Dictionary<string,string>TypeBySimpleName=new(StringComparer.Ordinal);
        var Builder=ImmutableArray.CreateBuilder<GameSystemComponentInfo>(ComponentTypesByIndex.Count);

        for(var Index=0;Index<ComponentTypesByIndex.Count;Index++){
            var TypeFqn=ComponentTypesByIndex[Index];
            var SimpleName=ExtractSimpleTypeName(TypeFqn);

            if(TypeBySimpleName.TryGetValue(SimpleName,out var Existing)){
                Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ComponentSimpleNameCollision,Location.None,Existing,TypeFqn,SimpleName));
            } else {
                TypeBySimpleName.Add(SimpleName,TypeFqn);
            }

            Builder.Add(new GameSystemComponentInfo(Index,TypeFqn,SimpleName));
        }

        return Builder.ToImmutable();
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
