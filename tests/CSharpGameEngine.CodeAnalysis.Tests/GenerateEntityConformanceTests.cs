using System.Reflection;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// 見本(samples/AosSoaGeneratedExpansion/AosSoaGeneratedExpansion.SingleFile.cs)が示す観測可能な挙動を、
/// 見本コードを直接参照せず、実際のGenerateEntityGenerator出力に対して両Backend共通のシナリオとして
/// 再現する。<c>CharacterStore</c>のコンストラクタ形状(AoS: 引数なし、SoA: Worldを受け取る)は
/// バックエンド間で意図的に異なる(ADR-0024)ため、TestEntryソースはKindごとに1つずつ用意し、
/// 同じメソッド名の組が両方に存在すること自体で「公開されるふるまいが両Backendで同じ」ことを示す。
/// </summary>
public sealed class GenerateEntityConformanceTests
{
    [Theory]
    [InlineData("Aos")]
    [InlineData("EcsSoa")]
    public void Spawn_Move_ApplyDamage_IsDead_MatchSampleObservableBehavior(string kindMemberName)
    {
        Assert.Equal("OK", RunTestEntryMethod(kindMemberName, "RunSpawnMoveApplyDamageIsDead"));
    }

    [Theory]
    [InlineData("Aos")]
    [InlineData("EcsSoa")]
    public void Generation_IncrementsWhenIdIsReused(string kindMemberName)
    {
        Assert.Equal("OK", RunTestEntryMethod(kindMemberName, "RunGenerationIncrementsOnReuse"));
    }

    [Theory]
    [InlineData("Aos")]
    [InlineData("EcsSoa")]
    public void StaleHandle_IsRejectedAfterIdReuse(string kindMemberName)
    {
        Assert.Equal("OK", RunTestEntryMethod(kindMemberName, "RunStaleHandleRejection"));
    }

    [Theory]
    [InlineData("Aos")]
    [InlineData("EcsSoa")]
    public void CopiedHandle_PointsAtSameEntity(string kindMemberName)
    {
        Assert.Equal("OK", RunTestEntryMethod(kindMemberName, "RunCopiedHandleSharesEntity"));
    }

    [Theory]
    [InlineData("Aos")]
    [InlineData("EcsSoa")]
    public void BatchLoop_MoveAllEquivalent_MatchesPerEntityMove(string kindMemberName)
    {
        Assert.Equal("OK", RunTestEntryMethod(kindMemberName, "RunMoveAllEquivalentMatchesPerEntityMove"));
    }

    [Fact]
    public void AosBackend_GeneratesDenseRowArrayPhysicalType_NotArchetypeChunkTypes()
    {
        Assembly assembly = CompileForKind("Aos");

        Assert.NotNull(assembly.GetType("Sample.CharacterAosGeneratedData"));
        Assert.Null(assembly.GetType("Sample.CharacterSoaGeneratedArchetype"));
    }

    [Fact]
    public void SoaBackend_GeneratesArchetypeColumnTypes_NotAosRowArrayType()
    {
        Assembly assembly = CompileForKind("EcsSoa");

        Assert.NotNull(assembly.GetType("Sample.CharacterSoaGeneratedArchetype"));
        Assert.Null(assembly.GetType("Sample.CharacterAosGeneratedData"));
    }

    private static object? RunTestEntryMethod(string kindMemberName, string methodName)
    {
        Assembly assembly = CompileForKind(kindMemberName);
        Type type = assembly.GetType("Sample.TestEntry") ?? throw new InvalidOperationException("Sample.TestEntry not found.");
        MethodInfo method = type.GetMethod(methodName) ?? throw new InvalidOperationException($"Method '{methodName}' not found.");
        return method.Invoke(null, null);
    }

    private static Assembly CompileForKind(string kindMemberName)
    {
        string characterAndTestEntrySource = kindMemberName == "Aos"
            ? CharacterAndTestEntrySourceAos
            : CharacterAndTestEntrySourceSoa;

        return GenerateEntityGeneratorTestHelper.CompileAndLoad(
            GenerateEntityGeneratorTestHelper.CommonComponents
            + GenerateEntityGeneratorTestHelper.EntityGenerationSettings(kindMemberName)
            + characterAndTestEntrySource);
    }

    private const string CharacterDeclarationAndDomainMethods = """


        [GenerateEntity(EntityGenerationSettings.Kind)]
        public partial struct Character
        {
            public partial LocalTransform Transform { get; set; }
            public partial Velocity Velocity { get; set; }
            public partial Health Health { get; set; }

            public void Move(float deltaTime)
            {
                ref LocalTransform transform = ref this.TransformRef;
                ref Velocity velocity = ref this.VelocityRef;
                transform.X += velocity.X * deltaTime;
            }

            public static Character Spawn(CharacterStore store, float positionX, float velocityX, int initialHealth)
            {
                return store.Create(new CharacterCreateInfo
                {
                    Transform = new LocalTransform { X = positionX, Y = 0, Z = 0 },
                    Velocity = new Velocity { X = velocityX, Y = 0, Z = 0 },
                    Health = new Health { Value = initialHealth }
                });
            }

            public static void ApplyDamage(Character target, int damage)
            {
                ref Health health = ref target.HealthRef;
                health.Value = System.Math.Max(0, health.Value - System.Math.Max(0, damage));
            }

            public static bool IsDead(Character target)
            {
                return target.Health.Value <= 0;
            }
        }
        """;

    private const string SharedTestEntryScenarios = """

                public static string RunSpawnMoveApplyDamageIsDead()
                {
                    CharacterStore store = CreateStore();

                    Character player = Character.Spawn(store, positionX: 0, velocityX: 1, initialHealth: 100);
                    Character enemy = Character.Spawn(store, positionX: 10, velocityX: -1, initialHealth: 50);

                    player.Move(0.5f);
                    if (player.Transform.X != 0.5f) return "FAIL:PlayerMove";

                    Character.ApplyDamage(enemy, 20);
                    if (enemy.Health.Value != 30) return "FAIL:EnemyDamage";
                    if (Character.IsDead(enemy)) return "FAIL:EnemyShouldNotBeDeadYet";

                    Character.ApplyDamage(enemy, 1000);
                    if (!Character.IsDead(enemy)) return "FAIL:EnemyShouldBeDead";

                    return "OK";
                }

                public static string RunGenerationIncrementsOnReuse()
                {
                    CharacterStore store = CreateStore();
                    Character first = Character.Spawn(store, positionX: 0, velocityX: 0, initialHealth: 1);
                    int firstId = first.Entity.Id;
                    int firstGeneration = first.Entity.Generation;

                    first.Destroy();
                    Character second = Character.Spawn(store, positionX: 0, velocityX: 0, initialHealth: 1);

                    if (second.Entity.Id != firstId) return "FAIL:IdShouldBeReused";
                    if (second.Entity.Generation == firstGeneration) return "FAIL:GenerationShouldIncrement";

                    return "OK";
                }

                public static string RunStaleHandleRejection()
                {
                    CharacterStore store = CreateStore();
                    Character character = Character.Spawn(store, positionX: 0, velocityX: 0, initialHealth: 1);
                    character.Destroy();

                    bool threw = false;
                    try
                    {
                        character.Move(1f);
                    }
                    catch (System.InvalidOperationException)
                    {
                        threw = true;
                    }

                    return threw ? "OK" : "FAIL:StaleHandleShouldThrow";
                }

                public static string RunCopiedHandleSharesEntity()
                {
                    CharacterStore store = CreateStore();
                    Character original = Character.Spawn(store, positionX: 0, velocityX: 0, initialHealth: 1);
                    Character copy = original;

                    return original.Entity == copy.Entity ? "OK" : "FAIL:CopyShouldShareEntity";
                }
        """;

    private const string CharacterAndTestEntrySourceAos = CharacterDeclarationAndDomainMethods + """


        public static class TestEntry
        {
            private static CharacterStore CreateStore()
            {
                return new CharacterStore();
            }
        """
        + SharedTestEntryScenarios + """

                public static string RunMoveAllEquivalentMatchesPerEntityMove()
                {
                    CharacterStore storeA = CreateStore();
                    Character a1 = Character.Spawn(storeA, positionX: 0, velocityX: 1f, initialHealth: 1);
                    Character a2 = Character.Spawn(storeA, positionX: 0, velocityX: 2f, initialHealth: 1);
                    a1.Move(1f);
                    a2.Move(1f);

                    CharacterStore storeB = CreateStore();
                    Character b1 = Character.Spawn(storeB, positionX: 0, velocityX: 1f, initialHealth: 1);
                    Character b2 = Character.Spawn(storeB, positionX: 0, velocityX: 2f, initialHealth: 1);

                    if (!storeB.TryGetChunk(out CharacterChunkView view)) return "FAIL:NoChunk";

                    for (int row = 0; row < view.Count; row++)
                    {
                        view.Rows[row].Transform.X += view.Rows[row].Velocity.X * 1f;
                    }

                    if (a1.Transform.X != b1.Transform.X) return "FAIL:FirstMismatch";
                    if (a2.Transform.X != b2.Transform.X) return "FAIL:SecondMismatch";

                    return "OK";
                }
        }
        """;

    private const string CharacterAndTestEntrySourceSoa = CharacterDeclarationAndDomainMethods + """


        public static class TestEntry
        {
            private static CSharpGameEngine.Ecs.World world = new CSharpGameEngine.Ecs.World();

            private static CharacterStore CreateStore()
            {
                return new CharacterStore(world);
            }
        """
        + SharedTestEntryScenarios + """

                public static string RunMoveAllEquivalentMatchesPerEntityMove()
                {
                    CharacterStore storeA = new CharacterStore(new CSharpGameEngine.Ecs.World());
                    Character a1 = Character.Spawn(storeA, positionX: 0, velocityX: 1f, initialHealth: 1);
                    Character a2 = Character.Spawn(storeA, positionX: 0, velocityX: 2f, initialHealth: 1);
                    a1.Move(1f);
                    a2.Move(1f);

                    CharacterStore storeB = new CharacterStore(new CSharpGameEngine.Ecs.World());
                    Character b1 = Character.Spawn(storeB, positionX: 0, velocityX: 1f, initialHealth: 1);
                    Character b2 = Character.Spawn(storeB, positionX: 0, velocityX: 2f, initialHealth: 1);

                    if (!storeB.TryGetChunk(out CharacterChunkView view)) return "FAIL:NoChunk";

                    for (int row = 0; row < view.Count; row++)
                    {
                        view.Transform[row].X += view.Velocity[row].X * 1f;
                    }

                    if (a1.Transform.X != b1.Transform.X) return "FAIL:FirstMismatch";
                    if (a2.Transform.X != b2.Transform.X) return "FAIL:SecondMismatch";

                    return "OK";
                }
        }
        """;
}
