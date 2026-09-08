using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Windows;
using UserEcsProject.Showcase;
namespace UserEcsProject.Scenes;
[StartupScene(Order=1)]
internal partial class EngineShowcaseScene:IScene {
    public Node Root=>new(){
        Name="EngineShowcaseGallery",
        Components=[
            new Transform(),
            new AmbientLight { ColorR=0.32f,ColorG=0.38f,ColorB=0.5f,Intensity=0.35f },
            new HeightFog { Density=0.008f,HeightFalloff=0.16f,ColorR=0.11f,ColorG=0.16f,ColorB=0.24f },
        ],
        Children=[
            new Node {
                Name="ShowcaseCamera",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(-2.5f,6.5f,-18),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0.14f,0,0)),
                    },
                    new Camera { Active=true,FarZ=200f,ExposureEv100=0.5f },
                ],
            },
            new Node {
                Name="GalleryGround",
                Components=[
                    new Transform { Position=new LogicalVector3(-2,-0.3f,8),Scale=new LogicalVector3(22,0.6f,18) },
                    new PrimitiveMesh { Type=PrimitiveType.Box },
                    new Collider { Shape=ColliderShape.Box,HalfExtents=new LogicalVector3(11,0.3f,9) },
                ],
            },
            new Node {
                Name="ECS_Motion_0",
                Components=[new Transform { Position=new LogicalVector3(-8,0.65f,5) },new PrimitiveMesh { Type=PrimitiveType.Sphere },new ShowcaseMotion { BaseX=-8,BaseY=0.65f,BaseZ=5,AmplitudeMillimeters=900,PeriodTicks=240,PhaseTicks=0 }],
            },
            new Node {
                Name="ECS_Motion_1",
                Components=[new Transform { Position=new LogicalVector3(-7,1.6f,7) },new PrimitiveMesh { Type=PrimitiveType.Sphere },new ShowcaseMotion { BaseX=-7,BaseY=1.6f,BaseZ=7,AmplitudeMillimeters=900,PeriodTicks=240,PhaseTicks=40 }],
            },
            new Node {
                Name="ECS_Motion_2",
                Components=[new Transform { Position=new LogicalVector3(-8,2.55f,9) },new PrimitiveMesh { Type=PrimitiveType.Sphere },new ShowcaseMotion { BaseX=-8,BaseY=2.55f,BaseZ=9,AmplitudeMillimeters=900,PeriodTicks=240,PhaseTicks=80 }],
            },
            new Node {
                Name="ECS_Motion_3",
                Components=[new Transform { Position=new LogicalVector3(-6,3.5f,11) },new PrimitiveMesh { Type=PrimitiveType.Sphere },new ShowcaseMotion { BaseX=-6,BaseY=3.5f,BaseZ=11,AmplitudeMillimeters=900,PeriodTicks=240,PhaseTicks=120 }],
            },
            new Node {
                Name="PBR_Box",
                Components=[new Transform { Position=new LogicalVector3(2,0.7f,6),Scale=new LogicalVector3(1.4f,1.4f,1.4f) },new PrimitiveMesh { Type=PrimitiveType.Box }],
            },
            new Node {
                Name="PBR_Sphere",
                Components=[new Transform { Position=new LogicalVector3(4,1,8),Scale=new LogicalVector3(2,2,2) },new PrimitiveMesh { Type=PrimitiveType.Sphere }],
            },
            new Node {
                Name="PBR_Torus",
                Components=[new Transform { Position=new LogicalVector3(2.5f,1.1f,11),Rotation=new LogicalQuaternion(new LogicalVector3(1.2f,0.3f,0)),Scale=new LogicalVector3(2.4f,2.4f,2.4f) },new PrimitiveMesh { Type=PrimitiveType.Torus }],
            },
            new Node {
                Name="Sun",
                Components=[
                    new Transform { Rotation=new LogicalQuaternion(new LogicalVector3(-0.75f,-0.6f,0)) },
                    new DirectionalLight { IlluminanceLux=65_000f,CastsShadows=true,ShadowPriority=100 },
                ],
            },
            new Node {
                Name="WarmPoint",
                Components=[
                    new Transform { Position=new LogicalVector3(2,3.5f,7) },
                    new PointLight { ColorR=1f,ColorG=0.45f,ColorB=0.2f,LuminousFluxLumens=1800f,RangeMeters=9f,CastsShadows=true,ShadowPriority=30 },
                ],
            },
            new Node {
                Name="CoolSpot",
                Components=[
                    new Transform { Position=new LogicalVector3(5,5,5),Rotation=new LogicalQuaternion(new LogicalVector3(0.75f,2.75f,0)) },
                    new SpotLight { ColorR=0.25f,ColorG=0.55f,ColorB=1f,LuminousFluxLumens=2400f,RangeMeters=14f,CastsShadows=true,ShadowPriority=20 },
                ],
            },
            new Node {
                Name="AreaFill",
                Components=[
                    new Transform { Position=new LogicalVector3(0,4,11),Rotation=new LogicalQuaternion(new LogicalVector3(1.2f,0,0)) },
                    new AreaLight { ColorR=0.45f,ColorG=0.8f,ColorB=1f,LuminanceNits=450f,WidthMeters=3f,HeightMeters=2f,RangeMeters=10f,CastsShadows=true,ShadowPriority=10 },
                ],
            },
        ],
    };
}
[StartupScene(Order=2)]
internal partial class EngineShowcaseRigidBottomScene:IScene {
    public Node Root=>new(){
        Name="Physics_Stack_0",
        Components=[
            new Transform { Position=new LogicalVector3(-2,0.7f,8) },
            new PrimitiveMesh { Type=PrimitiveType.Box },
            new Collider(),new RigidBody(),new PhysicsRotation(),new PhysicsVelocity(),
        ],
    };
}
[StartupScene(Order=3)]
internal partial class EngineShowcaseRigidMiddleScene:IScene {
    public Node Root=>new(){
        Name="Physics_Stack_1",
        Components=[
            new Transform { Position=new LogicalVector3(-2,1.75f,8) },
            new PrimitiveMesh { Type=PrimitiveType.Box },
            new Collider(),new RigidBody(),new PhysicsRotation(),new PhysicsVelocity(),
        ],
    };
}
[StartupScene(Order=4)]
internal partial class EngineShowcaseRigidTopScene:IScene {
    public Node Root=>new(){
        Name="Physics_Stack_2",
        Components=[
            new Transform { Position=new LogicalVector3(-2,2.8f,8) },
            new PrimitiveMesh { Type=PrimitiveType.Box },
            new Collider(),new RigidBody(),new PhysicsRotation(),new PhysicsVelocity(),
        ],
    };
}
[StartupScene(Order=5)]
internal partial class EngineShowcaseProjectileScene:IScene {
    public Node Root=>new(){
        Name="Physics_CCD_Projectile",
        Components=[
            new Transform { Position=new LogicalVector3(-5,1.1f,12),Scale=new LogicalVector3(0.5f,0.5f,0.5f) },
            new PrimitiveMesh { Type=PrimitiveType.Sphere },
            new Collider { Shape=ColliderShape.Sphere,Radius=0.25f },
            new RigidBody { Ccd=PhysicsCcdMode.ContinuousStatic },
            new PhysicsRotation(),
            new PhysicsVelocity { Linear=new LogicalVector3(10,1,-1) },
        ],
    };
}
