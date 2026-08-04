namespace CSharpGameEngine.Generation;

/// <summary>
/// GenerateEntityGenerator(CSharpGameEngine.CodeAnalysis)が選択する、生成Entityの物理ストレージ方式。
/// この値はコンパイル時に属性引数から定数として読み取られ、実行時分岐には一切使われない。
/// </summary>
public enum EntityStorageKind
{
    ObjectOrientedAos,
    EcsSoa
}
