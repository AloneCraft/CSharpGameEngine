using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using UserGameProject.Systems;

namespace UserGameProject.Tests;

/// <summary>
/// PlayerControl.Update's body has no ECS dependency of its own - GameSystemGenerator
/// (ADR-0033) derives the Archetype and dispatch purely from its parameters - so these tests call it
/// directly with local ref arguments and never need real entities. A throwaway World/State is still
/// passed because every Update ends with a trailing `ref State`, but this method never touches it.
/// InputState is scripted exactly as Input would have written it, so no Win32Window is needed
/// either.
/// </summary>
public class PlayerControlTests {
    private const float DeltaTime=1f/60f;

    private static void RunControl(ref Position Position,ref Rotation Rotation,ref VerticalMotion VerticalMotion,ref CameraRig CameraRig,ref InputState InputState){
        PlayerTag PlayerTag=default;
        FrameContext Frame=new(DeltaTime,0f);
        World World=new();
        State State=new(World);
        PlayerControl System=new();

        System.Update(ref Position,ref Rotation,ref VerticalMotion,ref CameraRig,ref InputState,ref PlayerTag,in Frame,ref State);
    }

    [Fact]
    public void Update_MoveForwardAtZeroYaw_MovesAlongPositiveZ(){
        Position Position=new(){ X=0,Y=0,Z=0 };
        Rotation Rotation=new(){ X=0,Y=0,Z=0,W=1 };
        VerticalMotion VerticalMotion=default;
        CameraRig CameraRig=default;
        InputState InputState=new(){ MoveForward=true };

        RunControl(ref Position,ref Rotation,ref VerticalMotion,ref CameraRig,ref InputState);

        Assert.Equal(0f,Position.X,precision:4);
        Assert.True(Position.Z>0f, $"Expected forward movement along +Z, got Z={Position.Z}.");
    }

    [Fact]
    public void Update_LookDeltaX_AccumulatesCameraRigYaw(){
        Position Position=new(){ X=0,Y=0,Z=0 };
        Rotation Rotation=new(){ X=0,Y=0,Z=0,W=1 };
        VerticalMotion VerticalMotion=default;
        CameraRig CameraRig=default;
        InputState InputState=new(){ LookDeltaX=100f };

        RunControl(ref Position,ref Rotation,ref VerticalMotion,ref CameraRig,ref InputState);

        Assert.True(CameraRig.Yaw>0f,"Expected a positive mouse-X delta to increase Yaw.");
    }

    [Fact]
    public void Update_LookDeltaY_ClampsPitchToFirstPersonRange(){
        Position Position=new(){ X=0,Y=0,Z=0 };
        Rotation Rotation=new(){ X=0,Y=0,Z=0,W=1 };
        VerticalMotion VerticalMotion=default;
        CameraRig CameraRig=default;
        // A huge upward look (negative Y delta pitches up, matching Camera.ApplyMouseDelta's convention).
        InputState InputState=new(){ LookDeltaY=-1_000_000f };

        RunControl(ref Position,ref Rotation,ref VerticalMotion,ref CameraRig,ref InputState);

        Assert.Equal(CSharpGameEngine.Graphics.D3D12.Camera.MaxPitchRadiansFirstPerson,CameraRig.Pitch,precision:4);
    }

    [Fact]
    public void Update_RotationAlwaysMatchesCameraRigYaw_FirstPersonBodyFacesView(){
        Position Position=new(){ X=0,Y=0,Z=0 };
        Rotation Rotation=new(){ X=0,Y=0,Z=0,W=1 };
        VerticalMotion VerticalMotion=default;
        CameraRig CameraRig=default;
        InputState InputState=new(){ LookDeltaX=250f };

        RunControl(ref Position,ref Rotation,ref VerticalMotion,ref CameraRig,ref InputState);

        Quaternion Expected=Quaternion.CreateFromAxisAngle(Vector3.UnitY,CameraRig.Yaw);
        Quaternion Actual=new(Rotation.X,Rotation.Y,Rotation.Z,Rotation.W);
        Assert.True(Quaternion.Dot(Expected,Actual)>0.9999f, $"Expected Rotation to match CameraRig.Yaw exactly, expected={Expected}, actual={Actual}.");
    }

    [Fact]
    public void Update_JumpWhileGrounded_LaunchesUpwardMatchingCharacterMotion(){
        Position Position=new(){ X=0,Y=0,Z=0 };
        Rotation Rotation=new(){ X=0,Y=0,Z=0,W=1 };
        VerticalMotion VerticalMotion=default;
        CameraRig CameraRig=default;
        InputState InputState=new(){ JumpPressed=true };

        RunControl(ref Position,ref Rotation,ref VerticalMotion,ref CameraRig,ref InputState);

        var ExpectedVelocity=CharacterMotion.JumpSpeed-(CharacterMotion.Gravity/60f);
        Assert.Equal(ExpectedVelocity,VerticalMotion.Velocity,precision:3);
        Assert.False(VerticalMotion.IsGrounded);
    }

    [Fact]
    public void Update_NoInput_PlayerStaysPutAndGrounded(){
        Position Position=new(){ X=1,Y=0,Z=2 };
        Rotation Rotation=new(){ X=0,Y=0,Z=0,W=1 };
        VerticalMotion VerticalMotion=default;
        CameraRig CameraRig=default;
        InputState InputState=new();

        RunControl(ref Position,ref Rotation,ref VerticalMotion,ref CameraRig,ref InputState);

        Assert.Equal(1f,Position.X,precision:4);
        Assert.Equal(0f,Position.Y,precision:4);
        Assert.Equal(2f,Position.Z,precision:4);
        Assert.True(VerticalMotion.IsGrounded);
    }

    [Fact]
    public void OrbitMotionSystem_AtQuarterTurn_MovesEntityAroundConfiguredCenter(){
        Position Position=default;
        OrbitMotion Motion=new(){
            CenterX=10f,
            CenterY=3f,
            CenterZ=-2f,
            Radius=4f,
            AngularSpeedRadiansPerSecond=MathF.PI/2f,
        };
        FrameContext Frame=new(DeltaTime,1f);
        World World=new();
        State State=new(World);
        OrbitMotionSystem System=new();

        System.Update(ref Position,ref Motion,in Frame,ref State);

        Assert.Equal(10f,Position.X,precision:4);
        Assert.Equal(3f,Position.Y,precision:4);
        Assert.Equal(2f,Position.Z,precision:4);
    }

    [Fact]
    public void RotationMotion_UsesConfiguredAxisSpeedAndPhase(){
        Rotation Rotation=default;
        RotateMotion Motion=new(){ AxisX=0f,AxisY=1f,AxisZ=0f,RadiansPerSecond=MathF.PI,PhaseRadians=0f };
        FrameContext Frame=new(DeltaTime,0.5f);
        World World=new();
        State State=new(World);
        RotationMotion System=new();

        System.Update(ref Rotation,ref Motion,in Frame,ref State);

        Quaternion Actual=new(Rotation.X,Rotation.Y,Rotation.Z,Rotation.W);
        Quaternion Expected=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2f);
        Assert.True(Quaternion.Dot(Actual,Expected)>0.9999f);
    }

    [Fact]
    public void ParentToWorld_CombinesChildLocalWithCachedParentWorld(){
        TransformHierarchyRuntime Hierarchy=new();
        Hierarchy.Set(7,Matrix4x4.CreateTranslation(5f,0f,0f));
        Position Position=new(){ X=1f,Y=2f,Z=3f };
        Rotation Rotation=new(){ W=1f };
        Scale Scale=new(){ X=1f,Y=1f,Z=1f };
        WorldTransform World=default;
        Parent Parent=new(){ Value=new Entity(7) };
        World StateWorld=new();
        State State=new(StateWorld);
        ParentToWorld System=new();

        System.Update(new Entity(8),ref Position,ref Rotation,ref Scale,ref World,ref Parent,Hierarchy,ref State);

        Assert.Equal(6f,World.Value.Translation.X,precision:4);
        Assert.Equal(2f,World.Value.Translation.Y,precision:4);
        Assert.Equal(3f,World.Value.Translation.Z,precision:4);
    }
}
