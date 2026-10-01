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

/// <summary>
/// Builds the compile-time access DAG for systems which explicitly opt into scheduling. Their order comes from
/// the same per-phase graph as execution order (ADR-0073): an unscheduled system in between still orders them.
/// </summary>
internal static class GameSystemScheduleAnalyzer {
    internal static GameSystemScheduleModel Build(GameSystemRegistry Registry){
        GameSystemResult[]All=[.. Registry.ValidSystems];
        List<int>[]AllDependents=GameSystemExecutionOrder.SamePhaseDependents(All);
        // A cycle is SYSGEN034 (GameSystemExecutionOrder.Validate); there is no schedule to build.
        if(HasCycle(AllDependents)){ return new([],[]); }
        int[]Scheduled=[.. Enumerable.Range(0,All.Length).Where(Index=>All[Index].IsScheduled)];
        Dictionary<int,int>ScheduledIndexByAllIndex=new();
        for(var Index=0;Index<Scheduled.Length;Index++){ ScheduledIndexByAllIndex[Scheduled[Index]]=Index; }
        List<int>[]AllPredecessors=new List<int>[All.Length];
        for(var Index=0;Index<All.Length;Index++){ AllPredecessors[Index]=[]; }
        for(var Index=0;Index<All.Length;Index++){
            foreach(var Next in AllDependents[Index]){ AllPredecessors[Next].Add(Index); }
        }
        var ComponentIndexByType=Registry.ComponentsByIndex.ToDictionary(
            static Component=>Component.TypeFullyQualifiedName,
            static Component=>Component.Index,
            StringComparer.Ordinal);
        AccessMask[]Accesses=[.. Scheduled.Select(Index=>BuildAccessMask(All[Index],ComponentIndexByType))];
        List<Diagnostic>Diagnostics=[];
        AddConflictDiagnostics(All,Scheduled,AllDependents,Accesses,Registry.ComponentsByIndex,Diagnostics);
        if(Diagnostics.Count>0){
            return new([], [.. Diagnostics]);
        }
        int[][]Predecessors=[.. Scheduled.Select(Index=>NearestScheduledPredecessors(Index,AllPredecessors,ScheduledIndexByAllIndex))];
        int[]Waves=new int[Scheduled.Length];
        bool[]WaveKnown=new bool[Scheduled.Length];
        List<GameSystemScheduleEntryModel>Entries=[];
        for(var Index=0;Index<Scheduled.Length;Index++){
            var Wave=ComputeWave(Index,Predecessors,Waves,WaveKnown);
            AccessMask Access=Accesses[Index];
            Entries.Add(new(
                Index,All[Scheduled[Index]],Wave,
                Access.ReadLow,Access.ReadHigh,Access.WriteLow,Access.WriteHigh,
                [.. Predecessors[Index]]));
        }
        return new([.. Entries],[]);
    }

    /// <summary>
    /// The scheduled systems that must finish before <paramref name="AllIndex"/>: each path back through
    /// unscheduled systems ends at the first scheduled one. Returned as schedule indices in ascending order.
    /// </summary>
    private static int[]NearestScheduledPredecessors(
        int AllIndex,List<int>[]AllPredecessors,IReadOnlyDictionary<int,int>ScheduledIndexByAllIndex){
        SortedSet<int>Result=[];
        HashSet<int>Visited=[];
        Stack<int>Pending=new(AllPredecessors[AllIndex]);
        while(Pending.Count>0){
            var Current=Pending.Pop();
            if(!Visited.Add(Current)){ continue; }
            if(ScheduledIndexByAllIndex.TryGetValue(Current,out var ScheduledIndex)){
                Result.Add(ScheduledIndex);
                continue;
            }
            foreach(var Previous in AllPredecessors[Current]){ Pending.Push(Previous); }
        }
        return [.. Result];
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

    /// <summary>
    /// Two scheduled systems of one phase that touch the same component, one of them writing, must be ordered
    /// by a declared path. The name order of unrelated systems is deterministic but is not a declared order.
    /// </summary>
    private static void AddConflictDiagnostics(
        GameSystemResult[]All,int[]Scheduled,List<int>[]AllDependents,AccessMask[]Accesses,
        ImmutableArray<GameSystemComponentInfo>Components,List<Diagnostic>Diagnostics){
        int[]Order=[.. Enumerable.Range(0,Scheduled.Length)
            .OrderBy(Index=>All[Scheduled[Index]].GlobalQualifiedName,StringComparer.Ordinal)];
        for(var LeftOrder=0;LeftOrder<Order.Length;LeftOrder++){
            var Left=Order[LeftOrder];
            for(var RightOrder=LeftOrder+1;RightOrder<Order.Length;RightOrder++){
                var Right=Order[RightOrder];
                GameSystemResult LeftSystem=All[Scheduled[Left]];
                GameSystemResult RightSystem=All[Scheduled[Right]];
                if(LeftSystem.Phase!=RightSystem.Phase){ continue; }
                if(HasPath(Scheduled[Left],Scheduled[Right],AllDependents)||HasPath(Scheduled[Right],Scheduled[Left],AllDependents)){ continue; }
                if(!TryFindConflict(Accesses[Left],Accesses[Right],Components,out var ComponentName)){ continue; }
                Diagnostics.Add(Diagnostic.Create(
                    GameSystemDiagnostics.AmbiguousScheduledAccessConflict,LeftSystem.DeclarationLocation,
                    LeftSystem.GlobalQualifiedName,RightSystem.GlobalQualifiedName,ComponentName));
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

    private static bool HasCycle(List<int>[]Dependents){
        byte[]States=new byte[Dependents.Length];
        for(var Index=0;Index<Dependents.Length;Index++){
            if(States[Index]==0&&Visit(Index)){ return true; }
        }
        return false;

        bool Visit(int Current){
            States[Current]=1;
            foreach(var Next in Dependents[Current]){
                if(States[Next]==1||(States[Next]==0&&Visit(Next))){ return true; }
            }
            States[Current]=2;
            return false;
        }
    }

    private static int ComputeWave(int Index,int[][]Predecessors,int[]Waves,bool[]Known){
        if(Known[Index]){ return Waves[Index]; }
        var Wave=0;
        foreach(var Predecessor in Predecessors[Index]){
            Wave=Math.Max(Wave,checked(ComputeWave(Predecessor,Predecessors,Waves,Known)+1));
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
