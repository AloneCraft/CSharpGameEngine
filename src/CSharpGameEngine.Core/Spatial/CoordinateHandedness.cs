namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// <see cref="ICoordinateConvention"/> が宣言する座標系の左右系。これは対象とする外部ツール自身の規約についての正式なメタデータであり（ツールの説明に合わせる）、RUF/XYZ の軸マッピングだけから導出する値ではない。この値と <see cref="Spatial{TConvention}.Cross"/> の検証済みの関係は Docs/Specifications/Spatial-RUF.md の「Handedness の定義」を参照。
/// </summary>
public enum CoordinateHandedness {
    LeftHanded,
    RightHanded,
}
