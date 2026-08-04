namespace CSharpGameEngine.Generation;

/// <summary>
/// GenerateEntityGenerator(CSharpGameEngine.CodeAnalysis)が選択する、生成Entityの物理ストレージ方式。
/// この値はコンパイル時に属性引数から定数として読み取られ、実行時分岐には一切使われない。
/// </summary>
/// <remarks>
/// ビットフラグであり、両方を指定すると1つの宣言から両バックエンドが生成される(比較・計測用)。
/// その場合、Facade(ユーザーのpartial struct本体)は<see cref="Aos"/>側にのみ生成される
/// — partialプロパティは実装宣言を1つしか持てず、Facadeの<c>_store</c>フィールドも型を1つしか
/// 取れないため。詳細はADR-0024を参照。
/// </remarks>
[Flags]
public enum EntityStorageKind
{
    Aos = 1,
    EcsSoa = 2
}
