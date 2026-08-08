namespace CSharpGameEngine.Entities;

/// <summary>
/// <see cref="EntityCommandBuffer"/>が値付きの<c>Add&lt;T&gt;(entity, value)</c>コマンドを記録する際、
/// コンポーネント型ごとに1つ生成される値の一時保管庫。型消去は<see cref="IComponentValueStore"/>
/// (<see cref="ComponentColumn{T}"/>と同じ「ジェネリッククラス+非ジェネリック基底」パターン)が担い、
/// <c>typeof(T)==typeof(X)</c>のような分岐やReflectionは使わない。
/// </summary>
internal interface IComponentValueStore
{
    void WriteValue(World world, Entity entity, int componentTypeId, int valueIndex);

    void Clear();
}

internal sealed class ComponentValueStore<T> : IComponentValueStore where T : struct
{
    private readonly List<T> values = [];

    internal int Add(in T value)
    {
        this.values.Add(value);
        return this.values.Count - 1;
    }

    public void WriteValue(World world, Entity entity, int componentTypeId, int valueIndex)
    {
        world.AddComponentValue(entity, componentTypeId, this.values[valueIndex]);
    }

    public void Clear()
    {
        this.values.Clear();
    }
}
