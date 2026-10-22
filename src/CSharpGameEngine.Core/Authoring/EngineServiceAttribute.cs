namespace CSharpGameEngine.Authoring;
/// <summary>
/// System の引数に書けるサービスのクラス(ADR-0079)。ゲームやプラグインが足す。
/// Source Generator が最初に使うときに1つ作り、同じゲームの全 System へ同じものを渡す。<see cref="IDisposable"/> なら Shutdown で破棄する。
/// コンストラクターは引数なし、または <c>EngineHost</c> を1つ取るもの。
/// </summary>
[AttributeUsage(AttributeTargets.Class,AllowMultiple=false,Inherited=false)]
public sealed class EngineServiceAttribute:Attribute {
}
