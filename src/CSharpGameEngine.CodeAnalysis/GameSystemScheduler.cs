using System.Collections.Immutable;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// 純関数: 複数<c>[Update]</c>メソッドのRead/Writeアクセスから、決定的な実行順序を計算する。
/// エッジ規則: AがCをWrite・BがCをRead(A≠B)なら A→B(生産者が先、唯一の意味的に確実な制約)。
/// A・BがともにCをWriteならbaseline順の小さい方→大きい方(決定的な全順序化のためのタイブレーク)。
/// AがCをRead・BがCをWriteならエッジを張らない(「書き込み前を見たいか後を見たいか」は
/// 推論不能で、張ると容易に循環するため)。baseline順は
/// (Order, ClassFullyQualifiedName ordinal, MemberIndex, MethodName ordinal)で、ファイルパスは
/// 使わない(マシン依存でバイト同一出力が壊れるため)。
/// </summary>
internal static class GameSystemScheduler
{
    internal readonly struct ScheduleResult
    {
        internal readonly ImmutableArray<int> BaselineOrder;
        internal readonly ImmutableArray<int> TopologicalOrder;
        internal readonly bool HasCycle;
        internal readonly ImmutableArray<int> CycleNodeIndices;

        internal ScheduleResult(ImmutableArray<int> baselineOrder, ImmutableArray<int> topologicalOrder, bool hasCycle, ImmutableArray<int> cycleNodeIndices)
        {
            this.BaselineOrder = baselineOrder;
            this.TopologicalOrder = topologicalOrder;
            this.HasCycle = hasCycle;
            this.CycleNodeIndices = cycleNodeIndices;
        }
    }

    internal static ScheduleResult Schedule(IReadOnlyList<SystemMethodModel> methods, IReadOnlyList<string> classFullyQualifiedNames)
    {
        int n = methods.Count;
        int[] baselineIndices = [.. Enumerable.Range(0, n)];
        Array.Sort(baselineIndices, (a, b) => CompareBaseline(methods[a], classFullyQualifiedNames[a], methods[b], classFullyQualifiedNames[b]));

        int[] baselineRank = new int[n];
        for (int position = 0; position < n; position++)
        {
            baselineRank[baselineIndices[position]] = position;
        }

        List<HashSet<int>> successors = [];
        for (int i = 0; i < n; i++)
        {
            successors.Add([]);
        }

        int[] inDegree = new int[n];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i == j)
                {
                    continue;
                }

                bool edge = HasWriteBeforeReadEdge(methods[i], methods[j])
                    || (HasSharedWrite(methods[i], methods[j]) && baselineRank[i] < baselineRank[j]);

                if (edge && successors[i].Add(j))
                {
                    inDegree[j]++;
                }
            }
        }

        SortedSet<int> available = new(Comparer<int>.Create((a, b) => baselineRank[a].CompareTo(baselineRank[b])));

        for (int i = 0; i < n; i++)
        {
            if (inDegree[i] == 0)
            {
                available.Add(i);
            }
        }

        List<int> topologicalOrder = [];
        int[] remainingInDegree = (int[])inDegree.Clone();

        while (available.Count > 0)
        {
            int current = available.Min;
            available.Remove(current);
            topologicalOrder.Add(current);

            foreach (int next in successors[current])
            {
                remainingInDegree[next]--;

                if (remainingInDegree[next] == 0)
                {
                    available.Add(next);
                }
            }
        }

        if (topologicalOrder.Count == n)
        {
            return new ScheduleResult([.. baselineIndices], [.. topologicalOrder], false, ImmutableArray<int>.Empty);
        }

        List<int> cycleNodes = [];

        foreach (int i in baselineIndices)
        {
            if (remainingInDegree[i] > 0)
            {
                cycleNodes.Add(i);
            }
        }

        return new ScheduleResult([.. baselineIndices], ImmutableArray<int>.Empty, true, [.. cycleNodes]);
    }

    private static bool HasWriteBeforeReadEdge(SystemMethodModel producer, SystemMethodModel consumer)
    {
        foreach (string writeType in producer.WriteComponentTypes)
        {
            if (Contains(consumer.ReadComponentTypes, writeType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSharedWrite(SystemMethodModel a, SystemMethodModel b)
    {
        foreach (string writeType in a.WriteComponentTypes)
        {
            if (Contains(b.WriteComponentTypes, writeType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(ImmutableArray<string> values, string value)
    {
        foreach (string candidate in values)
        {
            if (string.Equals(candidate, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int CompareBaseline(SystemMethodModel methodA, string classA, SystemMethodModel methodB, string classB)
    {
        int orderCompare = methodA.Order.CompareTo(methodB.Order);

        if (orderCompare != 0)
        {
            return orderCompare;
        }

        int classCompare = string.CompareOrdinal(classA, classB);

        if (classCompare != 0)
        {
            return classCompare;
        }

        int memberCompare = methodA.MemberIndex.CompareTo(methodB.MemberIndex);

        if (memberCompare != 0)
        {
            return memberCompare;
        }

        return string.CompareOrdinal(methodA.MethodName, methodB.MethodName);
    }
}
