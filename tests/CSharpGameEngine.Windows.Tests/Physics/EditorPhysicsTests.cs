using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Runtime.Windows.Tests;

/// <summary>
/// M0097：エディターワールドは物理デバッグオーバーレイ用にシーンのボディを登録するが、時間は進めない。そのためオーバーレイはSceneビューに描画されたオブジェクトに一致する。
/// シミュレーションを行うのは実行中のゲームだけ。
/// </summary>
public sealed class EditorPhysicsTests {
    [Theory]
    [InlineData(EngineExecutionMode.Runtime,true)]
    [InlineData(EngineExecutionMode.EditorPreview,false)]
    public void PhysicsAdvancesOnlyInTheRunningGame(EngineExecutionMode Mode,bool Falls){
        using EngineHost Host=new();
        State State=Host.CreateState();
        SceneInstance Instance=State.World.InstantiateScene<UserEcsProject.Scenes.PhysicsStackBottomScene>();
        State.Enabled=true;
        State.ExecutionMode=Mode;
        GeneratedSystemSet Systems=default;
        Entity Body=State.World.GetSceneEntities(Instance).ToArray()
            .Single(Entity=>State.World.TryGetComponent(Entity,out RigidBody Rigid)&&Rigid.MotionType==PhysicsMotionType.Dynamic);
        Assert.True(State.World.TryGetComponent(Body,out Transform Before));
        try {
            for(int Tick=0;Tick<8;Tick++){ GeneratedGameLoop.RunBakeTick(ref State,ref Systems,Host); }
            Assert.True(State.World.TryGetComponent(Body,out Transform After));
            if(Falls){ Assert.True(After.Position.U<Before.Position.U); }
            else { Assert.Equal(Before.Position,After.Position); }
            // ボディは登録済みのままなので、オーバーレイに描画対象がある。
            Assert.True(Host.PhysicsWorld.TryGetBodyState(Body,out _));
        }
        finally { GeneratedGameLoop.Shutdown(ref State,ref Systems,Host); }
    }
}
