namespace CSharpGameEngine.Entities;

/// <summary>
/// <c>[SceneDefinition]</c>クラスの<c>Define</c>メソッドをコンパイル可能にするためだけの
/// Authoring API。<c>SceneDefinitionGenerator</c>は<c>Define</c>を実行せず、その構文木を
/// 静的解析してRuntime構築コードを直接生成するため、通常のRuntime Buildではこのクラスの
/// メソッド本体は一切実行されない(Editor Preview等でインタープリター的に<c>Define</c>を
/// 直接呼び出す将来用途のための余地として存在する)。
/// </summary>
public sealed class SceneBuilder {
    public void Create(SceneNodeSymbol Symbol,Action<SceneNodeBuilder>Definition){
        ArgumentNullException.ThrowIfNull(Definition);

        SceneNodeBuilder Builder=new(Symbol);
        Definition(Builder);
    }
}
