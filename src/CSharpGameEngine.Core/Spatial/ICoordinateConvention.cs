namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// 1 つのプロジェクトプリセットにおける、意味上の <see cref="RufVector"/> と物理 <see cref="XyzVector"/> 格納形式の対応、および宣言された <see cref="CoordinateHandedness"/> を表す。
/// 実装は状態を持たない <c>readonly struct</c> でなければならない。そうすることで <see cref="Spatial{TConvention}"/> は各メンバーをコンパイル時に単相化された静的呼び出しとして解決できる。実行時の分岐、リフレクション、ボックス化、Service Locator、グローバルな可変設定は使わない (Docs/Specifications/Spatial-RUF.md の「パフォーマンス要件」/「コード品質」を参照)。
/// </summary>
public interface ICoordinateConvention {
    static abstract CoordinateHandedness Handedness { get;}

    static abstract XyzVector ToXyz(in RufVector Value);

    static abstract RufVector FromXyz(in XyzVector Value);

    static abstract XyzVector RightXyz { get;}

    static abstract XyzVector UpXyz { get;}

    static abstract XyzVector ForwardXyz { get;}
}
