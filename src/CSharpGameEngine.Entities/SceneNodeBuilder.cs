namespace CSharpGameEngine.Entities;

/// <summary>
/// <see cref="SceneBuilder"/>参照。現在定義中の1ノードを表すAuthoring時のみのBuilder。
/// <c>Create</c>呼び出しのネストが親子関係を、<c>Set</c>呼び出しがComponentを表す — どちらも
/// <c>SceneDefinitionGenerator</c>が構文として解析するだけで、このクラス自身のメソッド本体は
/// 通常のRuntime Buildでは実行されない。
/// </summary>
public sealed class SceneNodeBuilder
{
    internal SceneNodeBuilder(SceneNodeSymbol symbol)
    {
        this.Symbol = symbol;
    }

    public SceneNodeSymbol Symbol { get; }

    public void Create(SceneNodeSymbol symbol, Action<SceneNodeBuilder> Definition)
    {
        ArgumentNullException.ThrowIfNull(Definition);

        SceneNodeBuilder Child = new(symbol);
        Definition(Child);
    }

    public void Set<T>(T Component)
        where T : struct
    {
        // Authoring時の記述用API。SceneDefinitionGeneratorはDefineの構文木を解析するだけで
        // これを実行しないため、ここでは意図的に何もしない。
    }
}
