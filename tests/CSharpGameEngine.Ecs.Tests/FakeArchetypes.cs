namespace CSharpGameEngine.Ecs.Tests;

/// <summary>
/// EcsArchetypeGeneratorが生成する内容を、実際にはGeneratorを使わず手書きで再現したFakeアーキタイプ群。
/// CSharpGameEngine.Ecs.Testsはランタイム単体を対象とするため、Generator自体の検証は
/// CSharpGameEngine.CodeAnalysis.Tests側で行う。
/// </summary>
internal readonly struct FakeHealthOnly : IEcsArchetype<FakeHealthOnly>
{
    private readonly World world;
    private readonly Entity entity;

    private FakeHealthOnly(World world, Entity entity)
    {
        this.world = world;
        this.entity = entity;
    }

    public Entity Entity => this.entity;

    public ref FakeHealth Health => ref this.world.GetComponentRef<FakeHealth>(this.entity, 0);

    public static Type[] ComponentTypes { get; } = [typeof(FakeHealth)];

    public static IComponentColumn[] CreateColumns()
    {
        return [new ComponentColumn<FakeHealth>()];
    }

    public static FakeHealthOnly CreateView(World world, Entity entity)
    {
        return new FakeHealthOnly(world, entity);
    }
}

/// <summary>
/// 正規化後の構成(FakePosition, FakeVelocity)が<see cref="FakeMovingObjectB"/>と一致する、
/// ストレージ共有検証用のFakeアーキタイプ。
/// </summary>
internal readonly struct FakeMovingObjectA : IEcsArchetype<FakeMovingObjectA>
{
    private readonly World world;
    private readonly Entity entity;

    private FakeMovingObjectA(World world, Entity entity)
    {
        this.world = world;
        this.entity = entity;
    }

    public Entity Entity => this.entity;

    public ref FakePosition Position => ref this.world.GetComponentRef<FakePosition>(this.entity, 0);

    public ref FakeVelocity Velocity => ref this.world.GetComponentRef<FakeVelocity>(this.entity, 1);

    public static Type[] ComponentTypes { get; } = [typeof(FakePosition), typeof(FakeVelocity)];

    public static IComponentColumn[] CreateColumns()
    {
        return [new ComponentColumn<FakePosition>(), new ComponentColumn<FakeVelocity>()];
    }

    public static FakeMovingObjectA CreateView(World world, Entity entity)
    {
        return new FakeMovingObjectA(world, entity);
    }
}

/// <summary>
/// <see cref="FakeMovingObjectA"/>と正規化後の構成が完全に一致する、別名アーキタイプ。
/// </summary>
internal readonly struct FakeMovingObjectB : IEcsArchetype<FakeMovingObjectB>
{
    private readonly World world;
    private readonly Entity entity;

    private FakeMovingObjectB(World world, Entity entity)
    {
        this.world = world;
        this.entity = entity;
    }

    public Entity Entity => this.entity;

    public ref FakePosition Position => ref this.world.GetComponentRef<FakePosition>(this.entity, 0);

    public ref FakeVelocity Velocity => ref this.world.GetComponentRef<FakeVelocity>(this.entity, 1);

    public static Type[] ComponentTypes { get; } = [typeof(FakePosition), typeof(FakeVelocity)];

    public static IComponentColumn[] CreateColumns()
    {
        return [new ComponentColumn<FakePosition>(), new ComponentColumn<FakeVelocity>()];
    }

    public static FakeMovingObjectB CreateView(World world, Entity entity)
    {
        return new FakeMovingObjectB(world, entity);
    }
}
