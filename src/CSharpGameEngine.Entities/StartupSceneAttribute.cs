namespace CSharpGameEngine.Entities;

/// <summary>
/// このクラス内の<c>[SceneEntity]</c>プロパティを起動時Entityの宣言として<c>GameSystemGenerator</c>が
/// 認識する。手書きの<c>GameStartup : ISystem</c>(<c>Bootstrap.Done</c>判定 + <c>State.World.Create</c>)を
/// ユーザーが書く代わりに、Component初期値だけを宣言すればよいようにするためのAPI。
///
/// <para>
/// 生成側は<c>Order = 0</c>の<c>{Bootstrap}</c>を対象とする合成Systemとして、既存の
/// <c>[SystemOrder]</c>(<see cref="SystemOrderAttribute"/>)と同じ実行順序モデルへ合流する。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class,AllowMultiple=false,Inherited=false)]
public sealed class StartupSceneAttribute:Attribute {
    /// <summary>
    /// この宣言から合成される起動Systemの実行順序。<see cref="SystemOrderAttribute"/>を
    /// <c>ISystem</c>実装struct自身へ直接付けた場合と同じ優先度で扱われる。
    /// </summary>
    public int Order { get; init;}
}
