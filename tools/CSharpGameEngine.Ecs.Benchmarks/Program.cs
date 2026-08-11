using System.Diagnostics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;

const int defaultEntities = 100_000;
const int defaultFrames = 300;
int entities = args.Length > 0 && int.TryParse(args[0], out int parsedEntities) ? parsedEntities : defaultEntities;
int frames = args.Length > 1 && int.TryParse(args[1], out int parsedFrames) ? parsedFrames : defaultFrames;

Console.WriteLine($"AoS/SoA ECS benchmark: entities={entities:N0}, frames={frames:N0}");
RunAos(entities, frames);
RunSoa(entities, frames);

static void RunAos(int entities, int frames)
{
    AosRow[] rows = new AosRow[entities];
    for (int index = 0; index < rows.Length; index++)
    {
        rows[index].Velocity.X = 1f;
        rows[index].ColdA = index;
        rows[index].ColdB = index * 2L;
    }

    WarmUpAos(rows);
    long before = GC.GetAllocatedBytesForCurrentThread();
    long start = Stopwatch.GetTimestamp();
    for (int frame = 0; frame < frames; frame++)
    {
        for (int index = 0; index < rows.Length; index++)
        {
            rows[index].Position.X += rows[index].Velocity.X;
        }
    }

    Print("AoS", Stopwatch.GetElapsedTime(start), GC.GetAllocatedBytesForCurrentThread() - before, rows[^1].Position.X);
}

static void RunSoa(int entities, int frames)
{
    World world = new();
    State state = new(world) { Enabled = true };
    for (int index = 0; index < entities; index++)
    {
        world.Create(new Position(), new Velocity { X = 1f }, new ColdA { Value = index }, new ColdB { Value = index * 2L });
    }

    BenchmarkSystem system = new();
    GeneratedGameLoop.RunFrame(ref state, ref system);
    long before = GC.GetAllocatedBytesForCurrentThread();
    long start = Stopwatch.GetTimestamp();
    for (int frame = 0; frame < frames; frame++)
    {
        GeneratedGameLoop.RunFrame(ref state, ref system);
    }

    Print("SoA", Stopwatch.GetElapsedTime(start), GC.GetAllocatedBytesForCurrentThread() - before, world.Archetype1.GetPositionRefUnchecked(entities - 1).X);
}

static void WarmUpAos(AosRow[] rows)
{
    for (int index = 0; index < rows.Length; index++)
    {
        rows[index].Position.X += rows[index].Velocity.X;
    }
}

static void Print(string layout, TimeSpan elapsed, long allocatedBytes, float checksum)
{
    Console.WriteLine($"{layout}: {elapsed.TotalMilliseconds:F2} ms, allocated={allocatedBytes:N0} B, checksum={checksum:F0}");
}

internal struct BenchmarkSystem : ISystem
{
    public bool Update(ref Position position, ref Velocity velocity, ref State state)
    {
        position.X += velocity.X;
        return true;
    }
}

internal struct BenchmarkShape : ISystem
{
    public void Spawn(ref Position position, ref Velocity velocity, ref ColdA coldA, ref ColdB coldB)
    {
    }
}

public struct Position { public float X; }
public struct Velocity { public float X; }
public struct ColdA { public int Value; }
public struct ColdB { public long Value; }
internal struct AosRow { public Position Position; public Velocity Velocity; public int ColdA; public long ColdB; }


