namespace CSharpGameEngine.Entities;

/// <summary>
/// Source Generatorが<c>IScene</c>実装クラスへ実装する(ADR-0040)。<b>ユーザーは書かない。</b>
///
/// <para>
/// <c>static abstract</c>にしているのは、<c>World.InstantiateScene&lt;TScene&gt;()</c>を
/// <b>reflectionにも<c>Dictionary&lt;Type,…&gt;</c>にも頼らずに</b>解決するため。制約付き呼び出しは
/// JITが解決するので、型をキーにした動的ディスパッチは発生しない。
/// </para>
///
/// <para>
/// <c>TScene</c>は参照型なので共有ジェネリックコードになり、呼び出しはジェネリックディスパッチスタブを
/// 経由する。Sceneのロードは1シーンにつき1回なので許容範囲であり、ホットパスが必要なら生成された
/// <c>MainScene.Instantiate(ref World)</c>を直接呼べる(モノモーフィック)。
/// </para>
/// </summary>
public interface ISceneDefinition {
    /// <summary>このScene定義から新しい<see cref="SceneInstance"/>を構築する。</summary>
    static abstract SceneInstance Instantiate(ref World World);
}
