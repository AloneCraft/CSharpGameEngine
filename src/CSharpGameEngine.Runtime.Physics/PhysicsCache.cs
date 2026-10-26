using System.Text;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Simulation;

namespace CSharpGameEngine.Runtime.Physics;

/// <summary>
/// A bond of a destructible that broke in a tick (ADR-0078/M0113): the destructible's physics stable ID, the bond's index in its cooked asset and the strain
/// (N·s) that broke it. The cache records these so a played-back tick breaks the same bonds and makes the same pieces.
/// </summary>
public readonly record struct PhysicsBondBreak(ulong RootStableId,int Bond,LogicalScalar Strain);

/// <summary>
/// A bond of a destructible whose internal strain an impact raised in a tick (M0124): the destructible's physics stable ID, the bond and the internal strain
/// (N·s) it carries into the next tick. The cache records these so a played-back stretch leaves the same internal strains as the live run (every other
/// bond's internal strain only decays, which playback repeats exactly).
/// </summary>
public readonly record struct PhysicsBondStrain(ulong RootStableId,int Bond,LogicalScalar Strain);

/// <summary>
/// One recorded tick. <paramref name="BondBreaks"/> (schema 3, M0113) and <paramref name="BondStrains"/> (schema 4, M0124) may be left out: <see cref="Breaks"/>
/// and <see cref="Strains"/> are then empty.
/// </summary>
public readonly record struct PhysicsCacheFrame(ulong Tick,IReadOnlyList<PhysicsBodyState> Bodies,IReadOnlyList<PhysicsEvent> Events,IReadOnlyList<PhysicsBondBreak>? BondBreaks=null,IReadOnlyList<PhysicsBondStrain>? BondStrains=null) {
    /// <summary>The bonds that broke in this tick, never null.</summary>
    public IReadOnlyList<PhysicsBondBreak> Breaks=>this.BondBreaks??[];
    /// <summary>The bonds whose internal strain an impact raised in this tick, never null.</summary>
    public IReadOnlyList<PhysicsBondStrain> Strains=>this.BondStrains??[];
}

/// <summary>
/// The <c>.cgephyscache</c> format. Schema 3 (M0113) adds after each frame's events the bonds that broke in the tick (count, then RootStableId, Bond, Strain,
/// strictly ordered by RootStableId and Bond). Schema 4 (M0124, written) adds after them the bonds whose internal strain an impact raised (count, then
/// RootStableId, Bond, Strain, in the same order). Schema 2 (M0055) and 3 files are still read: the identity may name the older schema when everything else
/// matches, and their frames have no bond breaks (2) or internal strains (2 and 3).
/// </summary>
public static class PhysicsCacheSerializer {
    public const int SchemaVersion=4;
    /// <summary>The oldest schema still read (M0055's frames without bond breaks).</summary>
    public const int OldestReadableSchemaVersion=2;
    public const int MaximumFrameCount=10_000_000;
    internal const int MaximumBodiesPerFrame=1_000_000;
    internal const int MaximumEventsPerFrame=4_000_000;
    internal const int MaximumBondBreaksPerFrame=1_000_000;
    internal const int MaximumBondStrainsPerFrame=1_000_000;

    public static void Write(Stream Output,in SimulationCacheIdentity Identity,IReadOnlyList<PhysicsCacheFrame> Frames){
        ArgumentNullException.ThrowIfNull(Output);
        ArgumentNullException.ThrowIfNull(Frames);
        using PhysicsCacheStreamWriter Writer=new(Output,Identity,Frames.Count);
        foreach(PhysicsCacheFrame Frame in Frames){ Writer.WriteFrame(Frame); }
        Writer.Complete();
    }

    /// <summary>
    /// Whether a file of identity <paramref name="Actual"/> can be read where <paramref name="Expected"/> (the current schema) is wanted, and the schema its
    /// frames use: the same identity, or an older readable schema with every other part equal.
    /// </summary>
    internal static bool TryAccept(in SimulationCacheIdentity Actual,in SimulationCacheIdentity Expected,out int Schema){
        Schema=Actual.SchemaVersion;
        if(Actual==Expected){ return Actual.SchemaVersion is >=OldestReadableSchemaVersion and <=SchemaVersion; }
        return Expected.SchemaVersion==SchemaVersion&&Actual.SchemaVersion is >=OldestReadableSchemaVersion and <SchemaVersion
            &&(Actual with { SchemaVersion=Expected.SchemaVersion })==Expected;
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
            if(!TryAccept(Actual,Expected,out var Schema)){
                Error="Simulation cache identity does not match the current logical profile, tick rate, schema, or dependencies.";
                return false;
            }
            using BinaryReader Reader=new(Input,Encoding.UTF8,true);
            int FrameCount=Reader.ReadInt32();
            if(FrameCount is <0 or >MaximumFrameCount){ throw new InvalidDataException(); }
            ulong PreviousTick=0;
            for(int FrameIndex=0;FrameIndex<FrameCount;FrameIndex++){
                List<PhysicsBodyState> Bodies=[];
                List<PhysicsEvent> Events=[];
                List<PhysicsBondBreak> Breaks=[];
                List<PhysicsBondStrain> Strains=[];
                ulong Tick=ReadCanonicalFrame(Reader,Schema,Bodies,Events,Breaks,Strains);
                if(FrameIndex>0&&Tick<=PreviousTick){ throw new InvalidDataException(); }
                PreviousTick=Tick;
                Frames.Add(new PhysicsCacheFrame(Tick,Bodies,Events,Breaks,Strains));
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
        out long FramesOffset,out int FrameCount,out int MaximumBodyCount,out int MaximumEventCount,out int Schema,out string Error){
        ArgumentNullException.ThrowIfNull(Input);
        FramesOffset=0;
        FrameCount=0;
        MaximumBodyCount=0;
        MaximumEventCount=0;
        Schema=0;
        Error=string.Empty;
        try {
            if(!SimulationCacheHeader.TryRead(Input,out SimulationCacheIdentity Actual)){
                Error="Invalid simulation cache header.";
                return false;
            }
            if(!TryAccept(Actual,Expected,out Schema)){
                Error="Simulation cache identity does not match the current logical profile, tick rate, schema, or dependencies.";
                return false;
            }
            using BinaryReader Reader=new(Input,Encoding.UTF8,true);
            FrameCount=Reader.ReadInt32();
            if(FrameCount is <0 or >MaximumFrameCount){ throw new InvalidDataException(); }
            FramesOffset=Input.Position;
            ulong PreviousTick=0;
            List<PhysicsBodyState>Bodies=[];
            List<PhysicsEvent>Events=[];
            List<PhysicsBondBreak>Breaks=[];
            List<PhysicsBondStrain>Strains=[];
            for(int FrameIndex=0;FrameIndex<FrameCount;FrameIndex++){
                Bodies.Clear();
                Events.Clear();
                Breaks.Clear();
                Strains.Clear();
                ulong Tick=ReadCanonicalFrame(Reader,Schema,Bodies,Events,Breaks,Strains);
                if(FrameIndex>0&&Tick<=PreviousTick){ throw new InvalidDataException(); }
                PreviousTick=Tick;
                MaximumBodyCount=Math.Max(MaximumBodyCount,Bodies.Count);
                MaximumEventCount=Math.Max(MaximumEventCount,Events.Count);
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

    /// <summary>Reads one frame and checks that it is canonical (ordering, identities, enums, tick of every event, bond break and internal strain); returns its tick.</summary>
    private static ulong ReadCanonicalFrame(BinaryReader Reader,int Schema,List<PhysicsBodyState>Bodies,List<PhysicsEvent>Events,List<PhysicsBondBreak>Breaks,List<PhysicsBondStrain>Strains){
        ulong Tick=Reader.ReadUInt64();
        int BodyCount=ReadCount(Reader,MaximumBodiesPerFrame);
        ulong PreviousStableId=0;
        for(int BodyIndex=0;BodyIndex<BodyCount;BodyIndex++){
            PhysicsBodyState Body=ReadBody(Reader);
            if(!IsCanonicalBody(Body)||BodyIndex>0&&Body.StableId<=PreviousStableId){ throw new InvalidDataException(); }
            PreviousStableId=Body.StableId;
            Bodies.Add(Body);
        }
        int EventCount=ReadCount(Reader,MaximumEventsPerFrame);
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
        if(Schema>=3){
            int BreakCount=ReadCount(Reader,MaximumBondBreaksPerFrame);
            for(int BreakIndex=0;BreakIndex<BreakCount;BreakIndex++){
                PhysicsBondBreak Break=new(Reader.ReadUInt64(),Reader.ReadInt32(),ReadScalar(Reader));
                if(!IsCanonicalBreak(Break)||BreakIndex>0&&CompareBreaks(Breaks[^1],Break)>=0){ throw new InvalidDataException(); }
                Breaks.Add(Break);
            }
        }
        if(Schema>=4){
            int StrainCount=ReadCount(Reader,MaximumBondStrainsPerFrame);
            for(int StrainIndex=0;StrainIndex<StrainCount;StrainIndex++){
                PhysicsBondStrain Strain=new(Reader.ReadUInt64(),Reader.ReadInt32(),ReadScalar(Reader));
                if(!IsCanonicalStrain(Strain)||StrainIndex>0&&CompareStrains(Strains[^1],Strain)>=0){ throw new InvalidDataException(); }
                Strains.Add(Strain);
            }
        }
        return Tick;
    }

    internal static PhysicsCacheFrame ReadNext(
        BinaryReader Reader,int Schema,List<PhysicsBodyState> Bodies,List<PhysicsEvent> Events,List<PhysicsBondBreak> Breaks,List<PhysicsBondStrain> Strains){
        Bodies.Clear();
        Events.Clear();
        Breaks.Clear();
        Strains.Clear();
        ulong Tick=Reader.ReadUInt64();
        int BodyCount=ReadCount(Reader,MaximumBodiesPerFrame);
        for(int BodyIndex=0;BodyIndex<BodyCount;BodyIndex++){ Bodies.Add(ReadBody(Reader)); }
        int EventCount=ReadCount(Reader,MaximumEventsPerFrame);
        for(int EventIndex=0;EventIndex<EventCount;EventIndex++){ Events.Add(ReadEvent(Reader)); }
        if(Schema>=3){
            int BreakCount=ReadCount(Reader,MaximumBondBreaksPerFrame);
            for(int BreakIndex=0;BreakIndex<BreakCount;BreakIndex++){ Breaks.Add(new PhysicsBondBreak(Reader.ReadUInt64(),Reader.ReadInt32(),ReadScalar(Reader))); }
        }
        if(Schema>=4){
            int StrainCount=ReadCount(Reader,MaximumBondStrainsPerFrame);
            for(int StrainIndex=0;StrainIndex<StrainCount;StrainIndex++){ Strains.Add(new PhysicsBondStrain(Reader.ReadUInt64(),Reader.ReadInt32(),ReadScalar(Reader))); }
        }
        return new PhysicsCacheFrame(Tick,Bodies,Events,Breaks,Strains);
    }

    private static int ReadCount(BinaryReader Reader,int Maximum){
        int Count=Reader.ReadInt32();
        if(Count<0||Count>Maximum){ throw new InvalidDataException(); }
        return Count;
    }
    internal static bool IsCanonicalBreak(in PhysicsBondBreak Break)=>Break.RootStableId!=0&&Break.Bond>=0&&Break.Strain>=LogicalScalar.Zero;
    internal static int CompareBreaks(in PhysicsBondBreak Left,in PhysicsBondBreak Right){
        int Result=Left.RootStableId.CompareTo(Right.RootStableId);
        return Result!=0?Result:Left.Bond.CompareTo(Right.Bond);
    }
    internal static void WriteBreak(BinaryWriter Writer,in PhysicsBondBreak Break){ Writer.Write(Break.RootStableId);Writer.Write(Break.Bond);WriteScalar(Writer,Break.Strain); }
    internal static bool IsCanonicalStrain(in PhysicsBondStrain Strain)=>Strain.RootStableId!=0&&Strain.Bond>=0&&Strain.Strain>LogicalScalar.Zero;
    internal static int CompareStrains(in PhysicsBondStrain Left,in PhysicsBondStrain Right){
        int Result=Left.RootStableId.CompareTo(Right.RootStableId);
        return Result!=0?Result:Left.Bond.CompareTo(Right.Bond);
    }
    internal static void WriteStrain(BinaryWriter Writer,in PhysicsBondStrain Strain){ Writer.Write(Strain.RootStableId);Writer.Write(Strain.Bond);WriteScalar(Writer,Strain.Strain); }
    internal static bool IsCanonicalBody(in PhysicsBodyState Body)=>
        Body.StableId!=0&&Body.Entity.Index>=0&&Body.Entity.Generation!=0
        &&(!Body.MotionType.HasValue||Enum.IsDefined(Body.MotionType.Value))
        &&IsUnitRotation(Body.Rotation);
    /// <summary>
    /// How far a stored rotation's squared length may be from one (2^-30). Q64.64 normalization is not idempotent (normalizing a normalized quaternion
    /// again can move its last bits), so the cache takes what the integrator produced rather than demanding an exact fixed point of normalization (M0113:
    /// that refused the bake of every spinning body). The rounding error is near 2^-60; anything this far off was never normalized.
    /// </summary>
    private static readonly LogicalScalar UnitRotationTolerance=LogicalScalar.FromRaw((Int128)1<<34);
    internal static bool IsUnitRotation(LogicalQuaternion Rotation){
        // Bounded first, so a damaged file cannot overflow the squared length.
        LogicalScalar Two=LogicalScalar.FromInt64(2);
        foreach(LogicalScalar Component in (ReadOnlySpan<LogicalScalar>)[Rotation.R,Rotation.U,Rotation.F,Rotation.W]){
            if(Component>Two||Component< -Two){ return false; }
        }
        LogicalScalar Error=Rotation.LengthSquared-LogicalScalar.One;
        return Error<=UnitRotationTolerance&&-Error<=UnitRotationTolerance;
    }
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
    private readonly List<PhysicsBondBreak>Breaks=[];
    private readonly List<PhysicsBondStrain>Strains=[];
    private readonly int ExpectedFrameCount;
    private readonly int Schema;
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
        // Schema 4 is written; schema 2 (no bond breaks) or 3 (no internal strains) only when the identity asks for it, to make files of an older layout.
        if(Identity.SchemaVersion is <PhysicsCacheSerializer.OldestReadableSchemaVersion or >PhysicsCacheSerializer.SchemaVersion){
            throw new ArgumentOutOfRangeException(nameof(Identity),"The physics cache schema is not one this writer knows.");
        }
        this.Schema=Identity.SchemaVersion;
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
            ||Frame.Events.Count>PhysicsCacheSerializer.MaximumEventsPerFrame
            ||Frame.Breaks.Count>PhysicsCacheSerializer.MaximumBondBreaksPerFrame
            ||Frame.Strains.Count>PhysicsCacheSerializer.MaximumBondStrainsPerFrame){
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
        this.Breaks.Clear();
        foreach(var Break in Frame.Breaks){
            if(!PhysicsCacheSerializer.IsCanonicalBreak(Break)){ throw new ArgumentException("Physics cache frame contains an invalid bond break.",nameof(Frame)); }
            this.Breaks.Add(Break);
        }
        if(this.Schema<3&&this.Breaks.Count>0){ throw new ArgumentException("A schema 2 physics cache cannot record bond breaks.",nameof(Frame)); }
        this.Breaks.Sort(static(Left,Right)=>PhysicsCacheSerializer.CompareBreaks(Left,Right));
        for(var Index=1;Index<this.Breaks.Count;Index++){
            if(PhysicsCacheSerializer.CompareBreaks(this.Breaks[Index-1],this.Breaks[Index])==0){ throw new ArgumentException("Physics cache frame breaks one bond twice.",nameof(Frame)); }
        }
        this.Strains.Clear();
        foreach(var Strain in Frame.Strains){
            if(!PhysicsCacheSerializer.IsCanonicalStrain(Strain)){ throw new ArgumentException("Physics cache frame contains an invalid internal strain.",nameof(Frame)); }
            this.Strains.Add(Strain);
        }
        if(this.Schema<4&&this.Strains.Count>0){ throw new ArgumentException("A schema 2 or 3 physics cache cannot record internal strains.",nameof(Frame)); }
        this.Strains.Sort(static(Left,Right)=>PhysicsCacheSerializer.CompareStrains(Left,Right));
        for(var Index=1;Index<this.Strains.Count;Index++){
            if(PhysicsCacheSerializer.CompareStrains(this.Strains[Index-1],this.Strains[Index])==0){ throw new ArgumentException("Physics cache frame records one bond's internal strain twice.",nameof(Frame)); }
        }

        this.Writer.Write(Frame.Tick);
        this.Writer.Write(this.Bodies.Count);
        foreach(PhysicsBodyState Body in this.Bodies){ PhysicsCacheSerializer.WriteBody(this.Writer,Body); }
        this.Writer.Write(this.Events.Count);
        foreach(PhysicsEvent Event in this.Events){ PhysicsCacheSerializer.WriteEvent(this.Writer,Event); }
        if(this.Schema>=3){
            this.Writer.Write(this.Breaks.Count);
            foreach(var Break in this.Breaks){ PhysicsCacheSerializer.WriteBreak(this.Writer,Break); }
        }
        if(this.Schema>=4){
            this.Writer.Write(this.Strains.Count);
            foreach(var Strain in this.Strains){ PhysicsCacheSerializer.WriteStrain(this.Writer,Strain); }
        }
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
        this.Breaks.Clear();
        this.Strains.Clear();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Owns validated cache playback state; the ECS component stores only its authored intent.</summary>
public sealed class PhysicsCachePlayer:IDisposable {
    private readonly FileStream Spool;
    private readonly BinaryReader Reader;
    private readonly List<PhysicsBodyState>Bodies;
    private readonly List<PhysicsEvent>Events;
    private readonly List<PhysicsBondBreak>Breaks=[];
    private readonly List<PhysicsBondStrain>Strains=[];
    private readonly int FrameCount;
    private readonly int Schema;
    private int NextFrameIndex;
    private ulong?LastAppliedTick;
    private bool Disposed;

    private PhysicsCachePlayer(
        FileStream Spool,long FramesOffset,int FrameCount,int MaximumBodyCount,int MaximumEventCount,int Schema,
        bool AllowRealtimeHandoff){
        this.Schema=Schema;
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
                out int MaximumBodyCount,out int MaximumEventCount,out int Schema,out Error)){
                Spool.Dispose();
                Player=null;
                return false;
            }
            Player=new PhysicsCachePlayer(
                Spool,FramesOffset,FrameCount,MaximumBodyCount,MaximumEventCount,Schema,AllowRealtimeHandoff);
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
        try { Frame=PhysicsCacheSerializer.ReadNext(this.Reader,this.Schema,this.Bodies,this.Events,this.Breaks,this.Strains); }
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
