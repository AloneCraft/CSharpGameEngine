namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// 選択した 1 つの <typeparamref name="TConvention"/> を型で指定するエントリポイント。各メンバーは実行時にプリセットを選択せず、<typeparamref name="TConvention"/> の静的メンバーとしてコンパイル時に解決される。プリセットを 1 つだけ選択済みのゲームプロジェクト (Docs/Specifications/Spatial-RUF.md の「コンパイル時プリセット選択」を参照) は、<c>Spatial&lt;ThatPreset&gt;</c> に処理を転送する独自の非ジェネリックファサード（例：<c>ProjectSpatial</c>）を公開する。そうすれば、アプリケーションコードで <typeparamref name="TConvention"/> 自体を書く必要がない。
/// </summary>
public static class Spatial<TConvention>
    where TConvention:struct,ICoordinateConvention {
    public static XyzVector ToXyz(in RufVector Value)=>TConvention.ToXyz(Value);

    public static RufVector FromXyz(in XyzVector Value)=>TConvention.FromXyz(Value);

    /// <summary>
    /// RUF 空間の 2 つのベクトルの物理的に正しい外積：両方のオペランドをこの規約の物理 XYZ 格納形式に変換し、唯一の正しい（右手系の法則に従う）物理外積を適用してから、結果を RUF へ戻す。これが R/U/F 成分への単純な公式ではなく、規約の左右系を正しく反映する唯一の方法である理由と、検証済みの関係は Docs/Specifications/Spatial-RUF.md の「Handedness の定義」を参照。左手系の規約では <c>Cross(Right, Up) == Forward</c>、右手系の規約では <c>Cross(Right, Up) == Backward</c>。
    /// </summary>
    public static RufVector Cross(in RufVector Left,in RufVector Right){
        XyzVector CrossXyz=XyzVector.Cross(TConvention.ToXyz(Left),TConvention.ToXyz(Right));
        return TConvention.FromXyz(CrossXyz);
    }

    /// <summary>
    /// Up と Forward を受け取り、この規約の実際の左右系に一致する Right 方向を返す。<see cref="Cross"/> と異なり、左手系と右手系のどちらでも結果が必ず「右」を意味するよう符号を補正する。外積をどこでもそのまま公開することを避ける、範囲を限定した代替手段である (Docs/Specifications/Spatial-RUF.md の「Cross Product仕様」を参照)。
    /// </summary>
    public static RufVector RightFromUpForward(in RufVector Up,in RufVector Forward){
        RufVector RawCross=Cross(Up,Forward);
        return TConvention.Handedness==CoordinateHandedness.LeftHanded?RawCross:-RawCross;
    }
}
