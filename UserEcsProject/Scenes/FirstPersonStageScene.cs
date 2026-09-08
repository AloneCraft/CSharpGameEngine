using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;

namespace UserEcsProject.Scenes;

/// <summary>
/// /Game/FirstPerson/Lvl_FirstPersonから一度だけ取り込んだ宣言型ステージ。
/// このC#を正本として編集する。再取り込みはrun_export.cmd --forceを明示した場合だけ行う。
/// </summary>
internal partial class FirstPersonStageScene : IScene {
    public Node Root => new() {
        Name = "Lvl_FirstPerson",
        Components = [
            new AmbientLight {
                ColorR=1f,
                ColorG=1f,
                ColorB=1f,
                Intensity=1f,
            },
            new HeightFog {
                Density=0.0436f,
                HeightFalloff=0.2f,
                ColorR=0.6f,
                ColorG=0.65f,
                ColorB=0.7f,
            },
        ],
        Children = [
            new Node {
                Name="Floor",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0f,0f,0f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(4f,1f,4f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Template_Map_Floor_78eccfff4a,Mobility=MeshMobility.Movable },
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
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_7e024e0aa1,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube10",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-12f,0f,15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.14159262f,0f)),
                        Scale=new LogicalVector3(6f,2f,3f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_7e024e0aa1,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube11",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-120f,0f,-15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.57079619f,0f)),
                        Scale=new LogicalVector3(3f,2f,3f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_7e024e0aa1,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube12",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-15f,0f,-12f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.57079619f,0f)),
                        Scale=new LogicalVector3(6f,2f,3f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_7e024e0aa1,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube13",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-2f,0f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(7.000001f,2f,4f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube14",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(2f,0f,-2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.57079619f,0f)),
                        Scale=new LogicalVector3(3f,2f,4.0000005f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube15",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-2f,0f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079619f,0f)),
                        Scale=new LogicalVector3(3f,2f,4.0000005f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube16",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-5f,0f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(3f,2f,4f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube18",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-19f,2f,19f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079619f,0f)),
                        Scale=new LogicalVector3(1f,2f,38f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },            new Node {
                Name="SM_Cube17",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(19f,2f,20f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(1f,2f,40f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="19",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-19f,2f,-20f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079619f,0f)),
                        Scale=new LogicalVector3(1f,2f,38f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="2",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(18f,0f,20f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(2f,2f,40f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="20",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-20f,40.4545441f,20f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(1f,2f,40f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="3",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-20f,0f,20f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(2f,2f,40f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube4",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-18f,0f,18f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079619f,0f)),
                        Scale=new LogicalVector3(2f,2f,36f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube5",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-18f,0f,-20f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079619f,0f)),
                        Scale=new LogicalVector3(2f,2f,36f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_bfe650e90f,Mobility=MeshMobility.Static },
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
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_7e024e0aa1,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cube7",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(12f,0f,15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079619f,0f)),
                        Scale=new LogicalVector3(3f,2f,3f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_7e024e0aa1,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(18.62638f,24.8962975f,19.469429f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(7f,0.1f,7f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cylinder_2bf8575b55,Mobility=MeshMobility.Static },
                ],
                Children=[
                    new Node {
                        Name="SM_Cube9",
                        Components=[
                            new Transform {
                        Position=new LogicalVector3(-15f,0f,12f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.14159262f,0f)),
                        Scale=new LogicalVector3(3f,2f,3f),
                    },
                            new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_7e024e0aa1,Mobility=MeshMobility.Static },
                        ],
                        Children=[
                            new Node {
                                Name="8",
                                Components=[
                                    new Transform {
                        Position=new LogicalVector3(11.3372126f,-3.05175781E-05f,12f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079619f,0f)),
                        Scale=new LogicalVector3(6f,2f,3f),
                    },
                                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Cube_7e024e0aa1,Mobility=MeshMobility.Static },
                                ],
                            }
                        ]
                    }
                ]
            },
            new Node {
                Name="SM_Cylinder2",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(1f,0f,19f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(5.0000052f,4f,5.0000052f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder3",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0.099999994f,0f,19f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.57079619f,0f)),
                        Scale=new LogicalVector3(5.0000052f,4f,5.0000052f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder4",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0.099999994f,0f,-19f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079619f,0f)),
                        Scale=new LogicalVector3(5f,4f,5f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder5",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0.099999994f,0f,-19f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.14159263f,0f)),
                        Scale=new LogicalVector3(5f,4f,5f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder6",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-19f,73.7637f,16.7747269f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(5f,4f,5f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder7",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-19f,0f,0.09999907f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079619f,0f)),
                        Scale=new LogicalVector3(5f,4f,5f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder8",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(19f,0f,0.099999994f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,3.14159262f,0f)),
                        Scale=new LogicalVector3(5f,4f,5f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Cylinder9",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(19f,0f,0.099999994f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.57079619f,0f)),
                        Scale=new LogicalVector3(5f,4f,5f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(15f,0f,-15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.14159263f,0f)),
                        Scale=new LogicalVector3(6f,2f,6f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder10",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-1.9999996f,-1f,-2.1f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.57079619f,0f)),
                        Scale=new LogicalVector3(6.499999f,2f,6.499999f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder11",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-2f,-1f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.14159262f,0f)),
                        Scale=new LogicalVector3(6.5f,2f,6.5f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder12",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(2f,-1f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079597f,0f)),
                        Scale=new LogicalVector3(6.499999f,2f,6.499999f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder2",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(15f,0f,15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.57079619f,0f)),
                        Scale=new LogicalVector3(6f,2f,6f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
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
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder4",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-15f,0f,-15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079597f,0f)),
                        Scale=new LogicalVector3(5.999999f,2f,5.999999f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder5",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(2f,0f,-2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(6f,2f,6f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_75b03732c6,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder6",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(2f,0f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079597f,0f)),
                        Scale=new LogicalVector3(5.999999f,2f,5.999999f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_75b03732c6,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder7",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-2f,0f,-2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.57079619f,0f)),
                        Scale=new LogicalVector3(6f,2f,6f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_75b03732c6,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_QuarterCylinder8",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-2f,0f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.14159263f,0f)),
                        Scale=new LogicalVector3(6f,2f,6f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_75b03732c6,Mobility=MeshMobility.Static },
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
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_QuarterCylinder_236fd6061b,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(18f,0f,-12f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.14159263f,0f)),
                        Scale=new LogicalVector3(3f,2f,4f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Ramp_422fe5cd24,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp10",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(2f,0f,5f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.14159263f,0f)),
                        Scale=new LogicalVector3(4.0000005f,2f,4f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Ramp_3808c5dfe2,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp11",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(5f,0f,-2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079619f,0f)),
                        Scale=new LogicalVector3(4.000001f,2f,4f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Ramp_3808c5dfe2,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp12",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-2f,0f,-5f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(4f,2f,4f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Ramp_3808c5dfe2,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp2",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(12f,0f,-15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.57079619f,0f)),
                        Scale=new LogicalVector3(3f,2f,4f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Ramp_422fe5cd24,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp3",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(12f,0f,18f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.57079619f,0f)),
                        Scale=new LogicalVector3(3f,2f,4f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Ramp_422fe5cd24,Mobility=MeshMobility.Static },
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
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Ramp_422fe5cd24,Mobility=MeshMobility.Static },
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
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Ramp_422fe5cd24,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp6",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-12f,0f,15f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079597f,0f)),
                        Scale=new LogicalVector3(2.9999995f,2f,3.9999995f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Ramp_422fe5cd24,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp7",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-12f,0f,-18f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-1.57079597f,0f)),
                        Scale=new LogicalVector3(2.9999995f,2f,3.9999995f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Ramp_422fe5cd24,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp8",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-15f,0f,-12f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,-3.14159257f,0f)),
                        Scale=new LogicalVector3(3f,2f,4f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Ramp_422fe5cd24,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_Ramp9",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-5f,0f,2f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.57079619f,0f)),
                        Scale=new LogicalVector3(4.0000005f,2f,4f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_Ramp_3808c5dfe2,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="SM_SkySphere",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0f,0f,0f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(400f,400f,400f),
                    },
                    new StaticMesh { AssetPath=FirstPersonStageAssets.SM_SkySphere_f5cb2dbef4,Mobility=MeshMobility.Static },
                ],
            },
            new Node {
                Name="DirectionalLight",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0f,0f,0f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(-0.864236759f,-1.75071599f,1.00973235f)),
                        Scale=new LogicalVector3(1f,1f,1f),
                    },
                    new DirectionalLight {
                        Color=new LinearLightColor(1f,1f,1f),
                        Illuminance=new Illuminance(100_000f),
                    },
                    new LightShadow { Priority=100,Bias=new ShadowBias(0.005f) },
                ],
            },
            new Node {
                Name="PlayerStart",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0f,3.020126f,0f),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0f,0f,0f)),
                        Scale=new LogicalVector3(1f,1f,1f),
                    },
                    new PlayerStart(),
                    new Camera { Active=true },
                ],
            },
        ],
    };
}
