namespace CSharpGameEngine.Runtime.Simulation;

public readonly record struct SimulationDomainId(uint Value):IComparable<SimulationDomainId> {
    public int CompareTo(SimulationDomainId Other)=>this.Value.CompareTo(Other.Value);
}

public readonly record struct SimulationCommand<T>(
    ulong TargetTick,ulong SourceStableId,uint Sequence,T Payload);

public sealed class SimulationCommandBuffer<T> {
    private readonly List<SimulationCommand<T>> Items=[];
    public IReadOnlyList<SimulationCommand<T>> Pending=>this.Items;

    public void Enqueue(in SimulationCommand<T> Command)=>this.Items.Add(Command);

    public int Commit(ulong Tick,List<SimulationCommand<T>> Results){
        ArgumentNullException.ThrowIfNull(Results);
        int Start=Results.Count;
        if(this.Items.Count>1){
            this.Items.Sort(static(Left,Right)=>{
                int Result=Left.TargetTick.CompareTo(Right.TargetTick);
                if(Result!=0){ return Result; }
                Result=Left.SourceStableId.CompareTo(Right.SourceStableId);
                return Result!=0?Result:Left.Sequence.CompareTo(Right.Sequence);
            });
        }
        int RemoveCount=0;
        while(RemoveCount<this.Items.Count&&this.Items[RemoveCount].TargetTick<=Tick){
            if(this.Items[RemoveCount].TargetTick==Tick){
                Results.Add(this.Items[RemoveCount]);
            }
            RemoveCount++;
        }
        if(RemoveCount>0){
            this.Items.RemoveRange(0,RemoveCount);
        }
        return Results.Count-Start;
    }
}

public readonly record struct SimulationEvent<T>(
    ulong Tick,SimulationDomainId DomainId,ulong PairId,ushort Type,T Payload);

public static class SimulationEventOrder {
    public static void Sort<T>(List<SimulationEvent<T>> Events){
        ArgumentNullException.ThrowIfNull(Events);
        Events.Sort(static(Left,Right)=>{
            int Result=Left.Tick.CompareTo(Right.Tick);
            if(Result!=0){ return Result; }
            Result=Left.DomainId.CompareTo(Right.DomainId);
            if(Result!=0){ return Result; }
            Result=Left.PairId.CompareTo(Right.PairId);
            return Result!=0?Result:Left.Type.CompareTo(Right.Type);
        });
    }
}

public readonly record struct DomainBridgeMessage<T>(
    ulong SourceTick,SimulationDomainId SourceDomain,SimulationDomainId TargetDomain,T Payload){
    public ulong TargetTick=>checked(this.SourceTick+1);
}
