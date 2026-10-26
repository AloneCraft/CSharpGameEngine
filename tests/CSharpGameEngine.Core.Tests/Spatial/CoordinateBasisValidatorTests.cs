using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Blender;
using CSharpGameEngine.Spatial.Unity;
using CSharpGameEngine.Spatial.Unreal;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/RUF-Semantic-Coordinate-Specification-Prompt.md の「エラー処理」：無効な基底、直交しない軸、長さ 0 の軸、有限でない値は、作成時/ビルド時の明確なエラーにしなければならない。
/// </summary>
public class CoordinateBasisValidatorTests {
    [Fact]
    public void Unity_ValidBasis_DoesNotThrow()=>CoordinateBasisValidator.Validate<UnityCoordinateConvention>();

    [Fact]
    public void Unreal_ValidBasis_DoesNotThrow()=>CoordinateBasisValidator.Validate<UnrealCoordinateConvention>();

    [Fact]
    public void Blender_ValidBasis_DoesNotThrow()=>CoordinateBasisValidator.Validate<BlenderWorldConvention>();

    [Fact]
    public void NonOrthogonalAxes_Throws()
        =>Assert.Throws<SpatialConfigurationException>(CoordinateBasisValidator.Validate<NonOrthogonalConvention>);

    [Fact]
    public void NonUnitLengthAxis_Throws()
        =>Assert.Throws<SpatialConfigurationException>(CoordinateBasisValidator.Validate<NonUnitLengthConvention>);

    [Fact]
    public void NonFiniteAxis_Throws()
        =>Assert.Throws<SpatialConfigurationException>(CoordinateBasisValidator.Validate<NonFiniteConvention>);

    /// <summary>
    /// リバースエンジニアリングで確認した動作：単位軸の組が文字どおり重複していると、どの組の内積も 1 になる。そのため、明示的な重複軸チェックより先に実行される <see cref="CoordinateBasisValidator"/> の直交性チェックが、必ず先に重複基底を拒否する。構造上、重複軸専用のエラーメッセージには到達できない。いずれもエラーではあるが、常に直交性違反として表面化する。Docs/Specifications/Spatial-RUF.md の「エラー処理」を参照。
    /// </summary>
    [Fact]
    public void DuplicateAxes_ThrowsViaOrthogonalityCheck(){
        var Exception=Assert.Throws<SpatialConfigurationException>(CoordinateBasisValidator.Validate<DuplicateAxisConvention>);

        Assert.Contains("orthogonal",Exception.Message,StringComparison.OrdinalIgnoreCase);
    }

    private readonly struct NonOrthogonalConvention:ICoordinateConvention {
        public static CoordinateHandedness Handedness=>CoordinateHandedness.RightHanded;
        public static XyzVector ToXyz(in RufVector Value)=>new(Value.R,Value.U,Value.F);
        public static RufVector FromXyz(in XyzVector Value)=>new(Value.X,Value.Y,Value.Z);
        public static XyzVector RightXyz=>new(1f,0f,0f);
        public static XyzVector UpXyz=>new(0.7071f,0.7071f,0f);
        public static XyzVector ForwardXyz=>new(0f,0f,1f);
    }

    private readonly struct NonUnitLengthConvention:ICoordinateConvention {
        public static CoordinateHandedness Handedness=>CoordinateHandedness.RightHanded;
        public static XyzVector ToXyz(in RufVector Value)=>new(Value.R,Value.U,Value.F);
        public static RufVector FromXyz(in XyzVector Value)=>new(Value.X,Value.Y,Value.Z);
        public static XyzVector RightXyz=>new(2f,0f,0f);
        public static XyzVector UpXyz=>new(0f,1f,0f);
        public static XyzVector ForwardXyz=>new(0f,0f,1f);
    }

    private readonly struct NonFiniteConvention:ICoordinateConvention {
        public static CoordinateHandedness Handedness=>CoordinateHandedness.RightHanded;
        public static XyzVector ToXyz(in RufVector Value)=>new(Value.R,Value.U,Value.F);
        public static RufVector FromXyz(in XyzVector Value)=>new(Value.X,Value.Y,Value.Z);
        public static XyzVector RightXyz=>new(float.NaN,0f,0f);
        public static XyzVector UpXyz=>new(0f,1f,0f);
        public static XyzVector ForwardXyz=>new(0f,0f,1f);
    }

    private readonly struct DuplicateAxisConvention:ICoordinateConvention {
        public static CoordinateHandedness Handedness=>CoordinateHandedness.RightHanded;
        public static XyzVector ToXyz(in RufVector Value)=>new(Value.R,Value.U,Value.F);
        public static RufVector FromXyz(in XyzVector Value)=>new(Value.X,Value.Y,Value.Z);
        public static XyzVector RightXyz=>new(1f,0f,0f);
        public static XyzVector UpXyz=>new(0f,1f,0f);
        public static XyzVector ForwardXyz=>new(1f,0f,0f);
    }
}
