namespace CSharpGameEngine.Ecs;

/// <summary>
/// 1つの正規化済みアーキタイプストレージ全体を指す、行ごとのオーバーヘッドがゼロのバッチビュー。
/// このエンジンのSoAストレージはページ化されたChunkを持たないため、
/// 1つの<see cref="ArchetypeStorage"/>がそのまま1つの(暗黙の)Chunkとして扱われる。
/// コンストラクタはinternalであり、<see cref="World.TryGetArchetypeChunk{T}"/>だけが生成できる。
/// </summary>
public readonly struct ArchetypeChunk
{
    private readonly ArchetypeStorage? storage;

    internal ArchetypeChunk(ArchetypeStorage? storage)
    {
        this.storage = storage;
    }

    public int Count => this.storage?.Count ?? 0;

    public ReadOnlySpan<Entity> Entities =>
        this.storage is null ? ReadOnlySpan<Entity>.Empty : this.storage.GetEntitiesSpan();

    public Span<T> GetColumnSpan<T>(int columnIndex) where T : struct
    {
        return this.storage is null ? Span<T>.Empty : this.storage.GetColumnSpan<T>(columnIndex);
    }
}
