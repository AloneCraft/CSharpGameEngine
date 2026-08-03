using System.Reflection;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// EcsArchetypeGeneratorが出力したコードを実際にコンパイル・Emit・ロードし、リフレクションで実行して
/// 結果をアサートする。partial refプロパティ・static abstractディスパッチ・マーカーインターフェースに
/// よる拡張メソッド曖昧性解消が、Generatorが実際に出力するコードで機能することを証明する最重要テスト。
/// </summary>
public sealed class EcsArchetypeGeneratorEndToEndTests
{
    [Fact]
    public void UserSample_CompilesAndExecutesCorrectly()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct Player
            {
                // わざと正規化順とは異なる順番で書いている
                public partial ref PlayerTag PlayerTag { get; }
                public partial ref Velocity Velocity { get; }
                public partial ref Health Health { get; }
                public partial ref Rotation Rotation { get; }
                public partial ref Position Position { get; }
            }

            public static class TestEntry
            {
                public static string RunPlayerScenario()
                {
                    World world = new World();
                    Player player = world.Create<Player>();

                    player.Position = new Position { X = 10, Y = 2, Z = 5 };
                    player.Velocity.X = 3;
                    player.Health.Value = 100;

                    if (player.Position.X != 10 || player.Position.Y != 2 || player.Position.Z != 5) return "FAIL:Position";
                    if (player.Velocity.X != 3) return "FAIL:Velocity";
                    if (player.Health.Value != 100) return "FAIL:Health";
                    return "OK";
                }
            }
            """;

        Assembly assembly = EcsArchetypeGeneratorTestHelper.CompileAndLoad(source);
        object? result = Invoke(assembly, "Sample.TestEntry", "RunPlayerScenario");

        Assert.Equal("OK", result);
    }

    [Fact]
    public void NamedArgumentConstructor_BulletAndMovingObjectSamples_CompileAndExecuteCorrectly()
    {
        string source = EcsArchetypeGeneratorTestHelper.CommonComponents + """


            [EcsArchetype]
            public partial struct Bullet
            {
                public partial ref Position Position { get; }
                public partial ref Velocity Velocity { get; }
                public partial ref Damage Damage { get; }
            }

            // 正規化後に同じ形状(Position, Velocity)になる別名アーキタイプ。
            // 名前付き引数版ファクトリのオーバーロード解決がマーカーインターフェース制約だけで
            // 一意に決まることを実際にコンパイル・実行して証明する。
            [EcsArchetype]
            public partial struct MovingObjectA
            {
                public partial ref Velocity Velocity { get; }
                public partial ref Position Position { get; }
            }

            [EcsArchetype]
            public partial struct MovingObjectB
            {
                public partial ref Position Position { get; }
                public partial ref Velocity Velocity { get; }
            }

            public static class TestEntry
            {
                public static string RunScenario()
                {
                    World world = new World();

                    Bullet bullet = world.Create<Bullet>(
                        position: new Position { X = 0, Y = 1, Z = 0 },
                        velocity: new Velocity { X = 0, Y = 0, Z = 20 },
                        damage: new Damage { Value = 10 });

                    if (bullet.Position.Y != 1) return "FAIL:BulletPosition";
                    if (bullet.Velocity.Z != 20) return "FAIL:BulletVelocity";
                    if (bullet.Damage.Value != 10) return "FAIL:BulletDamage";

                    MovingObjectA moa = world.Create<MovingObjectA>(velocity: new Velocity { X = 1 }, position: new Position { X = 2 });
                    MovingObjectB mob = world.Create<MovingObjectB>(position: new Position { X = 3 }, velocity: new Velocity { X = 4 });

                    if (moa.Position.X != 2 || moa.Velocity.X != 1) return "FAIL:MovingObjectA";
                    if (mob.Position.X != 3 || mob.Velocity.X != 4) return "FAIL:MovingObjectB";

                    return "OK";
                }
            }
            """;

        Assembly assembly = EcsArchetypeGeneratorTestHelper.CompileAndLoad(source);
        object? result = Invoke(assembly, "Sample.TestEntry", "RunScenario");

        Assert.Equal("OK", result);
    }

    private static object? Invoke(Assembly assembly, string typeFullName, string methodName)
    {
        Type type = assembly.GetType(typeFullName) ?? throw new InvalidOperationException($"Type '{typeFullName}' not found.");
        MethodInfo method = type.GetMethod(methodName) ?? throw new InvalidOperationException($"Method '{methodName}' not found on '{typeFullName}'.");
        return method.Invoke(null, null);
    }
}
