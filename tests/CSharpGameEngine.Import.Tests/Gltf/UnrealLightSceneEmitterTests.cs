using System.Numerics;
using System.Text;
using CSharpGameEngine.Unreal.SceneImporter;

namespace CSharpGameEngine.Import.Gltf.Tests;

public sealed class UnrealLightSceneEmitterTests {
    [Fact]
    public void CreateRotationForLightTravelDirection_MapsLocalForwardToOppositeOfLegacySurfaceToLightDirection(){
        Vector3 DirectionTowardLight=Vector3.Normalize(new Vector3(0.6387412f,-0.7606f,0.11617841f));

        Quaternion Rotation=UnrealLightSceneEmitter.CreateRotationForLightTravelDirection(DirectionTowardLight);
        Vector3 ActualTravelDirection=Vector3.Transform(Vector3.UnitZ,Rotation);

        Assert.InRange(Vector3.Distance(-DirectionTowardLight,ActualTravelDirection),0f,0.0001f);
    }

    [Fact]
    public void AppendDirectionalLightComponents_UsesTransformAndPhysicalLuxWithoutLegacyMembers(){
        var Builder=new StringBuilder();

        UnrealLightSceneEmitter.AppendDirectionalLightComponents(
            Builder,Vector3.UnitY,new Vector3(1f,0.5f,0.25f),6f,"    ");

        string Source=Builder.ToString();
        Assert.Contains("new Transform {",Source);
        Assert.Contains("IlluminanceLux=6f",Source);
        Assert.Contains("CastsShadows=true",Source);
        Assert.DoesNotContain("DirectionTowardLight",Source);
        Assert.DoesNotContain("Intensity=",Source);
    }

    [Fact]
    public void CreateRotationForLightTravelDirection_RejectsZeroDirection(){
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            UnrealLightSceneEmitter.CreateRotationForLightTravelDirection(Vector3.Zero));
    }
}
