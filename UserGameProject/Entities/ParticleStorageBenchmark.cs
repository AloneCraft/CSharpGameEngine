using System.Numerics;

namespace UserGameProject.Entities;

// 注意: この名前空間は UserGameProject の子であり、親には Scene1.cs が宣言する別物の
// UserGameProject.Entity / UserGameProject.World が存在するため、ECSの型は完全修飾で書く。

/// <summary>
/// 同一の<c>Particle</c>宣言から生成されたAoSバックエンドとSoAバックエンドを、
/// 同一プロセス・同一データで比較計測する。各行に適用する計算はどちらも同じ
/// <see cref="ParticleMotion"/>を呼ぶため、測っているのはメモリレイアウトの差だけである。
/// </summary>
public static class ParticleStorageBenchmark
{
    private const int DefaultEntityCount = 200_000;
    private const int DefaultWarmupIterations = 3;
    private const int DefaultIterations = 15;
    private const float DeltaTime = 1f / 60f;

    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!TryParseOptions(args, out int entityCount, out int iterations, out string? error))
        {
            Console.Error.WriteLine($"FAILED: {error}");
            PrintHelp();
            return 2;
        }

        Console.WriteLine("Particle storage benchmark (AoS vs ECS SoA)");
        Console.WriteLine($"  entities   : {entityCount:N0}");
        Console.WriteLine($"  warmup     : {DefaultWarmupIterations}");
        Console.WriteLine($"  iterations : {iterations}");
        Console.WriteLine($"  runtime    : {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"  build      : {(IsDebugBuild() ? "Debug (NOT representative - use -c Release)" : "Release")}");

        ParticleAosStore aosStore = new();
        global::CSharpGameEngine.Ecs.World world = new();
        ParticleSoaStore soaStore = new(world);

        MeasureCreate(entityCount, iterations);

        Particle[] aosHandles = SpawnAos(aosStore, entityCount);
        global::CSharpGameEngine.Ecs.Entity[] soaEntities = SpawnSoa(soaStore, entityCount);

        MeasureBatchMove(aosStore, soaStore, entityCount, iterations);
        MeasureBatchSingleComponent(aosStore, soaStore, entityCount, iterations);
        MeasureSequentialRead(aosStore, soaStore, entityCount, iterations);
        MeasureRandomSingleAccess(aosStore, soaStore, aosHandles, soaEntities, iterations);

        Console.WriteLine();
        Console.WriteLine("SUCCESS: benchmark completed.");
        return 0;
    }

    private static void MeasureCreate(int entityCount, int iterations)
    {
        BenchmarkResult aos = BenchmarkHarness.Measure(
            "AoS  Create",
            DefaultWarmupIterations,
            iterations,
            entityCount,
            () =>
            {
                ParticleAosStore store = new();
                SpawnAos(store, entityCount);
            });

        BenchmarkResult soa = BenchmarkHarness.Measure(
            "SoA  Create",
            DefaultWarmupIterations,
            iterations,
            entityCount,
            () =>
            {
                global::CSharpGameEngine.Ecs.World world = new();
                ParticleSoaStore store = new(world);
                SpawnSoa(store, entityCount);
            });

        BenchmarkHarness.PrintComparison("Create (spawn all entities)", aos, soa);
    }

    private static void MeasureBatchMove(ParticleAosStore aosStore, ParticleSoaStore soaStore, int entityCount, int iterations)
    {
        BenchmarkResult aos = BenchmarkHarness.Measure(
            "AoS  MoveAll",
            DefaultWarmupIterations,
            iterations,
            entityCount,
            () => ParticleSystems.MoveAllAos(aosStore, DeltaTime));

        BenchmarkResult soa = BenchmarkHarness.Measure(
            "SoA  MoveAll",
            DefaultWarmupIterations,
            iterations,
            entityCount,
            () => ParticleSystems.MoveAllSoa(soaStore, DeltaTime));

        BenchmarkHarness.PrintComparison("Batch update: Transform += Velocity * dt (2 of 3 components)", aos, soa);
    }

    private static void MeasureBatchSingleComponent(ParticleAosStore aosStore, ParticleSoaStore soaStore, int entityCount, int iterations)
    {
        BenchmarkResult aos = BenchmarkHarness.Measure(
            "AoS  ApplyWearAll",
            DefaultWarmupIterations,
            iterations,
            entityCount,
            () => ParticleSystems.ApplyWearAllAos(aosStore, 0));

        BenchmarkResult soa = BenchmarkHarness.Measure(
            "SoA  ApplyWearAll",
            DefaultWarmupIterations,
            iterations,
            entityCount,
            () => ParticleSystems.ApplyWearAllSoa(soaStore, 0));

        BenchmarkHarness.PrintComparison("Batch update: Durability only (1 of 3 components)", aos, soa);
    }

    private static void MeasureSequentialRead(ParticleAosStore aosStore, ParticleSoaStore soaStore, int entityCount, int iterations)
    {
        Vector3 aosSum = Vector3.Zero;
        Vector3 soaSum = Vector3.Zero;

        BenchmarkResult aos = BenchmarkHarness.Measure(
            "AoS  SumPositions",
            DefaultWarmupIterations,
            iterations,
            entityCount,
            () => aosSum = ParticleSystems.SumPositionsAos(aosStore));

        BenchmarkResult soa = BenchmarkHarness.Measure(
            "SoA  SumPositions",
            DefaultWarmupIterations,
            iterations,
            entityCount,
            () => soaSum = ParticleSystems.SumPositionsSoa(soaStore));

        BenchmarkHarness.PrintComparison("Batch read: sum Transform.Position (1 of 3 components)", aos, soa);

        // 最適化でループごと削除されないよう、結果を必ず観測する。
        Console.WriteLine($"    (checksum AoS={aosSum.X:F1} SoA={soaSum.X:F1})");
    }

    private static void MeasureRandomSingleAccess(
        ParticleAosStore aosStore,
        ParticleSoaStore soaStore,
        Particle[] aosHandles,
        global::CSharpGameEngine.Ecs.Entity[] soaEntities,
        int iterations)
    {
        // Location Tableを必ず引くランダム順アクセス。バッチ経路との対比が目的なので
        // 件数はエンティティ総数より小さくし、両者で完全に同じ添字列を使う。
        int accessCount = Math.Min(50_000, aosHandles.Length);
        int[] order = BuildShuffledIndices(accessCount, aosHandles.Length);

        BenchmarkResult aos = BenchmarkHarness.Measure(
            "AoS  random Facade",
            DefaultWarmupIterations,
            iterations,
            accessCount,
            () =>
            {
                for (int i = 0; i < order.Length; i++)
                {
                    aosHandles[order[i]].Move(DeltaTime);
                }
            });

        BenchmarkResult soa = BenchmarkHarness.Measure(
            "SoA  random Store ref",
            DefaultWarmupIterations,
            iterations,
            accessCount,
            () =>
            {
                for (int i = 0; i < order.Length; i++)
                {
                    ParticleSystems.MoveSingleSoa(soaStore, soaEntities[order[i]], DeltaTime);
                }
            });

        BenchmarkHarness.PrintComparison("Random single-entity access (goes through the location table)", aos, soa);
    }

    private static Particle[] SpawnAos(ParticleAosStore store, int entityCount)
    {
        Particle[] handles = new Particle[entityCount];

        for (int i = 0; i < entityCount; i++)
        {
            handles[i] = store.Create(CreateInfoFor(i));
        }

        return handles;
    }

    private static global::CSharpGameEngine.Ecs.Entity[] SpawnSoa(ParticleSoaStore store, int entityCount)
    {
        global::CSharpGameEngine.Ecs.Entity[] entities = new global::CSharpGameEngine.Ecs.Entity[entityCount];

        for (int i = 0; i < entityCount; i++)
        {
            entities[i] = store.Create(CreateInfoFor(i));
        }

        return entities;
    }

    private static ParticleCreateInfo CreateInfoFor(int index)
    {
        return new ParticleCreateInfo
        {
            Transform = new LocalTransform { Position = new Vector3(index, 0f, 0f) },
            Velocity = new LinearVelocity { Value = new Vector3(1f, 0.5f, 0.25f) },
            Durability = new Durability { Value = 1000 },
        };
    }

    private static int[] BuildShuffledIndices(int count, int upperBound)
    {
        int[] order = new int[count];

        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        // 固定シードで再現性を確保する(実行ごとにアクセス順が変わると比較にならないため)。
        Random random = new(Seed: 12345);

        for (int i = count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        for (int i = 0; i < count; i++)
        {
            order[i] %= upperBound;
        }

        return order;
    }

    private static bool TryParseOptions(string[] args, out int entityCount, out int iterations, out string? error)
    {
        entityCount = DefaultEntityCount;
        iterations = DefaultIterations;
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--entities":
                    if (!TryReadPositiveInt(args, ref i, out entityCount))
                    {
                        error = "--entities must be a positive integer.";
                        return false;
                    }

                    break;

                case "--iterations":
                    if (!TryReadPositiveInt(args, ref i, out iterations))
                    {
                        error = "--iterations must be a positive integer.";
                        return false;
                    }

                    break;

                default:
                    error = $"Unknown option: {args[i]}";
                    return false;
            }
        }

        return true;
    }

    private static bool TryReadPositiveInt(string[] args, ref int index, out int value)
    {
        value = 0;

        if (index + 1 >= args.Length)
        {
            return false;
        }

        index++;
        return int.TryParse(args[index], out value) && value > 0;
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: UserGameProject benchmark [--entities <n>] [--iterations <n>]");
        Console.WriteLine($"  --entities <n>     Number of particles to spawn. Default {DefaultEntityCount}.");
        Console.WriteLine($"  --iterations <n>   Measured iterations per scenario. Default {DefaultIterations}.");
    }
}
