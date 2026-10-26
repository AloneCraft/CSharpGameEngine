using System.Numerics;
using System.Text;
using CSharpGameEngine.Unreal.SceneImporter;

namespace CSharpGameEngine.Import.Gltf.Tests;

public sealed class UnrealLightSceneEmitterTests {
    [Fact]
    public void CreateEulerRotationForLightTravelDirection_MapsLocalForwardToOppositeOfLegacySurfaceToLightDirection(){
        Vector3 DirectionTowardLight=Vector3.Normalize(new Vector3(0.6387412f,-0.7606f,0.11617841f));

        Vector3 Euler=UnrealLightSceneEmitter.CreateEulerRotationForLightTravelDirection(DirectionTowardLight);
        Vector3 ActualTravelDirection=Vector3.Transform(
            Vector3.UnitZ,Quaternion.CreateFromYawPitchRoll(Euler.Y,Euler.X,Euler.Z));

        Assert.InRange(Vector3.Distance(-DirectionTowardLight,ActualTravelDirection),0f,0.0001f);
    }

    [Fact]
    public void AppendDirectionalLightComponents_UsesTransformAndPhysicalLuxWithoutLegacyMembers(){
        var Builder=new StringBuilder();

        UnrealLightSceneEmitter.AppendDirectionalLightComponents(
            Builder,Vector3.UnitY,new Vector3(1f,0.5f,0.25f),6f,"    ");

        string Source=Builder.ToString();
        Assert.Contains("new Transform {",Source);
        Assert.Contains("Illuminance=new global::CSharpGameEngine.Lighting.Illuminance(6f)",Source);
        Assert.Contains("Runtime.Lighting.LightShadow",Source);
        Assert.DoesNotContain("CastsShadows=",Source);
        Assert.DoesNotContain("DirectionTowardLight",Source);
        Assert.DoesNotContain("Intensity=",Source);
    }

    [Fact]
    public void AppendDirectionalLightComponentsFromTravelDirection_PointsTransformForwardAlongTravel(){
        // UE5.8 の Lvl_ThirdPerson にある太陽は、エンジン軸でレベル内へ向かって進む。インポーターは
        // この進行方向である ImportedLightDirection.WorldForward を渡す。
        Vector3 Travel=Vector3.Normalize(new Vector3(0.4716f,-0.8727f,0.1262f));
        var Builder=new StringBuilder();
        UnrealLightSceneEmitter.AppendDirectionalLightComponentsFromTravelDirection(Builder,Travel,Vector3.One,3f,"");
        var Match=System.Text.RegularExpressions.Regex.Match(Builder.ToString(),@"Rotation=new LogicalQuaternion\(new LogicalVector3\(([^,]+),([^,]+),([^)]+)\)\)");
        Assert.True(Match.Success,Builder.ToString());
        float Parse(int Index)=>float.Parse(Match.Groups[Index].Value.TrimEnd('f'),System.Globalization.CultureInfo.InvariantCulture);
        Vector3 Forward=Vector3.Transform(Vector3.UnitZ,Quaternion.CreateFromYawPitchRoll(Parse(2),Parse(1),Parse(3)));
        Assert.InRange(Vector3.Distance(Travel,Forward),0f,0.0001f);
        Assert.True(Forward.Y<0f,"A sun above the horizon must travel downward.");
    }

    [Fact]
    public void CreateEulerRotationForLightTravelDirection_RejectsZeroDirection(){
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            UnrealLightSceneEmitter.CreateEulerRotationForLightTravelDirection(Vector3.Zero));
    }
}
