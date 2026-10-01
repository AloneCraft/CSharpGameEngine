using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// Systemの実行順序を決める唯一の場所(ADR-0073)。<see cref="GameSystemIndexAssigner"/>と
/// <see cref="GameSystemEntryPointEmitter"/>が同じ関数を呼ぶので、「生成されたループの実行順」と
/// 「エントリポイントの診断が指すSystem」が食い違わない。
///
/// <para>
/// 順序はPhase順(FrameInput→…→Presentation)、Phaseの中は<c>[RunsAfter]</c>/<c>[RunsBefore]</c>の辺を守る
/// トポロジカル順で、実行できるもののうち完全修飾名(Ordinal)が最も小さいものから並べる。起動シーンの合成
/// SystemはFrameInputの先頭に<c>startupOrder</c>→完全修飾名の順で置く。別のPhaseへの辺は、Phase順で満たされる
/// ものは何もせず、満たせないものは<see cref="Validate"/>がSYSGEN035にする。
/// </para>
/// </summary>
internal static class GameSystemExecutionOrder {
    private const string RunsAfterName="RunsAfter";
    private const string RunsBeforeName="RunsBefore";

    internal static List<GameSystemResult>Order(IEnumerable<GameSystemResult>Systems){
        List<GameSystemResult>Result=[];
        foreach(var Phase in Systems.GroupBy(static System=>System.Phase).OrderBy(static Group=>Group.Key)){
            Result.AddRange(Phase.Where(static System=>System.StartupOrder is not null)
                .OrderBy(static System=>System.StartupOrder)
                .ThenBy(static System=>System.ClassFullyQualifiedName,StringComparer.Ordinal));
            GameSystemResult[]Nodes=[.. Phase.Where(static System=>System.StartupOrder is null)];
            List<int>[]Dependents=SamePhaseDependents(Nodes);
            int[]Pending=new int[Nodes.Length];
            foreach(List<int>Edges in Dependents){
                foreach(var Next in Edges){ Pending[Next]++; }
            }
            SortedSet<int>Ready=new(Comparer<int>.Create((Left,Right)=>{
                var ByName=string.CompareOrdinal(Nodes[Left].ClassFullyQualifiedName,Nodes[Right].ClassFullyQualifiedName);
                return ByName!=0?ByName:Left.CompareTo(Right);
            }));
            for(var Index=0;Index<Nodes.Length;Index++){
                if(Pending[Index]==0){ Ready.Add(Index); }
            }
            bool[]Placed=new bool[Nodes.Length];
            while(Ready.Count>0){
                var Current=Ready.Min;
                Ready.Remove(Current);
                Placed[Current]=true;
                Result.Add(Nodes[Current]);
                foreach(var Next in Dependents[Current]){
                    if(--Pending[Next]==0){ Ready.Add(Next); }
                }
            }
            // A cycle (SYSGEN034) leaves nodes unplaced; they still get a deterministic place so the
            // other diagnostics and outputs stay stable while the build reports the error.
            Result.AddRange(Enumerable.Range(0,Nodes.Length).Where(Index=>!Placed[Index])
                .Select(Index=>Nodes[Index])
                .OrderBy(static System=>System.ClassFullyQualifiedName,StringComparer.Ordinal));
        }
        return Result;
    }

    /// <summary>
    /// <paramref name="Systems"/>の中の同じPhaseの辺を、前に動くSystemから後に動くSystemへの隣接リストで返す
    /// (添字は<paramref name="Systems"/>の位置、重複なし、後に動く側の完全修飾名順)。起動シーンの合成Systemへの
    /// 辺は含めない(常にFrameInputの先頭にいる)。
    /// </summary>
    internal static List<int>[]SamePhaseDependents(IReadOnlyList<GameSystemResult>Systems){
        Dictionary<string,int>IndexByName=new(StringComparer.Ordinal);
        for(var Index=0;Index<Systems.Count;Index++){
            IndexByName[Systems[Index].GlobalQualifiedName]=Index;
        }
        List<int>[]Dependents=new List<int>[Systems.Count];
        for(var Index=0;Index<Systems.Count;Index++){ Dependents[Index]=[]; }
        for(var Index=0;Index<Systems.Count;Index++){
            GameSystemResult System=Systems[Index];
            foreach(var Name in System.RunsAfterSystemFullyQualifiedNames){
                if(TryGetSamePhase(Name,System,Systems,IndexByName,out var Before)){ AddEdge(Before,Index); }
            }
            foreach(var Name in System.RunsBeforeSystemFullyQualifiedNames){
                if(TryGetSamePhase(Name,System,Systems,IndexByName,out var After)){ AddEdge(Index,After); }
            }
        }
        foreach(List<int>Edges in Dependents){
            Edges.Sort((Left,Right)=>string.CompareOrdinal(Systems[Left].ClassFullyQualifiedName,Systems[Right].ClassFullyQualifiedName));
        }
        return Dependents;

        void AddEdge(int From,int To){
            if(!Dependents[From].Contains(To)){ Dependents[From].Add(To); }
        }
    }

    /// <summary>
    /// 順序の宣言そのものの誤り: 対象がSystemでない(SYSGEN033)、Phaseの順と矛盾する(SYSGEN035)、同じPhaseの中で
    /// 循環する(SYSGEN034、Phaseごとに最初の1つ)。診断のあるSystemも対象に含める(存在の判定のため)。
    /// </summary>
    internal static ImmutableArray<Diagnostic>Validate(ImmutableArray<GameSystemResult>Systems){
        var Diagnostics=ImmutableArray.CreateBuilder<Diagnostic>();
        Dictionary<string,GameSystemResult>ByName=new(StringComparer.Ordinal);
        foreach(GameSystemResult System in Systems){ ByName[System.GlobalQualifiedName]=System; }
        GameSystemResult[]Sorted=[.. Systems.OrderBy(static System=>System.ClassFullyQualifiedName,StringComparer.Ordinal)];
        foreach(GameSystemResult System in Sorted){
            Check(System,System.RunsAfterSystemFullyQualifiedNames,RunsAfterName);
            Check(System,System.RunsBeforeSystemFullyQualifiedNames,RunsBeforeName);
        }
        foreach(var Phase in Sorted.Where(static System=>System.StartupOrder is null).GroupBy(static System=>System.Phase).OrderBy(static Group=>Group.Key)){
            GameSystemResult[]Nodes=[.. Phase];
            if(TryFindCycle(SamePhaseDependents(Nodes),out ImmutableArray<int>Cycle)){
                Diagnostics.Add(Diagnostic.Create(
                    GameSystemDiagnostics.SystemOrderCycle,Nodes[Cycle[0]].DeclarationLocation,
                    string.Join(" -> ",Cycle.Select(Index=>Nodes[Index].ClassFullyQualifiedName))));
            }
        }
        return Diagnostics.ToImmutable();

        void Check(GameSystemResult System,ImmutableArray<string>Names,string Attribute){
            foreach(var Name in Names.Distinct(StringComparer.Ordinal).OrderBy(static Name=>Name,StringComparer.Ordinal)){
                if(!ByName.TryGetValue(Name,out GameSystemResult Target)){
                    Diagnostics.Add(Diagnostic.Create(
                        GameSystemDiagnostics.SystemOrderTargetMustBeSystem,System.DeclarationLocation,
                        System.ClassFullyQualifiedName,Attribute,Name));
                    continue;
                }
                var Impossible=Attribute==RunsAfterName?Target.Phase>System.Phase:Target.Phase<System.Phase;
                if(Impossible){
                    Diagnostics.Add(Diagnostic.Create(
                        GameSystemDiagnostics.SystemOrderAgainstPhaseOrder,System.DeclarationLocation,
                        System.ClassFullyQualifiedName,Attribute,Target.ClassFullyQualifiedName,PhaseName(System.Phase),PhaseName(Target.Phase)));
                }
            }
        }
    }

    private static bool TryGetSamePhase(
        string Name,GameSystemResult System,IReadOnlyList<GameSystemResult>Systems,
        IReadOnlyDictionary<string,int>IndexByName,out int Index){
        return IndexByName.TryGetValue(Name,out Index)
            &&Systems[Index].Phase==System.Phase
            &&Systems[Index].StartupOrder is null
            &&System.StartupOrder is null;
    }

    private static bool TryFindCycle(List<int>[]Dependents,out ImmutableArray<int>Cycle){
        byte[]States=new byte[Dependents.Length];
        List<int>Stack=[];
        ImmutableArray<int>Found=[];
        for(var Index=0;Index<Dependents.Length;Index++){
            if(States[Index]==0&&Visit(Index)){
                Cycle=Found;
                return true;
            }
        }
        Cycle=[];
        return false;

        bool Visit(int Current){
            States[Current]=1;
            Stack.Add(Current);
            foreach(var Next in Dependents[Current]){
                if(States[Next]==0){
                    if(Visit(Next)){ return true; }
                } else if(States[Next]==1){
                    Found=[.. Stack.Skip(Stack.IndexOf(Next)),Next];
                    return true;
                }
            }
            Stack.RemoveAt(Stack.Count-1);
            States[Current]=2;
            return false;
        }
    }

    private static string PhaseName(int Phase)=>Phase switch {
        0=>"FrameInput",
        1=>"FixedSimulation",
        2=>"PhysicsSimulation",
        3=>"PostPhysics",
        4=>"Presentation",
        _=>Phase.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
