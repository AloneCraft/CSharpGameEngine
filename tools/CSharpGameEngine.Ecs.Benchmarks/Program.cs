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
    for(var index=0;index<Rows.Length;index++){
        Rows[index].Velocity.X=1f;
        Rows[index].ColdA=index;
        Rows[index].ColdB=index*2L;
    }

    WarmUpAos(Rows);
    var Before=GC.GetAllocatedBytesForCurrentThread();
    var Start=Stopwatch.GetTimestamp();
    for(var frame=0;frame<Frames;frame++){
        for(var index=0;index<Rows.Length;index++){
            Rows[index].Position.X+=Rows[index].Velocity.X;
        }
    }

    Print("AoS",Stopwatch.GetElapsedTime(Start),GC.GetAllocatedBytesForCurrentThread()-Before,Rows[^1].Position.X);
}

static void RunSoa(int Entities,int Frames){
    World world=new();
    State state=new(world){ Enabled=true };
    for(var index=0;index<Entities;index++){
        world.Create(new Position(),new Velocity { X=1f },new ColdA { Value=index },new ColdB { Value=index*2L });
    }

    BenchmarkSystem system=new();
    GeneratedGameLoop.RunFrame(ref state,ref system);
    var Before=GC.GetAllocatedBytesForCurrentThread();
    var Start=Stopwatch.GetTimestamp();
    for(var frame=0;frame<Frames;frame++){
        GeneratedGameLoop.RunFrame(ref state,ref system);
    }

    Print("SoA",Stopwatch.GetElapsedTime(Start),GC.GetAllocatedBytesForCurrentThread()-Before,world.Archetype1.GetPositionRefUnchecked(Entities-1).X);
}

static void WarmUpAos(AosRow[]Rows){
    for(var index=0;index<Rows.Length;index++){
        Rows[index].Position.X+=Rows[index].Velocity.X;
    }
}

static void Print(string Layout,TimeSpan Elapsed,long AllocatedBytes,float Checksum){
    Console.WriteLine($"{Layout}: {Elapsed.TotalMilliseconds:F2} ms, allocated={AllocatedBytes:N0} B, checksum={Checksum:F0}");
}

internal struct BenchmarkSystem:ISystem {
    public bool Update(ref Position position,ref Velocity velocity,ref State state){
        position.X+=velocity.X;
        return true;
    }
}

internal struct BenchmarkShape:ISystem {
    public void Spawn(ref Position position,ref Velocity velocity,ref ColdA coldA,ref ColdB coldB){
    }
}

public struct Position { public float X;}
public struct Velocity { public float X;}
public struct ColdA { public int Value;}
public struct ColdB { public long Value;}
internal struct AosRow { public Position Position;public Velocity Velocity;public int ColdA;public long ColdB;}
