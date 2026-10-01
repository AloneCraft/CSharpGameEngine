using System.Reflection;
using CSharpGameEngine.Generated;
namespace CSharpGameEngine.Windows.Tests.Runtime;
/// <summary>
/// ADR-0073: the order UserEcsProject's systems run in, phase by phase. The generated system set lists the
/// systems in execution order, so its fields read back the order the loop calls them in. The engine systems keep
/// the order they had under [SystemOrder]; startup scenes run first in FrameInput and ShowcaseDashboard, which
/// only declares that it reads the level, runs after Present by name.
/// </summary>
public sealed class GeneratedSystemOrderTests {
    private static readonly string[]FrameInput=[
        "MainScene__StartupSystem","EngineShowcaseScene__StartupSystem","EngineShowcaseRigidBottomScene__StartupSystem",
        "EngineShowcaseRigidMiddleScene__StartupSystem","EngineShowcaseRigidTopScene__StartupSystem",
        "EngineShowcaseProjectileScene__StartupSystem","Window","ShowcaseControls",
    ];
    private static readonly string[]FixedSimulation=["ShowcaseFixedMotion"];
    private static readonly string[]PhysicsSimulation=[
        "PhysicsSyncBegin","PhysicsBodySync","StaticMeshColliderSync","PhysicsStep","PhysicsTransformWriteback","CharacterMovementUpdate",
    ];
    private static readonly string[]Presentation=[
        "SpringArmUpdate","LightShadowValidation","LevelLoad","CameraOriginSync","DirectionalLightSync","AmbientLightSync",
        "EnvironmentCollect","HeightFogSync","PointLightSync","SpotLightSync","RectAreaLightSync","TransformSync",
        "EmissiveMeshLightSync","SkeletalMeshSetup","LocomotionAnimation","SkinnedMeshSync","AudioFrameBegin",
        "AudioListenerSync","AudioSourceSync","Present","ShowcaseDashboard",
    ];
    [Fact]
    public void UserEcsProjectSystems_RunInTheDeclaredOrderPhaseByPhase(){
        string[]Expected=[.. FrameInput,.. FixedSimulation,.. PhysicsSimulation,.. Presentation];
        string[]Fields=[.. typeof(GeneratedSystemSet).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            .OrderBy(static Field=>Field.MetadataToken)
            .Select(static Field=>Field.Name)];
        Assert.Equal(Expected,Fields.Where(Expected.Contains));
    }
}
