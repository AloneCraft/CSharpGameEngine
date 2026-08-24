using System.Text;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Simulation;

namespace CSharpGameEngine.Runtime.Physics;

public readonly record struct PhysicsCacheFrame(ulong Tick,IReadOnlyList<PhysicsBodyState> Bodies,IReadOnlyList<PhysicsEvent> Events);

public static class PhysicsCacheSerializer {
    public const int SchemaVersion=1;

    public static void Write(Stream Output,in SimulationCacheIdentity Identity,IReadOnlyList<PhysicsCacheFrame> Frames){
        ArgumentNullException.ThrowIfNull(Output);
        ArgumentNullException.ThrowIfNull(Frames);
        SimulationCacheHeader.Write(Output,Identity);
        using BinaryWriter Writer=new(Output,Encoding.UTF8,true);
        Writer.Write(Frames.Count);
        foreach(PhysicsCacheFrame Frame in Frames){
            Writer.Write(Frame.Tick);
            Writer.Write(Frame.Bodies.Count);
            foreach(PhysicsBodyState Body in Frame.Bodies.OrderBy(static Body=>Body.StableId)){
                WriteBody(Writer,Body);
            }
            Writer.Write(Frame.Events.Count);
            foreach(PhysicsEvent Event in Frame.Events.OrderBy(static Event=>Event.PairId).ThenBy(static Event=>Event.Type)){
                WriteEvent(Writer,Event);
            }
        }
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
            if(FrameCount<0){ throw new InvalidDataException(); }
            for(int FrameIndex=0;FrameIndex<FrameCount;FrameIndex++){
                ulong Tick=Reader.ReadUInt64();
                int BodyCount=ReadCount(Reader);
                List<PhysicsBodyState> Bodies=new(BodyCount);
                for(int BodyIndex=0;BodyIndex<BodyCount;BodyIndex++){ Bodies.Add(ReadBody(Reader)); }
                int EventCount=ReadCount(Reader);
                List<PhysicsEvent> Events=new(EventCount);
                for(int EventIndex=0;EventIndex<EventCount;EventIndex++){ Events.Add(ReadEvent(Reader)); }
                Frames.Add(new PhysicsCacheFrame(Tick,Bodies,Events));
            }
            return true;
        }
        catch(Exception Exception)when(Exception is EndOfStreamException or IOException or InvalidDataException){
            Frames.RemoveRange(Start,Frames.Count-Start);
            Error="Simulation cache is truncated or malformed.";
            return false;
        }
    }

    private static int ReadCount(BinaryReader Reader){
        int Count=Reader.ReadInt32();
        if(Count is <0 or >10_000_000){ throw new InvalidDataException(); }
        return Count;
    }
    private static void WriteBody(BinaryWriter Writer,in PhysicsBodyState Body){
        Writer.Write(Body.Entity.Id);Writer.Write(Body.StableId);Writer.Write(Body.MotionType.HasValue);
        if(Body.MotionType.HasValue){ Writer.Write((byte)Body.MotionType.Value); }
        WriteVector(Writer,Body.Position);WriteQuaternion(Writer,Body.Rotation);
        WriteVector(Writer,Body.Velocity.Linear);WriteVector(Writer,Body.Velocity.Angular);
        Writer.Write(Body.IsSleeping);Writer.Write(Body.SleepTicks);
    }
    private static PhysicsBodyState ReadBody(BinaryReader Reader){
        Entity Entity=new(Reader.ReadInt32());ulong StableId=Reader.ReadUInt64();
        PhysicsMotionType? MotionType=Reader.ReadBoolean()?(PhysicsMotionType)Reader.ReadByte():null;
        PhysicsVector3 Position=ReadVector(Reader);PhysicsQuaternion Rotation=ReadQuaternion(Reader);
        PhysicsVelocity Velocity=new() { Linear=ReadVector(Reader),Angular=ReadVector(Reader) };
        return new PhysicsBodyState(Entity,StableId,MotionType,Position,Rotation,Velocity,Reader.ReadBoolean(),Reader.ReadUInt16());
    }
    private static void WriteEvent(BinaryWriter Writer,in PhysicsEvent Event){
        Writer.Write(Event.Tick);Writer.Write(Event.PairId);Writer.Write((ushort)Event.Type);
        Writer.Write(Event.First.Id);Writer.Write(Event.Second.Id);Writer.Write(Event.Impulse.RawValue);Writer.Write((ushort)Event.Fault);
    }
    private static PhysicsEvent ReadEvent(BinaryReader Reader)=>new(
        Reader.ReadUInt64(),Reader.ReadUInt64(),(PhysicsEventType)Reader.ReadUInt16(),
        new Entity(Reader.ReadInt32()),new Entity(Reader.ReadInt32()),
        PhysicsScalar.FromRaw(Reader.ReadInt64()),(PhysicsFaultType)Reader.ReadUInt16());
    private static void WriteVector(BinaryWriter Writer,in PhysicsVector3 Value){ Writer.Write(Value.R.RawValue);Writer.Write(Value.U.RawValue);Writer.Write(Value.F.RawValue); }
    private static PhysicsVector3 ReadVector(BinaryReader Reader)=>new(PhysicsScalar.FromRaw(Reader.ReadInt64()),PhysicsScalar.FromRaw(Reader.ReadInt64()),PhysicsScalar.FromRaw(Reader.ReadInt64()));
    private static void WriteQuaternion(BinaryWriter Writer,in PhysicsQuaternion Value){ Writer.Write(Value.R.RawValue);Writer.Write(Value.U.RawValue);Writer.Write(Value.F.RawValue);Writer.Write(Value.W.RawValue); }
    private static PhysicsQuaternion ReadQuaternion(BinaryReader Reader)=>new(PhysicsScalar.FromRaw(Reader.ReadInt64()),PhysicsScalar.FromRaw(Reader.ReadInt64()),PhysicsScalar.FromRaw(Reader.ReadInt64()),PhysicsScalar.FromRaw(Reader.ReadInt64()));
}
