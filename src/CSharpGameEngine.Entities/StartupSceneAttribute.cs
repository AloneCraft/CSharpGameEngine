namespace CSharpGameEngine.Entities;

/// <summary>
/// このクラス内の<c>[SceneEntity]</c>プロパティを起動時Entityの宣言として<c>GameSystemGenerator</c>が
/// 認識する。手書きの<c>GameStartup : ISystem</c>(<c>Bootstrap.Done</c>判定 + <c>State.World.Create</c>)を
/// ユーザーが書く代わりに、Component初期値だけを宣言すればよいようにするためのAPI。
///
/// <para>
/// 生成側は<c>{Bootstrap}</c>を対象とする合成Systemを作り、FrameInputの他のSystemより前に、
/// <see cref="Order"/>→完全修飾名の順で動かす(ADR-0073決定事項4)。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class,AllowMultiple=false,Inherited=false)]
public sealed class StartupSceneAttribute:Attribute {
    /// <summary>起動シーン同士の順序(シーン JSON の<c>startupOrder</c>)。小さいほうが先に動く。</summary>
    public int Order { get; init;}
}
