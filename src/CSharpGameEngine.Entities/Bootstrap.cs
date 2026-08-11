namespace CSharpGameEngine.Entities;

/// <summary>
/// 生成されたエントリポイント(<c>CSharpGameEngine.Generated.GeneratedEntryPoint.Main</c>)が
/// 起動時に唯一作成するシードEntityのComponent。これを<c>ref</c>で受け取る<c>Update</c>が
/// 「起動時に一度だけ走る処理」になり、そこから<c>State.World.Create(...)</c>で残りのEntityを
/// すべて組み立てる。
///
/// <para>
/// <see cref="Done"/>を持つのは、Archetypeループが毎フレーム回るため。消費側は
/// <c>if (bootstrap.Done) return true; bootstrap.Done = true;</c>で一度きりを表現する
/// (<c>Destroy</c>は閉集合方式に存在しないため、フラグが唯一の手段)。
/// </para>
/// </summary>
public struct Bootstrap {
    public bool Done;
}
