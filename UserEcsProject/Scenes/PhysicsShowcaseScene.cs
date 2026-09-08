using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Windows;

namespace UserEcsProject.Scenes;

/// <summary>Deterministic v1 samples: ground, stack, projectile, prefracture wall, and cache marker.</summary>
internal partial class PhysicsGroundScene:IScene {
    public Node Root=>new(){
        Name="Physics_Ground",
        Components=[
            new Transform {
                Position=new LogicalVector3(0,-0.25f,0),
                Scale=new LogicalVector3(20,0.5f,20),
            },
            new PrimitiveMesh { Type=PrimitiveType.Box },
            new Collider {
                Shape=ColliderShape.Box,
                HalfExtents=new LogicalVector3(10,0.25f,10),
            },
        ],
    };
}

internal partial class PhysicsStackBottomScene:IScene {
    public Node Root=>new(){
        Name="Physics_Stack_0",
        Components=[
            new Transform { Position=new LogicalVector3(0,1,0) },
            new PrimitiveMesh { Type=PrimitiveType.Box },
            new Collider(),new RigidBody(),new PhysicsRotation(),new PhysicsVelocity(),
        ],
    };
}

internal partial class PhysicsStackMiddleScene:IScene {
    public Node Root=>new(){
        Name="Physics_Stack_1",
        Components=[
            new Transform { Position=new LogicalVector3(0,2.05f,0) },
            new PrimitiveMesh { Type=PrimitiveType.Box },
            new Collider(),new RigidBody(),new PhysicsRotation(),new PhysicsVelocity(),
        ],
    };
}

internal partial class PhysicsStackTopScene:IScene {
    public Node Root=>new(){
        Name="Physics_Stack_2",
        Components=[
            new Transform { Position=new LogicalVector3(0,3.1f,0) },
            new PrimitiveMesh { Type=PrimitiveType.Box },
            new Collider(),new RigidBody(),new PhysicsRotation(),new PhysicsVelocity(),
        ],
    };
}

internal partial class PhysicsProjectileScene:IScene {
    public Node Root=>new(){
        Name="Physics_Projectile",
        Components=[
            new Transform { Position=new LogicalVector3(-8,1.5f,0) },
            new PrimitiveMesh { Type=PrimitiveType.Sphere },
            new Collider { Shape=ColliderShape.Sphere,Radius=0.25f },
            new RigidBody { Ccd=PhysicsCcdMode.ContinuousStatic },
            new PhysicsRotation(),
            new PhysicsVelocity { Linear=new LogicalVector3(20,2,0) },
        ],
    };
}

internal partial class PhysicsPrefractureWallScene:IScene {
    public Node Root=>new(){
        Name="Physics_PrefractureWall",
        Components=[
            new Transform { Position=new LogicalVector3(5,2,0),Scale=new LogicalVector3(0.5f,4,6) },
            new PrimitiveMesh { Type=PrimitiveType.Box },
            new Collider { Shape=ColliderShape.Box,HalfExtents=new LogicalVector3(0.25f,2,3) },
            new Destructible {
                FractureAsset="Physics/PrefractureWall.cgefracture",
                FractureThreshold=8,
                FragmentBudget=64,
            },
        ],
    };
}

internal partial class PhysicsCachePlaybackScene:IScene {
    public Node Root=>new(){
        Name="Physics_CachePlayback",
        Components=[
            new PhysicsCachePlayback {
                Cache="Physics/Showcase.cgephyscache",
                State=PhysicsCachePlaybackState.Stopped,
                AllowRealtimeHandoff=true,
            },
        ],
    };
}
