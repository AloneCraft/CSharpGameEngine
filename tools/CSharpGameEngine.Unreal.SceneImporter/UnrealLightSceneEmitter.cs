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
    public static Vector3 CreateEulerRotationForLightTravelDirection(Vector3 DirectionTowardLight){
        ValidateFiniteNonZero(DirectionTowardLight,nameof(DirectionTowardLight));

        Vector3 TravelDirection=-Vector3.Normalize(DirectionTowardLight);
        float Dot=Math.Clamp(Vector3.Dot(Vector3.UnitZ,TravelDirection),-1f,1f);
        if(Dot>=1f-1e-6f){
            return Vector3.Zero;
        }
        if(Dot<=-1f+1e-6f){
            // +Y is the deterministic 180-degree axis that maps +Z to -Z.
            return new Vector3(0f,MathF.PI,0f);
        }

        Vector3 Axis=Vector3.Normalize(Vector3.Cross(Vector3.UnitZ,TravelDirection));
        Quaternion Rotation=Quaternion.CreateFromAxisAngle(Axis,MathF.Acos(Dot));
        return ToEulerRuf(Rotation.W<0f?Quaternion.Negate(Rotation):Rotation);
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

        Vector3 Euler=CreateEulerRotationForLightTravelDirection(DirectionTowardLight);
        Builder.Append(Indent).AppendLine("new Transform {");
        Builder.Append(Indent).AppendLine("    Position=new LogicalVector3(0f,0f,0f),");
        Builder.Append(Indent).Append("    Rotation=new LogicalQuaternion(new LogicalVector3(").Append(FormatFloat(Euler.X)).Append(',').Append(FormatFloat(Euler.Y)).Append(',').Append(FormatFloat(Euler.Z)).AppendLine(")),");
        Builder.Append(Indent).AppendLine("    Scale=new LogicalVector3(1f,1f,1f),");
        Builder.Append(Indent).AppendLine("},");
        Builder.Append(Indent).AppendLine("new global::CSharpGameEngine.Runtime.Lighting.DirectionalLight {");
        Builder.Append(Indent).Append("    Color=new global::CSharpGameEngine.Lighting.LinearLightColor(").Append(FormatFloat(LinearRgb.X)).Append(',').Append(FormatFloat(LinearRgb.Y)).Append(',').Append(FormatFloat(LinearRgb.Z)).AppendLine("),");
        Builder.Append(Indent).Append("    Illuminance=new global::CSharpGameEngine.Lighting.Illuminance(").Append(FormatFloat(IlluminanceLux)).AppendLine("),");
        Builder.Append(Indent).AppendLine("},");
        Builder.Append(Indent).AppendLine("new global::CSharpGameEngine.Runtime.Lighting.LightShadow { Priority=100,Bias=new global::CSharpGameEngine.Lighting.ShadowBias(0.005f) },");
    }

    private static Vector3 ToEulerRuf(Quaternion Rotation){
        Rotation=Quaternion.Normalize(Rotation);
        float Pitch=MathF.Asin(Math.Clamp(2f*(Rotation.W*Rotation.X-Rotation.Y*Rotation.Z),-1f,1f));
        float Yaw=MathF.Atan2(2f*(Rotation.W*Rotation.Y+Rotation.Z*Rotation.X),1f-2f*(Rotation.X*Rotation.X+Rotation.Y*Rotation.Y));
        float Roll=MathF.Atan2(2f*(Rotation.W*Rotation.Z+Rotation.X*Rotation.Y),1f-2f*(Rotation.X*Rotation.X+Rotation.Z*Rotation.Z));
        return new Vector3(Pitch,Yaw,Roll);
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
