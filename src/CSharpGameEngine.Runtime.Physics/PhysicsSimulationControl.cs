using System.Collections.Concurrent;

namespace CSharpGameEngine.Runtime.Physics;

internal static class PhysicsSimulationControl {
    private sealed class ControlState {
        internal bool Paused;
        internal int PendingSteps;
        internal bool ResetRequested;
    }

    private static readonly ConcurrentDictionary<Guid,ControlState> States=[];

    internal static void Register(Guid WorldId)=>States.TryAdd(WorldId,new ControlState());
    internal static void Unregister(Guid WorldId)=>States.TryRemove(WorldId,out _);

    internal static string Apply(string? Command,string? WorldId){
        if(!TryResolve(WorldId,out ControlState? State)){ return "world unavailable"; }
        lock(State!){
            switch(Command){
                case "simulate": State.Paused=false;State.PendingSteps=0;return "simulating";
                case "pause": State.Paused=true;return "paused";
                case "step": State.Paused=true;State.PendingSteps=checked(State.PendingSteps+1);return "step queued";
                case "reset": State.Paused=true;State.PendingSteps=0;State.ResetRequested=true;return "reset queued";
                default:return "unsupported command";
            }
        }
    }

    internal static bool ConsumeReset(Guid WorldId){
        ControlState State=States.GetOrAdd(WorldId,static _=>new ControlState());
        lock(State!){
            bool Result=State.ResetRequested;
            State.ResetRequested=false;
            return Result;
        }
    }

    internal static bool ShouldStep(Guid WorldId){
        ControlState State=States.GetOrAdd(WorldId,static _=>new ControlState());
        lock(State!){
            if(!State.Paused){ return true; }
            if(State.PendingSteps==0){ return false; }
            State.PendingSteps--;
            return true;
        }
    }

    private static bool TryResolve(string? WorldId,out ControlState? State){
        State=null;
        if(Guid.TryParse(WorldId,out Guid Parsed)){ return States.TryGetValue(Parsed,out State); }
        foreach(KeyValuePair<Guid,ControlState> Pair in States.OrderBy(static Pair=>Pair.Key)){
            State=Pair.Value;
            return true;
        }
        return false;
    }
}
