namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// 作成/インポート/ビルド時の座標設定エラー（無効な基底、直交しない軸、長さ 0 の軸、重複した軸、退化した LookRotation 入力、無効なインポート単位スケール、有限でない値）に対してスローする。ホットパスのフレームごとの変換では意図的にスローしない (Docs/Specifications/Spatial-RUF.md の「エラー処理」を参照)。
/// </summary>
public sealed class SpatialConfigurationException:Exception {
    public SpatialConfigurationException(string Message)
        :base(Message){
    }

    public SpatialConfigurationException(string Message,Exception InnerException)
        :base(Message,InnerException){
    }
}
