using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.CodeAnalysis;

internal readonly struct GameSystemScheduleEntryModel(
        int SystemIndex,GameSystemResult System,int Wave,
        ulong ReadLow,ulong ReadHigh,ulong WriteLow,ulong WriteHigh,
        ImmutableArray<int>Predecessors) {
    internal readonly int SystemIndex=SystemIndex;
    internal readonly GameSystemResult System=System;
    internal readonly int Wave=Wave;
    internal readonly ulong ReadLow=ReadLow;
    internal readonly ulong ReadHigh=ReadHigh;
    internal readonly ulong WriteLow=WriteLow;
    internal readonly ulong WriteHigh=WriteHigh;
    internal readonly ImmutableArray<int>Predecessors=Predecessors;
}

internal readonly struct GameSystemScheduleModel(
        ImmutableArray<GameSystemScheduleEntryModel>Entries,
        ImmutableArray<Diagnostic>Diagnostics) {
    internal readonly ImmutableArray<GameSystemScheduleEntryModel>Entries=Entries;
    internal readonly ImmutableArray<Diagnostic>Diagnostics=Diagnostics;
    internal bool CanEmit=>this.Diagnostics.IsEmpty;
}

/// <summary>Builds the compile-time access DAG for systems which explicitly opt into scheduling.</summary>
internal static class GameSystemScheduleAnalyzer {
    internal static GameSystemScheduleModel Build(GameSystemRegistry Registry){
        GameSystemResult[]Systems=[.. Registry.ValidSystems.Where(static System=>System.IsScheduled)];
        Dictionary<string,int>IndexByName=new(StringComparer.Ordinal);
        for(var Index=0;Index<Systems.Length;Index++){
            IndexByName[Systems[Index].GlobalQualifiedName]=Index;
        }

        List<int>[]Predecessors=CreateLists(Systems.Length);
        List<int>[]Dependents=CreateLists(Systems.Length);
        List<Diagnostic>Diagnostics=[];

        int[]DiagnosticOrder=[.. Enumerable.Range(0,Systems.Length)
            .OrderBy(Index=>Systems[Index].GlobalQualifiedName,StringComparer.Ordinal)];
        foreach(var SystemIndex in DiagnosticOrder){
            GameSystemResult System=Systems[SystemIndex];
            foreach(var DependencyName in System.RunsAfterSystemFullyQualifiedNames
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static Name=>Name,StringComparer.Ordinal)){
                if(!IndexByName.TryGetValue(DependencyName,out var DependencyIndex)){
                    Diagnostics.Add(Diagnostic.Create(
                        GameSystemDiagnostics.ScheduledDependencyMustExist,System.DeclarationLocation,
                        System.GlobalQualifiedName,DependencyName));
                    continue;
                }
                if(System.Phase<Systems[DependencyIndex].Phase){
                    Diagnostics.Add(Diagnostic.Create(
                        GameSystemDiagnostics.ScheduledDependencyCycle,System.DeclarationLocation,
                        $"{System.GlobalQualifiedName} -> {DependencyName} -> {System.GlobalQualifiedName} (phase order)"));
                    continue;
                }
                Predecessors[SystemIndex].Add(DependencyIndex);
                Dependents[DependencyIndex].Add(SystemIndex);
            }
        }

        SortEdges(Predecessors,Systems);
        SortEdges(Dependents,Systems);
        if(TryFindCycle(Dependents,Systems,out var Cycle)){
            GameSystemResult System=Systems[Cycle[0]];
            Diagnostics.Add(Diagnostic.Create(
                GameSystemDiagnostics.ScheduledDependencyCycle,System.DeclarationLocation,
                string.Join(" -> ",Cycle.Select(Index=>Systems[Index].GlobalQualifiedName))));
        }

        var ComponentIndexByType=Registry.ComponentsByIndex.ToDictionary(
            static Component=>Component.TypeFullyQualifiedName,
            static Component=>Component.Index,
            StringComparer.Ordinal);
        AccessMask[]Accesses=new AccessMask[Systems.Length];
        for(var Index=0;Index<Systems.Length;Index++){
            Accesses[Index]=BuildAccessMask(Systems[Index],ComponentIndexByType);
        }

        if(!Diagnostics.Any(static Diagnostic=>Diagnostic.Id==GameSystemDiagnostics.ScheduledDependencyCycle.Id)){
            AddConflictDiagnostics(Systems,Dependents,Accesses,Registry.ComponentsByIndex,Diagnostics);
        }

        if(Diagnostics.Count>0){
            return new([], [.. Diagnostics]);
        }

        int[]Waves=new int[Systems.Length];
        bool[]WaveKnown=new bool[Systems.Length];
        List<GameSystemScheduleEntryModel>Entries=[];
        for(var Index=0;Index<Systems.Length;Index++){
            var Wave=ComputeWave(Index,Systems,Predecessors,Waves,WaveKnown);
            AccessMask Access=Accesses[Index];
            Entries.Add(new(
                Index,Systems[Index],Wave,
                Access.ReadLow,Access.ReadHigh,Access.WriteLow,Access.WriteHigh,
                [.. Predecessors[Index]]));
        }
        return new([.. Entries],[]);
    }

    private static List<int>[]CreateLists(int Count){
        List<int>[]Result=new List<int>[Count];
        for(var Index=0;Index<Count;Index++){ Result[Index]=[]; }
        return Result;
    }

    private static void SortEdges(List<int>[]Edges,GameSystemResult[]Systems){
        foreach(List<int>EdgeList in Edges){
            EdgeList.Sort((Left,Right)=>string.CompareOrdinal(
                Systems[Left].GlobalQualifiedName,Systems[Right].GlobalQualifiedName));
        }
    }

    private static AccessMask BuildAccessMask(
        GameSystemResult System,IReadOnlyDictionary<string,int>ComponentIndexByType){
        ulong ReadLow=0;
        ulong ReadHigh=0;
        ulong WriteLow=0;
        ulong WriteHigh=0;
        foreach(GameSystemMethodModel Method in System.Methods){
            if(Method.Role!=GameSystemMethodRole.Dispatch){ continue; }
            foreach(GameSystemComponentParameterModel Component in Method.Components){
                var Index=ComponentIndexByType[Component.TypeFullyQualifiedName];
                if(Component.IsReadOnly){
                    if(Index<64){ ReadLow|=1UL<<Index; }
                    else { ReadHigh|=1UL<<(Index-64); }
                } else {
                    if(Index<64){ WriteLow|=1UL<<Index; }
                    else { WriteHigh|=1UL<<(Index-64); }
                }
            }
        }
        return new(ReadLow,ReadHigh,WriteLow,WriteHigh);
    }

    private static void AddConflictDiagnostics(
        GameSystemResult[]Systems,List<int>[]Dependents,AccessMask[]Accesses,
        ImmutableArray<GameSystemComponentInfo>Components,List<Diagnostic>Diagnostics){
        int[]Order=[.. Enumerable.Range(0,Systems.Length)
            .OrderBy(Index=>Systems[Index].GlobalQualifiedName,StringComparer.Ordinal)];
        for(var LeftOrder=0;LeftOrder<Order.Length;LeftOrder++){
            var Left=Order[LeftOrder];
            for(var RightOrder=LeftOrder+1;RightOrder<Order.Length;RightOrder++){
                var Right=Order[RightOrder];
                if(Systems[Left].Phase!=Systems[Right].Phase){ continue; }
                if(HasPath(Left,Right,Dependents)||HasPath(Right,Left,Dependents)){ continue; }
                if(!TryFindConflict(Accesses[Left],Accesses[Right],Components,out var ComponentName)){ continue; }
                Diagnostics.Add(Diagnostic.Create(
                    GameSystemDiagnostics.AmbiguousScheduledAccessConflict,Systems[Left].DeclarationLocation,
                    Systems[Left].GlobalQualifiedName,Systems[Right].GlobalQualifiedName,ComponentName));
            }
        }
    }

    private static bool TryFindConflict(
        AccessMask Left,AccessMask Right,ImmutableArray<GameSystemComponentInfo>Components,
        out string ComponentName){
        var Low=(Left.WriteLow&(Right.ReadLow|Right.WriteLow))
            |(Right.WriteLow&(Left.ReadLow|Left.WriteLow));
        var High=(Left.WriteHigh&(Right.ReadHigh|Right.WriteHigh))
            |(Right.WriteHigh&(Left.ReadHigh|Left.WriteHigh));
        for(var Index=0;Index<Components.Length;Index++){
            var Conflicts=Index<64?(Low&(1UL<<Index))!=0:(High&(1UL<<(Index-64)))!=0;
            if(Conflicts){
                ComponentName=Components[Index].TypeFullyQualifiedName;
                return true;
            }
        }
        ComponentName=string.Empty;
        return false;
    }

    private static bool HasPath(int Start,int Target,List<int>[]Dependents){
        bool[]Visited=new bool[Dependents.Length];
        Stack<int>Pending=new();
        Pending.Push(Start);
        while(Pending.Count>0){
            var Current=Pending.Pop();
            if(!Visited[Current]){
                Visited[Current]=true;
                foreach(var Next in Dependents[Current]){
                    if(Next==Target){ return true; }
                    Pending.Push(Next);
                }
            }
        }
        return false;
    }

    private static bool TryFindCycle(
        List<int>[]Dependents,GameSystemResult[]Systems,out ImmutableArray<int>Cycle){
        byte[]States=new byte[Systems.Length];
        List<int>Stack=[];
        ImmutableArray<int>FoundCycle=[];
        int[]Order=[.. Enumerable.Range(0,Systems.Length)
            .OrderBy(Index=>Systems[Index].GlobalQualifiedName,StringComparer.Ordinal)];
        foreach(var Index in Order){
            if(States[Index]==0&&Visit(Index)){
                Cycle=FoundCycle;
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
                    var Start=Stack.IndexOf(Next);
                    FoundCycle=[.. Stack.Skip(Start),Next];
                    return true;
                }
            }
            Stack.RemoveAt(Stack.Count-1);
            States[Current]=2;
            return false;
        }
    }

    private static int ComputeWave(
        int Index,GameSystemResult[]Systems,List<int>[]Predecessors,int[]Waves,bool[]Known){
        if(Known[Index]){ return Waves[Index]; }
        var Wave=0;
        foreach(var Predecessor in Predecessors[Index]){
            if(Systems[Predecessor].Phase==Systems[Index].Phase){
                Wave=Math.Max(Wave,checked(ComputeWave(Predecessor,Systems,Predecessors,Waves,Known)+1));
            }
        }
        Known[Index]=true;
        Waves[Index]=Wave;
        return Wave;
    }

    private readonly struct AccessMask(
        ulong ReadLow,ulong ReadHigh,ulong WriteLow,ulong WriteHigh) {
        internal readonly ulong ReadLow=ReadLow;
        internal readonly ulong ReadHigh=ReadHigh;
        internal readonly ulong WriteLow=WriteLow;
        internal readonly ulong WriteHigh=WriteHigh;
    }
}

internal static class GameSystemScheduleEmitter {
    internal static void Emit(SourceProductionContext Context,GameSystemScheduleModel Schedule){
        if(!Schedule.CanEmit){ return; }
        List<string>Lines=[
            "// <auto-generated/>",
            "#nullable enable",
            "namespace CSharpGameEngine.Generated;",
            "internal readonly struct GeneratedSystemScheduleEntry {",
            "    internal GeneratedSystemScheduleEntry(int SystemIndex, int Phase, int Wave, int PredecessorStart, int PredecessorCount, ulong ReadLow, ulong ReadHigh, ulong WriteLow, ulong WriteHigh) {",
            "        this.SystemIndex = SystemIndex; this.Phase = Phase; this.Wave = Wave;",
            "        this.PredecessorStart = PredecessorStart; this.PredecessorCount = PredecessorCount;",
            "        this.ReadLow = ReadLow; this.ReadHigh = ReadHigh; this.WriteLow = WriteLow; this.WriteHigh = WriteHigh;",
            "    }",
            "    internal readonly int SystemIndex;",
            "    internal readonly int Phase;",
            "    internal readonly int Wave;",
            "    internal readonly int PredecessorStart;",
            "    internal readonly int PredecessorCount;",
            "    internal readonly ulong ReadLow;",
            "    internal readonly ulong ReadHigh;",
            "    internal readonly ulong WriteLow;",
            "    internal readonly ulong WriteHigh;",
            "}",
            "internal static class GeneratedSystemSchedule {",
            "    private static readonly GeneratedSystemScheduleEntry[] ScheduleEntries = [",
        ];
        var PredecessorStart=0;
        foreach(GameSystemScheduleEntryModel Entry in Schedule.Entries){
            Lines.Add($"        new({Entry.SystemIndex}, {Entry.System.Phase}, {Entry.Wave}, {PredecessorStart}, {Entry.Predecessors.Length}, 0x{Entry.ReadLow:X16}UL, 0x{Entry.ReadHigh:X16}UL, 0x{Entry.WriteLow:X16}UL, 0x{Entry.WriteHigh:X16}UL),");
            PredecessorStart+=Entry.Predecessors.Length;
        }
        Lines.Add("    ];");
        Lines.Add("    private static readonly int[] PredecessorIndices = [");
        foreach(GameSystemScheduleEntryModel Entry in Schedule.Entries){
            foreach(var Predecessor in Entry.Predecessors){ Lines.Add($"        {Predecessor},"); }
        }
        Lines.Add("    ];");
        Lines.Add("    internal static global::System.ReadOnlySpan<GeneratedSystemScheduleEntry> Entries => ScheduleEntries;");
        Lines.Add("    internal static global::System.ReadOnlySpan<int> Predecessors => PredecessorIndices;");
        Lines.Add("    internal static string GetSystemName(int SystemIndex) => SystemIndex switch {");
        foreach(GameSystemScheduleEntryModel Entry in Schedule.Entries){
            Lines.Add($"        {Entry.SystemIndex} => \"{Entry.System.GlobalQualifiedName}\",");
        }
        Lines.Add("        _ => throw new global::System.ArgumentOutOfRangeException(nameof(SystemIndex)),");
        Lines.Add("    };");
        Lines.Add("}");
        Context.AddSource("GameSystem.Schedule.g.cs",SourceText.From(
            string.Join(((char)10).ToString(),Lines),Encoding.UTF8));
    }
}
