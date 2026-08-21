using System.Globalization;
using System.Numerics;
using System.Text;

namespace CSharpGameEngine.Unreal.SceneImporter;

/// <summary>
/// Emits the physical-light scene declarations used by the Unreal level importer.
/// </summary>
public static class UnrealLightSceneEmitter {
    /// <summary>
    /// Builds the rotation whose local +Z axis is the direction in which a directional light
    /// travels. The former scene field named <c>DirectionTowardLight</c> used the opposite,
    /// surface-to-light convention, so it must be negated at this boundary.
    /// </summary>
    public static Quaternion CreateRotationForLightTravelDirection(Vector3 DirectionTowardLight){
        ValidateFiniteNonZero(DirectionTowardLight,nameof(DirectionTowardLight));

        Vector3 TravelDirection=-Vector3.Normalize(DirectionTowardLight);
        float Dot=Math.Clamp(Vector3.Dot(Vector3.UnitZ,TravelDirection),-1f,1f);
        if(Dot>=1f-1e-6f){
            return Quaternion.Identity;
        }
        if(Dot<=-1f+1e-6f){
            // +Y is the deterministic 180-degree axis that maps +Z to -Z.
            return new Quaternion(0f,1f,0f,0f);
        }

        Vector3 Axis=Vector3.Normalize(Vector3.Cross(Vector3.UnitZ,TravelDirection));
        Quaternion Rotation=Quaternion.CreateFromAxisAngle(Axis,MathF.Acos(Dot));
        return Rotation.W<0f?Quaternion.Negate(Rotation):Rotation;
    }

    /// <summary>
    /// Appends a <c>Transform</c> followed by a physical <c>DirectionalLight</c> declaration.
    /// KHR_lights_punctual defines directional intensity as illuminance in lux, so the source
    /// value is preserved in <c>IlluminanceLux</c> without an arbitrary legacy-intensity scale.
    /// </summary>
    public static void AppendDirectionalLightComponents(
        StringBuilder Builder,Vector3 DirectionTowardLight,Vector3 LinearRgb,float IlluminanceLux,string Indent){
        ArgumentNullException.ThrowIfNull(Builder);
        ArgumentNullException.ThrowIfNull(Indent);
        ValidateFiniteNonZero(DirectionTowardLight,nameof(DirectionTowardLight));
        ValidateFiniteNonNegative(LinearRgb,nameof(LinearRgb));
        if(!float.IsFinite(IlluminanceLux)||IlluminanceLux<0f){
            throw new ArgumentOutOfRangeException(nameof(IlluminanceLux),"Directional illuminance must be finite and non-negative lux.");
        }

        Quaternion Rotation=CreateRotationForLightTravelDirection(DirectionTowardLight);
        Builder.Append(Indent).AppendLine("new Transform {");
        Builder.Append(Indent).Append("    PositionX=0f,").AppendLine();
        Builder.Append(Indent).Append("    PositionY=0f,").AppendLine();
        Builder.Append(Indent).Append("    PositionZ=0f,").AppendLine();
        Builder.Append(Indent).Append("    RotationX=").Append(FormatFloat(Rotation.X)).AppendLine(",");
        Builder.Append(Indent).Append("    RotationY=").Append(FormatFloat(Rotation.Y)).AppendLine(",");
        Builder.Append(Indent).Append("    RotationZ=").Append(FormatFloat(Rotation.Z)).AppendLine(",");
        Builder.Append(Indent).Append("    RotationW=").Append(FormatFloat(Rotation.W)).AppendLine(",");
        Builder.Append(Indent).Append("    ScaleX=1f,").AppendLine();
        Builder.Append(Indent).Append("    ScaleY=1f,").AppendLine();
        Builder.Append(Indent).Append("    ScaleZ=1f,").AppendLine();
        Builder.Append(Indent).AppendLine("},");
        Builder.Append(Indent).AppendLine("new DirectionalLight {");
        Builder.Append(Indent).Append("    ColorR=").Append(FormatFloat(LinearRgb.X)).AppendLine(",");
        Builder.Append(Indent).Append("    ColorG=").Append(FormatFloat(LinearRgb.Y)).AppendLine(",");
        Builder.Append(Indent).Append("    ColorB=").Append(FormatFloat(LinearRgb.Z)).AppendLine(",");
        Builder.Append(Indent).Append("    IlluminanceLux=").Append(FormatFloat(IlluminanceLux)).AppendLine(",");
        Builder.Append(Indent).AppendLine("    CastsShadows=true,");
        Builder.Append(Indent).AppendLine("    ShadowPriority=100,");
        Builder.Append(Indent).AppendLine("    ShadowBiasMeters=0.005f,");
        Builder.Append(Indent).AppendLine("},");
    }

    private static string FormatFloat(float Value)=>Value==0f
        ?"0f"
        :Value.ToString("R",CultureInfo.InvariantCulture)+"f";

    private static void ValidateFiniteNonZero(Vector3 Value,string ParameterName){
        if(!float.IsFinite(Value.X)||!float.IsFinite(Value.Y)||!float.IsFinite(Value.Z)||Value.LengthSquared()<=1e-12f){
            throw new ArgumentOutOfRangeException(ParameterName,"The light direction must be finite and non-zero.");
        }
    }

    private static void ValidateFiniteNonNegative(Vector3 Value,string ParameterName){
        if(!float.IsFinite(Value.X)||!float.IsFinite(Value.Y)||!float.IsFinite(Value.Z)
            ||Value.X<0f||Value.Y<0f||Value.Z<0f){
            throw new ArgumentOutOfRangeException(ParameterName,"The linear RGB color must be finite and non-negative.");
        }
    }
}
