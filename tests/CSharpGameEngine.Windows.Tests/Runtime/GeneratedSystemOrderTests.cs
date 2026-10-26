using System.Reflection;
using CSharpGameEngine.Generated;
namespace CSharpGameEngine.Windows.Tests.Runtime;
/// <summary>
/// UserEcsProject のシステムがフェーズごとに実行される順序。生成されたシステムセットは実行順にシステムを列挙するため、そのフィールドからループ内の呼び出し順を確認できます。エンジンシステムは [SystemOrder] の順序を保ちます。起動シーンは FrameInput で最初に実行されます。ShowcaseDashboard はレベルを読み取ることだけを宣言し、名前で Present の後に実行されます。
/// </summary>
public sealed class GeneratedSystemOrderTests {
    private static readonly string[]FrameInput=[
        "MainScene__StartupSystem","EngineShowcaseScene__StartupSystem","EngineShowcaseRigidBottomScene__StartupSystem",
        "EngineShowcaseRigidMiddleScene__StartupSystem","EngineShowcaseRigidTopScene__StartupSystem",
        "EngineShowcaseProjectileScene__StartupSystem","Window","ShowcaseControls",
    ];
    private static readonly string[]FixedSimulation=["ShowcaseFixedMotion"];
    private static readonly string[]PhysicsSimulation=[
        "PhysicsSyncBegin","PhysicsBodySync","StaticMeshColliderSync","PhysicsStep","PhysicsTransformWriteback","CharacterMotionUpdate",
    ];
    private static readonly string[]Presentation=[
        "OrbitCameraUpdate","LightShadowValidation","LevelLoad","CameraOriginSync","DirectionalLightSync","AmbientLightSync",
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
