using System.Text;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Simulation;

namespace CSharpGameEngine.Runtime.Physics;

public readonly record struct PhysicsCacheFrame(ulong Tick,IReadOnlyList<PhysicsBodyState> Bodies,IReadOnlyList<PhysicsEvent> Events);

public static class PhysicsCacheSerializer {
    public const int SchemaVersion=2;
    public const int MaximumFrameCount=10_000_000;
    internal const int MaximumBodiesPerFrame=1_000_000;
    internal const int MaximumEventsPerFrame=4_000_000;

    public static void Write(Stream Output,in SimulationCacheIdentity Identity,IReadOnlyList<PhysicsCacheFrame> Frames){
        ArgumentNullException.ThrowIfNull(Output);
        ArgumentNullException.ThrowIfNull(Frames);
        using PhysicsCacheStreamWriter Writer=new(Output,Identity,Frames.Count);
        foreach(PhysicsCacheFrame Frame in Frames){ Writer.WriteFrame(Frame); }
        Writer.Complete();
    }

    public static bool TryRead(Stream Input,in SimulationCacheIdentity Expected,List<PhysicsCacheFrame> Frames,out string Error){
        ArgumentNullException.ThrowIfNull(Input);
        ArgumentNullException.ThrowIfNull(Frames);
        int Start=Frames.Count;
        Error=string.Empty;
        try {
            if(!SimulationCacheHeader.TryRead(Input,out SimulationCacheIdentity Actual)){
                Error="Invalid simulation cache header.";
                return false;
            }
            if(Actual!=Expected){
                Error="Simulation cache identity does not match the current logical profile, tick rate, schema, or dependencies.";
                return false;
            }
            using BinaryReader Reader=new(Input,Encoding.UTF8,true);
            int FrameCount=Reader.ReadInt32();
            if(FrameCount is <0 or >MaximumFrameCount){ throw new InvalidDataException(); }
            ulong PreviousTick=0;
            for(int FrameIndex=0;FrameIndex<FrameCount;FrameIndex++){
                ulong Tick=Reader.ReadUInt64();
                if(FrameIndex>0&&Tick<=PreviousTick){ throw new InvalidDataException(); }
                PreviousTick=Tick;
                int BodyCount=ReadCount(Reader,MaximumBodiesPerFrame);
                List<PhysicsBodyState> Bodies=new(BodyCount);
                ulong PreviousStableId=0;
                for(int BodyIndex=0;BodyIndex<BodyCount;BodyIndex++){
                    PhysicsBodyState Body=ReadBody(Reader);
                    if(!IsCanonicalBody(Body)||BodyIndex>0&&Body.StableId<=PreviousStableId){ throw new InvalidDataException(); }
                    PreviousStableId=Body.StableId;
                    Bodies.Add(Body);
                }
                int EventCount=ReadCount(Reader,MaximumEventsPerFrame);
                List<PhysicsEvent> Events=new(EventCount);
                ulong PreviousPairId=0;
                PhysicsEventType PreviousType=default;
                for(int EventIndex=0;EventIndex<EventCount;EventIndex++){
                    PhysicsEvent Event=ReadEvent(Reader);
                    if(Event.Tick!=Tick||!Enum.IsDefined(Event.Type)||!Enum.IsDefined(Event.Fault)
                        ||EventIndex>0&&(Event.PairId<PreviousPairId
                        ||Event.PairId==PreviousPairId&&Event.Type<PreviousType)){ throw new InvalidDataException(); }
                    PreviousPairId=Event.PairId;
                    PreviousType=Event.Type;
                    Events.Add(Event);
                }
                Frames.Add(new PhysicsCacheFrame(Tick,Bodies,Events));
            }
            if(Input.ReadByte()!=-1){ throw new InvalidDataException(); }
            return true;
        }
        catch(Exception Exception)when(Exception is EndOfStreamException or IOException or InvalidDataException){
            Frames.RemoveRange(Start,Frames.Count-Start);
            Error="Simulation cache is truncated or malformed.";
            return false;
        }
    }

    internal static bool TryInspect(
        Stream Input,in SimulationCacheIdentity Expected,
        out long FramesOffset,out int FrameCount,out int MaximumBodyCount,out int MaximumEventCount,out string Error){
        ArgumentNullException.ThrowIfNull(Input);
        FramesOffset=0;
        FrameCount=0;
        MaximumBodyCount=0;
        MaximumEventCount=0;
        Error=string.Empty;
        try {
            if(!SimulationCacheHeader.TryRead(Input,out SimulationCacheIdentity Actual)){
                Error="Invalid simulation cache header.";
                return false;
            }
            if(Actual!=Expected){
                Error="Simulation cache identity does not match the current logical profile, tick rate, schema, or dependencies.";
                return false;
            }
            using BinaryReader Reader=new(Input,Encoding.UTF8,true);
            FrameCount=Reader.ReadInt32();
            if(FrameCount is <0 or >MaximumFrameCount){ throw new InvalidDataException(); }
            FramesOffset=Input.Position;
            ulong PreviousTick=0;
            for(int FrameIndex=0;FrameIndex<FrameCount;FrameIndex++){
                ulong Tick=Reader.ReadUInt64();
                if(FrameIndex>0&&Tick<=PreviousTick){ throw new InvalidDataException(); }
                PreviousTick=Tick;
                int BodyCount=ReadCount(Reader,MaximumBodiesPerFrame);
                MaximumBodyCount=Math.Max(MaximumBodyCount,BodyCount);
                ulong PreviousStableId=0;
                for(int BodyIndex=0;BodyIndex<BodyCount;BodyIndex++){
                    PhysicsBodyState Body=ReadBody(Reader);
                    if(!IsCanonicalBody(Body)||BodyIndex>0&&Body.StableId<=PreviousStableId){ throw new InvalidDataException(); }
                    PreviousStableId=Body.StableId;
                }
                int EventCount=ReadCount(Reader,MaximumEventsPerFrame);
                MaximumEventCount=Math.Max(MaximumEventCount,EventCount);
                ulong PreviousPairId=0;
                PhysicsEventType PreviousType=default;
                for(int EventIndex=0;EventIndex<EventCount;EventIndex++){
                    PhysicsEvent Event=ReadEvent(Reader);
                    if(Event.Tick!=Tick||!Enum.IsDefined(Event.Type)||!Enum.IsDefined(Event.Fault)
                        ||EventIndex>0&&(Event.PairId<PreviousPairId
                            ||Event.PairId==PreviousPairId&&Event.Type<PreviousType)){ throw new InvalidDataException(); }
                    PreviousPairId=Event.PairId;
                    PreviousType=Event.Type;
                }
            }
            if(Input.ReadByte()!=-1){ throw new InvalidDataException(); }
            Input.Position=FramesOffset;
            return true;
        }
        catch(Exception Exception)when(Exception is EndOfStreamException or IOException or InvalidDataException){
            Error="Simulation cache is truncated or malformed.";
            return false;
        }
    }

    internal static PhysicsCacheFrame ReadNext(
        BinaryReader Reader,List<PhysicsBodyState> Bodies,List<PhysicsEvent> Events){
        Bodies.Clear();
        Events.Clear();
        ulong Tick=Reader.ReadUInt64();
        int BodyCount=ReadCount(Reader,MaximumBodiesPerFrame);
        for(int BodyIndex=0;BodyIndex<BodyCount;BodyIndex++){ Bodies.Add(ReadBody(Reader)); }
        int EventCount=ReadCount(Reader,MaximumEventsPerFrame);
        for(int EventIndex=0;EventIndex<EventCount;EventIndex++){ Events.Add(ReadEvent(Reader)); }
        return new PhysicsCacheFrame(Tick,Bodies,Events);
    }

    private static int ReadCount(BinaryReader Reader,int Maximum){
        int Count=Reader.ReadInt32();
        if(Count<0||Count>Maximum){ throw new InvalidDataException(); }
        return Count;
    }
    internal static bool IsCanonicalBody(in PhysicsBodyState Body)=>
        Body.StableId!=0&&Body.Entity.Index>=0&&Body.Entity.Generation!=0
        &&(!Body.MotionType.HasValue||Enum.IsDefined(Body.MotionType.Value))
        &&Body.Rotation.TryNormalize(out LogicalQuaternion Normalized)&&Normalized==Body.Rotation;
    internal static void WriteBody(BinaryWriter Writer,in PhysicsBodyState Body){
        WriteEntity(Writer,Body.Entity);Writer.Write(Body.StableId);Writer.Write(Body.MotionType.HasValue);
        if(Body.MotionType.HasValue){ Writer.Write((byte)Body.MotionType.Value); }
        WriteVector(Writer,Body.Position);WriteQuaternion(Writer,Body.Rotation);
        WriteVector(Writer,Body.Velocity.Linear);WriteVector(Writer,Body.Velocity.Angular);
        Writer.Write(Body.IsSleeping);Writer.Write(Body.SleepTicks);
    }
    private static PhysicsBodyState ReadBody(BinaryReader Reader){
        Entity Entity=ReadEntity(Reader);ulong StableId=Reader.ReadUInt64();
        PhysicsMotionType? MotionType=Reader.ReadBoolean()?(PhysicsMotionType)Reader.ReadByte():null;
        LogicalVector3 Position=ReadVector(Reader);LogicalQuaternion Rotation=ReadQuaternion(Reader);
        PhysicsVelocity Velocity=new() { Linear=ReadVector(Reader),Angular=ReadVector(Reader) };
        return new PhysicsBodyState(Entity,StableId,MotionType,Position,Rotation,Velocity,Reader.ReadBoolean(),Reader.ReadUInt16());
    }
    internal static void WriteEvent(BinaryWriter Writer,in PhysicsEvent Event){
        Writer.Write(Event.Tick);Writer.Write(Event.PairId);Writer.Write((ushort)Event.Type);
        WriteEntity(Writer,Event.First);WriteEntity(Writer,Event.Second);WriteScalar(Writer,Event.Impulse);Writer.Write((ushort)Event.Fault);
    }
    private static PhysicsEvent ReadEvent(BinaryReader Reader)=>new(
        Reader.ReadUInt64(),Reader.ReadUInt64(),(PhysicsEventType)Reader.ReadUInt16(),
        ReadEntity(Reader),ReadEntity(Reader),ReadScalar(Reader),(PhysicsFaultType)Reader.ReadUInt16());
    private static void WriteEntity(BinaryWriter Writer,Entity Entity){ Writer.Write(Entity.Index);Writer.Write(Entity.Generation); }
    private static Entity ReadEntity(BinaryReader Reader)=>new(Reader.ReadInt32(),Reader.ReadUInt32());
    private static void WriteScalar(BinaryWriter Writer,LogicalScalar Value){ Span<byte>Bytes=stackalloc byte[LogicalScalar.SerializedSize];Value.WriteLittleEndian(Bytes);Writer.Write(Bytes); }
    private static LogicalScalar ReadScalar(BinaryReader Reader){ Span<byte>Bytes=stackalloc byte[LogicalScalar.SerializedSize];Reader.ReadExactly(Bytes);return LogicalScalar.TryReadLittleEndian(Bytes,out var Value)?Value:throw new InvalidDataException(); }
    private static void WriteVector(BinaryWriter Writer,in LogicalVector3 Value){ WriteScalar(Writer,Value.R);WriteScalar(Writer,Value.U);WriteScalar(Writer,Value.F); }
    private static LogicalVector3 ReadVector(BinaryReader Reader)=>new(ReadScalar(Reader),ReadScalar(Reader),ReadScalar(Reader));
    private static void WriteQuaternion(BinaryWriter Writer,in LogicalQuaternion Value){ WriteScalar(Writer,Value.R);WriteScalar(Writer,Value.U);WriteScalar(Writer,Value.F);WriteScalar(Writer,Value.W); }
    private static LogicalQuaternion ReadQuaternion(BinaryReader Reader)=>new(ReadScalar(Reader),ReadScalar(Reader),ReadScalar(Reader),ReadScalar(Reader));
}

/// <summary>
/// Writes one canonical physics frame at a time. The declared frame count is part of the header,
/// so an incomplete bake cannot be mistaken for a complete cache.
/// </summary>
public sealed class PhysicsCacheStreamWriter:IDisposable {
    private readonly BinaryWriter Writer;
    private readonly List<PhysicsBodyState>Bodies=[];
    private readonly List<PhysicsEvent>Events=[];
    private readonly int ExpectedFrameCount;
    private int WrittenFrameCount;
    private ulong PreviousTick;
    private bool Completed;
    private bool Disposed;

    public PhysicsCacheStreamWriter(
        Stream Output,in SimulationCacheIdentity Identity,int ExpectedFrameCount){
        ArgumentNullException.ThrowIfNull(Output);
        if(!Output.CanWrite){ throw new ArgumentException("Physics cache output must be writable.",nameof(Output)); }
        if(ExpectedFrameCount is <0 or >PhysicsCacheSerializer.MaximumFrameCount){
            throw new ArgumentOutOfRangeException(nameof(ExpectedFrameCount));
        }
        this.ExpectedFrameCount=ExpectedFrameCount;
        SimulationCacheHeader.Write(Output,Identity);
        this.Writer=new BinaryWriter(Output,Encoding.UTF8,true);
        this.Writer.Write(ExpectedFrameCount);
    }

    public int FramesWritten=>this.WrittenFrameCount;

    public void WriteFrame(in PhysicsCacheFrame Frame){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if(this.Completed){ throw new InvalidOperationException("The physics cache writer is already complete."); }
        if(this.WrittenFrameCount>=this.ExpectedFrameCount){
            throw new InvalidOperationException("The physics cache already contains its declared frame count.");
        }
        if(this.WrittenFrameCount>0&&Frame.Tick<=this.PreviousTick){
            throw new ArgumentException("Physics cache frames must have strictly increasing ticks.",nameof(Frame));
        }
        if(Frame.Bodies.Count>PhysicsCacheSerializer.MaximumBodiesPerFrame
            ||Frame.Events.Count>PhysicsCacheSerializer.MaximumEventsPerFrame){
            throw new ArgumentException("Physics cache frame exceeds its bounded body or event count.",nameof(Frame));
        }

        this.Bodies.Clear();
        foreach(PhysicsBodyState Body in Frame.Bodies){
            if(!PhysicsCacheSerializer.IsCanonicalBody(Body)){
                throw new ArgumentException("Physics cache frame contains an invalid body state.",nameof(Frame));
            }
            this.Bodies.Add(Body);
        }
        this.Bodies.Sort(static(Left,Right)=>Left.StableId.CompareTo(Right.StableId));
        for(int Index=1;Index<this.Bodies.Count;Index++){
            if(this.Bodies[Index-1].StableId==this.Bodies[Index].StableId){
                throw new ArgumentException("Physics cache frame contains a duplicate body state.",nameof(Frame));
            }
        }

        this.Events.Clear();
        foreach(PhysicsEvent Event in Frame.Events){
            if(Event.Tick!=Frame.Tick||!Enum.IsDefined(Event.Type)||!Enum.IsDefined(Event.Fault)){
                throw new ArgumentException("Physics cache events must be valid and match their frame tick.",nameof(Frame));
            }
            this.Events.Add(Event);
        }
        this.Events.Sort(static(Left,Right)=>{
            int Pair=Left.PairId.CompareTo(Right.PairId);
            return Pair!=0?Pair:Left.Type.CompareTo(Right.Type);
        });

        this.Writer.Write(Frame.Tick);
        this.Writer.Write(this.Bodies.Count);
        foreach(PhysicsBodyState Body in this.Bodies){ PhysicsCacheSerializer.WriteBody(this.Writer,Body); }
        this.Writer.Write(this.Events.Count);
        foreach(PhysicsEvent Event in this.Events){ PhysicsCacheSerializer.WriteEvent(this.Writer,Event); }
        this.PreviousTick=Frame.Tick;
        this.WrittenFrameCount++;
    }

    public void Complete(){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if(this.Completed){ return; }
        if(this.WrittenFrameCount!=this.ExpectedFrameCount){
            throw new InvalidOperationException(
                $"Physics cache expected {this.ExpectedFrameCount} frames but received {this.WrittenFrameCount}.");
        }
        this.Writer.Flush();
        this.Completed=true;
    }

    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        this.Writer.Dispose();
        this.Bodies.Clear();
        this.Events.Clear();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Owns validated cache playback state; the ECS component stores only its authored intent.</summary>
public sealed class PhysicsCachePlayer:IDisposable {
    private readonly FileStream Spool;
    private readonly BinaryReader Reader;
    private readonly List<PhysicsBodyState>Bodies;
    private readonly List<PhysicsEvent>Events;
    private readonly int FrameCount;
    private int NextFrameIndex;
    private ulong?LastAppliedTick;
    private bool Disposed;

    private PhysicsCachePlayer(
        FileStream Spool,long FramesOffset,int FrameCount,int MaximumBodyCount,int MaximumEventCount,
        bool AllowRealtimeHandoff){
        this.Spool=Spool;
        this.Spool.Position=FramesOffset;
        this.Reader=new BinaryReader(this.Spool,Encoding.UTF8,true);
        this.Bodies=new List<PhysicsBodyState>(MaximumBodyCount);
        this.Events=new List<PhysicsEvent>(MaximumEventCount);
        this.FrameCount=FrameCount;
        this.AllowRealtimeHandoff=AllowRealtimeHandoff;
        this.State=PhysicsCachePlaybackState.Playing;
    }

    public PhysicsCachePlaybackState State { get;private set; }
    public bool AllowRealtimeHandoff { get; }
    public string Error { get;private set; }=string.Empty;

    public static bool TryLoad(
        Stream Input,in SimulationCacheIdentity Expected,bool AllowRealtimeHandoff,
        out PhysicsCachePlayer? Player,out string Error){
        ArgumentNullException.ThrowIfNull(Input);
        FileStream?Spool=null;
        try {
            string TemporaryPath=Path.Combine(Path.GetTempPath(),$"cge-physics-cache-{Guid.NewGuid():N}.tmp");
            Spool=new FileStream(
                TemporaryPath,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read,65_536,
                FileOptions.SequentialScan|FileOptions.DeleteOnClose);
            Input.CopyTo(Spool);
            Spool.Flush();
            Spool.Position=0;
            if(!PhysicsCacheSerializer.TryInspect(
                Spool,Expected,out long FramesOffset,out int FrameCount,
                out int MaximumBodyCount,out int MaximumEventCount,out Error)){
                Spool.Dispose();
                Player=null;
                return false;
            }
            Player=new PhysicsCachePlayer(
                Spool,FramesOffset,FrameCount,MaximumBodyCount,MaximumEventCount,AllowRealtimeHandoff);
            return true;
        }
        catch(Exception Exception)when(Exception is IOException or UnauthorizedAccessException or InvalidDataException){
            Spool?.Dispose();
            Player=null;
            Error=Exception.Message;
            return false;
        }
    }

    public bool TryApply(ulong Tick,DeterministicPhysicsWorld World){
        ArgumentNullException.ThrowIfNull(World);
        if(this.State!=PhysicsCachePlaybackState.Playing){ return false; }
        if(this.NextFrameIndex>=this.FrameCount){
            return this.Fault($"Physics cache has no frame for tick {Tick}.");
        }
        PhysicsCacheFrame Frame;
        try { Frame=PhysicsCacheSerializer.ReadNext(this.Reader,this.Bodies,this.Events); }
        catch(Exception Exception)when(Exception is EndOfStreamException or IOException or InvalidDataException){
            return this.Fault("Physics cache became unreadable during playback.");
        }
        if(Frame.Tick!=Tick){ return this.Fault($"Physics cache has no frame for tick {Tick}."); }
        if(this.LastAppliedTick.HasValue&&Tick!=this.LastAppliedTick.Value+1){
            return this.Fault("Physics cache playback ticks must be contiguous.");
        }
        if(!World.TryApplyCacheFrame(Frame,out string Error)){
            return this.Fault(Error);
        }
        this.LastAppliedTick=Tick;
        this.NextFrameIndex++;
        return true;
    }

    public bool TryHandoffToRealtime(ulong NextTick){
        if(this.State!=PhysicsCachePlaybackState.Playing||!this.AllowRealtimeHandoff||!this.LastAppliedTick.HasValue
            ||NextTick!=this.LastAppliedTick.Value+1){ return false; }
        this.State=PhysicsCachePlaybackState.HandedOffToRealtime;
        this.ReleaseSpool();
        return true;
    }

    public void Stop(){
        if(this.State==PhysicsCachePlaybackState.Playing){
            this.State=PhysicsCachePlaybackState.Stopped;
            this.ReleaseSpool();
        }
    }

    public void Dispose(){
        if(this.Disposed){ return; }
        if(this.State==PhysicsCachePlaybackState.Playing){ this.State=PhysicsCachePlaybackState.Stopped; }
        this.ReleaseSpool();
        GC.SuppressFinalize(this);
    }

    private bool Fault(string Error){
        this.Error=Error;
        this.State=PhysicsCachePlaybackState.Faulted;
        this.ReleaseSpool();
        return false;
    }

    private void ReleaseSpool(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        this.Reader.Dispose();
        this.Spool.Dispose();
    }
}
