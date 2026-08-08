using System.Collections.Immutable;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// 診断のないSystemだけを対象に、Component型へグローバルインデックスを割り当て、Archetype集合を
/// 導出する純粋関数(ADR-0032決定事項2/3)。Systemを完全修飾名の英字順にソートし、各Systemの
/// <c>Update</c>オーバーロードを宣言順、各オーバーロードのComponentパラメータを宣言順に走査して、
/// 初めて出現したComponent型へ0から連番を割り当てる。Archetypeは、全オーバーロードが要求する
/// Component集合(順序無視)の和集合を重複排除したもの。出力順序(enum定義順・フィールド順等)は
/// 発見順ではなく「コンポーネント数昇順→インデックスタプル辞書式昇順」で確定する
/// (最終サンプルの<c>Archetype0,Archetype1,Archetype2,Archetype01</c>という並びから判明した規則)。
/// </summary>
internal static class StaticArchetypeIndexAssigner
{
    internal static StaticArchetypeRegistry Assign(ImmutableArray<StaticArchetypeSystemResult> allSystems)
    {
        List<StaticArchetypeSystemResult> validSystems = [.. allSystems.Where(static s => s.Diagnostics.Length == 0)];
        validSystems.Sort(static (a, b) => string.CompareOrdinal(a.ClassFullyQualifiedName, b.ClassFullyQualifiedName));

        Dictionary<string, int> componentIndexByType = new(StringComparer.Ordinal);
        List<string> componentTypesByIndex = [];
        Dictionary<string, List<int>> comboByKey = new(StringComparer.Ordinal);

        foreach (StaticArchetypeSystemResult system in validSystems)
        {
            foreach (StaticArchetypeUpdateOverloadModel overload in system.Overloads)
            {
                List<int> indices = new(overload.Components.Length);

                foreach (StaticArchetypeComponentParameterModel component in overload.Components)
                {
                    if (!componentIndexByType.TryGetValue(component.TypeFullyQualifiedName, out int index))
                    {
                        index = componentTypesByIndex.Count;
                        componentIndexByType.Add(component.TypeFullyQualifiedName, index);
                        componentTypesByIndex.Add(component.TypeFullyQualifiedName);
                    }

                    indices.Add(index);
                }

                indices.Sort();
                string key = string.Join(",", indices);

                if (!comboByKey.ContainsKey(key))
                {
                    comboByKey.Add(key, indices);
                }
            }
        }

        List<List<int>> combos = [.. comboByKey.Values];
        combos.Sort(static (a, b) =>
        {
            if (a.Count != b.Count)
            {
                return a.Count.CompareTo(b.Count);
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return a[i].CompareTo(b[i]);
                }
            }

            return 0;
        });

        int padWidth = ComputePadWidth(componentTypesByIndex.Count);
        List<StaticArchetypeArchetypeInfo> archetypes = [];

        foreach (List<int> combo in combos)
        {
            string name = "Archetype" + string.Join(string.Empty, combo.Select(i => i.ToString().PadLeft(padWidth, '0')));
            long mask = 0;

            foreach (int index in combo)
            {
                mask |= 1L << index;
            }

            archetypes.Add(new StaticArchetypeArchetypeInfo(name, [.. combo], mask));
        }

        ImmutableArray<StaticArchetypeComponentInfo> componentsByIndex =
            [.. componentTypesByIndex.Select(static (typeFqn, index) => new StaticArchetypeComponentInfo(index, typeFqn))];

        return new StaticArchetypeRegistry(componentsByIndex, [.. archetypes], [.. validSystems]);
    }

    private static int ComputePadWidth(int componentCount)
    {
        int maxIndex = componentCount - 1;
        return maxIndex < 10 ? 1 : maxIndex.ToString().Length;
    }
}
