using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// 診断のないSystemだけを対象に、Component型へグローバルインデックスを割り当て、Archetype集合を
/// 導出する純粋関数(ADR-0033)。形状の要求元は3つ: <c>Update</c>オーバーロード、<c>Spawn</c>
/// オーバーロード、そして<c>[SceneDefinition]</c>のノード形状。いずれも同一のArchetype空間へ入る
/// (単一の<c>partial struct World</c>/<c>enum Archetype</c>を作るため、生成は1回しか行えない)。
///
/// <para>
/// 実行順序(ADR-0033決定事項1)は「ソース上の記述順」。Systemは(ファイルパス, 宣言位置)で整列し、
/// 同一struct内のオーバーロードは宣言位置順(<see cref="GameSystemAnalyzer"/>で整列済み)。
/// ファイルパスをキーに使うのはマシン依存の再現性リスクがあるため、実運用では1ファイルへの集約を
/// 推奨する(ファイルをまたいだ場合の決定性を担保するためのタイブレークとしてのみ使う)。
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
internal static class GameSystemIndexAssigner
{
    internal static GameSystemRegistry Assign(
        ImmutableArray<GameSystemResult> allSystems,
        IReadOnlyList<IReadOnlyList<string>> additionalComponentSets)
    {
        List<GameSystemResult> validSystems = [.. allSystems.Where(static s => s.Diagnostics.Length == 0)];
        validSystems.Sort(static (a, b) =>
        {
            int fileCompare = string.CompareOrdinal(a.FilePath, b.FilePath);
            return fileCompare != 0 ? fileCompare : a.DeclarationPosition.CompareTo(b.DeclarationPosition);
        });

        Dictionary<string, int> componentIndexByType = new(StringComparer.Ordinal);
        List<string> componentTypesByIndex = [];
        HashSet<string> seenComboKeys = new(StringComparer.Ordinal);
        List<List<int>> combosInDiscoveryOrder = [];

        void RegisterSet(IReadOnlyList<string> componentTypeFqns)
        {
            List<int> indices = new(componentTypeFqns.Count);

            foreach (string typeFqn in componentTypeFqns)
            {
                if (!componentIndexByType.TryGetValue(typeFqn, out int index))
                {
                    index = componentTypesByIndex.Count;
                    componentIndexByType.Add(typeFqn, index);
                    componentTypesByIndex.Add(typeFqn);
                }

                indices.Add(index);
            }

            // 同一性の判定は集合(ソート済みキー)で行うが、保持するのは宣言順のまま。生成される
            // World.Create/MoveArchetypeのパラメータ順が、最初にこの構成を宣言したメソッドの
            // 引数順と一致するようにするため。
            List<int> sorted = [.. indices];
            sorted.Sort();

            if (seenComboKeys.Add(string.Join(",", sorted)))
            {
                combosInDiscoveryOrder.Add(indices);
            }
        }

        foreach (GameSystemResult system in validSystems)
        {
            foreach (GameSystemMethodModel method in system.Methods)
            {
                RegisterSet([.. method.Components.Select(static c => c.TypeFullyQualifiedName)]);
            }
        }

        foreach (IReadOnlyList<string> componentSet in additionalComponentSets)
        {
            RegisterSet(componentSet);
        }

        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        ImmutableArray<GameSystemComponentInfo> componentsByIndex = BuildComponents(componentTypesByIndex, diagnostics);

        // comboByKeyは挿入順を保つDictionaryではないため、出現順は別途記録したリストから取る。
        List<GameSystemArchetypeInfo> archetypes = [];

        for (int archetypeIndex = 0; archetypeIndex < combosInDiscoveryOrder.Count; archetypeIndex++)
        {
            List<int> combo = combosInDiscoveryOrder[archetypeIndex];
            long mask = 0;

            foreach (int index in combo)
            {
                mask |= 1L << index;
            }

            archetypes.Add(new GameSystemArchetypeInfo($"Archetype{archetypeIndex}", [.. combo], mask));
        }

        return new GameSystemRegistry(componentsByIndex, [.. archetypes], [.. validSystems], diagnostics.ToImmutable());
    }

    /// <summary>
    /// 生成される<c>ArchetypeNN</c>のフィールド名にはComponent型の単純名を使う(サンプルに忠実)。
    /// 異なる名前空間の同名型が2つあるとフィールド名が衝突するため、SYSGEN020で明示的に弾く。
    /// </summary>
    private static ImmutableArray<GameSystemComponentInfo> BuildComponents(List<string> componentTypesByIndex, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        Dictionary<string, string> typeBySimpleName = new(StringComparer.Ordinal);
        ImmutableArray<GameSystemComponentInfo>.Builder builder = ImmutableArray.CreateBuilder<GameSystemComponentInfo>(componentTypesByIndex.Count);

        for (int index = 0; index < componentTypesByIndex.Count; index++)
        {
            string typeFqn = componentTypesByIndex[index];
            string simpleName = ExtractSimpleTypeName(typeFqn);

            if (typeBySimpleName.TryGetValue(simpleName, out string? existing))
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.ComponentSimpleNameCollision, Location.None, existing, typeFqn, simpleName));
            }
            else
            {
                typeBySimpleName.Add(simpleName, typeFqn);
            }

            builder.Add(new GameSystemComponentInfo(index, typeFqn, simpleName));
        }

        return builder.ToImmutable();
    }

    internal static string ExtractSimpleTypeName(string typeFullyQualifiedName)
    {
        const string GlobalPrefix = "global::";
        string value = typeFullyQualifiedName.StartsWith(GlobalPrefix, StringComparison.Ordinal)
            ? typeFullyQualifiedName.Substring(GlobalPrefix.Length)
            : typeFullyQualifiedName;

        int lastDot = value.LastIndexOf('.');
        return lastDot >= 0 ? value.Substring(lastDot + 1) : value;
    }
}
