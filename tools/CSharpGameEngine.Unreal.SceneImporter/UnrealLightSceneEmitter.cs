using System.Globalization;
using System.Numerics;
using System.Text;

namespace CSharpGameEngine.Unreal.SceneImporter;

/// <summary>Unreal レベルインポーターが使用する、物理ライトのシーン宣言を生成する。</summary>
public static class UnrealLightSceneEmitter {
    /// <summary>
    /// 指向性ライトが進む方向にローカル +Z 軸を向ける回転を構築する。以前のシーンフィールド <c>DirectionTowardLight</c> は反対の、面から光源へ向かう規約を使っていたため、この境界で符号を反転しなければならない。
    /// </summary>
    public static Vector3 CreateEulerRotationForLightTravelDirection(Vector3 DirectionTowardLight){
        ValidateFiniteNonZero(DirectionTowardLight,nameof(DirectionTowardLight));

        Vector3 TravelDirection=-Vector3.Normalize(DirectionTowardLight);
        float Dot=Math.Clamp(Vector3.Dot(Vector3.UnitZ,TravelDirection),-1f,1f);
        if(Dot>=1f-1e-6f){
            return Vector3.Zero;
        }
        if(Dot<=-1f+1e-6f){
            // +Z を -Z へ対応させる、決定論的な 180 度回転軸は +Y。
            return new Vector3(0f,MathF.PI,0f);
        }

        Vector3 Axis=Vector3.Normalize(Vector3.Cross(Vector3.UnitZ,TravelDirection));
        Quaternion Rotation=Quaternion.CreateFromAxisAngle(Axis,MathF.Acos(Dot));
        return ToEulerRuf(Rotation.W<0f?Quaternion.Negate(Rotation):Rotation);
    }

    /// <summary>
    /// <c>Transform</c> の後に、物理特性を持つ <c>DirectionalLight</c> 宣言を追加する。
    /// KHR_lights_punctual では指向性ライトの強度を lux 単位の照度として定義しているため、任意の旧式強度スケールを掛けず、ソース値を <c>IlluminanceLux</c> に保持する。
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

    /// <summary>
    /// ソースノードのワールド前方、つまりライトが進む方向 (<c>ImportedLightDirection.WorldForward</c>) から指向性ライトを追加する。インポーターはこのオーバーロードを使わなければならない。進行方向を面から光源へ向かうベクトルとして <see cref="AppendDirectionalLightComponents"/> に渡すと、太陽が空を向いてしまう。
    /// </summary>
    public static void AppendDirectionalLightComponentsFromTravelDirection(
        StringBuilder Builder,Vector3 TravelDirection,Vector3 LinearRgb,float IlluminanceLux,string Indent)=>
        AppendDirectionalLightComponents(Builder,-TravelDirection,LinearRgb,IlluminanceLux,Indent);

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
