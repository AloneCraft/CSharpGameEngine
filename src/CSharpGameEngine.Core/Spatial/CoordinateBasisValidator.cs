namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// 作成時/ビルド時に行う、<see cref="ICoordinateConvention"/> が宣言する物理基底の検証。ホットパスでは呼び出さない (Docs/Specifications/Spatial-RUF.md の「エラー処理」を参照)。
/// </summary>
public static class CoordinateBasisValidator {
    private const float LengthTolerance=1e-4f;
    private const float OrthogonalityTolerance=1e-4f;

    /// <summary>
    /// <typeparamref name="TConvention"/>のRight／Up／Forward軸が単位長でない、互いに直交しない、有限値でない、
    /// または同じ物理軸を重複して指す場合、<see cref="SpatialConfigurationException"/>をスローする。
    /// </summary>
    public static void Validate<TConvention>()
        where TConvention:struct,ICoordinateConvention {
        var Right=TConvention.RightXyz;
        var Up=TConvention.UpXyz;
        var Forward=TConvention.ForwardXyz;
        var ConventionName=typeof(TConvention).Name;

        ValidateFinite(Right,ConventionName,nameof(ICoordinateConvention.RightXyz));
        ValidateFinite(Up,ConventionName,nameof(ICoordinateConvention.UpXyz));
        ValidateFinite(Forward,ConventionName,nameof(ICoordinateConvention.ForwardXyz));

        ValidateUnitLength(Right,ConventionName,nameof(ICoordinateConvention.RightXyz));
        ValidateUnitLength(Up,ConventionName,nameof(ICoordinateConvention.UpXyz));
        ValidateUnitLength(Forward,ConventionName,nameof(ICoordinateConvention.ForwardXyz));

        ValidateOrthogonal(Right,Up,ConventionName,"Right","Up");
        ValidateOrthogonal(Up,Forward,ConventionName,"Up","Forward");
        ValidateOrthogonal(Forward,Right,ConventionName,"Forward","Right");

        if(Right==Up||Up==Forward||Forward==Right){
            throw new SpatialConfigurationException($"{ConventionName}: Right/Up/Forward must not duplicate the same physical axis (Right={Right}, Up={Up}, Forward={Forward}).");
        }
    }

    private static void ValidateFinite(XyzVector Axis,string ConventionName,string AxisName){
        if(!float.IsFinite(Axis.X)||!float.IsFinite(Axis.Y)||!float.IsFinite(Axis.Z)){
            throw new SpatialConfigurationException($"{ConventionName}.{AxisName} must be finite, but was {Axis}.");
        }
    }

    private static void ValidateUnitLength(XyzVector Axis,string ConventionName,string AxisName){
        if(MathF.Abs(Axis.Length-1f)>LengthTolerance){
            throw new SpatialConfigurationException($"{ConventionName}.{AxisName} must be unit length, but was {Axis} (length {Axis.Length}).");
        }
    }

    private static void ValidateOrthogonal(XyzVector A,XyzVector B,string ConventionName,string AName,string BName){
        var Dot=XyzVector.Dot(A,B);
        if(MathF.Abs(Dot)>OrthogonalityTolerance){
            throw new SpatialConfigurationException($"{ConventionName}: {AName} and {BName} must be orthogonal, but their dot product was {Dot}.");
        }
    }
}
