namespace CSharpGameEngine.Authoring;
/// <summary>
/// プラグインの Library モジュール(DLL)だと名乗る(ADR-0079)。付けた DLL の公開 struct は、シーンの Component の候補として
/// Scene Designer のパレットとシーン JSON のスキーマに出る。Library は <c>World</c>・<c>State</c>・<c>Entity</c>・<c>ISystem</c> を名指ししない。
/// </summary>
[AttributeUsage(AttributeTargets.Assembly,AllowMultiple=false,Inherited=false)]
public sealed class PluginLibraryAttribute:Attribute {
}
