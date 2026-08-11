using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Blender;
using CSharpGameEngine.Spatial.Unity;
using CSharpGameEngine.Spatial.Unreal;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/RUF-Semantic-Coordinate-Specification-Prompt.md "エラー処理": invalid basis, non-orthogonal
/// axes, zero-length axes, non-finite values must be clear Authoring/Build-time errors.
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
    /// Reverse-engineered behavior: any pair of literally duplicate unit axes has dot product 1, so
    /// <see cref="CoordinateBasisValidator"/>'s orthogonality checks (which run before its explicit
    /// duplicate-axis check) always reject a duplicate basis first. The duplicate-axis-specific error
    /// message is therefore unreachable by construction - still an error, just always surfaced as an
    /// orthogonality violation. See Docs/Specifications/Spatial-RUF.md "エラー処理".
    /// </summary>
    [Fact]
    public void DuplicateAxes_ThrowsViaOrthogonalityCheck(){
        SpatialConfigurationException exception=Assert.Throws<SpatialConfigurationException>(CoordinateBasisValidator.Validate<DuplicateAxisConvention>);

        Assert.Contains("orthogonal",exception.Message,StringComparison.OrdinalIgnoreCase);
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
