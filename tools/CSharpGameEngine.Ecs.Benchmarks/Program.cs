using System.Diagnostics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;

const int DefaultEntities=100_000;
const int DefaultFrames=300;
var Entities=args.Length>0&&int.TryParse(args[0],out var ParsedEntities)?ParsedEntities:DefaultEntities;
var Frames=args.Length>1&&int.TryParse(args[1],out var ParsedFrames)?ParsedFrames:DefaultFrames;

Console.WriteLine($"AoS/SoA ECS benchmark: entities={Entities:N0}, frames={Frames:N0}");
RunAos(Entities,Frames);
RunSoa(Entities,Frames);

static void RunAos(int Entities,int Frames){
    AosRow[]Rows=new AosRow[Entities];
    for(var Index=0;Index<Rows.Length;Index++){
        Rows[Index].Velocity.X=1f;
        Rows[Index].ColdA=Index;
        Rows[Index].ColdB=Index*2L;
    }

    WarmUpAos(Rows);
    var Before=GC.GetAllocatedBytesForCurrentThread();
    var Start=Stopwatch.GetTimestamp();
    for(var Frame=0;Frame<Frames;Frame++){
        for(var Index=0;Index<Rows.Length;Index++){
            Rows[Index].Position.X+=Rows[Index].Velocity.X;
        }
    }

    Print("AoS",Stopwatch.GetElapsedTime(Start),GC.GetAllocatedBytesForCurrentThread()-Before,Rows[^1].Position.X);
}

static void RunSoa(int Entities,int Frames){
    World World=new();
    State State=new(World){ Enabled=true };
    for(var Index=0;Index<Entities;Index++){
        World.Create(new Position(),new Velocity { X=1f },new ColdA { Value=Index },new ColdB { Value=Index*2L });
    }

    BenchmarkSystem System=new();
    GeneratedGameLoop.RunFrame(ref State,ref System);
    var Before=GC.GetAllocatedBytesForCurrentThread();
    var Start=Stopwatch.GetTimestamp();
    for(var Frame=0;Frame<Frames;Frame++){
        GeneratedGameLoop.RunFrame(ref State,ref System);
    }

    Print("SoA",Stopwatch.GetElapsedTime(Start),GC.GetAllocatedBytesForCurrentThread()-Before,World.Archetype1.GetPositionRefUnchecked(Entities-1).X);
}

static void WarmUpAos(AosRow[]Rows){
    for(var Index=0;Index<Rows.Length;Index++){
        Rows[Index].Position.X+=Rows[Index].Velocity.X;
    }
}

static void Print(string Layout,TimeSpan Elapsed,long AllocatedBytes,float Checksum){
    Console.WriteLine($"{Layout}: {Elapsed.TotalMilliseconds:F2} ms, allocated={AllocatedBytes:N0} B, checksum={Checksum:F0}");
}

internal struct BenchmarkSystem:ISystem {
    public bool Update(ref Position Position,ref Velocity Velocity,ref State State){
        Position.X+=Velocity.X;
        return true;
    }
}

internal struct BenchmarkShape:ISystem {
    public void Spawn(ref Position Position,ref Velocity Velocity,ref ColdA ColdA,ref ColdB ColdB){
    }
}

public struct Position { public float X;}
public struct Velocity { public float X;}
public struct ColdA { public int Value;}
public struct ColdB { public long Value;}
internal struct AosRow { public Position Position;public Velocity Velocity;public int ColdA;public long ColdB;}
