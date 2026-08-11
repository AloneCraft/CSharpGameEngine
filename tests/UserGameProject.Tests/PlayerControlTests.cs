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

    private static void RunControl(ref Position position,ref Rotation rotation,ref VerticalMotion verticalMotion,ref CameraRig cameraRig,ref InputState inputState){
        PlayerTag playerTag=default;
        FrameContext Frame=new(DeltaTime,0f);
        World world=new();
        State state=new(world);
        PlayerControl system=new();

        system.Update(ref position,ref rotation,ref verticalMotion,ref cameraRig,ref inputState,ref playerTag,in Frame,ref state);
    }

    [Fact]
    public void Update_MoveForwardAtZeroYaw_MovesAlongPositiveZ(){
        Position position=new(){ X=0,Y=0,Z=0 };
        Rotation rotation=new(){ X=0,Y=0,Z=0,W=1 };
        VerticalMotion verticalMotion=default;
        CameraRig cameraRig=default;
        InputState inputState=new(){ MoveForward=true };

        RunControl(ref position,ref rotation,ref verticalMotion,ref cameraRig,ref inputState);

        Assert.Equal(0f,position.X,precision:4);
        Assert.True(position.Z>0f, $"Expected forward movement along +Z, got Z={position.Z}.");
    }

    [Fact]
    public void Update_LookDeltaX_AccumulatesCameraRigYaw(){
        Position position=new(){ X=0,Y=0,Z=0 };
        Rotation rotation=new(){ X=0,Y=0,Z=0,W=1 };
        VerticalMotion verticalMotion=default;
        CameraRig cameraRig=default;
        InputState inputState=new(){ LookDeltaX=100f };

        RunControl(ref position,ref rotation,ref verticalMotion,ref cameraRig,ref inputState);

        Assert.True(cameraRig.Yaw>0f,"Expected a positive mouse-X delta to increase Yaw.");
    }

    [Fact]
    public void Update_LookDeltaY_ClampsPitchToFirstPersonRange(){
        Position position=new(){ X=0,Y=0,Z=0 };
        Rotation rotation=new(){ X=0,Y=0,Z=0,W=1 };
        VerticalMotion verticalMotion=default;
        CameraRig cameraRig=default;
        // A huge upward look (negative Y delta pitches up, matching Camera.ApplyMouseDelta's convention).
        InputState inputState=new(){ LookDeltaY=-1_000_000f };

        RunControl(ref position,ref rotation,ref verticalMotion,ref cameraRig,ref inputState);

        Assert.Equal(CSharpGameEngine.Graphics.D3D12.Camera.MaxPitchRadiansFirstPerson,cameraRig.Pitch,precision:4);
    }

    [Fact]
    public void Update_RotationAlwaysMatchesCameraRigYaw_FirstPersonBodyFacesView(){
        Position position=new(){ X=0,Y=0,Z=0 };
        Rotation rotation=new(){ X=0,Y=0,Z=0,W=1 };
        VerticalMotion verticalMotion=default;
        CameraRig cameraRig=default;
        InputState inputState=new(){ LookDeltaX=250f };

        RunControl(ref position,ref rotation,ref verticalMotion,ref cameraRig,ref inputState);

        Quaternion Expected=Quaternion.CreateFromAxisAngle(Vector3.UnitY,cameraRig.Yaw);
        Quaternion Actual=new(rotation.X,rotation.Y,rotation.Z,rotation.W);
        Assert.True(Quaternion.Dot(Expected,Actual)>0.9999f, $"Expected Rotation to match CameraRig.Yaw exactly, expected={Expected}, actual={Actual}.");
    }

    [Fact]
    public void Update_JumpWhileGrounded_LaunchesUpwardMatchingCharacterMotion(){
        Position position=new(){ X=0,Y=0,Z=0 };
        Rotation rotation=new(){ X=0,Y=0,Z=0,W=1 };
        VerticalMotion verticalMotion=default;
        CameraRig cameraRig=default;
        InputState inputState=new(){ JumpPressed=true };

        RunControl(ref position,ref rotation,ref verticalMotion,ref cameraRig,ref inputState);

        var ExpectedVelocity=CharacterMotion.JumpSpeed-(CharacterMotion.Gravity/60f);
        Assert.Equal(ExpectedVelocity,verticalMotion.Velocity,precision:3);
        Assert.False(verticalMotion.IsGrounded);
    }

    [Fact]
    public void Update_NoInput_PlayerStaysPutAndGrounded(){
        Position position=new(){ X=1,Y=0,Z=2 };
        Rotation rotation=new(){ X=0,Y=0,Z=0,W=1 };
        VerticalMotion verticalMotion=default;
        CameraRig cameraRig=default;
        InputState inputState=new();

        RunControl(ref position,ref rotation,ref verticalMotion,ref cameraRig,ref inputState);

        Assert.Equal(1f,position.X,precision:4);
        Assert.Equal(0f,position.Y,precision:4);
        Assert.Equal(2f,position.Z,precision:4);
        Assert.True(verticalMotion.IsGrounded);
    }

    [Fact]
    public void OrbitMotionSystem_AtQuarterTurn_MovesEntityAroundConfiguredCenter(){
        Position position=default;
        OrbitMotion motion=new(){
            CenterX=10f,
            CenterY=3f,
            CenterZ=-2f,
            Radius=4f,
            AngularSpeedRadiansPerSecond=MathF.PI/2f,
        };
        FrameContext frame=new(DeltaTime,1f);
        World world=new();
        State state=new(world);
        OrbitMotionSystem system=new();

        system.Update(ref position,ref motion,in frame,ref state);

        Assert.Equal(10f,position.X,precision:4);
        Assert.Equal(3f,position.Y,precision:4);
        Assert.Equal(2f,position.Z,precision:4);
    }

    [Fact]
    public void RotationMotion_UsesConfiguredAxisSpeedAndPhase(){
        Rotation rotation=default;
        RotateMotion motion=new(){ AxisX=0f,AxisY=1f,AxisZ=0f,RadiansPerSecond=MathF.PI,PhaseRadians=0f };
        FrameContext frame=new(DeltaTime,0.5f);
        World world=new();
        State state=new(world);
        RotationMotion system=new();

        system.Update(ref rotation,ref motion,in frame,ref state);

        Quaternion actual=new(rotation.X,rotation.Y,rotation.Z,rotation.W);
        Quaternion expected=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2f);
        Assert.True(Quaternion.Dot(actual,expected)>0.9999f);
    }

    [Fact]
    public void ParentToWorld_CombinesChildLocalWithCachedParentWorld(){
        TransformHierarchyRuntime hierarchy=new();
        hierarchy.Set(7,Matrix4x4.CreateTranslation(5f,0f,0f));
        Position position=new(){ X=1f,Y=2f,Z=3f };
        Rotation rotation=new(){ W=1f };
        Scale scale=new(){ X=1f,Y=1f,Z=1f };
        WorldTransform world=default;
        Parent parent=new(){ Value=new Entity(7) };
        World stateWorld=new();
        State state=new(stateWorld);
        ParentToWorld system=new();

        system.Update(new Entity(8),ref position,ref rotation,ref scale,ref world,ref parent,hierarchy,ref state);

        Assert.Equal(6f,world.Value.Translation.X,precision:4);
        Assert.Equal(2f,world.Value.Translation.Y,precision:4);
        Assert.Equal(3f,world.Value.Translation.Z,precision:4);
    }
}
