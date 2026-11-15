using System.Globalization;
using System.Numerics;
using System.Text;
namespace CSharpGameEngine.Unreal.SceneImporter;
/// <summary>生成するC# Scene DSLの数値・Transform・環境Componentの書式。FirstPerson/ThirdPersonの両取り込みで共有する。</summary>
internal static class SceneSourceFormatting {
    internal static void AppendAmbient(StringBuilder Builder,ImportAmbient Ambient){
        Builder.AppendLine("            new AmbientLight {");
        Builder.Append("                ColorR=").Append(F(Ambient.ColorRgb[0])).AppendLine(",");
        Builder.Append("                ColorG=").Append(F(Ambient.ColorRgb[1])).AppendLine(",");
        Builder.Append("                ColorB=").Append(F(Ambient.ColorRgb[2])).AppendLine(",");
        Builder.Append("                Intensity=").Append(F(Ambient.Intensity)).AppendLine(",");
        Builder.AppendLine("            },");
    }
    internal static void AppendFog(StringBuilder Builder,ImportFog Fog){
        Builder.AppendLine("            new HeightFog {");
        Builder.Append("                Density=").Append(F(Fog.Density)).AppendLine(",");
        Builder.Append("                HeightFalloff=").Append(F(Fog.HeightFalloff)).AppendLine(",");
        Builder.Append("                ColorR=").Append(F(Fog.ColorRgb[0])).AppendLine(",");
        Builder.Append("                ColorG=").Append(F(Fog.ColorRgb[1])).AppendLine(",");
        Builder.Append("                ColorB=").Append(F(Fog.ColorRgb[2])).AppendLine(",");
        Builder.AppendLine("            },");
    }
    internal static void AppendTransform(StringBuilder Builder,Vector3 Translation,Quaternion Rotation,Vector3 Scale,string Indent){
        var Euler=ToEulerRuf(Rotation);
        Builder.Append(Indent).AppendLine("new Transform {");
        Builder.Append(Indent).Append("    Position=new LogicalVector3(").Append(F(Translation.X)).Append(',').Append(F(Translation.Y)).Append(',').Append(F(Translation.Z)).AppendLine("),");
        Builder.Append(Indent).Append("    Rotation=new LogicalQuaternion(new LogicalVector3(").Append(F(Euler.X)).Append(',').Append(F(Euler.Y)).Append(',').Append(F(Euler.Z)).AppendLine(")),");
        Builder.Append(Indent).Append("    Scale=new LogicalVector3(").Append(F(Scale.X)).Append(',').Append(F(Scale.Y)).Append(',').Append(F(Scale.Z)).AppendLine("),");
        Builder.Append(Indent).AppendLine("},");
    }
    internal static Vector3 ToEulerRuf(Quaternion Rotation){
        Rotation=Quaternion.Normalize(Rotation);
        var Pitch=MathF.Asin(Math.Clamp(2f*(Rotation.W*Rotation.X-Rotation.Y*Rotation.Z),-1f,1f));
        var Yaw=MathF.Atan2(2f*(Rotation.W*Rotation.Y+Rotation.Z*Rotation.X),1f-2f*(Rotation.X*Rotation.X+Rotation.Y*Rotation.Y));
        var Roll=MathF.Atan2(2f*(Rotation.W*Rotation.Z+Rotation.X*Rotation.Y),1f-2f*(Rotation.X*Rotation.X+Rotation.Z*Rotation.Z));
        return new Vector3(Pitch,Yaw,Roll);
    }
    internal static string AssetMemberName(string AssetPath)=>Path.GetFileNameWithoutExtension(AssetPath);
    internal static string Escape(string Value)=>Value.Replace("\\","\\\\",StringComparison.Ordinal).Replace("\"","\\\"",StringComparison.Ordinal);
    internal static string F(float Value){
        if(Value==0f){
            return "0f";
        }
        return Value.ToString("R",CultureInfo.InvariantCulture)+"f";
    }
}
