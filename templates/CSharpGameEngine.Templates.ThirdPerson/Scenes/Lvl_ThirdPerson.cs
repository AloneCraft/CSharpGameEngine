using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Lighting;
using CSharpGameEngine.Runtime.Windows;
namespace CgeThirdPerson.Scenes;
/// <summary>
/// UE5.8 ThirdPersonテンプレートの/Game/ThirdPerson/Lvl_ThirdPersonから取り込んだ宣言型レベル。
/// このC#を正本として編集する。再取り込みはtools/UnrealExport/run_export_thirdperson.cmd --forceを明示した場合だけ行う。
/// </summary>
[StartupScene(Order=1)]
internal partial class Lvl_ThirdPerson:IScene {
    public Node Root=>new(){
        Name="Lvl_ThirdPerson",
        Components=[
            new SkyAtmosphere {
                BottomRadiusKm=6360f,AtmosphereHeightKm=60f,
                GroundAlbedoR=0.40197778f,GroundAlbedoG=0.40197778f,GroundAlbedoB=0.40197778f,
                RayleighScatteringR=0.175287f,RayleighScatteringG=0.40960723f,RayleighScatteringB=1f,
                RayleighScatteringScale=0.0331f,RayleighExponentialDistributionKm=8f,
                MieScatteringR=1f,MieScatteringG=1f,MieScatteringB=1f,
                MieScatteringScale=0.003996f,
                MieAbsorptionR=1f,MieAbsorptionG=1f,MieAbsorptionB=1f,
                MieAbsorptionScale=0.000444f,MieAnisotropy=0.8f,MieExponentialDistributionKm=1.2f,
                OtherAbsorptionR=0.34556085f,OtherAbsorptionG=0.99999994f,OtherAbsorptionB=0.045188725f,
                OtherAbsorptionScale=0.001881f,OtherTentTipAltitudeKm=25f,OtherTentWidthKm=30f,
                MultiScatteringFactor=1f,SunDiskAngleDegrees=0.7357f,
            },
            new SkyLight { Intensity=1f,LowerHemisphereR=0f,LowerHemisphereG=0f,LowerHemisphereB=0f },
            new ExponentialHeightFog { FogDensity=0.0436f,FogHeightFalloff=0.2f,FogHeight=-68.5f,FogMaxOpacity=1f,InscatteringLuminanceR=0f,InscatteringLuminanceG=0f,InscatteringLuminanceB=0f,SkyAtmosphereAmbientContributionScale=1f },
            new PostProcessVolume { ExposureMethod=ExposureMethod.Manual,MinEv100=0f,MaxEv100=0f,ExposureCompensation=1f,ToneMapper=ToneMapperKind.UnrealFilmic,BloomIntensity=0.675f,AmbientOcclusionIntensity=0.5f,AmbientOcclusionRadiusMeters=2f },
        ],
        Children=[
            new Node {
                Name="Floor",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0f,0f,0f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(4f,1f,4f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Template_Map_Floor_78eccfff4a,Mobility=MeshMobility.Movable },
                ],
            },
            new Node {
                Name="SM_Cube",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(12f,0f,-15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(6f,2f,3f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_913717f394,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube10",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-12f,0f,15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.1415925f,0f)),
                        Scale=new LogicalVector3(6f,2f,3f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_913717f394,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube11",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-12f,0f,-15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.5707961f,0f)),
                        Scale=new LogicalVector3(2.9999995f,2f,2.9999995f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_913717f394,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube12",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-15f,0f,-12f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.5707961f,0f)),
                        Scale=new LogicalVector3(5.999999f,2f,2.9999995f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_913717f394,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube13",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-2f,0f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(7.000001f,2f,4.100001f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube14",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(2f,0f,-2.1f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.5707961f,0f)),
                        Scale=new LogicalVector3(3f,2f,4f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube15",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-2f,0f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.5707961f,0f)),
                        Scale=new LogicalVector3(3f,2f,4f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube16",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-5f,0f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(3f,2f,4.1f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube17",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(19f,2f,20f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(1f,2f,40f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube18",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-19f,2f,19f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.5707961f,0f)),
                        Scale=new LogicalVector3(1f,2f,38f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube19",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-19f,2f,-20f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.5707961f,0f)),
                        Scale=new LogicalVector3(1f,2f,38f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube2",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(18f,0f,20f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(2f,2f,40f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube20",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-20f,2f,20f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(1f,2f,40f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube3",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-20f,0f,20f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(2f,2f,40f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube4",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-18f,0f,18f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.5707961f,0f)),
                        Scale=new LogicalVector3(2f,2f,36f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube5",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-18f,0f,-20f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.5707961f,0f)),
                        Scale=new LogicalVector3(2f,2f,36f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube6",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(15f,0f,-12f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(3f,2f,3f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_913717f394,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube7",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(12f,0f,15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.5707961f,0f)),
                        Scale=new LogicalVector3(3f,2f,3f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_913717f394,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube8",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(15f,0f,12f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.5707961f,0f)),
                        Scale=new LogicalVector3(6f,2f,3f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_913717f394,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube9",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-15f,0f,12f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.1415925f,0f)),
                        Scale=new LogicalVector3(3f,2f,3f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cube_913717f394,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0f,2f,0.099999994f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(7f,0.1f,7f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Cylinder_e89d7d5009,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder2",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0.099999994f,0f,19f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(5.000005f,4f,5.000005f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder3",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0.099999994f,0f,19f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.5707961f,0f)),
                        Scale=new LogicalVector3(5.000005f,4f,5.000005f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder4",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0.099999994f,0f,-19f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.5707961f,0f)),
                        Scale=new LogicalVector3(5f,4f,5f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder5",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0.099999994f,0f,-19f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.1415927f,0f)),
                        Scale=new LogicalVector3(5f,4f,5f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder6",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-19f,0f,0.099999085f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(5f,4f,5f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder7",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-19f,0f,0.099999085f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.5707961f,0f)),
                        Scale=new LogicalVector3(5f,4f,5f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder8",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(19f,0f,0.099999994f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,3.1415925f,0f)),
                        Scale=new LogicalVector3(5f,4f,5f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder9",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(19f,0f,0.099999994f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.5707961f,0f)),
                        Scale=new LogicalVector3(5f,4f,5f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(15f,0f,-15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.1415927f,0f)),
                        Scale=new LogicalVector3(6f,2f,6f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder10",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-1.9999996f,-1f,-2.1f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.5707961f,0f)),
                        Scale=new LogicalVector3(6.499999f,2f,6.499999f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder11",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-2f,-1f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.1415925f,0f)),
                        Scale=new LogicalVector3(6.5f,2f,6.5f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder12",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(2f,-1f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.570796f,0f)),
                        Scale=new LogicalVector3(6.499999f,2f,6.499999f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder2",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(15f,0f,15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.5707961f,0f)),
                        Scale=new LogicalVector3(6f,2f,6f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder3",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-15f,0f,15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(6f,2f,6f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder4",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-15f,0f,-15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.570796f,0f)),
                        Scale=new LogicalVector3(5.999999f,2f,5.999999f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder5",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(2f,0f,-2.1f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(6f,2f,6f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_75b03732c6,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder6",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(2f,0f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.570796f,0f)),
                        Scale=new LogicalVector3(5.999999f,2f,5.999999f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_75b03732c6,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder7",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-1.9999996f,0f,-2.1f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.5707961f,0f)),
                        Scale=new LogicalVector3(5.999999f,2f,5.999999f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_75b03732c6,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder8",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-2f,0f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.1415925f,0f)),
                        Scale=new LogicalVector3(6f,2f,6f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_75b03732c6,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder9",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(2f,-1f,-2.1f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(6.5f,2f,6.5f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_QuarterCylinder_8c7c560cd3,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(18f,0f,-12f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.1415927f,0f)),
                        Scale=new LogicalVector3(3f,2f,4f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Ramp_8bc3259d14,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp10",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(2f,0f,5f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.1415927f,0f)),
                        Scale=new LogicalVector3(4.000001f,2f,4f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Ramp_3808c5dfe2,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp11",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(5f,0f,-2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.5707961f,0f)),
                        Scale=new LogicalVector3(4.000001f,2f,4f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Ramp_3808c5dfe2,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp12",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-2f,0f,-5.1f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(4.000001f,2f,3.9f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Ramp_3808c5dfe2,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp2",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(12f,0f,-15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.5707961f,0f)),
                        Scale=new LogicalVector3(3f,2f,4f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Ramp_8bc3259d14,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp3",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(12f,0f,18f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.5707961f,0f)),
                        Scale=new LogicalVector3(3f,2f,4f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Ramp_8bc3259d14,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp4",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(15f,0f,12f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(3f,2f,4f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Ramp_8bc3259d14,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp5",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-18f,0f,12f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(3f,2f,4f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Ramp_8bc3259d14,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp6",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-12f,0f,15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.570796f,0f)),
                        Scale=new LogicalVector3(2.9999995f,2f,3.9999995f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Ramp_8bc3259d14,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp7",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-12f,0f,-18f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.570796f,0f)),
                        Scale=new LogicalVector3(2.9999995f,2f,3.9999995f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Ramp_8bc3259d14,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp8",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-15f,0f,-12f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.1415925f,0f)),
                        Scale=new LogicalVector3(3f,2f,4f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Ramp_8bc3259d14,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp9",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-5f,0f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.5707961f,0f)),
                        Scale=new LogicalVector3(4.000001f,2f,4f),
                    },
                    new StaticMesh { AssetPath=ThirdPersonAssets.SM_Ramp_3808c5dfe2,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="DirectionalLight",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0f,0f,0f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(1.0607965f,1.3092037f,0.845869f)),
                        Scale=new LogicalVector3(1f,1f,1f),
                    },
                    new global::CSharpGameEngine.Runtime.Lighting.DirectionalLight {
                        Color=new global::CSharpGameEngine.Lighting.LinearLightColor(1f,1f,1f),
                        Illuminance=new global::CSharpGameEngine.Lighting.Illuminance(3f),
                    },
                    new global::CSharpGameEngine.Runtime.Lighting.LightShadow { Priority=100,Bias=new global::CSharpGameEngine.Lighting.ShadowBias(0.005f) },
                ],
            },
            new Node {
                Name="GlobalIlluminationVolume",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-5.722046E-06f,2.15f,-9.536743E-07f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(1f,1f,1f),
                    },
                    new GlobalIlluminationVolume { HalfExtentX=20.000006f,HalfExtentY=1.85f,HalfExtentZ=20f,ProbeSpacingMeters=2.5f },
                ],
            },
            new Node {
                Name="PlayerStart",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0f,3.0201263f,0f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(1f,1f,1f),
                    },
                    new PlayerStart(),
                ],
            },
        ],
    };
}
